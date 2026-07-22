using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal enum CoCoClaimRestoreOverlayPolicy
    {
        PreservePendingRelease = 0,
        RebuildForSuspended = 1,
        DiscardAbandonedFuture = 2
    }

    internal sealed class CoCoStateGraphOperatorRuntime : ICoCoOperatorOutcomeSink, IDisposable
    {
        private readonly OperatorBinding[] _operators;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoContextFrameLayout _contextLayout;
        private readonly ClaimResource[] _claimResources;
        private readonly int[] _committedClaimOwners;
        private readonly CoCoActivationId[] _committedClaimActivations;
        private readonly int[] _candidateClaimOwners;
        private readonly CoCoActivationId[] _candidateClaimActivations;
        private readonly int[] _preparedRestoreClaimOwners;
        private readonly CoCoActivationId[] _preparedRestoreClaimActivations;
        private readonly bool[] _retainedClaimOwners;
        private CoCoPreparedContextCommit _preparedContext;
        private ulong _activeToken;
        private int _activeOperatorIndex = -1;
        private int _currentOutcomeWriteCount;
        private bool _outcomeWriteFault;
        private bool _worldMayBeDirty;
        private bool _releaseClaimsOnNextArbitration;
        private bool _preparedReleaseClaimsOnNextArbitration;
        private ulong _preparedRestoreToken;
        private bool _isDisposed;

        private CoCoStateGraphOperatorRuntime(
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            OperatorBinding[] operators,
            ClaimResource[] claimResources)
        {
            _graphInstanceId = graphInstanceId;
            _contextLayout = contextLayout;
            _operators = operators;
            _claimResources = claimResources;
            _committedClaimOwners = new int[claimResources.Length];
            _committedClaimActivations = new CoCoActivationId[claimResources.Length];
            _candidateClaimOwners = new int[claimResources.Length];
            _candidateClaimActivations = new CoCoActivationId[claimResources.Length];
            _preparedRestoreClaimOwners = new int[claimResources.Length];
            _preparedRestoreClaimActivations = new CoCoActivationId[claimResources.Length];
            _retainedClaimOwners = new bool[operators.Length];
            ClearOwners(_committedClaimOwners);
            ClearOwners(_candidateClaimOwners);
            ClearOwners(_preparedRestoreClaimOwners);
        }

        internal int Count => _operators.Length;
        internal bool WorldMayBeDirty => _worldMayBeDirty;

        internal bool TryCaptureCommittedClaims(
            out CoCoOperatorClaimState[] claims,
            out CoCoDiagnostic diagnostic)
        {
            claims = null;
            diagnostic = CoCoDiagnostic.None;
            if (_isDisposed || _activeToken != 0UL || _preparedRestoreToken != 0UL ||
                !TryValidateCommittedClaims(out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = LifecycleError(
                        "Committed Claim debugging requires one idle Operator boundary.");
                }

                return false;
            }

            claims = new CoCoOperatorClaimState[_claimResources.Length];
            for (int index = 0; index < claims.Length; index++)
            {
                ClaimResource resource = _claimResources[index];
                int ownerIndex = _committedClaimOwners[index];
                if (ownerIndex < 0)
                {
                    claims[index] = CoCoOperatorClaimState.Unheld(
                        resource.ClaimId,
                        resource.Section.SectionId);
                    continue;
                }

                if (ownerIndex >= _operators.Length ||
                    !CoCoOperatorClaimState.TryCreateHeld(
                        resource.ClaimId,
                        resource.Section.SectionId,
                        _operators[ownerIndex].Descriptor.OperatorId,
                        _committedClaimActivations[index],
                        out claims[index]))
                {
                    claims = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "Committed Claim debugging found inconsistent canonical ownership.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCreateOutboxLanes(
            int ledgerCapacity,
            out ICoCoEventOutboxLane[] lanes,
            out CoCoDiagnostic diagnostic)
        {
            var requirements = new List<CoCoEventOutboxRequirement>();
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                IReadOnlyList<CoCoEventOutboxRequirement> emits =
                    _operators[operatorIndex].Descriptor.Emits;
                for (int emitIndex = 0; emitIndex < emits.Count; emitIndex++)
                {
                    CoCoEventOutboxRequirement requirement = emits[emitIndex];
                    int existingIndex = -1;
                    for (int laneIndex = 0; laneIndex < requirements.Count; laneIndex++)
                    {
                        CoCoEventOutboxRequirement existing = requirements[laneIndex];
                        if (existing.EventTypeId != requirement.EventTypeId)
                        {
                            continue;
                        }

                        if (existing != requirement)
                        {
                            lanes = null;
                            diagnostic = Error(
                                CoCoDiagnosticCode.InvalidOperatorDescriptor,
                                "One EventType cannot use conflicting Outbox domain, payload, or capacity declarations.");
                            return false;
                        }

                        existingIndex = laneIndex;
                        break;
                    }

                    if (existingIndex < 0)
                    {
                        requirements.Add(requirement);
                    }
                }
            }

            int requiredLedgerCapacity = 0;
            for (int index = 0; index < requirements.Count; index++)
            {
                int laneCapacity = requirements[index].Capacity;
                if (laneCapacity > int.MaxValue - requiredLedgerCapacity)
                {
                    lanes = null;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.EventOutbox,
                        CoCoDiagnosticCode.EventOutboxOverflow,
                        "Declared EventOutbox lane capacities overflow the Host ledger limit.");
                    return false;
                }

                requiredLedgerCapacity += laneCapacity;
            }

            if (ledgerCapacity < requiredLedgerCapacity)
            {
                lanes = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.EventOutbox,
                    CoCoDiagnosticCode.EventOutboxOverflow,
                    "Host EventOutbox capacity must reserve every declared typed lane before callbacks begin.");
                return false;
            }

            lanes = new ICoCoEventOutboxLane[requirements.Count];
            for (int index = 0; index < lanes.Length; index++)
            {
                lanes[index] = requirements[index].CreateLane();
                if (lanes[index] == null)
                {
                    lanes = null;
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.EventOutbox,
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "An AOT EventOutbox lane factory could not materialize its declared payload.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryCreate(
            CoCoStateGraphHost host,
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoContextFrameLayout contextLayout,
            CoCoOperationSectionRegistry operationRegistry,
            IReadOnlyList<MonoBehaviour> components,
            IReadOnlyList<CoCoStateSlotId> claimStateSlots,
            out CoCoStateGraphOperatorRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            if (host == null || graph == null || !graphInstanceId.IsValid ||
                contextLayout == null || operationRegistry == null || components == null ||
                claimStateSlots == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperatorDescriptor,
                    "Operator setup requires one Host, compiled Graph, Context layout, and explicit component list.");
                return false;
            }

            var bindings = new OperatorBinding[components.Count];
            var operatorIds = new HashSet<CoCoOperatorId>();
            var componentIds = new HashSet<int>();
            var ownedOutcomes = new HashSet<CoCoStateSlotId>();
            var resources = new List<ClaimResourceBuilder>();
            var sectionClaims = new Dictionary<CoCoOperationSectionId, CoCoOperatorClaimId>();
            var slotClaims = new Dictionary<CoCoStateSlotId, CoCoOperatorClaimId>();
            for (int operatorIndex = 0; operatorIndex < components.Count; operatorIndex++)
            {
                MonoBehaviour component = components[operatorIndex];
                if (component == null ||
                    !componentIds.Add(component.GetInstanceID()) ||
                    !(component is ICoCoOperator executable) ||
                    !CoCoStateGraphHostBoundary.Contains(host, component))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "Every Host Operator entry must be one unique live component inside that Host boundary.");
                    return false;
                }

                CoCoOperatorDescriptor descriptor;
                try
                {
                    descriptor = executable.Descriptor;
                }
                catch (Exception)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "An Operator descriptor getter threw during Host setup.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;

                if (descriptor == null ||
                    !descriptor.IsValid ||
                    descriptor.OperatorType != component.GetType() ||
                    !operatorIds.Add(descriptor.OperatorId) ||
                    !TryValidateRequirements(graph, descriptor, out diagnostic) ||
                    !TryResolveOutcomes(
                        contextLayout,
                        descriptor,
                        ownedOutcomes,
                        out OutcomeBinding[] outcomes,
                        out diagnostic) ||
                    !TryResolveClaims(
                        descriptor,
                        operatorIndex,
                        contextLayout,
                        operationRegistry,
                        resources,
                        sectionClaims,
                        slotClaims,
                        out int[] claimResourceIndices,
                        out int[] claimSectionIndices,
                        out diagnostic))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidOperatorDescriptor,
                            "Operator descriptors and ids must be immutable, exact, and unique per Host.");
                    }

                    return false;
                }

                bindings[operatorIndex] = new OperatorBinding(
                    component,
                    executable,
                    descriptor,
                    outcomes,
                    claimResourceIndices,
                    claimSectionIndices);
            }

            if (!TryValidateCoverage(graph, bindings, out diagnostic) ||
                !TryValidateOutcomeCoverage(contextLayout, ownedOutcomes, out diagnostic) ||
                !TryValidateClaimCoverage(claimStateSlots, resources, out diagnostic))
            {
                return false;
            }

            var frozenResources = new ClaimResource[resources.Count];
            for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
            {
                frozenResources[resourceIndex] = resources[resourceIndex].Freeze();
            }

            runtime = new CoCoStateGraphOperatorRuntime(
                graphInstanceId,
                contextLayout,
                bindings,
                frozenResources);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryExecute(
            in CoCoStagedGraphStep stagedStep,
            in CoCoContextFrameReadView previousContext,
            in CoCoPreparedContextCommit preparedContext,
            ICoCoEventOutboxSink eventOutbox,
            ulong token,
            ICoCoStateGraphCommitGuard commitGuard,
            CoCoStateFlowTraceBuffer trace,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _activeToken != 0UL ||
                token == 0UL ||
                !stagedStep.IsValid ||
                !previousContext.IsValid ||
                !preparedContext.IsValid ||
                eventOutbox == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.OperatorExecutionFailed,
                    "Operator execution requires one fresh staged transaction.");
                return false;
            }

            _activeToken = token;
            _preparedContext = preparedContext;
            _worldMayBeDirty = false;
            _outcomeWriteFault = false;
            CoCoStateFlowTraceFrameReference previousReference = default;
            if (trace != null &&
                !CoCoStateFlowTraceFrameReference.TryCreate(
                    previousContext,
                    out previousReference))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.OperatorExecutionFailed,
                    "Operator trace could not snapshot the previous Context identity.");
                ClearActiveTransaction();
                return false;
            }

            if (!TryArbitrateClaims(stagedStep.FinalizedOperationFrame, out diagnostic))
            {
                ClearActiveTransaction();
                return false;
            }

            if (!TryWriteClaimCandidates(out diagnostic))
            {
                ClearActiveTransaction();
                return false;
            }

            // Materialize every normal loser before the first real callback. A later
            // callback failure must not prevent ClaimDenied from becoming observable.
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (binding.Eligible)
                {
                    continue;
                }

                CoCoOperatorOutcome denied = CoCoOperatorOutcome.Denied(Error(
                    CoCoDiagnosticCode.OperatorClaimConflict,
                    "Operator execution was skipped because it did not win all requested Claims."));
                trace?.Append(CoCoStateFlowTraceEntry.Operator(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    binding.Descriptor.OperatorId,
                    denied.Status,
                    previousReference));
            }

            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (!binding.Eligible)
                {
                    continue;
                }

                if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                {
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled Operator execution before the next callback.");
                    ClearActiveTransaction();
                    return false;
                }

                CoCoOperatorOutcome outcome;

                if (binding.Component == null)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        "An explicit Operator component was destroyed after Host startup.");
                    ClearActiveTransaction();
                    return false;
                }

                _activeOperatorIndex = operatorIndex;
                _currentOutcomeWriteCount = 0;
                int outboxCountBefore = eventOutbox is CoCoStateGraphTransaction transaction
                    ? transaction.CandidateEventCount
                    : 0;
                _worldMayBeDirty = true;
                bool executed;
                try
                {
                    var context = new CoCoOperatorExecutionContext(
                        binding.Descriptor,
                        stagedStep.TickFrame,
                        previousContext,
                        stagedStep.FinalizedOperationFrame,
                        this,
                        new CoCoEventOutboxWriter(binding.Descriptor, eventOutbox, token),
                        token);
                    executed = binding.Operator.TryExecute(context, out outcome);
                }
                catch (Exception)
                {
                    executed = false;
                    outcome = default;
                }
                finally
                {
                    _activeOperatorIndex = -1;
                }

                int eventWrites = eventOutbox is CoCoStateGraphTransaction outboxTransaction
                    ? outboxTransaction.CandidateEventCount - outboxCountBefore
                    : 0;
                if (!executed ||
                    !outcome.IsValid ||
                    _outcomeWriteFault ||
                    (eventOutbox is CoCoStateGraphTransaction failedOutbox &&
                     failedOutbox.HasOutboxFailure) ||
                    outcome.Status == CoCoOperatorOutcomeStatus.ClaimDenied ||
                    ((outcome.Status == CoCoOperatorOutcomeStatus.NoOp ||
                      outcome.Status == CoCoOperatorOutcomeStatus.Rejected) &&
                     (_currentOutcomeWriteCount != 0 || eventWrites != 0)))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorExecutionFailed,
                        "Operator execution failed, returned an invalid Outcome, or left writes behind for a non-writing Outcome.");
                    ClearActiveTransaction();
                    return false;
                }

                trace?.Append(CoCoStateFlowTraceEntry.Operator(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    binding.Descriptor.OperatorId,
                    outcome.Status,
                    previousReference));
                if (outcome.Status == CoCoOperatorOutcomeStatus.Rejected)
                {
                    diagnostic = outcome.Diagnostic.IsError
                        ? outcome.Diagnostic
                        : Error(
                            CoCoDiagnosticCode.OperatorExecutionFailed,
                            "Operator rejected the candidate transaction.");
                    ClearActiveTransaction();
                    return false;
                }

                if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                {
                    diagnostic = LifecycleError(
                        "Unity destruction cancelled Operator execution after a callback.");
                    ClearActiveTransaction();
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool IsOperatorActive(ulong token, CoCoOperatorId operatorId)
        {
            return !_isDisposed &&
                   token != 0UL &&
                   token == _activeToken &&
                   _activeOperatorIndex >= 0 &&
                   _activeOperatorIndex < _operators.Length &&
                   _operators[_activeOperatorIndex].Descriptor.OperatorId == operatorId;
        }

        bool ICoCoOperatorOutcomeSink.IsActive(ulong token, CoCoOperatorId operatorId) =>
            IsOperatorActive(token, operatorId);

        void ICoCoOperatorOutcomeSink.RejectWrite(
            ulong token,
            CoCoOperatorId operatorId)
        {
            if (_activeToken != 0UL)
            {
                _outcomeWriteFault = true;
            }
        }

        bool ICoCoOperatorOutcomeSink.TryWrite<TValue>(
            ulong token,
            CoCoOperatorId operatorId,
            CoCoStateSlotId slotId,
            in TValue value)
        {
            if (!IsOperatorActive(token, operatorId) ||
                !_contextLayout.TryResolveSlot(
                    slotId,
                    out CoCoStateSlot<TValue> slot))
            {
                return false;
            }

            OutcomeBinding[] outcomes = _operators[_activeOperatorIndex].Outcomes;
            for (int index = 0; index < outcomes.Length; index++)
            {
                OutcomeBinding outcome = outcomes[index];
                if (outcome.SlotId != slotId || outcome.ValueType != typeof(TValue) ||
                    !_preparedContext.TryGetWriter(
                        outcome.Block,
                        out CoCoContextFrameWriter writer) ||
                    !writer.Write(slot, value))
                {
                    continue;
                }

                _currentOutcomeWriteCount++;
                return true;
            }

            return false;
        }

        internal void CommitClaimsNoFail()
        {
            for (int index = 0; index < _claimResources.Length; index++)
            {
                _committedClaimOwners[index] = _candidateClaimOwners[index];
                _committedClaimActivations[index] = _candidateClaimActivations[index];
            }

            _releaseClaimsOnNextArbitration = false;
            ClearActiveTransaction();
        }

        internal void Cancel()
        {
            ClearOwners(_candidateClaimOwners);
            Array.Clear(_candidateClaimActivations, 0, _candidateClaimActivations.Length);
            ClearActiveTransaction();
        }

        internal bool TryValidateRestore(
            in CoCoContextRestoreReadView restore,
            CoCoStateGraphContextRuntime contextRuntime,
            out CoCoDiagnostic diagnostic)
        {
            if (_isDisposed || _activeToken != 0UL || !restore.IsValid ||
                !_contextLayout.IsSameInstance(restore.Layout) || contextRuntime == null)
            {
                diagnostic = RestoreError(
                    "Claim restore validation requires one idle Runtime and an exact post-policy Context view.");
                return false;
            }

            for (int resourceIndex = 0; resourceIndex < _claimResources.Length; resourceIndex++)
            {
                if (!TryReadRestoreClaim(
                        restore,
                        contextRuntime,
                        resourceIndex,
                        out _,
                        out _,
                        out diagnostic))
                {
                    return false;
                }
            }

            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                bool ownsAny = false;
                bool ownsAll = true;
                for (int claimIndex = 0;
                     claimIndex < binding.ClaimResourceIndices.Length;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    if (!TryReadRestoreClaim(
                            restore,
                            contextRuntime,
                            resourceIndex,
                            out int owner,
                            out _,
                            out diagnostic))
                    {
                        return false;
                    }

                    ownsAny |= owner == operatorIndex;
                    ownsAll &= owner == operatorIndex;
                }

                if (ownsAny && !ownsAll)
                {
                    diagnostic = RestoreError(
                        "Restored Claim ownership violates an Operator's all-or-none descriptor.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareRestore(
            in CoCoContextRestoreReadView restore,
            CoCoStateGraphContextRuntime contextRuntime,
            ulong token,
            CoCoClaimRestoreOverlayPolicy overlayPolicy,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (token == 0UL || _preparedRestoreToken != 0UL ||
                !TryValidateRestore(restore, contextRuntime, out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = RestoreError("Claim restore preparation requires one fresh token.");
                }

                return false;
            }

            for (int resourceIndex = 0; resourceIndex < _claimResources.Length; resourceIndex++)
            {
                if (!TryReadRestoreClaim(
                        restore,
                        contextRuntime,
                        resourceIndex,
                        out _preparedRestoreClaimOwners[resourceIndex],
                        out _preparedRestoreClaimActivations[resourceIndex],
                        out diagnostic))
                {
                    CancelPreparedRestore();
                    return false;
                }
            }

            switch (overlayPolicy)
            {
                case CoCoClaimRestoreOverlayPolicy.PreservePendingRelease:
                    _preparedReleaseClaimsOnNextArbitration =
                        _releaseClaimsOnNextArbitration;
                    break;
                case CoCoClaimRestoreOverlayPolicy.RebuildForSuspended:
                    _preparedReleaseClaimsOnNextArbitration =
                        HasPreparedReleaseClaimOwner();
                    break;
                case CoCoClaimRestoreOverlayPolicy.DiscardAbandonedFuture:
                    _preparedReleaseClaimsOnNextArbitration = false;
                    break;
                default:
                    CancelPreparedRestore();
                    diagnostic = RestoreError(
                        "Claim restore preparation received an invalid overlay policy.");
                    return false;
            }

            _preparedRestoreToken = token;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void CommitPreparedRestoreNoFail(ulong token)
        {
            if (token == 0UL || token != _preparedRestoreToken)
            {
                return;
            }

            for (int index = 0; index < _claimResources.Length; index++)
            {
                _committedClaimOwners[index] = _preparedRestoreClaimOwners[index];
                _committedClaimActivations[index] = _preparedRestoreClaimActivations[index];
            }

            _releaseClaimsOnNextArbitration = _preparedReleaseClaimsOnNextArbitration;
            CancelPreparedRestore();
        }

        internal bool IsPreparedRestoreTokenCurrent(ulong token) =>
            !_isDisposed &&
            token != 0UL &&
            token == _preparedRestoreToken;

        internal void CancelPreparedRestore()
        {
            _preparedRestoreToken = 0UL;
            _preparedReleaseClaimsOnNextArbitration = false;
            ClearOwners(_preparedRestoreClaimOwners);
            Array.Clear(
                _preparedRestoreClaimActivations,
                0,
                _preparedRestoreClaimActivations.Length);
        }

        internal void Suspend()
        {
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    if (_committedClaimOwners[resourceIndex] == operatorIndex &&
                        binding.Descriptor.Claims[claimIndex].SuspendPolicy ==
                        CoCoOperatorClaimSuspendPolicy.Release)
                    {
                        // ContextFrame remains the sole committed Claim authority while
                        // Suspended. Apply the release as a candidate overlay on the next
                        // arbitration, then persist it through the normal composite commit.
                        _releaseClaimsOnNextArbitration = true;
                        return;
                    }
                }
            }
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            Cancel();
            CancelPreparedRestore();
            ClearOwners(_committedClaimOwners);
            Array.Clear(_committedClaimActivations, 0, _committedClaimActivations.Length);
            _releaseClaimsOnNextArbitration = false;
            _isDisposed = true;
        }

        private bool TryArbitrateClaims(
            in CoCoFinalizedOperationFrame operationFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateCommittedClaims(out diagnostic))
            {
                return false;
            }

            Array.Clear(_retainedClaimOwners, 0, _retainedClaimOwners.Length);
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                binding.Eligible = true;
                for (int claimIndex = 0; claimIndex < binding.Descriptor.Claims.Count; claimIndex++)
                {
                    CoCoOperatorClaimRequirement claim = binding.Descriptor.Claims[claimIndex];
                    if (!operationFrame.TryGetHeader(
                            binding.ClaimSectionIndices[claimIndex],
                            out CoCoOperationSectionEntryHeader header))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidOperatorDescriptor,
                            "Claim arbitration could not resolve its finalized Operation Section.");
                        return false;
                    }

                    binding.ClaimActivations[claimIndex] = header.ActivationId;
                    if (!header.Enabled || !header.ActivationId.IsValid)
                    {
                        binding.Eligible = false;
                    }
                }
            }

            // Preserve a committed owner while every member of its all-or-none
            // Claim set still addresses the same enabled Activation. Any changed
            // or exited Activation releases the complete set before arbitration.
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                if (binding.Descriptor.Claims.Count == 0 ||
                    _committedClaimOwners[binding.ClaimResourceIndices[0]] != operatorIndex ||
                    _releaseClaimsOnNextArbitration && ShouldReleaseOnSuspend(binding))
                {
                    continue;
                }

                bool retain = binding.Eligible;
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count && retain;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    retain = _committedClaimOwners[resourceIndex] == operatorIndex &&
                             _committedClaimActivations[resourceIndex] ==
                             binding.ClaimActivations[claimIndex];
                }

                _retainedClaimOwners[operatorIndex] = retain;
            }

            bool changed;
            do
            {
                ClearOwners(_candidateClaimOwners);
                Array.Clear(_candidateClaimActivations, 0, _candidateClaimActivations.Length);
                for (int operatorIndex = 0;
                     operatorIndex < _operators.Length;
                     operatorIndex++)
                {
                    if (!_retainedClaimOwners[operatorIndex])
                    {
                        continue;
                    }

                    OperatorBinding retained = _operators[operatorIndex];
                    for (int claimIndex = 0;
                         claimIndex < retained.Descriptor.Claims.Count;
                         claimIndex++)
                    {
                        int resourceIndex = retained.ClaimResourceIndices[claimIndex];
                        _candidateClaimOwners[resourceIndex] = operatorIndex;
                        _candidateClaimActivations[resourceIndex] =
                            retained.ClaimActivations[claimIndex];
                    }
                }

                for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
                {
                    OperatorBinding binding = _operators[operatorIndex];
                    if (!binding.Eligible || _retainedClaimOwners[operatorIndex])
                    {
                        continue;
                    }

                    for (int claimIndex = 0;
                         claimIndex < binding.Descriptor.Claims.Count;
                         claimIndex++)
                    {
                        int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                        int current = _candidateClaimOwners[resourceIndex];
                        if (current < 0 ||
                            (!_retainedClaimOwners[current] && IsHigherPriority(
                                operatorIndex,
                                claimIndex,
                                current,
                                FindClaimIndex(current, resourceIndex))))
                        {
                            _candidateClaimOwners[resourceIndex] = operatorIndex;
                            _candidateClaimActivations[resourceIndex] =
                                binding.ClaimActivations[claimIndex];
                        }
                    }
                }

                changed = false;
                for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
                {
                    OperatorBinding binding = _operators[operatorIndex];
                    if (!binding.Eligible || binding.Descriptor.Claims.Count == 0)
                    {
                        continue;
                    }

                    for (int claimIndex = 0;
                         claimIndex < binding.Descriptor.Claims.Count;
                         claimIndex++)
                    {
                        if (_candidateClaimOwners[binding.ClaimResourceIndices[claimIndex]] ==
                            operatorIndex)
                        {
                            continue;
                        }

                        binding.Eligible = false;
                        changed = true;
                        break;
                    }
                }
            }
            while (changed);

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryWriteClaimCandidates(out CoCoDiagnostic diagnostic)
        {
            for (int resourceIndex = 0; resourceIndex < _claimResources.Length; resourceIndex++)
            {
                ClaimResource resource = _claimResources[resourceIndex];
                int ownerIndex = _candidateClaimOwners[resourceIndex];
                CoCoOperatorClaimState state;
                if (ownerIndex < 0)
                {
                    state = CoCoOperatorClaimState.Unheld(
                        resource.ClaimId,
                        resource.Section.SectionId);
                }
                else if (ownerIndex >= _operators.Length ||
                         !CoCoOperatorClaimState.TryCreateHeld(
                             resource.ClaimId,
                             resource.Section.SectionId,
                             _operators[ownerIndex].Descriptor.OperatorId,
                             _candidateClaimActivations[resourceIndex],
                             out state))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "Claim arbitration produced an invalid canonical ownership record.");
                    return false;
                }

                if (!_preparedContext.TryGetWriter(
                        resource.Block,
                        out CoCoContextFrameWriter writer) ||
                    !writer.Write(resource.Slot, state))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "Canonical Claim ownership could not be written to its Graph-owned State Slot.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateCommittedClaims(out CoCoDiagnostic diagnostic)
        {
            for (int resourceIndex = 0;
                 resourceIndex < _claimResources.Length;
                 resourceIndex++)
            {
                int owner = _committedClaimOwners[resourceIndex];
                CoCoActivationId activation = _committedClaimActivations[resourceIndex];
                if (owner < 0)
                {
                    if (activation.IsValid)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.OperatorClaimConflict,
                            "A committed Claim resource has an Activation without an owner.");
                        return false;
                    }

                    continue;
                }

                if (owner >= _operators.Length ||
                    !activation.IsValid ||
                    FindClaimIndex(owner, resourceIndex) < 0)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "Committed Claim ownership or Activation identity is inconsistent.");
                    return false;
                }
            }

            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                bool ownsAny = false;
                bool ownsAll = true;
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int owner = _committedClaimOwners[
                        binding.ClaimResourceIndices[claimIndex]];
                    ownsAny |= owner == operatorIndex;
                    ownsAll &= owner == operatorIndex;
                }

                if (ownsAny && !ownsAll)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "A multi-Claim Operator has partial committed ownership.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryReadRestoreClaim(
            in CoCoContextRestoreReadView restore,
            CoCoStateGraphContextRuntime contextRuntime,
            int resourceIndex,
            out int ownerIndex,
            out CoCoActivationId activationId,
            out CoCoDiagnostic diagnostic)
        {
            ownerIndex = -1;
            activationId = default;
            if (resourceIndex < 0 || resourceIndex >= _claimResources.Length)
            {
                diagnostic = RestoreError("Restored Claim resource index is invalid.");
                return false;
            }

            ClaimResource resource = _claimResources[resourceIndex];
            if (!restore.TryRead(resource.Slot, out CoCoOperatorClaimState state) ||
                !state.IsValid ||
                state.ClaimId != resource.ClaimId ||
                state.SectionId != resource.Section.SectionId)
            {
                diagnostic = RestoreError(
                    "Restored Claim identity or Operation Section does not match its descriptor.");
                return false;
            }

            if (!state.IsHeld)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            ownerIndex = FindOperatorIndex(state.OwnerOperatorId);
            activationId = state.ActivationId;
            if (ownerIndex < 0 ||
                FindClaimIndex(ownerIndex, resourceIndex) < 0 ||
                !contextRuntime.IsRestoredActiveActivation(restore, activationId))
            {
                ownerIndex = -1;
                activationId = default;
                diagnostic = RestoreError(
                    "Restored Claim owner or Activation is inconsistent with Graph authority.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool HasPreparedReleaseClaimOwner()
        {
            for (int operatorIndex = 0; operatorIndex < _operators.Length; operatorIndex++)
            {
                OperatorBinding binding = _operators[operatorIndex];
                for (int claimIndex = 0;
                     claimIndex < binding.Descriptor.Claims.Count;
                     claimIndex++)
                {
                    int resourceIndex = binding.ClaimResourceIndices[claimIndex];
                    if (_preparedRestoreClaimOwners[resourceIndex] == operatorIndex &&
                        binding.Descriptor.Claims[claimIndex].SuspendPolicy ==
                        CoCoOperatorClaimSuspendPolicy.Release)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private int FindOperatorIndex(CoCoOperatorId operatorId)
        {
            for (int index = 0; index < _operators.Length; index++)
            {
                if (_operators[index].Descriptor.OperatorId == operatorId)
                {
                    return index;
                }
            }

            return -1;
        }

        private bool IsHigherPriority(
            int candidateOperator,
            int candidateClaim,
            int currentOperator,
            int currentClaim)
        {
            CoCoOperatorClaimRequirement candidate =
                _operators[candidateOperator].Descriptor.Claims[candidateClaim];
            CoCoOperatorClaimRequirement current =
                _operators[currentOperator].Descriptor.Claims[currentClaim];
            if (candidate.Priority != current.Priority)
            {
                return candidate.Priority > current.Priority;
            }

            if (candidateOperator != currentOperator)
            {
                return candidateOperator < currentOperator;
            }

            CoCoOperatorId candidateId = _operators[candidateOperator].Descriptor.OperatorId;
            CoCoOperatorId currentId = _operators[currentOperator].Descriptor.OperatorId;
            return candidateId.High < currentId.High ||
                   (candidateId.High == currentId.High && candidateId.Low < currentId.Low);
        }

        private int FindClaimIndex(int operatorIndex, int resourceIndex)
        {
            int[] indices = _operators[operatorIndex].ClaimResourceIndices;
            for (int index = 0; index < indices.Length; index++)
            {
                if (indices[index] == resourceIndex)
                {
                    return index;
                }
            }

            return -1;
        }

        private static bool ShouldReleaseOnSuspend(OperatorBinding binding)
        {
            for (int index = 0; index < binding.Descriptor.Claims.Count; index++)
            {
                if (binding.Descriptor.Claims[index].SuspendPolicy ==
                    CoCoOperatorClaimSuspendPolicy.Release)
                {
                    return true;
                }
            }

            return false;
        }

        private CoCoOperatorClaimRequirement FindClaim(int operatorIndex, int resourceIndex)
        {
            int claimIndex = FindClaimIndex(operatorIndex, resourceIndex);
            return claimIndex >= 0
                ? _operators[operatorIndex].Descriptor.Claims[claimIndex]
                : default;
        }

        private void ClearActiveTransaction()
        {
            _preparedContext = default;
            _activeToken = 0UL;
            _activeOperatorIndex = -1;
            _currentOutcomeWriteCount = 0;
            _outcomeWriteFault = false;
        }

        private static bool TryValidateRequirements(
            CoCoCompiledStateGraph graph,
            CoCoOperatorDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
        {
            for (int requirementIndex = 0;
                 requirementIndex < descriptor.Requires.Count;
                 requirementIndex++)
            {
                CoCoOperationSectionRequirement requirement =
                    descriptor.Requires[requirementIndex];
                bool found = false;
                for (int provideIndex = 0;
                     provideIndex < graph.OperationProvides.Count;
                     provideIndex++)
                {
                    if (Matches(graph.OperationProvides.Provides[provideIndex], requirement))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingOperatorBinding,
                        "Operator Requires must be an exact subset of the compiled Operation Provides manifest.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryValidateCoverage(
            CoCoCompiledStateGraph graph,
            OperatorBinding[] bindings,
            out CoCoDiagnostic diagnostic)
        {
            for (int provideIndex = 0;
                 provideIndex < graph.OperationProvides.Count;
                 provideIndex++)
            {
                CoCoGraphOperationProvideRequirement provided =
                    graph.OperationProvides.Provides[provideIndex];
                bool covered = false;
                for (int operatorIndex = 0; operatorIndex < bindings.Length && !covered; operatorIndex++)
                {
                    IReadOnlyList<CoCoOperationSectionRequirement> requires =
                        bindings[operatorIndex].Descriptor.Requires;
                    for (int requirementIndex = 0;
                         requirementIndex < requires.Count;
                         requirementIndex++)
                    {
                        if (Matches(provided, requires[requirementIndex]))
                        {
                            covered = true;
                            break;
                        }
                    }
                }

                if (!covered)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.MissingOperatorBinding,
                        "Explicit Operators must cover every compiled Operation Provides Section.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryResolveOutcomes(
            CoCoContextFrameLayout layout,
            CoCoOperatorDescriptor descriptor,
            HashSet<CoCoStateSlotId> owned,
            out OutcomeBinding[] outcomes,
            out CoCoDiagnostic diagnostic)
        {
            outcomes = new OutcomeBinding[descriptor.OutcomeCount];
            for (int outcomeIndex = 0; outcomeIndex < descriptor.OutcomeCount; outcomeIndex++)
            {
                CoCoOperatorOutcomeRequirement outcome =
                    descriptor.OutcomeRequirements[outcomeIndex];
                CoCoStateSlotDescriptor slot = FindSlot(layout, outcome.SlotId);
                CoCoStateBlockDescriptor block = slot == null
                    ? null
                    : FindBlock(layout, slot.WriterBlockId);
                if (slot == null ||
                    block == null ||
                    slot.ValueType != outcome.ValueType ||
                    slot.RestorePolicy == CoCoContextRestorePolicy.Derived ||
                    block.Owner != CoCoStateBlockOwner.Operator ||
                    !owned.Add(outcome.SlotId) ||
                    !layout.TryResolveBlock(block.BlockId, out CoCoStateBlockHandle blockHandle))
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OutcomeOwnershipConflict,
                        "Every Operator Outcome must uniquely own one writable Operator StateSlot.");
                    return false;
                }

                outcomes[outcomeIndex] = new OutcomeBinding(
                    outcome.SlotId,
                    outcome.ValueType,
                    blockHandle);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryValidateOutcomeCoverage(
            CoCoContextFrameLayout layout,
            HashSet<CoCoStateSlotId> owned,
            out CoCoDiagnostic diagnostic)
        {
            for (int blockIndex = 0; blockIndex < layout.Blocks.Count; blockIndex++)
            {
                CoCoStateBlockDescriptor block = layout.Blocks[blockIndex];
                if (block.Owner != CoCoStateBlockOwner.Operator)
                {
                    continue;
                }

                for (int slotIndex = 0; slotIndex < block.Slots.Count; slotIndex++)
                {
                    CoCoStateSlotDescriptor slot = block.Slots[slotIndex];
                    if (slot.RestorePolicy != CoCoContextRestorePolicy.Derived &&
                        !owned.Contains(slot.SlotId))
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.OutcomeOwnershipConflict,
                            "Every writable Operator StateSlot requires exactly one explicit Outcome owner.");
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryResolveClaims(
            CoCoOperatorDescriptor descriptor,
            int operatorIndex,
            CoCoContextFrameLayout contextLayout,
            CoCoOperationSectionRegistry operationRegistry,
            List<ClaimResourceBuilder> resources,
            Dictionary<CoCoOperationSectionId, CoCoOperatorClaimId> sectionClaims,
            Dictionary<CoCoStateSlotId, CoCoOperatorClaimId> slotClaims,
            out int[] resourceIndices,
            out int[] sectionIndices,
            out CoCoDiagnostic diagnostic)
        {
            resourceIndices = new int[descriptor.Claims.Count];
            sectionIndices = new int[descriptor.Claims.Count];
            for (int claimIndex = 0; claimIndex < descriptor.Claims.Count; claimIndex++)
            {
                CoCoOperatorClaimRequirement claim = descriptor.Claims[claimIndex];
                if (!operationRegistry.TryResolveDenseIndex(
                        claim.Section,
                        out sectionIndices[claimIndex]) ||
                    !TryResolveClaimStateSlot(
                        contextLayout,
                        claim.StateSlotId,
                        out CoCoStateBlockHandle claimBlock,
                        out CoCoStateSlot<CoCoOperatorClaimState> claimSlot,
                        out CoCoOperatorClaimState defaultClaim) ||
                    defaultClaim.ClaimId != claim.ClaimId ||
                    defaultClaim.SectionId != claim.Section.SectionId ||
                    defaultClaim.IsHeld)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperatorDescriptor,
                        "Claim Section and trusted Slot default must exactly match the frozen descriptor.");
                    return false;
                }

                if (sectionClaims.TryGetValue(
                        claim.Section.SectionId,
                        out CoCoOperatorClaimId existingClaimId) &&
                    existingClaimId != claim.ClaimId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "One Operation Section cannot be arbitrated under multiple Claim identities.");
                    return false;
                }

                sectionClaims[claim.Section.SectionId] = claim.ClaimId;
                if (slotClaims.TryGetValue(
                        claim.StateSlotId,
                        out CoCoOperatorClaimId existingSlotClaim) &&
                    existingSlotClaim != claim.ClaimId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "One Claim State Slot cannot represent multiple Claim identities.");
                    return false;
                }

                slotClaims[claim.StateSlotId] = claim.ClaimId;
                int resourceIndex = -1;
                for (int index = 0; index < resources.Count; index++)
                {
                    if (resources[index].ClaimId != claim.ClaimId)
                    {
                        continue;
                    }

                    if (resources[index].Section != claim.Section ||
                        resources[index].StateSlotId != claim.StateSlotId)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.OperatorClaimConflict,
                            "One Claim identity cannot map to conflicting Operation Sections.");
                        return false;
                    }

                    resourceIndex = index;
                    break;
                }

                if (resourceIndex < 0)
                {
                    resourceIndex = resources.Count;
                    resources.Add(new ClaimResourceBuilder(
                        claim.ClaimId,
                        claim.Section,
                        claim.StateSlotId,
                        claimBlock,
                        claimSlot));
                }

                resources[resourceIndex].Add(operatorIndex);
                resourceIndices[claimIndex] = resourceIndex;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryValidateClaimCoverage(
            IReadOnlyList<CoCoStateSlotId> declaredSlots,
            List<ClaimResourceBuilder> resources,
            out CoCoDiagnostic diagnostic)
        {
            if (declaredSlots.Count != resources.Count)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.OperatorClaimConflict,
                    "Graph-owned Claim State Slots must be consumed exactly once by Claim arbitration.");
                return false;
            }

            for (int slotIndex = 0; slotIndex < declaredSlots.Count; slotIndex++)
            {
                bool found = false;
                for (int resourceIndex = 0; resourceIndex < resources.Count; resourceIndex++)
                {
                    if (resources[resourceIndex].StateSlotId == declaredSlots[slotIndex])
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.OperatorClaimConflict,
                        "A declared Claim State Slot has no matching Claim descriptor.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryResolveClaimStateSlot(
            CoCoContextFrameLayout layout,
            CoCoStateSlotId slotId,
            out CoCoStateBlockHandle blockHandle,
            out CoCoStateSlot<CoCoOperatorClaimState> slotHandle,
            out CoCoOperatorClaimState defaultValue)
        {
            blockHandle = default;
            slotHandle = default;
            defaultValue = default;
            CoCoStateSlotDescriptor slot = FindSlot(layout, slotId);
            CoCoStateBlockDescriptor block = slot == null
                ? null
                : FindBlock(layout, slot.WriterBlockId);
            if (slot == null ||
                block == null ||
                slot.ValueType != typeof(CoCoOperatorClaimState) ||
                slot.RestorePolicy == CoCoContextRestorePolicy.Derived ||
                block.Owner != CoCoStateBlockOwner.Graph ||
                !layout.TryResolveBlock(block.BlockId, out blockHandle) ||
                !layout.TryResolveSlot(slotId, out slotHandle))
            {
                return false;
            }

            defaultValue = CoCoStateFlowTypeRules.Read<CoCoOperatorClaimState>(
                slot.DefaultBytes,
                0);
            return defaultValue.IsValid;
        }

        private static bool Matches(
            CoCoGraphOperationProvideRequirement provided,
            CoCoOperationSectionRequirement required)
        {
            return required.IsValid &&
                   provided.SectionId == required.SectionId &&
                   provided.Mode == required.Mode &&
                   provided.SectionType == required.SectionType &&
                   provided.Shape != null &&
                   provided.Shape.Equals(required.Shape);
        }

        private static CoCoStateSlotDescriptor FindSlot(
            CoCoContextFrameLayout layout,
            CoCoStateSlotId slotId)
        {
            for (int index = 0; index < layout.Slots.Count; index++)
            {
                if (layout.Slots[index].SlotId == slotId)
                {
                    return layout.Slots[index];
                }
            }

            return null;
        }

        private static CoCoStateBlockDescriptor FindBlock(
            CoCoContextFrameLayout layout,
            CoCoStateBlockId blockId)
        {
            for (int index = 0; index < layout.Blocks.Count; index++)
            {
                if (layout.Blocks[index].BlockId == blockId)
                {
                    return layout.Blocks[index];
                }
            }

            return null;
        }

        private static void ClearOwners(int[] owners)
        {
            for (int index = 0; index < owners.Length; index++)
            {
                owners[index] = -1;
            }
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operator, code, message);

        private static CoCoDiagnostic RestoreError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.InvalidClaimRestore,
                message);

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        private sealed class OperatorBinding
        {
            public OperatorBinding(
                MonoBehaviour component,
                ICoCoOperator executable,
                CoCoOperatorDescriptor descriptor,
                OutcomeBinding[] outcomes,
                int[] claimResourceIndices,
                int[] claimSectionIndices)
            {
                Component = component;
                Operator = executable;
                Descriptor = descriptor;
                Outcomes = outcomes;
                ClaimResourceIndices = claimResourceIndices;
                ClaimSectionIndices = claimSectionIndices;
                ClaimActivations = new CoCoActivationId[claimResourceIndices.Length];
            }

            public MonoBehaviour Component { get; }
            public ICoCoOperator Operator { get; }
            public CoCoOperatorDescriptor Descriptor { get; }
            public OutcomeBinding[] Outcomes { get; }
            public int[] ClaimResourceIndices { get; }
            public int[] ClaimSectionIndices { get; }
            public CoCoActivationId[] ClaimActivations { get; }
            public bool Eligible { get; set; }
        }

        private readonly struct OutcomeBinding
        {
            public OutcomeBinding(
                CoCoStateSlotId slotId,
                Type valueType,
                CoCoStateBlockHandle block)
            {
                SlotId = slotId;
                ValueType = valueType;
                Block = block;
            }

            public CoCoStateSlotId SlotId { get; }
            public Type ValueType { get; }
            public CoCoStateBlockHandle Block { get; }
        }

        private readonly struct ClaimResource
        {
            public ClaimResource(
                CoCoOperatorClaimId claimId,
                CoCoOperationSectionRequirement section,
                CoCoStateSlotId stateSlotId,
                CoCoStateBlockHandle block,
                CoCoStateSlot<CoCoOperatorClaimState> slot,
                int[] contenders)
            {
                ClaimId = claimId;
                Section = section;
                StateSlotId = stateSlotId;
                Block = block;
                Slot = slot;
                Contenders = contenders;
            }

            public CoCoOperatorClaimId ClaimId { get; }
            public CoCoOperationSectionRequirement Section { get; }
            public CoCoStateSlotId StateSlotId { get; }
            public CoCoStateBlockHandle Block { get; }
            public CoCoStateSlot<CoCoOperatorClaimState> Slot { get; }
            public int[] Contenders { get; }
        }

        private sealed class ClaimResourceBuilder
        {
            private readonly List<int> _contenders = new List<int>();

            public ClaimResourceBuilder(
                CoCoOperatorClaimId claimId,
                CoCoOperationSectionRequirement section,
                CoCoStateSlotId stateSlotId,
                CoCoStateBlockHandle block,
                CoCoStateSlot<CoCoOperatorClaimState> slot)
            {
                ClaimId = claimId;
                Section = section;
                StateSlotId = stateSlotId;
                Block = block;
                Slot = slot;
            }

            public CoCoOperatorClaimId ClaimId { get; }
            public CoCoOperationSectionRequirement Section { get; }
            public CoCoStateSlotId StateSlotId { get; }
            public CoCoStateBlockHandle Block { get; }
            public CoCoStateSlot<CoCoOperatorClaimState> Slot { get; }

            public void Add(int operatorIndex) => _contenders.Add(operatorIndex);

            public ClaimResource Freeze() =>
                new ClaimResource(
                    ClaimId,
                    Section,
                    StateSlotId,
                    Block,
                    Slot,
                    _contenders.ToArray());
        }
    }
}
