using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    internal sealed class PoolInstanceRecord
    {
        internal PoolInstanceRecord(
            PoolEntry entry,
            long instanceSequence,
            GameObject gameObject,
            PoolInstanceSentinel sentinel,
            IPoolable[] participants)
        {
            Entry = entry;
            InstanceSequence = instanceSequence;
            GameObject = gameObject;
            Sentinel = sentinel;
            Participants = participants ?? Array.Empty<IPoolable>();
            State = PooledInstanceState.Internal;
        }

        internal PoolEntry Entry { get; }
        internal long InstanceSequence { get; }
        internal GameObject GameObject { get; set; }
        internal PoolInstanceSentinel Sentinel { get; }
        internal IPoolable[] Participants { get; }
        internal PooledInstanceState State { get; set; }
        internal uint Generation { get; set; }
        internal uint LastReturnedGeneration { get; set; }
        internal bool ExpectedDestroy { get; set; }
        internal bool DestroyObservationScheduled { get; set; }
        internal Transform TemporalActivationParent { get; set; }
        internal string AllocationStack { get; set; }
    }

    internal sealed class PoolEntry
    {
        private const int PrewarmBatchSize = 8;

        private readonly PoolScope scope;
        private readonly PoolDiagnosticLedger ledger;
        private readonly Dictionary<long, PoolInstanceRecord> records =
            new Dictionary<long, PoolInstanceRecord>();
        private readonly CancellationToken scopeCancellation;
        private readonly bool captureRentalStacks;
        private ContentLease<GameObject> sourceLease;
        private UnityObjectPoolAdapter adapter;
        private Task<PoolPrepareResult> prepareTask;
        private Task<PoolPrewarmResult> prewarmTask;
        private int prepareWaiters;
        private int prewarmWaiters;
        private bool lifecycleCallbackActive;
        private bool instanceMutationActive;
        private bool closeDrainPending;
        private bool forceDrainPending;
        private bool prepareInFlight;
        private bool prewarmInFlight;
        private bool terminalNotified;
        private bool forceClosed;
        private CancellationTokenSource operationCancellation;
        private int activeCount;
        private int inactiveCount;
        private int temporalRetainedCount;
        private int quarantineCount;
        private int pendingDestroyCount;
        private long createdCount;
        private long destroyedCount;
        private long rentCount;
        private long idleHitCount;
        private long createMissCount;
        private long resetFailureCount;
        private long externalDestroyCount;
        private CoCoDiagnostic lastDiagnostic;

        internal PoolEntry(
            PoolScope scope,
            PoolProfile profile,
            CancellationToken scopeCancellation)
        {
            this.scope = scope;
            Profile = profile;
            ledger = scope.Ledger;
            captureRentalStacks = scope.CaptureRentalStacks;
            this.scopeCancellation = scopeCancellation;
            State = PoolEntryState.Preparing;
        }

        internal PoolProfile Profile { get; }
        internal PoolId Id => Profile.Id;
        internal PoolEntryState State { get; private set; }
        internal int RecordCount => records.Count;
        internal int InactiveCount => inactiveCount;
        internal bool IsTerminal => State == PoolEntryState.Closed;

        internal void StartPrepare(ContentScope contentScope)
        {
            if (prepareTask != null) return;

            operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(scopeCancellation);
            prepareInFlight = true;
            var completion = new TaskCompletionSource<PoolPrepareResult>();
            prepareTask = completion.Task;
            CompletePrepareAsync(contentScope, completion).Forget();
        }

        internal async UniTask<PoolPrepareResult> AwaitPrepareAsync(
            CancellationToken cancellationToken)
        {
            prepareWaiters++;
            try
            {
                return await AwaitSharedTaskAsync(prepareTask)
                    .AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                return PoolPrepareResult.Cancellation(Id, PoolingErrors.Cancelled(Id));
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (prepareWaiters > 0) prepareWaiters--;
                if (prepareWaiters == 0 && prepareInFlight)
                {
                    operationCancellation?.Cancel();
                }
            }
        }

        internal async UniTask<PoolPrewarmResult> AwaitPrewarmAsync(
            CancellationToken cancellationToken)
        {
            if (State == PoolEntryState.Prewarming && prewarmTask != null)
            {
                prewarmWaiters++;
                return await AwaitCurrentPrewarmAsync(cancellationToken);
            }

            if (State != PoolEntryState.Ready)
            {
                return PoolPrewarmResult.Failure(
                    Id,
                    0,
                    inactiveCount,
                    State == PoolEntryState.Closing || State == PoolEntryState.Closed
                        ? PoolingErrors.ScopeClosing(Id)
                        : PoolingErrors.NotReady(Id));
            }

            State = PoolEntryState.Prewarming;
            operationCancellation?.Dispose();
            operationCancellation =
                CancellationTokenSource.CreateLinkedTokenSource(scopeCancellation);
            prewarmInFlight = true;
            prewarmWaiters = 1;
            var completion = new TaskCompletionSource<PoolPrewarmResult>();
            prewarmTask = completion.Task;
            CompletePrewarmAsync(completion).Forget();
            return await AwaitCurrentPrewarmAsync(cancellationToken);
        }

        private async UniTask<PoolPrewarmResult> AwaitCurrentPrewarmAsync(
            CancellationToken cancellationToken)
        {
            Task<PoolPrewarmResult> observedTask = prewarmTask;
            CancellationTokenSource observedCancellation = operationCancellation;
            try
            {
                return await AwaitSharedTaskAsync(observedTask)
                    .AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                return PoolPrewarmResult.Cancellation(
                    Id,
                    0,
                    inactiveCount,
                    PoolingErrors.Cancelled(Id));
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                if (ReferenceEquals(prewarmTask, observedTask))
                {
                    if (prewarmWaiters > 0) prewarmWaiters--;
                    if (prewarmWaiters == 0 &&
                        prewarmInFlight &&
                        ReferenceEquals(operationCancellation, observedCancellation))
                    {
                        observedCancellation?.Cancel();
                    }
                }
            }
        }

        internal bool TryRent(
            out PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            handle = default;
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            try
            {
                return TryRentCore(out handle, out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryRentCore(
            out PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            handle = default;
            if (lifecycleCallbackActive)
            {
                diagnostic = PoolingErrors.CallbackReentry(Id);
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            if (State != PoolEntryState.Ready)
            {
                diagnostic = State == PoolEntryState.Closing ||
                             State == PoolEntryState.Closed
                    ? PoolingErrors.ScopeClosing(Id)
                    : PoolingErrors.NotReady(Id);
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            bool wasIdleHit = inactiveCount > 0;
            PoolInstanceRecord record;
            try
            {
                record = adapter.Get();
            }
            catch (Exception exception)
            {
                diagnostic = PoolingErrors.CreateFailed(Id, exception.Message);
                lastDiagnostic = diagnostic;
                Record(PoolDiagnosticEventKind.LifecycleFailed, null, diagnostic);
                return false;
            }

            if (State != PoolEntryState.Ready)
            {
                ScheduleDestroy(record);
                diagnostic = State == PoolEntryState.Closing ||
                             State == PoolEntryState.Closed
                    ? PoolingErrors.ScopeClosing(Id)
                    : PoolingErrors.NotReady(Id);
                Record(PoolDiagnosticEventKind.HandleRejected, record, diagnostic);
                return false;
            }

            AdvanceGeneration(record);
            record.AllocationStack = captureRentalStacks
                ? Environment.StackTrace
                : string.Empty;
            Transition(record, PooledInstanceState.LeasedInactive);
            rentCount++;
            if (wasIdleHit) idleHitCount++;
            else createMissCount++;

            handle = new PooledHandle(
                scope,
                Id,
                scope.ScopeSequence,
                record.InstanceSequence,
                record.Generation);
            diagnostic = CoCoDiagnostic.None;
            Record(PoolDiagnosticEventKind.RentSucceeded, record, diagnostic);
            return true;
        }

        internal bool TryGetInstance(
            in PooledHandle handle,
            out GameObject instance,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateHandle(handle, out PoolInstanceRecord record, out diagnostic))
            {
                instance = null;
                return false;
            }

            if (record.State != PooledInstanceState.LeasedInactive &&
                record.State != PooledInstanceState.Active)
            {
                instance = null;
                diagnostic = PoolingErrors.InvalidTransition(
                    Id,
                    record.InstanceSequence,
                    record.State,
                    "resolve its instance");
                Record(PoolDiagnosticEventKind.HandleRejected, record, diagnostic);
                return false;
            }

            if (record.GameObject == null)
            {
                instance = null;
                diagnostic = PoolingErrors.InstanceDestroyed(
                    Id,
                    record.InstanceSequence,
                    false);
                ScheduleDestroy(record);
                Record(PoolDiagnosticEventKind.HandleRejected, record, diagnostic);
                return false;
            }

            instance = record.GameObject;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryActivate(
            in PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            try
            {
                return TryActivateCore(handle, out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryActivateCore(
            in PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (lifecycleCallbackActive)
            {
                diagnostic = PoolingErrors.CallbackReentry(Id);
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            if (State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed)
            {
                diagnostic = PoolingErrors.ScopeClosing(Id);
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            if (!TryValidateHandle(handle, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            if (record.State != PooledInstanceState.LeasedInactive)
            {
                diagnostic = PoolingErrors.InvalidTransition(
                    Id,
                    record.InstanceSequence,
                    record.State,
                    "activate");
                Record(PoolDiagnosticEventKind.HandleRejected, record, diagnostic);
                return false;
            }

            if (!TryRunRentCallbacks(
                    record,
                    false,
                    out int completedCount,
                    out diagnostic))
            {
                TryRunReturnCallbacks(
                    record,
                    completedCount,
                    PoolReturnReason.ActivationFailure,
                    false,
                    record.Generation,
                    out _);
                ScheduleDestroy(record);
                lastDiagnostic = diagnostic;
                Record(PoolDiagnosticEventKind.LifecycleFailed, record, diagnostic);
                return false;
            }

            if (!ContainsRecord(record) ||
                record.GameObject == null ||
                record.State != PooledInstanceState.LeasedInactive ||
                record.Generation != handle.Generation ||
                State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed)
            {
                diagnostic = ContainsRecord(record) && record.GameObject != null
                    ? State == PoolEntryState.Closing ||
                      State == PoolEntryState.Closed
                        ? PoolingErrors.ScopeClosing(Id)
                        : PoolingErrors.ActivationFailed(
                            Id,
                            record.InstanceSequence,
                            "Activation callbacks changed the expected instance state.")
                    : PoolingErrors.InstanceDestroyed(
                        Id,
                        record.InstanceSequence,
                        false);
                if (ContainsRecord(record))
                {
                    TryRunReturnCallbacks(
                        record,
                        record.Participants.Length,
                        State == PoolEntryState.Closing ||
                        State == PoolEntryState.Closed
                            ? PoolReturnReason.ScopeClosing
                            : PoolReturnReason.ActivationFailure,
                        false,
                        record.Generation,
                        out _);
                    ScheduleDestroy(record);
                }

                lastDiagnostic = diagnostic;
                Record(PoolDiagnosticEventKind.LifecycleFailed, record, diagnostic);
                return false;
            }

            try
            {
                record.GameObject.SetActive(true);
            }
            catch (Exception exception)
            {
                diagnostic = PoolingErrors.ActivationFailed(
                    Id,
                    record.InstanceSequence,
                    exception.Message);
                TryRunReturnCallbacks(
                    record,
                    record.Participants.Length,
                    PoolReturnReason.ActivationFailure,
                    false,
                    record.Generation,
                    out _);
                ScheduleDestroy(record);
                lastDiagnostic = diagnostic;
                Record(PoolDiagnosticEventKind.LifecycleFailed, record, diagnostic);
                return false;
            }

            if (!ContainsRecord(record) ||
                record.GameObject == null ||
                record.State != PooledInstanceState.LeasedInactive ||
                record.Generation != handle.Generation ||
                !record.GameObject.activeInHierarchy ||
                State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed)
            {
                diagnostic = ContainsRecord(record) && record.GameObject != null
                    ? State == PoolEntryState.Closing ||
                      State == PoolEntryState.Closed
                        ? PoolingErrors.ScopeClosing(Id)
                        : PoolingErrors.ActivationFailed(
                            Id,
                            record.InstanceSequence,
                            "OnEnable changed the expected instance state.")
                    : PoolingErrors.InstanceDestroyed(
                        Id,
                        record.InstanceSequence,
                        false);
                if (ContainsRecord(record))
                {
                    TryRunReturnCallbacks(
                        record,
                        record.Participants.Length,
                        State == PoolEntryState.Closing ||
                        State == PoolEntryState.Closed
                            ? PoolReturnReason.ScopeClosing
                            : PoolReturnReason.ActivationFailure,
                        false,
                        record.Generation,
                        out _);
                    ScheduleDestroy(record);
                }

                lastDiagnostic = diagnostic;
                Record(PoolDiagnosticEventKind.LifecycleFailed, record, diagnostic);
                return false;
            }

            Transition(record, PooledInstanceState.Active);
            diagnostic = CoCoDiagnostic.None;
            Record(PoolDiagnosticEventKind.ActivateSucceeded, record, diagnostic);
            return true;
        }

        internal bool TryReturn(
            in PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            try
            {
                return TryReturnCore(handle, out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryReturnCore(
            in PooledHandle handle,
            out CoCoDiagnostic diagnostic)
        {
            if (lifecycleCallbackActive)
            {
                diagnostic = PoolingErrors.CallbackReentry(Id);
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            if (!TryValidateHandle(handle, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            if (record.State != PooledInstanceState.LeasedInactive &&
                record.State != PooledInstanceState.Active)
            {
                diagnostic = PoolingErrors.InvalidTransition(
                    Id,
                    record.InstanceSequence,
                    record.State,
                    "return");
                Record(PoolDiagnosticEventKind.HandleRejected, record, diagnostic);
                return false;
            }

            uint rentalGeneration = record.Generation;
            record.LastReturnedGeneration = rentalGeneration;
            AdvanceGeneration(record);
            if (record.GameObject == null)
            {
                diagnostic = PoolingErrors.InstanceDestroyed(
                    Id,
                    record.InstanceSequence,
                    false);
                ScheduleDestroy(record);
                Record(PoolDiagnosticEventKind.HandleRejected, record, diagnostic);
                return false;
            }

            return ReturnRecord(
                record,
                State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed
                    ? PoolReturnReason.ScopeClosing
                    : PoolReturnReason.ConsumerReturn,
                false,
                rentalGeneration,
                out diagnostic);
        }

        internal bool TryClearInactive(out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                return false;
            }

            try
            {
                return TryClearInactiveCore(out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryClearInactiveCore(out CoCoDiagnostic diagnostic)
        {
            if (lifecycleCallbackActive)
            {
                diagnostic = PoolingErrors.CallbackReentry(Id);
                return false;
            }

            if (State != PoolEntryState.Ready)
            {
                diagnostic = State == PoolEntryState.Prewarming
                    ? PoolingErrors.OperationInProgress(Id)
                    : State == PoolEntryState.Closing ||
                      State == PoolEntryState.Closed
                        ? PoolingErrors.ScopeClosing(Id)
                        : PoolingErrors.NotReady(Id);
                return false;
            }

            int cleared = inactiveCount;
            adapter?.Clear();
            diagnostic = CoCoDiagnostic.None;
            Record(PoolDiagnosticEventKind.InactiveCleared, null, diagnostic);
            return cleared >= 0;
        }

        internal PoolEntrySnapshot CaptureSnapshot()
        {
            return new PoolEntrySnapshot(
                Id,
                Profile.PrefabSource.Id,
                State,
                Profile.PrewarmCount,
                Profile.MaxRetained,
                activeCount,
                inactiveCount,
                temporalRetainedCount,
                quarantineCount,
                pendingDestroyCount,
                createdCount,
                destroyedCount,
                rentCount,
                idleHitCount,
                createMissCount,
                resetFailureCount,
                externalDestroyCount,
                sourceLease != null && !sourceLease.IsReleased,
                lastDiagnostic);
        }

        internal void BeginClosing()
        {
            if (State == PoolEntryState.Closed || State == PoolEntryState.Closing) return;

            State = PoolEntryState.Closing;
            operationCancellation?.Cancel();
            Record(PoolDiagnosticEventKind.ScopeClosing, null, CoCoDiagnostic.None);
            PoolInstanceRecord[] live = CaptureRecords();
            foreach (PoolInstanceRecord record in live)
            {
                EnsureDestroyObservation(record);
            }

            if (instanceMutationActive || lifecycleCallbackActive)
            {
                closeDrainPending = true;
                return;
            }

            DrainNormalClose();
        }

        internal void ForceClose()
        {
            if (State == PoolEntryState.Closed) return;
            if (forceClosed)
            {
                return;
            }

            forceClosed = true;
            State = PoolEntryState.Closing;
            operationCancellation?.Cancel();
            closeDrainPending = false;
            if (instanceMutationActive || lifecycleCallbackActive)
            {
                forceDrainPending = true;
                return;
            }

            DrainForceClose();
        }

        private void DrainNormalClose()
        {
            closeDrainPending = false;
            adapter?.Clear();
            TryFinalizeClose();
        }

        private void DrainForceClose()
        {
            forceDrainPending = false;

            // Dispose the Unity pool first. Idle records are scheduled for
            // destruction, and any records still held by an async warm batch
            // will take the adapter's terminal Release path in its finally.
            UnityObjectPoolAdapter currentAdapter = adapter;
            adapter = null;
            currentAdapter?.Dispose();

            bool canReset = !instanceMutationActive && !lifecycleCallbackActive;
            if (canReset) instanceMutationActive = true;
            PoolInstanceRecord[] live = CaptureRecords();
            try
            {
                foreach (PoolInstanceRecord record in live)
                {
                    if (!ContainsRecord(record))
                    {
                        continue;
                    }

                    PooledInstanceState originalState = record.State;
                    uint contextGeneration = record.Generation;
                    if (originalState == PooledInstanceState.LeasedInactive ||
                        originalState == PooledInstanceState.Active)
                    {
                        CoCoDiagnostic leak = PoolingErrors.HandleLeak(
                            Id,
                            record.InstanceSequence,
                            record.AllocationStack);
                        Record(PoolDiagnosticEventKind.ForcedShutdown, record, leak);
                    }

                    if (originalState != PooledInstanceState.DestroyPending &&
                        originalState != PooledInstanceState.Destroyed)
                    {
                        if (originalState == PooledInstanceState.LeasedInactive ||
                            originalState == PooledInstanceState.Active)
                        {
                            record.LastReturnedGeneration = contextGeneration;
                        }

                        AdvanceGeneration(record);
                    }

                    bool requiresReset =
                        originalState == PooledInstanceState.LeasedInactive ||
                        originalState == PooledInstanceState.Active ||
                        originalState == PooledInstanceState.TemporalInactive ||
                        originalState == PooledInstanceState.TemporalActive;
                    if (requiresReset)
                    {
                        if (canReset)
                        {
                            BestEffortForceReset(
                                record,
                                originalState == PooledInstanceState.TemporalInactive ||
                                originalState == PooledInstanceState.TemporalActive,
                                contextGeneration);
                        }
                        else
                        {
                            CoCoDiagnostic reentry = PoolingErrors.CallbackReentry(Id);
                            lastDiagnostic = reentry;
                            Record(
                                PoolDiagnosticEventKind.ForcedShutdown,
                                record,
                                reentry);
                        }
                    }

                    ScheduleDestroy(record);
                }
            }
            finally
            {
                if (canReset) instanceMutationActive = false;
            }

            // The source ContentLease is intentionally retained. Sentinel
            // OnDestroy callbacks remove the physical records, and only the
            // zero-record terminal barrier is allowed to release ownership.
            TryFinalizeClose();
        }

        internal PoolInstanceRecord CreateInstance()
        {
            GameObject prefab = sourceLease?.Value;
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "The pool's ContentLease no longer contains a prefab source.");
            }

            GameObject instance = null;
            try
            {
                instance = UnityEngine.Object.Instantiate(
                    prefab,
                    scope.RetentionRoot,
                    false);
                instance.SetActive(false);

                MonoBehaviour[] components =
                    instance.GetComponentsInChildren<MonoBehaviour>(true);
                var participants = new List<IPoolable>(components.Length);
                foreach (MonoBehaviour component in components)
                {
                    if (component is IPoolable poolable)
                    {
                        participants.Add(poolable);
                    }
                }

                long instanceSequence = scope.AllocateInstanceSequence();
                PoolInstanceSentinel sentinel =
                    instance.AddComponent<PoolInstanceSentinel>();
                var record = new PoolInstanceRecord(
                    this,
                    instanceSequence,
                    instance,
                    sentinel,
                    participants.ToArray());
                sentinel.Initialize(this, instanceSequence);
                records.Add(instanceSequence, record);
                createdCount++;
                Record(PoolDiagnosticEventKind.InstanceCreated, record, CoCoDiagnostic.None);
                return record;
            }
            catch
            {
                if (instance != null)
                {
                    DestroyUnityObject(instance);
                }

                throw;
            }
        }

        internal void OnTakenFromUnityPool(PoolInstanceRecord record)
        {
            if (record == null || !ContainsRecord(record)) return;
            Transition(record, PooledInstanceState.Internal);
        }

        internal void OnInvalidRecordTakenFromUnityPool(
            PoolInstanceRecord record)
        {
            if (!ContainsRecord(record))
            {
                return;
            }

            AdvanceGeneration(record);
            ScheduleDestroy(record);
        }

        internal void OnReleasedToUnityPool(PoolInstanceRecord record)
        {
            if (record == null || !ContainsRecord(record)) return;
            Transition(record, PooledInstanceState.Inactive);
            record.AllocationStack = string.Empty;
        }

        internal void OnDestroyedByUnityPool(PoolInstanceRecord record)
        {
            ScheduleDestroy(record);
        }

        internal bool ContainsRecord(PoolInstanceRecord record)
        {
            return record != null &&
                   records.TryGetValue(
                       record.InstanceSequence,
                       out PoolInstanceRecord current) &&
                   ReferenceEquals(current, record);
        }

        internal void OnSentinelDestroyed(long instanceSequence)
        {
            if (!records.TryGetValue(
                    instanceSequence,
                    out PoolInstanceRecord record))
            {
                return;
            }

            if (Application.isPlaying)
            {
                // Other components on the same GameObject may still be inside
                // their OnDestroy callbacks. Cross a full frame boundary before
                // releasing source ownership for the physical instance.
                EnsureDestroyObservation(record);
                return;
            }

            FinalizeObservedDestroyedRecord(record);
        }

        internal bool TryAdoptTemporal(
            in PooledHandle handle,
            out PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            token = default;
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                return false;
            }

            try
            {
                return TryAdoptTemporalCore(handle, out token, out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryAdoptTemporalCore(
            in PooledHandle handle,
            out PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            token = default;
            if (State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed)
            {
                diagnostic = PoolingErrors.ScopeClosing(Id);
                return false;
            }

            if (lifecycleCallbackActive)
            {
                diagnostic = PoolingErrors.CallbackReentry(Id);
                return false;
            }

            if (!TryValidateHandle(handle, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            if (record.State != PooledInstanceState.LeasedInactive)
            {
                diagnostic = PoolingErrors.TemporalConflict(
                    "Only a leased inactive instance can transfer to temporal authority.");
                return false;
            }

            AdvanceGeneration(record);
            Transition(record, PooledInstanceState.TemporalInactive);
            record.TemporalActivationParent = record.GameObject.transform.parent;
            token = new PoolTemporalToken(
                scope,
                Id,
                record.InstanceSequence,
                record.Generation);
            Record(PoolDiagnosticEventKind.TemporalAdopted, record, CoCoDiagnostic.None);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryGetTemporalInstance(
            in PoolTemporalToken token,
            out GameObject instance,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateTemporalToken(token, out PoolInstanceRecord record, out diagnostic))
            {
                instance = null;
                return false;
            }

            if (record.GameObject == null)
            {
                instance = null;
                diagnostic = PoolingErrors.TemporalUnavailable(
                    "The physical GameObject was destroyed.");
                ScheduleDestroy(record);
                return false;
            }

            instance = record.GameObject;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryActivateTemporal(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                return false;
            }

            try
            {
                return TryActivateTemporalCore(ref token, out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryActivateTemporalCore(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed)
            {
                diagnostic = PoolingErrors.ScopeClosing(Id);
                return false;
            }

            if (!TryValidateTemporalToken(token, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            if (record.State != PooledInstanceState.TemporalInactive)
            {
                diagnostic = PoolingErrors.InvalidTransition(
                    Id,
                    record.InstanceSequence,
                    record.State,
                    "activate under temporal authority");
                return false;
            }

            Transform currentParent = record.GameObject.transform.parent;
            if (currentParent == scope.RetentionRoot &&
                record.TemporalActivationParent != null)
            {
                record.GameObject.transform.SetParent(
                    record.TemporalActivationParent,
                    false);
            }
            else if (currentParent != scope.RetentionRoot)
            {
                record.TemporalActivationParent = currentParent;
            }

            if (!TryRunRentCallbacks(
                    record,
                    true,
                    out int completedCount,
                    out diagnostic))
            {
                TryRunReturnCallbacks(
                    record,
                    completedCount,
                    PoolReturnReason.ActivationFailure,
                    true,
                    record.Generation,
                    out _);
                AdvanceGeneration(record);
                ScheduleDestroy(record);
                token = default;
                return false;
            }

            uint expectedGeneration = record.Generation;
            if (!ContainsRecord(record) ||
                record.GameObject == null ||
                record.State != PooledInstanceState.TemporalInactive ||
                record.Generation != expectedGeneration ||
                State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed)
            {
                diagnostic = ContainsRecord(record) && record.GameObject != null
                    ? State == PoolEntryState.Closing ||
                      State == PoolEntryState.Closed
                        ? PoolingErrors.ScopeClosing(Id)
                        : PoolingErrors.ActivationFailed(
                            Id,
                            record.InstanceSequence,
                            "Temporal activation callbacks changed the expected instance state.")
                    : PoolingErrors.TemporalUnavailable(
                        "The physical GameObject was destroyed during activation.");
                if (ContainsRecord(record))
                {
                    TryRunReturnCallbacks(
                        record,
                        record.Participants.Length,
                        State == PoolEntryState.Closing ||
                        State == PoolEntryState.Closed
                            ? PoolReturnReason.ScopeClosing
                            : PoolReturnReason.ActivationFailure,
                        true,
                        expectedGeneration,
                        out _);
                    AdvanceGeneration(record);
                    ScheduleDestroy(record);
                }

                token = default;
                return false;
            }

            try
            {
                record.GameObject.SetActive(true);
            }
            catch (Exception exception)
            {
                diagnostic = PoolingErrors.ActivationFailed(
                    Id,
                    record.InstanceSequence,
                    exception.Message);
                TryRunReturnCallbacks(
                    record,
                    record.Participants.Length,
                    PoolReturnReason.ActivationFailure,
                    true,
                    expectedGeneration,
                    out _);
                AdvanceGeneration(record);
                ScheduleDestroy(record);
                token = default;
                return false;
            }

            if (!ContainsRecord(record) ||
                record.GameObject == null ||
                record.State != PooledInstanceState.TemporalInactive ||
                record.Generation != expectedGeneration ||
                !record.GameObject.activeInHierarchy ||
                State == PoolEntryState.Closing ||
                State == PoolEntryState.Closed)
            {
                diagnostic = ContainsRecord(record) && record.GameObject != null
                    ? State == PoolEntryState.Closing ||
                      State == PoolEntryState.Closed
                        ? PoolingErrors.ScopeClosing(Id)
                        : PoolingErrors.ActivationFailed(
                            Id,
                            record.InstanceSequence,
                            "OnEnable changed the expected Temporal instance state.")
                    : PoolingErrors.TemporalUnavailable(
                        "The physical GameObject was destroyed during activation.");
                if (ContainsRecord(record))
                {
                    TryRunReturnCallbacks(
                        record,
                        record.Participants.Length,
                        State == PoolEntryState.Closing ||
                        State == PoolEntryState.Closed
                            ? PoolReturnReason.ScopeClosing
                            : PoolReturnReason.ActivationFailure,
                        true,
                        expectedGeneration,
                        out _);
                    AdvanceGeneration(record);
                    ScheduleDestroy(record);
                }

                token = default;
                return false;
            }

            Transition(record, PooledInstanceState.TemporalActive);
            Record(PoolDiagnosticEventKind.TemporalStateChanged, record, CoCoDiagnostic.None);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryDespawnTemporal(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                return false;
            }

            try
            {
                return TryDespawnTemporalCore(ref token, out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryDespawnTemporalCore(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateTemporalToken(token, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            if (record.State != PooledInstanceState.TemporalActive &&
                record.State != PooledInstanceState.TemporalInactive)
            {
                diagnostic = PoolingErrors.InvalidTransition(
                    Id,
                    record.InstanceSequence,
                    record.State,
                    "despawn under temporal authority");
                return false;
            }

            uint expectedGeneration = record.Generation;
            PooledInstanceState expectedState = record.State;
            if (!ResetTemporalRecord(
                    record,
                    expectedState,
                    expectedGeneration,
                    PoolReturnReason.TemporalDespawn,
                    out diagnostic))
            {
                if (ContainsRecord(record) &&
                    record.Generation == expectedGeneration)
                {
                    AdvanceGeneration(record);
                }

                ScheduleDestroy(record);
                token = default;
                return false;
            }

            Transition(record, PooledInstanceState.TemporalQuarantined);
            Record(PoolDiagnosticEventKind.TemporalStateChanged, record, CoCoDiagnostic.None);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareTemporalPresence(
            ref PoolTemporalToken token,
            bool desiredPresent,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                return false;
            }

            try
            {
                return TryPrepareTemporalPresenceCore(
                    ref token,
                    desiredPresent,
                    out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryPrepareTemporalPresenceCore(
            ref PoolTemporalToken token,
            bool desiredPresent,
            out CoCoDiagnostic diagnostic)
        {
            if (desiredPresent &&
                (State == PoolEntryState.Closing ||
                 State == PoolEntryState.Closed))
            {
                diagnostic = PoolingErrors.ScopeClosing(Id);
                return false;
            }

            if (!TryValidateTemporalToken(token, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            if (!desiredPresent)
            {
                if (record.State == PooledInstanceState.TemporalQuarantined)
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                return TryDespawnTemporalCore(ref token, out diagnostic);
            }

            if (record.State == PooledInstanceState.TemporalActive)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (record.State == PooledInstanceState.TemporalInactive)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (record.State != PooledInstanceState.TemporalQuarantined)
            {
                diagnostic = PoolingErrors.InvalidTransition(
                    Id,
                    record.InstanceSequence,
                    record.State,
                    "prepare temporal presence");
                return false;
            }

            Transition(record, PooledInstanceState.TemporalInactive);
            Record(PoolDiagnosticEventKind.TemporalStateChanged, record, CoCoDiagnostic.None);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryReleaseTemporal(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterInstanceMutation(out diagnostic))
            {
                return false;
            }

            try
            {
                return TryReleaseTemporalCore(ref token, out diagnostic);
            }
            finally
            {
                ExitInstanceMutation();
            }
        }

        private bool TryReleaseTemporalCore(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateTemporalToken(token, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            if (record.State == PooledInstanceState.TemporalActive ||
                record.State == PooledInstanceState.TemporalInactive)
            {
                uint expectedGeneration = record.Generation;
                PooledInstanceState expectedState = record.State;
                if (!ResetTemporalRecord(
                        record,
                        expectedState,
                        expectedGeneration,
                        PoolReturnReason.TemporalRelease,
                        out diagnostic))
                {
                    if (ContainsRecord(record) &&
                        record.Generation == expectedGeneration)
                    {
                        AdvanceGeneration(record);
                    }

                    ScheduleDestroy(record);
                    token = default;
                    return false;
                }
            }
            else if (record.State != PooledInstanceState.TemporalQuarantined)
            {
                diagnostic = PoolingErrors.InvalidTransition(
                    Id,
                    record.InstanceSequence,
                    record.State,
                    "release temporal authority");
                return false;
            }

            AdvanceGeneration(record);
            token = default;
            record.TemporalActivationParent = null;
            if (State == PoolEntryState.Closing || State == PoolEntryState.Closed)
            {
                ScheduleDestroy(record);
            }
            else
            {
                ReparentForRetention(record);
                adapter.Release(record);
            }

            Record(PoolDiagnosticEventKind.TemporalReleased, record, CoCoDiagnostic.None);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool ForceDestroyTemporal(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            // Terminal cleanup must win over callback/activation re-entry. The
            // outer mutation revalidates generation and state before publishing.
            return ForceDestroyTemporalCore(ref token, out diagnostic);
        }

        private bool ForceDestroyTemporalCore(
            ref PoolTemporalToken token,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateTemporalToken(token, out PoolInstanceRecord record, out diagnostic))
            {
                return false;
            }

            AdvanceGeneration(record);
            token = default;
            ScheduleDestroy(record);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private async UniTask CompletePrepareAsync(
            ContentScope contentScope,
            TaskCompletionSource<PoolPrepareResult> completion)
        {
            PoolPrepareResult finalResult = default;
            Record(PoolDiagnosticEventKind.PrepareStarted, null, CoCoDiagnostic.None);
            try
            {
                ContentAcquireResult<GameObject> acquired =
                    await contentScope.AcquirePrefabSourceAsync(
                        Profile.PrefabSource,
                        operationCancellation.Token);
                await UniTask.SwitchToMainThread();

                if (!acquired.Succeeded)
                {
                    if (acquired.Cancelled ||
                        operationCancellation.IsCancellationRequested)
                    {
                        CoCoDiagnostic cancelled = acquired.Diagnostic.IsNone
                            ? PoolingErrors.Cancelled(Id)
                            : acquired.Diagnostic;
                        finalResult = PoolPrepareResult.Cancellation(Id, cancelled);
                        lastDiagnostic = cancelled;
                        Record(PoolDiagnosticEventKind.PrepareCancelled, null, cancelled);
                    }
                    else
                    {
                        finalResult = PoolPrepareResult.Failure(Id, acquired.Diagnostic);
                        lastDiagnostic = acquired.Diagnostic;
                        Record(
                            PoolDiagnosticEventKind.PrepareFailed,
                            null,
                            acquired.Diagnostic);
                    }

                    State = State == PoolEntryState.Closing
                        ? PoolEntryState.Closing
                        : PoolEntryState.Failed;
                    return;
                }

                if (forceClosed || State == PoolEntryState.Closed)
                {
                    acquired.Lease.Dispose();
                    CoCoDiagnostic cancelled = PoolingErrors.Cancelled(Id);
                    finalResult = PoolPrepareResult.Cancellation(Id, cancelled);
                    return;
                }

                sourceLease = acquired.Lease;
                adapter = new UnityObjectPoolAdapter(
                    this,
                    Math.Max(1, Profile.PrewarmCount),
                    Profile.MaxRetained);
                int created = await WarmToTargetAsync(
                    Profile.PrewarmCount,
                    operationCancellation.Token);
                await UniTask.SwitchToMainThread();
                if (forceClosed ||
                    State != PoolEntryState.Preparing ||
                    operationCancellation.IsCancellationRequested)
                {
                    throw new OperationCanceledException();
                }

                State = PoolEntryState.Ready;
                finalResult = PoolPrepareResult.Success(Id, created);
                Record(PoolDiagnosticEventKind.PrepareSucceeded, null, CoCoDiagnostic.None);
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                CoCoDiagnostic cancelled = PoolingErrors.Cancelled(Id);
                finalResult = PoolPrepareResult.Cancellation(Id, cancelled);
                lastDiagnostic = cancelled;
                Record(PoolDiagnosticEventKind.PrepareCancelled, null, cancelled);
                if (State != PoolEntryState.Closing &&
                    State != PoolEntryState.Closed)
                {
                    State = PoolEntryState.Failed;
                }
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                CoCoDiagnostic failure = PoolingErrors.CreateFailed(Id, exception.Message);
                finalResult = PoolPrepareResult.Failure(Id, failure);
                lastDiagnostic = failure;
                Record(PoolDiagnosticEventKind.PrepareFailed, null, failure);
                if (State != PoolEntryState.Closing &&
                    State != PoolEntryState.Closed)
                {
                    State = PoolEntryState.Failed;
                }
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                prepareInFlight = false;
                operationCancellation?.Dispose();
                operationCancellation = null;
                if (State == PoolEntryState.Failed ||
                    State == PoolEntryState.Closing ||
                    forceClosed)
                {
                    adapter?.Clear();
                    await AwaitNoRecordsAsync();
                    adapter?.Dispose();
                    adapter = null;
                    sourceLease?.Dispose();
                    sourceLease = null;
                }

                completion.TrySetResult(finalResult);
                if (State == PoolEntryState.Failed && !forceClosed)
                {
                    scope.OnEntryPrepareFailed(this);
                }

                TryFinalizeClose();
            }
        }

        private async UniTask CompletePrewarmAsync(
            TaskCompletionSource<PoolPrewarmResult> completion)
        {
            PoolPrewarmResult result = default;
            long beforeCreated = createdCount;
            Record(PoolDiagnosticEventKind.PrewarmStarted, null, CoCoDiagnostic.None);
            try
            {
                await WarmToTargetAsync(
                    Profile.PrewarmCount,
                    operationCancellation.Token);
                await UniTask.SwitchToMainThread();
                result = PoolPrewarmResult.Success(
                    Id,
                    (int)(createdCount - beforeCreated),
                    inactiveCount);
            }
            catch (OperationCanceledException)
            {
                await UniTask.SwitchToMainThread();
                result = PoolPrewarmResult.Cancellation(
                    Id,
                    (int)(createdCount - beforeCreated),
                    inactiveCount,
                    PoolingErrors.Cancelled(Id));
            }
            catch (Exception exception)
            {
                await UniTask.SwitchToMainThread();
                CoCoDiagnostic failure = PoolingErrors.CreateFailed(Id, exception.Message);
                lastDiagnostic = failure;
                result = PoolPrewarmResult.Failure(
                    Id,
                    (int)(createdCount - beforeCreated),
                    inactiveCount,
                    failure);
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                prewarmInFlight = false;
                operationCancellation?.Dispose();
                operationCancellation = null;
                if (State == PoolEntryState.Prewarming)
                {
                    State = PoolEntryState.Ready;
                }
                else if (State == PoolEntryState.Closing)
                {
                    // A cancelled prewarm returns its temporarily held records from
                    // WarmToTargetAsync before this continuation runs. Closing must
                    // clear those late idle returns as well or the Scope can never
                    // reach its zero-record terminal state.
                    adapter?.Clear();
                }

                Record(
                    PoolDiagnosticEventKind.PrewarmCompleted,
                    null,
                    result.Diagnostic);
                completion.TrySetResult(result);
                TryFinalizeClose();
            }
        }

        private async UniTask<int> WarmToTargetAsync(
            int target,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (target <= 0) return 0;

            int beforeCreated = (int)createdCount;
            var held = new List<PoolInstanceRecord>(target);
            UnityObjectPoolAdapter currentAdapter = adapter;
            try
            {
                int existingIdle = inactiveCount;
                for (int index = 0; index < existingIdle; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    held.Add(currentAdapter.Get());
                }

                while (held.Count < target)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    held.Add(currentAdapter.Get());
                    if (held.Count % PrewarmBatchSize == 0)
                    {
                        await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                foreach (PoolInstanceRecord record in held)
                {
                    if (!ContainsRecord(record)) continue;

                    if (State == PoolEntryState.Closing ||
                        State == PoolEntryState.Closed ||
                        forceClosed)
                    {
                        ScheduleDestroy(record);
                    }
                    else
                    {
                        currentAdapter.Release(record);
                    }
                }
            }

            return (int)createdCount - beforeCreated;
        }

        private bool ReturnRecord(
            PoolInstanceRecord record,
            PoolReturnReason reason,
            bool temporal,
            uint contextGeneration,
            out CoCoDiagnostic diagnostic)
        {
            Transition(record, PooledInstanceState.Returning);
            if (record.GameObject != null)
            {
                record.GameObject.SetActive(false);
            }

            bool resetSucceeded = TryRunReturnCallbacks(
                record,
                record.Participants.Length,
                reason,
                temporal,
                contextGeneration,
                out diagnostic);
            if (!ContainsRecord(record) || record.GameObject == null)
            {
                diagnostic = PoolingErrors.InstanceDestroyed(
                    Id,
                    record.InstanceSequence,
                    false);
                if (ContainsRecord(record))
                {
                    ScheduleDestroy(record);
                }

                return false;
            }

            if (!resetSucceeded)
            {
                resetFailureCount++;
                lastDiagnostic = diagnostic;
                ScheduleDestroy(record);
                Record(PoolDiagnosticEventKind.LifecycleFailed, record, diagnostic);
                return false;
            }

            ReparentForRetention(record);
            if (State == PoolEntryState.Closing || State == PoolEntryState.Closed)
            {
                ScheduleDestroy(record);
            }
            else
            {
                adapter.Release(record);
            }

            diagnostic = CoCoDiagnostic.None;
            Record(PoolDiagnosticEventKind.ReturnSucceeded, record, diagnostic);
            return true;
        }

        private bool ResetTemporalRecord(
            PoolInstanceRecord record,
            PooledInstanceState expectedState,
            uint expectedGeneration,
            PoolReturnReason reason,
            out CoCoDiagnostic diagnostic)
        {
            if (record.GameObject != null)
            {
                record.GameObject.SetActive(false);
            }

            if (!IsExpectedTemporalResetState(
                    record,
                    expectedState,
                    expectedGeneration))
            {
                diagnostic = PoolingErrors.TemporalUnavailable(
                    "Temporal authority changed during deactivation.");
                lastDiagnostic = diagnostic;
                return false;
            }

            bool reset = TryRunReturnCallbacks(
                record,
                record.Participants.Length,
                reason,
                true,
                expectedGeneration,
                out diagnostic);
            if (!reset)
            {
                resetFailureCount++;
                lastDiagnostic = diagnostic;
                return false;
            }

            if (!IsExpectedTemporalResetState(
                    record,
                    expectedState,
                    expectedGeneration))
            {
                diagnostic = PoolingErrors.TemporalUnavailable(
                    "Temporal authority changed during reset.");
                lastDiagnostic = diagnostic;
                return false;
            }

            ReparentForRetention(record);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool IsExpectedTemporalResetState(
            PoolInstanceRecord record,
            PooledInstanceState expectedState,
            uint expectedGeneration)
        {
            return ContainsRecord(record) &&
                   record.GameObject != null &&
                   record.State == expectedState &&
                   record.Generation == expectedGeneration;
        }

        private void BestEffortForceReset(
            PoolInstanceRecord record,
            bool temporal,
            uint contextGeneration)
        {
            CoCoDiagnostic failure = CoCoDiagnostic.None;
            if (record.GameObject != null)
            {
                try
                {
                    record.GameObject.SetActive(false);
                }
                catch (Exception exception)
                {
                    failure = PoolingErrors.ResetFailed(
                        Id,
                        record.InstanceSequence,
                        exception.Message);
                }
            }

            if (ContainsRecord(record) && record.GameObject != null)
            {
                bool reset = TryRunReturnCallbacks(
                    record,
                    record.Participants.Length,
                    PoolReturnReason.ForcedShutdown,
                    temporal,
                    contextGeneration,
                    out CoCoDiagnostic callbackFailure);
                if (!reset && failure.IsNone)
                {
                    failure = callbackFailure;
                }
            }
            else if (failure.IsNone)
            {
                failure = PoolingErrors.InstanceDestroyed(
                    Id,
                    record.InstanceSequence,
                    false);
            }

            if (failure.IsNone)
            {
                return;
            }

            resetFailureCount++;
            lastDiagnostic = failure;
            Record(PoolDiagnosticEventKind.LifecycleFailed, record, failure);
        }

        private bool TryRunRentCallbacks(
            PoolInstanceRecord record,
            bool temporal,
            out int completedCount,
            out CoCoDiagnostic diagnostic)
        {
            completedCount = 0;
            diagnostic = CoCoDiagnostic.None;
            lifecycleCallbackActive = true;
            try
            {
                var context = new PoolRentContext(
                    Id,
                    scope.OwnerId,
                    scope.ScopeSequence,
                    record.InstanceSequence,
                    record.Generation,
                    temporal);
                for (int index = 0; index < record.Participants.Length; index++)
                {
                    IPoolable participant = record.Participants[index];
                    try
                    {
                        if (!participant.TryOnRent(context, out CoCoDiagnostic current))
                        {
                            diagnostic = current.IsNone
                                ? PoolingErrors.ActivationFailed(
                                    Id,
                                    record.InstanceSequence,
                                    "An IPoolable participant rejected rent.")
                                : current;
                            return false;
                        }
                    }
                    catch (Exception exception)
                    {
                        diagnostic = PoolingErrors.ActivationFailed(
                            Id,
                            record.InstanceSequence,
                            exception.Message);
                        return false;
                    }

                    completedCount++;
                }

                return true;
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        private bool TryRunReturnCallbacks(
            PoolInstanceRecord record,
            int participantCount,
            PoolReturnReason reason,
            bool temporal,
            uint contextGeneration,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            bool succeeded = true;
            lifecycleCallbackActive = true;
            try
            {
                var context = new PoolReturnContext(
                    Id,
                    scope.OwnerId,
                    scope.ScopeSequence,
                    record.InstanceSequence,
                    contextGeneration,
                    reason,
                    temporal);
                int end = Math.Min(participantCount, record.Participants.Length);
                for (int index = end - 1; index >= 0; index--)
                {
                    IPoolable participant = record.Participants[index];
                    try
                    {
                        if (participant.TryOnReturn(
                                context,
                                out CoCoDiagnostic current))
                        {
                            continue;
                        }

                        if (diagnostic.IsNone)
                        {
                            diagnostic = current.IsNone
                                ? PoolingErrors.ResetFailed(
                                    Id,
                                    record.InstanceSequence,
                                    "An IPoolable participant rejected return.")
                                : current;
                        }

                        succeeded = false;
                    }
                    catch (Exception exception)
                    {
                        if (diagnostic.IsNone)
                        {
                            diagnostic = PoolingErrors.ResetFailed(
                                Id,
                                record.InstanceSequence,
                                exception.Message);
                        }

                        succeeded = false;
                    }
                }
            }
            finally
            {
                lifecycleCallbackActive = false;
            }

            return succeeded;
        }

        private bool TryValidateHandle(
            in PooledHandle handle,
            out PoolInstanceRecord record,
            out CoCoDiagnostic diagnostic)
        {
            record = null;
            if (!handle.IsValid)
            {
                diagnostic = PoolingErrors.InvalidHandle();
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            if (!ReferenceEquals(handle.Scope, scope) ||
                handle.ScopeSequence != scope.ScopeSequence ||
                !handle.PoolId.Equals(Id))
            {
                diagnostic = PoolingErrors.OwnerMismatch(
                    handle.PoolId,
                    scope.ScopeSequence);
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            if (!records.TryGetValue(handle.InstanceSequence, out record))
            {
                diagnostic = PoolingErrors.StaleHandle(
                    handle.PoolId,
                    handle.InstanceSequence);
                Record(PoolDiagnosticEventKind.HandleRejected, null, diagnostic);
                return false;
            }

            if (record.Generation != handle.Generation)
            {
                diagnostic = record.LastReturnedGeneration == handle.Generation
                    ? PoolingErrors.AlreadyReturned(Id, record.InstanceSequence)
                    : PoolingErrors.StaleHandle(Id, record.InstanceSequence);
                Record(PoolDiagnosticEventKind.HandleRejected, record, diagnostic);
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateTemporalToken(
            in PoolTemporalToken token,
            out PoolInstanceRecord record,
            out CoCoDiagnostic diagnostic)
        {
            record = null;
            if (!token.IsValid ||
                !ReferenceEquals(token.Scope, scope) ||
                !token.PoolId.Equals(Id) ||
                !records.TryGetValue(token.InstanceSequence, out record) ||
                record.Generation != token.Generation)
            {
                diagnostic = PoolingErrors.TemporalUnavailable(
                    "The internal authority token is invalid or stale.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void ReparentForRetention(PoolInstanceRecord record)
        {
            if (record.GameObject == null) return;

            record.GameObject.transform.SetParent(scope.RetentionRoot, false);
            record.GameObject.SetActive(false);
        }

        private void ScheduleDestroy(PoolInstanceRecord record)
        {
            if (!ContainsRecord(record) ||
                record.State == PooledInstanceState.DestroyPending ||
                record.State == PooledInstanceState.Destroyed)
            {
                return;
            }

            GameObject instance = record.GameObject;
            bool externalDestroyAlreadyScheduled =
                !ReferenceEquals(instance, null) &&
                instance == null &&
                !record.ExpectedDestroy;
            if (!externalDestroyAlreadyScheduled)
            {
                record.ExpectedDestroy = true;
            }

            Transition(record, PooledInstanceState.DestroyPending);
            if (ReferenceEquals(instance, null))
            {
                FinalizeDestroyedRecord(record, true);
                return;
            }

            DestroyUnityObject(instance);
            if (Application.isPlaying)
            {
                EnsureDestroyObservation(record);
            }
            else if (ContainsRecord(record) && record.GameObject == null)
            {
                FinalizeObservedDestroyedRecord(record);
            }
        }

        private void EnsureDestroyObservation(PoolInstanceRecord record)
        {
            if (!Application.isPlaying ||
                !ContainsRecord(record) ||
                record.DestroyObservationScheduled)
            {
                return;
            }

            record.DestroyObservationScheduled = true;
            ObserveDestroyCompletionAsync(record).Forget();
        }

        private async UniTask ObserveDestroyCompletionAsync(
            PoolInstanceRecord record)
        {
            // A sentinel on a GameObject that was never active does not receive
            // OnDestroy. Wait at least one frame so Object.Destroy has crossed
            // Unity's physical destruction boundary, then reconcile that path.
            do
            {
                await UniTask.NextFrame();
                await UniTask.SwitchToMainThread();
            }
            while (ContainsRecord(record) && record.GameObject != null);

            if (ContainsRecord(record))
            {
                FinalizeObservedDestroyedRecord(record);
            }
        }

        private void FinalizeObservedDestroyedRecord(
            PoolInstanceRecord record)
        {
            bool expected = record.ExpectedDestroy;
            bool wasIdle = record.State == PooledInstanceState.Inactive;
            FinalizeDestroyedRecord(record, expected);
            if (!expected && wasIdle)
            {
                adapter?.Clear();
            }
        }

        private void FinalizeDestroyedRecord(
            PoolInstanceRecord record,
            bool expected)
        {
            if (!ContainsRecord(record)) return;

            if (!expected)
            {
                externalDestroyCount++;
                AdvanceGeneration(record);
            }

            Transition(record, PooledInstanceState.Destroyed);
            records.Remove(record.InstanceSequence);
            record.GameObject = null;
            record.TemporalActivationParent = null;
            destroyedCount++;
            CoCoDiagnostic diagnostic = PoolingErrors.InstanceDestroyed(
                Id,
                record.InstanceSequence,
                expected);
            if (!expected) lastDiagnostic = diagnostic;
            Record(
                expected
                    ? PoolDiagnosticEventKind.InstanceDestroyed
                    : PoolDiagnosticEventKind.ExternalDestroy,
                record,
                diagnostic);
            TryFinalizeClose();
        }

        private void Transition(
            PoolInstanceRecord record,
            PooledInstanceState next)
        {
            DecrementStateCount(record.State);
            record.State = next;
            IncrementStateCount(next);
        }

        private void DecrementStateCount(PooledInstanceState state)
        {
            switch (state)
            {
                case PooledInstanceState.Inactive:
                    if (inactiveCount > 0) inactiveCount--;
                    break;
                case PooledInstanceState.LeasedInactive:
                case PooledInstanceState.Active:
                case PooledInstanceState.Returning:
                    if (activeCount > 0) activeCount--;
                    break;
                case PooledInstanceState.TemporalInactive:
                case PooledInstanceState.TemporalActive:
                    if (temporalRetainedCount > 0) temporalRetainedCount--;
                    break;
                case PooledInstanceState.TemporalQuarantined:
                    if (quarantineCount > 0) quarantineCount--;
                    break;
                case PooledInstanceState.DestroyPending:
                    if (pendingDestroyCount > 0) pendingDestroyCount--;
                    break;
            }
        }

        private void IncrementStateCount(PooledInstanceState state)
        {
            switch (state)
            {
                case PooledInstanceState.Inactive:
                    inactiveCount++;
                    break;
                case PooledInstanceState.LeasedInactive:
                case PooledInstanceState.Active:
                case PooledInstanceState.Returning:
                    activeCount++;
                    break;
                case PooledInstanceState.TemporalInactive:
                case PooledInstanceState.TemporalActive:
                    temporalRetainedCount++;
                    break;
                case PooledInstanceState.TemporalQuarantined:
                    quarantineCount++;
                    break;
                case PooledInstanceState.DestroyPending:
                    pendingDestroyCount++;
                    break;
            }
        }

        private static void AdvanceGeneration(PoolInstanceRecord record)
        {
            unchecked
            {
                record.Generation++;
                if (record.Generation == 0) record.Generation++;
            }
        }

        private void TryFinalizeClose()
        {
            if (State != PoolEntryState.Closing ||
                records.Count != 0 ||
                prepareInFlight ||
                prewarmInFlight)
            {
                return;
            }

            adapter?.Dispose();
            adapter = null;
            sourceLease?.Dispose();
            sourceLease = null;
            operationCancellation?.Dispose();
            operationCancellation = null;
            State = PoolEntryState.Closed;
            NotifyTerminal();
        }

        private void NotifyTerminal()
        {
            if (terminalNotified) return;

            terminalNotified = true;
            scope.OnEntryClosed(this);
        }

        private async UniTask AwaitNoRecordsAsync()
        {
            while (records.Count != 0)
            {
                await UniTask.Yield();
                await UniTask.SwitchToMainThread();
            }
        }

        private PoolInstanceRecord[] CaptureRecords()
        {
            var snapshot = new PoolInstanceRecord[records.Count];
            records.Values.CopyTo(snapshot, 0);
            return snapshot;
        }

        private void Record(
            PoolDiagnosticEventKind eventKind,
            PoolInstanceRecord record,
            CoCoDiagnostic diagnostic,
            string releaseStack = null)
        {
            ledger.Record(
                eventKind,
                scope.OwnerId,
                scope.ScopeSequence,
                Id,
                record?.InstanceSequence ?? 0,
                record?.Generation ?? 0,
                record?.State ?? PooledInstanceState.Internal,
                activeCount,
                inactiveCount,
                quarantineCount,
                diagnostic,
                record?.AllocationStack,
                releaseStack);
        }

        private bool TryEnterInstanceMutation(out CoCoDiagnostic diagnostic)
        {
            if (lifecycleCallbackActive || instanceMutationActive)
            {
                diagnostic = PoolingErrors.CallbackReentry(Id);
                return false;
            }

            instanceMutationActive = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void ExitInstanceMutation()
        {
            instanceMutationActive = false;
            if (forceDrainPending)
            {
                DrainForceClose();
            }
            else if (closeDrainPending)
            {
                DrainNormalClose();
            }
        }

        private static async UniTask<T> AwaitSharedTaskAsync<T>(Task<T> task)
        {
            return await task;
        }

        private static void DestroyUnityObject(GameObject instance)
        {
            if (instance == null) return;

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(instance);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }
    }
}
