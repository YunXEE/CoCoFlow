using System;
using System.Collections;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoRuntimeFault : IEquatable<CoCoRuntimeFault>
    {
        internal CoCoRuntimeFault(CoCoDiagnostic diagnostic)
        {
            Diagnostic = diagnostic;
        }

        public CoCoDiagnostic Diagnostic { get; }
        public bool IsFaulted => Diagnostic.IsError;

        public bool Equals(CoCoRuntimeFault other) => Diagnostic == other.Diagnostic;
        public override bool Equals(object obj) => obj is CoCoRuntimeFault other && Equals(other);
        public override int GetHashCode() => Diagnostic.GetHashCode();
        public static bool operator ==(CoCoRuntimeFault left, CoCoRuntimeFault right) => left.Equals(right);
        public static bool operator !=(CoCoRuntimeFault left, CoCoRuntimeFault right) => !left.Equals(right);
    }

    /// <summary>
    /// Stable read-only view over one Layer's committed Root-to-Leaf ActivePath.
    /// </summary>
    public sealed class CoCoActivePath : IReadOnlyList<CoCoStateId>
    {
        private readonly CoCoStateGraphRuntime _runtime;
        private readonly int _layerIndex;

        internal CoCoActivePath(CoCoStateGraphRuntime runtime, int layerIndex, CoCoLayerId layerId)
        {
            _runtime = runtime;
            _layerIndex = layerIndex;
            LayerId = layerId;
        }

        public CoCoLayerId LayerId { get; }
        public int Count => _runtime.GetCommittedPathCount(_layerIndex);
        public CoCoStateId ActiveLeaf => _runtime.GetCommittedLeafId(_layerIndex);

        public CoCoStateId this[int index] => _runtime.GetCommittedPathStateId(_layerIndex, index);

        public Enumerator GetEnumerator() => new Enumerator(this);
        IEnumerator<CoCoStateId> IEnumerable<CoCoStateId>.GetEnumerator() => GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public struct Enumerator : IEnumerator<CoCoStateId>
        {
            private readonly CoCoActivePath _path;
            private int _index;

            internal Enumerator(CoCoActivePath path)
            {
                _path = path;
                _index = -1;
            }

            public CoCoStateId Current => _path[_index];
            object IEnumerator.Current => Current;
            public bool MoveNext() => ++_index < _path.Count;
            public void Reset() => _index = -1;
            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    /// Single-use candidate Tick. Pre5 accepts or rejects this token after Context finalization.
    /// </summary>
    public readonly struct CoCoStagedGraphStep : IEquatable<CoCoStagedGraphStep>
    {
        private readonly CoCoStateGraphRuntime _runtime;
        private readonly ulong _token;
        private readonly CoCoFinalizedOperationFrame _finalizedOperationFrame;

        internal CoCoStagedGraphStep(
            CoCoStateGraphRuntime runtime,
            ulong token,
            in CoCoTickFrame tickFrame,
            CoCoFinalizedOperationFrame operationFrame)
        {
            _runtime = runtime;
            _token = token;
            _finalizedOperationFrame = operationFrame;
            TickFrame = tickFrame;
        }

        public CoCoTickFrame TickFrame { get; }
        public CoCoStagedOperationFrame OperationFrame =>
            new CoCoStagedOperationFrame(_finalizedOperationFrame);
        public bool IsValid => _runtime != null && _runtime.IsStagedTokenCurrent(_token);

        internal CoCoStateGraphRuntime Runtime => _runtime;
        internal ulong Token => _token;
        internal CoCoFinalizedOperationFrame FinalizedOperationFrame => _finalizedOperationFrame;

        public bool Equals(CoCoStagedGraphStep other) =>
            ReferenceEquals(_runtime, other._runtime) &&
            _token == other._token &&
            TickFrame == other.TickFrame;

        public override bool Equals(object obj) => obj is CoCoStagedGraphStep other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((_runtime?.GetHashCode() ?? 0) * 397) ^ _token.GetHashCode());
        public static bool operator ==(CoCoStagedGraphStep left, CoCoStagedGraphStep right) => left.Equals(right);
        public static bool operator !=(CoCoStagedGraphStep left, CoCoStagedGraphStep right) => !left.Equals(right);
    }

    /// <summary>
    /// Single-use proof that the staged Graph, OperationFrame, and Clock are ready for
    /// the no-fail portion of their Actor authority barrier.
    /// </summary>
    internal readonly struct CoCoPreparedGraphCommit
    {
        private readonly CoCoStateGraphRuntime _runtime;
        private readonly ulong _token;

        internal CoCoPreparedGraphCommit(
            CoCoStateGraphRuntime runtime,
            ulong token,
            in CoCoTickFrame tickFrame)
        {
            _runtime = runtime;
            _token = token;
            TickFrame = tickFrame;
        }

        internal CoCoTickFrame TickFrame { get; }
        internal bool IsValid =>
            _runtime != null && _runtime.IsPreparedCommitTokenCurrent(_token);
        internal CoCoStateGraphRuntime Runtime => _runtime;
        internal ulong Token => _token;

        internal void CommitNoFail()
        {
            _runtime.CommitPreparedStepNoFail();
        }
    }

    /// <summary>
    /// Purely decoded metadata for one State restore candidate. ActivationMemory is prepared
    /// separately in the Runtime's non-authoritative candidate bank.
    /// </summary>
    internal readonly struct CoCoStateGraphRestoreState
    {
        internal CoCoStateGraphRestoreState(
            CoCoLayerId layerId,
            CoCoStateId stateId,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            bool isValid)
        {
            LayerId = layerId;
            StateId = stateId;
            IsOnActivePath = isOnActivePath;
            ActivationId = activationId;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
            EnterPending = enterPending;
            MemoryFingerprint = memoryFingerprint;
            IsValid = isValid;
        }

        internal CoCoLayerId LayerId { get; }
        internal CoCoStateId StateId { get; }
        internal bool IsOnActivePath { get; }
        internal CoCoActivationId ActivationId { get; }
        internal double LocalSeconds { get; }
        internal double ActionProgress { get; }
        internal bool EnterPending { get; }
        internal ulong MemoryFingerprint { get; }
        internal bool IsValid { get; }
    }

    /// <summary>
    /// Core-facing adapter implemented by the Host Context producer runtime. Keeping this
    /// seam in Core prevents a reverse assembly dependency on the Unity Host assembly.
    /// </summary>
    internal interface ICoCoStateGraphContextRuntime
    {
        bool TryBeginGraphCapture(
            in CoCoStagedGraphStep stagedStep,
            CoCoContextFrameReadView previous,
            in CoCoPreparedContextCommit prepared,
            ulong token,
            out CoCoDiagnostic diagnostic);

        bool TryCaptureState(
            int orderedStateIndex,
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            out CoCoDiagnostic diagnostic);

        bool TryValidateInitialStateDefault(
            int orderedStateIndex,
            CoCoActivationMemory memory,
            bool isOnActivePath,
            CoCoActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            CoCoContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic);

        bool TryCompleteGraphCapture(out CoCoDiagnostic diagnostic);
        void CancelCapture();

        bool TryValidateRestore(
            CoCoContextRestoreReadView source,
            out ulong nextActivationValue,
            out CoCoDiagnostic diagnostic);

        bool TryPrepareStateRestore(
            int orderedStateIndex,
            CoCoContextRestoreReadView source,
            CoCoActivationMemory candidateMemory,
            out CoCoStateGraphRestoreState state,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>
    /// Single-use proof that State banks, ActivePaths, activation allocation, and Clock
    /// are ready for a no-callback, no-fail restore authority swap.
    /// </summary>
    internal readonly struct CoCoPreparedGraphRestore
    {
        private readonly CoCoStateGraphRuntime _runtime;
        private readonly ulong _token;

        internal CoCoPreparedGraphRestore(
            CoCoStateGraphRuntime runtime,
            ulong token,
            in CoCoTickFrame tickFrame)
        {
            _runtime = runtime;
            _token = token;
            TickFrame = tickFrame;
        }

        internal CoCoTickFrame TickFrame { get; }
        internal bool IsValid =>
            _runtime != null && _runtime.IsPreparedRestoreTokenCurrent(_token);

        internal void ApplyNoFail()
        {
            _runtime.ApplyPreparedRestoreNoFail();
        }

        internal bool Cancel() =>
            _runtime != null && _runtime.CancelPreparedRestore(_token);
    }

    internal interface ICoCoStateGraphCommitGuard
    {
        bool IsCommitCancellationRequested { get; }
    }

    /// <summary>
    /// Single-use Host proof that one faulted Runtime reached an idle Temporal
    /// correction boundary. This is intentionally not a general Fault reset API.
    /// </summary>
    internal readonly struct CoCoPreparedTemporalRecovery
    {
        private readonly CoCoStateGraphRuntime _runtime;
        private readonly ulong _token;

        internal CoCoPreparedTemporalRecovery(
            CoCoStateGraphRuntime runtime,
            ulong token)
        {
            _runtime = runtime;
            _token = token;
        }

        internal bool IsValid =>
            _runtime != null &&
            _runtime.IsTemporalRecoveryTokenCurrent(_token);

        internal void CompleteNoFail()
        {
            _runtime?.CompleteTemporalRecoveryNoFail(_token);
        }

        internal bool Cancel() =>
            _runtime != null &&
            _runtime.CancelTemporalRecovery(_token);
    }

    /// <summary>
    /// Per-Host, allocation-stable StateGraph instance. Compiled Graph data is shared; all execution state is local.
    /// </summary>
    public sealed class CoCoStateGraphRuntime : IDisposable
    {
        private readonly CoCoCompiledStateGraph _graph;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoStateGraphLogicBindings _bindings;
        private readonly CoCoOperationFrame _operationFrame;
        private readonly CoCoActorClock _clock;
        private readonly object _transitionHandleOwner;
        private readonly LayerRuntime[] _layers;
        private readonly CoCoActivePath[] _activePaths;
        private readonly CoCoConditionEvaluationContext _conditionContext;
        private readonly CoCoStateCallbackOperationLease _stateCallbackOperationLease;
        private readonly bool[] _requestedTransitionsScratch;
        private CoCoRuntimeLifecycleState _lifecycle;
        private CoCoRuntimeFault _fault;
        private CoCoOperationFrameWriter _operationWriter;
        private CoCoFinalizedOperationFrame _finalizedOperationFrame;
        private CoCoTickFrame _stagedTickFrame;
        private ulong _nextActivationValue;
        private ulong _candidateNextActivationValue;
        private ulong _stageGeneration;
        private ulong _activeStageToken;
        private ulong _activePreparedCommitToken;
        private ulong _restoreGeneration;
        private ulong _activeRestoreToken;
        private ulong _temporalRecoveryGeneration;
        private ulong _activeTemporalRecoveryToken;
        private CoCoPreparedActorClockRestore _preparedClockRestore;
        private int _executingLayerIndex;
        private int _executingStateIndex;
        private bool _acceptsTransitionRequests;
        private bool _isExecutingStep;
        private bool _disposeRequested;
        private bool _isDisposed;

        private CoCoStateGraphRuntime(
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoStateGraphLogicBindings bindings,
            CoCoOperationFrame operationFrame,
            CoCoActorClock clock,
            object transitionHandleOwner,
            LayerRuntime[] layers,
            int maximumTransitionCount)
        {
            _graph = graph;
            _graphInstanceId = graphInstanceId;
            _bindings = bindings;
            _operationFrame = operationFrame;
            _clock = clock;
            _transitionHandleOwner = transitionHandleOwner;
            _layers = layers;
            _activePaths = new CoCoActivePath[layers.Length];
            for (int index = 0; index < layers.Length; index++)
            {
                _activePaths[index] = new CoCoActivePath(this, index, layers[index].Compiled.LayerId);
            }

            _conditionContext = new CoCoConditionEvaluationContext();
            _stateCallbackOperationLease = new CoCoStateCallbackOperationLease();
            _requestedTransitionsScratch = new bool[maximumTransitionCount];
            _lifecycle = CoCoRuntimeLifecycleState.Created;
            _nextActivationValue = 1UL;
            _executingLayerIndex = -1;
            _executingStateIndex = -1;
        }

        public CoCoCompiledStateGraph Graph => _graph;
        public CoCoGraphInstanceId GraphInstanceId => _graphInstanceId;
        public CoCoRuntimeLifecycleState Lifecycle => _lifecycle;
        public CoCoRuntimeFault Fault => _fault;
        public bool IsFaulted => _fault.IsFaulted;
        public bool HasStagedStep => _activeStageToken != 0UL;
        internal bool HasPreparedRestore => _activeRestoreToken != 0UL;
        public CoCoActorClock Clock => _clock;
        public IReadOnlyList<CoCoActivePath> ActivePaths => _activePaths;

        public static bool TryCreate(
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoStateGraphLogicBindings bindings,
            CoCoOperationFrame operationFrame,
            CoCoActorClock clock,
            out CoCoStateGraphRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            if (graph == null ||
                !graphInstanceId.IsValid ||
                bindings == null ||
                !ReferenceEquals(bindings.Graph, graph) ||
                operationFrame == null ||
                clock == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.MissingDescriptor,
                    "StateGraph Runtime requires one compiled Graph and its exact immutable bindings, OperationFrame, Clock, and GraphInstanceId.");
                return false;
            }

            if (!ValidateOperationFrame(
                    graph,
                    graphInstanceId,
                    operationFrame,
                    out diagnostic))
            {
                return false;
            }

            if (clock.GraphInstanceId.IsValid && clock.GraphInstanceId != graphInstanceId)
            {
                diagnostic = Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "Actor Clock belongs to another GraphInstance.");
                return false;
            }

            if (!ValidateFactories(graph, bindings, out diagnostic))
            {
                return false;
            }

            object owner = new object();
            var claimedInstances = new List<object>();
            bool clockClaimed = false;
            bool operationFrameClaimed = false;
            try
            {
                var layers = new LayerRuntime[graph.Layers.Count];
                int maximumTransitionCount = 0;
                for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
                {
                    CoCoCompiledStateLayer compiledLayer = graph.Layers[layerIndex];
                    layers[layerIndex] = BuildLayer(
                        owner,
                        graphInstanceId,
                        compiledLayer,
                        bindings,
                        operationFrame.Registry,
                        layerIndex,
                        claimedInstances);
                    maximumTransitionCount = Math.Max(
                        maximumTransitionCount,
                        compiledLayer.Transitions.Count);
                }

                if (!clock.TryClaimRuntimeOwner(owner, graphInstanceId))
                {
                    throw new InvalidOperationException("Actor Clock is already claimed or not fresh.");
                }

                clockClaimed = true;
                if (!operationFrame.TryClaimRuntimeOwner(owner, graphInstanceId))
                {
                    throw new InvalidOperationException("OperationFrame is already claimed or not fresh.");
                }

                operationFrameClaimed = true;

                runtime = new CoCoStateGraphRuntime(
                    graph,
                    graphInstanceId,
                    bindings,
                    operationFrame,
                    clock,
                    owner,
                    layers,
                    maximumTransitionCount);
            }
            catch (Exception)
            {
                if (operationFrameClaimed)
                {
                    operationFrame.ReleaseRuntimeOwner(owner);
                }

                if (clockClaimed)
                {
                    clock.ReleaseRuntimeOwner(owner);
                }

                ReleaseRuntimeInstanceClaims(owner, claimedInstances);
                runtime = null;
                diagnostic = Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "StateGraph setup requires fresh, isolated Logic, Condition, Memory, Clock, and OperationFrame instances.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryStart(out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != CoCoRuntimeLifecycleState.Created || _isDisposed || IsFaulted)
            {
                diagnostic = LifecycleError("Only a healthy Created StateGraph Runtime can start.");
                return false;
            }

            try
            {
                for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
                {
                    LayerRuntime layer = _layers[layerIndex];
                    for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
                    {
                        StateRuntime state = layer.States[stateIndex];
                        state.Factory.ResetMemory(state.Memory0);
                        state.Factory.ResetMemory(state.Memory1);
                        if (!state.TryInitializeMemoryFingerprints())
                        {
                            diagnostic = StateError(
                                "ActivationMemory reset produced inconsistent committed and candidate Banks.");
                            LatchFault(diagnostic);
                            return false;
                        }
                    }

                    int initialLeaf = ResolveInitialLeaf(layer.Compiled, layer.Compiled.InitialStateIndex);
                    layer.CommittedLeafIndex = initialLeaf;
                    layer.CommittedEnterStartDepth = 0;
                    IReadOnlyList<int> path = layer.Compiled.States[initialLeaf].RootPathStateIndices;
                    for (int depth = 0; depth < path.Count; depth++)
                    {
                        StateRuntime state = layer.States[path[depth]];
                        if (!TryTakeActivation(out CoCoActivationId activationId))
                        {
                            diagnostic = StateError("State activation identity capacity is exhausted.");
                            LatchFault(diagnostic);
                            return false;
                        }

                        state.CommittedActivationId = activationId;
                        state.CommittedLocalSeconds = 0d;
                        state.CommittedActionProgress = 0d;
                    }
                }
            }
            catch (Exception)
            {
                diagnostic = StateError("ActivationMemory reset failed while starting the StateGraph Runtime.");
                LatchFault(diagnostic);
                return false;
            }

            TransitionLifecycleTo(CoCoRuntimeLifecycleState.Running);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TrySuspend(out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != CoCoRuntimeLifecycleState.Running ||
                HasStagedStep ||
                HasPreparedRestore ||
                IsFaulted ||
                _isExecutingStep ||
                _disposeRequested)
            {
                diagnostic = LifecycleError("Suspend requires a healthy Running Runtime at a resolved Tick boundary.");
                return false;
            }

            TransitionLifecycleTo(CoCoRuntimeLifecycleState.Suspended);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryResume(out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != CoCoRuntimeLifecycleState.Suspended ||
                IsFaulted ||
                HasPreparedRestore)
            {
                diagnostic = LifecycleError("Resume requires a healthy Suspended Runtime.");
                return false;
            }

            TransitionLifecycleTo(CoCoRuntimeLifecycleState.Running);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryStop(out CoCoDiagnostic diagnostic)
        {
            if ((_lifecycle != CoCoRuntimeLifecycleState.Running &&
                 _lifecycle != CoCoRuntimeLifecycleState.Suspended) ||
                _isExecutingStep ||
                _disposeRequested ||
                _isDisposed)
            {
                diagnostic = LifecycleError(
                    "Stop requires a Running or Suspended StateGraph Runtime at a safe boundary.");
                return false;
            }

            CancelOutstandingStep(false, default);
            TransitionLifecycleTo(CoCoRuntimeLifecycleState.Stopped);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPreviewNextTick(
            double deltaTime,
            double actorTimeScale,
            out CoCoTickFrame tickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != CoCoRuntimeLifecycleState.Running ||
                IsFaulted ||
                HasStagedStep ||
                HasPreparedRestore ||
                _isExecutingStep ||
                _disposeRequested)
            {
                tickFrame = default;
                diagnostic = LifecycleError("Only a healthy Running Runtime can preview its next resolved Tick.");
                return false;
            }

            return _clock.TryPreviewNext(
                _transitionHandleOwner,
                deltaTime,
                actorTimeScale,
                out tickFrame,
                out diagnostic);
        }

        public bool TryStageStep(
            in CoCoTickFrame tickFrame,
            ICoCoIntentFrame intents,
            in CoCoContextFrameReadView previousContext,
            out CoCoStagedGraphStep stagedStep,
            out CoCoDiagnostic diagnostic)
        {
            stagedStep = default;
            if (_lifecycle != CoCoRuntimeLifecycleState.Running ||
                IsFaulted ||
                HasStagedStep ||
                HasPreparedRestore ||
                _isExecutingStep ||
                _disposeRequested)
            {
                diagnostic = LifecycleError("Only a healthy Running Runtime with no unresolved Tick can Step.");
                return false;
            }

            if (!ValidateIntentFrame(tickFrame, intents, out diagnostic) ||
                !_clock.TryStage(_transitionHandleOwner, tickFrame))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = LifecycleError("TickFrame is not the next candidate produced by this Runtime Clock.");
                }

                return false;
            }

            _isExecutingStep = true;
            if (!TryPrepareCandidateState(out diagnostic) || _disposeRequested || IsFaulted)
            {
                if (_disposeRequested)
                {
                    diagnostic = LifecycleError("Dispose was requested while preparing the candidate Tick.");
                }
                else if (IsFaulted)
                {
                    diagnostic = _fault.Diagnostic;
                }

                EndFailedStep(tickFrame, false, diagnostic);
                return false;
            }

            if (!_operationFrame.TryBegin(
                    _transitionHandleOwner,
                    tickFrame,
                    out _operationWriter))
            {
                diagnostic = OperationError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "OperationFrame could not begin the candidate Tick.");
                EndFailedStep(tickFrame, false, diagnostic);
                return false;
            }

            bool callbacksSucceeded;
            try
            {
                callbacksSucceeded = ExecuteLayers(tickFrame, intents, previousContext, out diagnostic);
            }
            catch (Exception)
            {
                callbacksSucceeded = false;
                diagnostic = StateError("State or Condition execution threw while staging the Tick.");
            }
            finally
            {
                _conditionContext.Clear();
                _acceptsTransitionRequests = false;
                _executingLayerIndex = -1;
                _executingStateIndex = -1;
            }

            if (!callbacksSucceeded)
            {
                EndFailedStep(tickFrame, true, diagnostic);
                return false;
            }

            if (!TryValidateCandidateMemory(out diagnostic) || _disposeRequested || IsFaulted)
            {
                if (_disposeRequested)
                {
                    diagnostic = LifecycleError("Dispose was requested while validating the candidate Tick.");
                }
                else if (IsFaulted)
                {
                    diagnostic = _fault.Diagnostic;
                }

                EndFailedStep(tickFrame, true, diagnostic);
                return false;
            }

            if (!_operationWriter.TryFinalize(out _finalizedOperationFrame))
            {
                diagnostic = OperationError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "OperationFrame finalization failed; the candidate Tick was cancelled.");
                EndFailedStep(tickFrame, true, diagnostic);
                return false;
            }

            _stagedTickFrame = tickFrame;
            _stageGeneration = _stageGeneration == ulong.MaxValue ? 1UL : _stageGeneration + 1UL;
            _activeStageToken = _stageGeneration;
            stagedStep = new CoCoStagedGraphStep(
                this,
                _activeStageToken,
                tickFrame,
                _finalizedOperationFrame);
            _isExecutingStep = false;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public CoCoActivePath GetActivePath(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _activePaths.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIndex));
            }

            return _activePaths[layerIndex];
        }

        public bool TryGetActivePath(CoCoLayerId layerId, out CoCoActivePath activePath)
        {
            for (int index = 0; index < _activePaths.Length; index++)
            {
                if (_activePaths[index].LayerId == layerId)
                {
                    activePath = _activePaths[index];
                    return true;
                }
            }

            activePath = null;
            return false;
        }

        public void Dispose()
        {
            if (_isDisposed)
            {
                return;
            }

            if (_isExecutingStep)
            {
                _disposeRequested = true;
                return;
            }

            CancelOutstandingStep(false, default);
            DisposeAtSafeBoundary();
        }

        internal bool TryRequestTransition(CoCoTransitionHandle handle, CoCoStateId stateId)
        {
            if (!_acceptsTransitionRequests ||
                _executingLayerIndex < 0 ||
                _executingStateIndex < 0 ||
                !handle.IsOwnedBy(_transitionHandleOwner) ||
                handle.LayerIndex != _executingLayerIndex ||
                handle.SourceStateId != stateId)
            {
                return false;
            }

            LayerRuntime layer = _layers[_executingLayerIndex];
            if (_executingStateIndex != layer.CommittedLeafIndex ||
                handle.TransitionIndex < 0 ||
                handle.TransitionIndex >= layer.Compiled.Transitions.Count ||
                layer.Compiled.Transitions[handle.TransitionIndex].SourceStateIndex != _executingStateIndex)
            {
                return false;
            }

            _requestedTransitionsScratch[handle.TransitionIndex] = true;
            return true;
        }

        internal bool IsStagedTokenCurrent(ulong token) => token != 0UL && token == _activeStageToken;

        internal bool IsPreparedCommitTokenCurrent(ulong token) =>
            token != 0UL &&
            token == _activeStageToken &&
            token == _activePreparedCommitToken;

        internal bool TryCaptureGraphContext(
            ICoCoStateGraphContextRuntime contextRuntime,
            in CoCoStagedGraphStep stagedStep,
            CoCoContextFrameReadView previousContext,
            in CoCoPreparedContextCommit preparedContext,
            ulong transactionToken,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (contextRuntime == null || !Owns(stagedStep) ||
                !contextRuntime.TryBeginGraphCapture(
                    stagedStep,
                    previousContext,
                    preparedContext,
                    transactionToken,
                    out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = Error(
                        CoCoDiagnosticDomain.Context,
                        CoCoDiagnosticCode.ContextCaptureFailed,
                        "Staged Graph authority could not begin Context capture.");
                }

                return false;
            }

            int orderedStateIndex = 0;
            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                IReadOnlyList<int> path =
                    layer.Compiled.States[layer.CandidateLeafIndex].RootPathStateIndices;
                for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
                {
                    StateRuntime state = layer.States[stateIndex];
                    int pathDepth = FindPathDepth(path, stateIndex);
                    bool isOnActivePath = pathDepth >= 0;
                    bool enterPending = isOnActivePath &&
                                        layer.CandidateEnterStartDepth >= 0 &&
                                        pathDepth >= layer.CandidateEnterStartDepth;
                    if (!contextRuntime.TryCaptureState(
                            orderedStateIndex++,
                            state.CandidateMemory,
                            isOnActivePath,
                            state.CandidateActivationId,
                            state.CandidateLocalSeconds,
                            state.CandidateActionProgress,
                            enterPending,
                            state.CandidateMemoryFingerprint,
                            out diagnostic))
                    {
                        contextRuntime.CancelCapture();
                        return false;
                    }
                }
            }

            if (!TryValidateStagedMemoryIntegrity(out diagnostic))
            {
                contextRuntime.CancelCapture();
                return false;
            }

            return contextRuntime.TryCompleteGraphCapture(out diagnostic);
        }

        internal bool TryValidateInitialGraphContextDefaults(
            ICoCoStateGraphContextRuntime contextRuntime,
            CoCoContextFrameReadView defaults,
            ICoCoStateGraphCommitGuard commitGuard,
            out CoCoDiagnostic diagnostic)
        {
            if (contextRuntime == null || _lifecycle != CoCoRuntimeLifecycleState.Running ||
                HasStagedStep || !defaults.IsValid || defaults.HasCommittedFrame)
            {
                diagnostic = Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Initial Graph defaults can be validated only after Runtime start and before the first commit.");
                return false;
            }

            int orderedStateIndex = 0;
            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                IReadOnlyList<int> path =
                    layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices;
                for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
                {
                    StateRuntime state = layer.States[stateIndex];
                    int pathDepth = FindPathDepth(path, stateIndex);
                    bool isOnActivePath = pathDepth >= 0;
                    bool enterPending = isOnActivePath &&
                                        layer.CommittedEnterStartDepth >= 0 &&
                                        pathDepth >= layer.CommittedEnterStartDepth;
                    if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                    {
                        diagnostic = LifecycleError(
                            "Initial Graph Context validation was cancelled by its Host before State capture.");
                        return false;
                    }

                    bool validated = contextRuntime.TryValidateInitialStateDefault(
                            orderedStateIndex++,
                            state.CommittedMemory,
                            isOnActivePath,
                            state.CommittedActivationId,
                            state.CommittedLocalSeconds,
                            state.CommittedActionProgress,
                            enterPending,
                            state.CommittedMemoryFingerprint,
                            defaults,
                            out diagnostic);

                    if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                    {
                        diagnostic = LifecycleError(
                            "Initial Graph Context validation was cancelled by its Host after State capture.");
                        return false;
                    }

                    if (!validated)
                    {
                        return false;
                    }
                }
            }

            if (!TryValidateCommittedMemoryIntegrity(
                    commitGuard,
                    out CoCoDiagnostic integrityDiagnostic))
            {
                diagnostic = integrityDiagnostic.Domain == CoCoDiagnosticDomain.Lifecycle
                    ? integrityDiagnostic
                    : Error(
                        CoCoDiagnosticDomain.Context,
                        CoCoDiagnosticCode.InvalidContextProducer,
                        "Initial Graph capture mutated committed ActivationMemory outside Runtime authority.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryValidateRestore(
            ICoCoStateGraphContextRuntime contextRuntime,
            CoCoContextRestoreReadView source,
            in CoCoTickFrame resumedTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            return TryValidateRestoreCore(
                contextRuntime,
                source,
                resumedTickFrame,
                out _,
                out diagnostic);
        }

        internal bool TryPrepareRestore(
            ICoCoStateGraphContextRuntime contextRuntime,
            CoCoContextRestoreReadView source,
            in CoCoTickFrame resumedTickFrame,
            out CoCoPreparedGraphRestore preparedRestore,
            out CoCoDiagnostic diagnostic)
        {
            preparedRestore = default;
            if (!TryValidateRestoreCore(
                    contextRuntime,
                    source,
                    resumedTickFrame,
                    out ulong nextActivationValue,
                    out diagnostic) ||
                !_clock.TryPrepareRestore(
                    _transitionHandleOwner,
                    resumedTickFrame,
                    out CoCoPreparedActorClockRestore preparedClock,
                    out diagnostic))
            {
                return false;
            }

            bool preparedStates = false;
            try
            {
                int orderedStateIndex = 0;
                for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
                {
                    LayerRuntime layer = _layers[layerIndex];
                    int activeLeafIndex = -1;
                    int activeLeafCount = 0;
                    for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
                    {
                        StateRuntime state = layer.States[stateIndex];
                        if (!contextRuntime.TryPrepareStateRestore(
                                orderedStateIndex++,
                                source,
                                state.CandidateMemory,
                                out CoCoStateGraphRestoreState restoreState,
                                out diagnostic) ||
                            !restoreState.IsValid ||
                            restoreState.LayerId != layer.Compiled.LayerId ||
                            restoreState.StateId != state.Compiled.StateId ||
                            !state.TryPrepareRestoreCandidate(restoreState))
                        {
                            if (!diagnostic.IsError)
                            {
                                diagnostic = RestoreError(
                                    "A Graph State restore record or prepared ActivationMemory is inconsistent.");
                            }

                            preparedClock.Cancel();
                            return false;
                        }

                        if (restoreState.IsOnActivePath && state.Compiled.IsLeaf)
                        {
                            activeLeafIndex = stateIndex;
                            activeLeafCount++;
                        }
                    }

                    if (activeLeafCount != 1 ||
                        !TryPrepareRestoredLayer(layer, activeLeafIndex, out diagnostic))
                    {
                        preparedClock.Cancel();
                        return false;
                    }
                }

                if (!TryValidateCommittedMemoryIntegrity(null, out diagnostic))
                {
                    preparedClock.Cancel();
                    return false;
                }

                _candidateNextActivationValue = nextActivationValue;
                _restoreGeneration = _restoreGeneration == ulong.MaxValue
                    ? 1UL
                    : _restoreGeneration + 1UL;
                _activeRestoreToken = _restoreGeneration;
                _preparedClockRestore = preparedClock;
                preparedRestore = new CoCoPreparedGraphRestore(
                    this,
                    _activeRestoreToken,
                    resumedTickFrame);
                preparedStates = true;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception)
            {
                diagnostic = RestoreError(
                    "Graph restore preparation threw while rebuilding candidate ActivationMemory.");
                return false;
            }
            finally
            {
                if (!preparedStates)
                {
                    preparedClock.Cancel();
                }
            }
        }

        internal bool IsPreparedRestoreTokenCurrent(ulong token) =>
            token != 0UL &&
            token == _activeRestoreToken &&
            _preparedClockRestore.IsValid;

        internal void ApplyPreparedRestoreNoFail()
        {
            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
                {
                    layer.States[stateIndex].CommitCandidate();
                }

                layer.CommittedLeafIndex = layer.CandidateLeafIndex;
                layer.CommittedEnterStartDepth = layer.CandidateEnterStartDepth;
                layer.CommittedWinnerTransitionId = default;
            }

            _nextActivationValue = _candidateNextActivationValue;
            _preparedClockRestore.ApplyNoFail();
            _preparedClockRestore = default;
            _activeRestoreToken = 0UL;
        }

        internal bool CancelPreparedRestore(ulong token)
        {
            if (!IsPreparedRestoreTokenCurrent(token))
            {
                return false;
            }

            _preparedClockRestore.Cancel();
            _preparedClockRestore = default;
            _activeRestoreToken = 0UL;
            return true;
        }

        private bool TryValidateRestoreCore(
            ICoCoStateGraphContextRuntime contextRuntime,
            CoCoContextRestoreReadView source,
            in CoCoTickFrame resumedTickFrame,
            out ulong nextActivationValue,
            out CoCoDiagnostic diagnostic)
        {
            nextActivationValue = 0UL;
            if (contextRuntime == null ||
                (_lifecycle != CoCoRuntimeLifecycleState.Running &&
                 _lifecycle != CoCoRuntimeLifecycleState.Suspended) ||
                HasStagedStep ||
                HasPreparedRestore ||
                _isExecutingStep ||
                _disposeRequested ||
                _isDisposed ||
                !source.IsValid ||
                source.Header.Identity.GraphInstanceId != _graphInstanceId)
            {
                diagnostic = RestoreError(
                    "Graph restore validation requires one idle Runtime at a resolved Tick boundary and an alive frame from the same GraphInstance.");
                return false;
            }

            if (!_clock.TryValidateRestore(
                    _transitionHandleOwner,
                    resumedTickFrame,
                    out diagnostic) ||
                !contextRuntime.TryValidateRestore(
                    source,
                    out nextActivationValue,
                    out diagnostic) ||
                nextActivationValue == 0UL ||
                nextActivationValue == ulong.MaxValue)
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = RestoreError(
                        "Graph Context records cannot rebuild a complete restore candidate.");
                }

                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareStagedCommit(
            in CoCoStagedGraphStep stagedStep,
            ICoCoStateGraphCommitGuard commitGuard,
            out CoCoPreparedGraphCommit preparedCommit,
            out CoCoDiagnostic diagnostic)
        {
            preparedCommit = default;
            if (_lifecycle != CoCoRuntimeLifecycleState.Running ||
                IsFaulted ||
                _isExecutingStep ||
                _disposeRequested ||
                _activePreparedCommitToken != 0UL ||
                !Owns(stagedStep))
            {
                diagnostic = LifecycleError(
                    "Only a healthy Running Runtime may prepare its current staged Tick once.");
                return false;
            }

            bool memoryIntegrity = TryValidateStagedMemoryIntegrity(out diagnostic);
            if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
            {
                CancelOutstandingStep(false, default);
                diagnostic = LifecycleError(
                    "The staged Tick was cancelled by its Host before commit preparation.");
                return false;
            }

            if (!memoryIntegrity)
            {
                CancelOutstandingStep(false, default);
                LatchFault(diagnostic);
                return false;
            }

            if (!_finalizedOperationFrame.IsCommitReady)
            {
                diagnostic = OperationError(
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Finalized OperationFrame is not ready for the authority barrier.");
                CancelOutstandingStep(false, default);
                LatchFault(diagnostic);
                return false;
            }

            if (!_clock.IsCommitReady(_transitionHandleOwner, _stagedTickFrame))
            {
                diagnostic = LifecycleError(
                    "The Runtime Clock is not ready for the authority barrier.");
                CancelOutstandingStep(false, default);
                LatchFault(diagnostic);
                return false;
            }

            _activePreparedCommitToken = _activeStageToken;
            preparedCommit = new CoCoPreparedGraphCommit(
                this,
                _activePreparedCommitToken,
                _stagedTickFrame);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void CommitPreparedStep(in CoCoPreparedGraphCommit preparedCommit)
        {
            if (!Owns(preparedCommit))
            {
                throw new InvalidOperationException(
                    "The prepared Graph commit token is no longer ready.");
            }

            preparedCommit.CommitNoFail();
        }

        internal void CommitPreparedStepNoFail()
        {
            // TryPrepareStagedCommit establishes token ownership, memory integrity,
            // OperationFrame readiness, and exact Clock candidate identity. The Host
            // enters this unchecked method only after every other Actor participant
            // preflights. The proof is intentionally not revalidated inside the barrier.
            _finalizedOperationFrame.CommitPreparedNoFail();

            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
                {
                    layer.States[stateIndex].CommitCandidate();
                }

                layer.CommittedLeafIndex = layer.CandidateLeafIndex;
                layer.CommittedEnterStartDepth = layer.CandidateEnterStartDepth;
                layer.CommittedWinnerTransitionId = layer.CandidateWinnerTransitionId;
            }

            _nextActivationValue = _candidateNextActivationValue;
            _clock.CommitPreparedNoFail();
            ClearStagedStep();
        }

        internal bool TryAcceptStagedStep(
            in CoCoStagedGraphStep stagedStep,
            out CoCoDiagnostic diagnostic) =>
            TryAcceptStagedStep(stagedStep, null, out diagnostic);

        internal bool TryAcceptStagedStep(
            in CoCoStagedGraphStep stagedStep,
            ICoCoStateGraphCommitGuard commitGuard,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryPrepareStagedCommit(
                    stagedStep,
                    commitGuard,
                    out CoCoPreparedGraphCommit preparedCommit,
                    out diagnostic))
            {
                return false;
            }

            preparedCommit.CommitNoFail();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCancelStagedStep(
            in CoCoStagedGraphStep stagedStep,
            out CoCoDiagnostic diagnostic)
        {
            if (!Owns(stagedStep))
            {
                diagnostic = LifecycleError(
                    "The staged Tick is stale or belongs to another Runtime.");
                return false;
            }

            CancelOutstandingStep(false, default);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryRejectStagedStep(
            in CoCoStagedGraphStep stagedStep,
            CoCoDiagnostic reason,
            bool latchFault,
            out CoCoDiagnostic diagnostic) =>
            TryRejectStagedStep(stagedStep, reason, latchFault, null, out diagnostic);

        internal bool TryRejectStagedStep(
            in CoCoStagedGraphStep stagedStep,
            CoCoDiagnostic reason,
            bool latchFault,
            ICoCoStateGraphCommitGuard commitGuard,
            out CoCoDiagnostic diagnostic)
        {
            if (!Owns(stagedStep))
            {
                diagnostic = LifecycleError("The staged Tick is stale or belongs to another Runtime.");
                return false;
            }

            bool memoryIntegrity = TryValidateStagedMemoryIntegrity(out CoCoDiagnostic memoryDiagnostic);
            if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
            {
                CancelOutstandingStep(false, default);
                diagnostic = LifecycleError(
                    "The staged Tick rejection was cancelled by its Host before resolution.");
                return true;
            }

            CancelOutstandingStep(false, default);
            if (!memoryIntegrity)
            {
                diagnostic = memoryDiagnostic;
                LatchFault(diagnostic);
            }
            else if (latchFault)
            {
                diagnostic = reason.IsError
                    ? reason
                    : OperationError(
                        CoCoDiagnosticCode.CommitCancelled,
                        "The candidate Tick was rejected by the transaction coordinator.");
                LatchFault(diagnostic);
            }
            else
            {
                diagnostic = CoCoDiagnostic.None;
            }

            return true;
        }

        internal bool TryLatchExternalFault(CoCoDiagnostic diagnostic)
        {
            if (_isDisposed ||
                _lifecycle == CoCoRuntimeLifecycleState.Stopped ||
                _lifecycle == CoCoRuntimeLifecycleState.Disposed ||
                !diagnostic.IsError)
            {
                return false;
            }

            if (!_isExecutingStep)
            {
                CancelOutstandingStep(false, default);
            }

            LatchFault(diagnostic);
            return true;
        }

        internal bool TryPrepareTemporalRecovery(
            out CoCoPreparedTemporalRecovery prepared,
            out CoCoDiagnostic diagnostic)
        {
            prepared = default;
            if (!_fault.IsFaulted ||
                (_lifecycle != CoCoRuntimeLifecycleState.Running &&
                 _lifecycle != CoCoRuntimeLifecycleState.Suspended) ||
                HasStagedStep ||
                HasPreparedRestore ||
                _isExecutingStep ||
                _disposeRequested ||
                _isDisposed ||
                _activeTemporalRecoveryToken != 0UL ||
                _temporalRecoveryGeneration == ulong.MaxValue)
            {
                diagnostic = RestoreError(
                    "Temporal Fault recovery requires one faulted, idle Runtime boundary.");
                return false;
            }

            _activeTemporalRecoveryToken = ++_temporalRecoveryGeneration;
            prepared = new CoCoPreparedTemporalRecovery(
                this,
                _activeTemporalRecoveryToken);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool IsTemporalRecoveryTokenCurrent(ulong token) =>
            !_isDisposed &&
            _fault.IsFaulted &&
            token != 0UL &&
            token == _activeTemporalRecoveryToken;

        internal void CompleteTemporalRecoveryNoFail(ulong token)
        {
            if (!IsTemporalRecoveryTokenCurrent(token))
            {
                return;
            }

            _fault = default;
            _activeTemporalRecoveryToken = 0UL;
        }

        internal bool CancelTemporalRecovery(ulong token)
        {
            if (!IsTemporalRecoveryTokenCurrent(token))
            {
                return false;
            }

            _activeTemporalRecoveryToken = 0UL;
            return true;
        }

        internal int GetCommittedPathCount(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layers.Length)
            {
                return 0;
            }

            int leafIndex = _layers[layerIndex].CommittedLeafIndex;
            return leafIndex >= 0
                ? _layers[layerIndex].Compiled.States[leafIndex].RootPathStateIndices.Count
                : 0;
        }

        internal CoCoStateId GetCommittedLeafId(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _layers.Length)
            {
                return default;
            }

            LayerRuntime layer = _layers[layerIndex];
            return layer.CommittedLeafIndex >= 0
                ? layer.Compiled.States[layer.CommittedLeafIndex].StateId
                : default;
        }

        internal CoCoStateId GetCommittedPathStateId(int layerIndex, int pathIndex)
        {
            LayerRuntime layer = _layers[layerIndex];
            IReadOnlyList<int> path = layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices;
            if (pathIndex < 0 || pathIndex >= path.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(pathIndex));
            }

            return layer.Compiled.States[path[pathIndex]].StateId;
        }

        internal int CommittedTraceLayerCount => _layers.Length;

        internal CoCoLayerId GetCommittedTraceLayerId(int layerIndex) =>
            _layers[layerIndex].Compiled.LayerId;

        internal int GetCommittedTracePathCount(int layerIndex)
        {
            LayerRuntime layer = _layers[layerIndex];
            return layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices.Count;
        }

        internal CoCoStateId GetCommittedTracePathStateId(int layerIndex, int pathIndex)
        {
            LayerRuntime layer = _layers[layerIndex];
            IReadOnlyList<int> path =
                layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices;
            return layer.Compiled.States[path[pathIndex]].StateId;
        }

        internal CoCoTransitionId GetCommittedTraceWinnerTransitionId(int layerIndex) =>
            _layers[layerIndex].CommittedWinnerTransitionId;

        internal void AppendCommittedStateTrace(
            CoCoStateFlowTraceBuffer trace,
            in CoCoTickFrame tickFrame)
        {
            if (trace == null)
            {
                return;
            }

            for (int layerIndex = 0; layerIndex < CommittedTraceLayerCount; layerIndex++)
            {
                CoCoLayerId layerId = GetCommittedTraceLayerId(layerIndex);
                int pathCount = GetCommittedTracePathCount(layerIndex);
                for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
                {
                    trace.Append(CoCoStateFlowTraceEntry.Path(
                        _graphInstanceId,
                        tickFrame,
                        layerId,
                        GetCommittedTracePathStateId(layerIndex, pathIndex)));
                }

                CoCoTransitionId winner =
                    GetCommittedTraceWinnerTransitionId(layerIndex);
                if (winner.IsValid)
                {
                    trace.Append(CoCoStateFlowTraceEntry.Transition(
                        _graphInstanceId,
                        tickFrame,
                        layerId,
                        winner));
                }
            }
        }

        internal void AppendStagedTransitionTrace(
            CoCoStateFlowTraceBuffer trace,
            in CoCoStagedGraphStep stagedStep,
            CoCoStateFlowTraceFrameReference previousContext)
        {
            if (trace == null || !Owns(stagedStep))
            {
                return;
            }

            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                for (int transitionIndex = 0;
                     transitionIndex < layer.CandidateAcceptedTransitions.Length;
                     transitionIndex++)
                {
                    if (!layer.CandidateAcceptedTransitions[transitionIndex])
                    {
                        continue;
                    }

                    trace.Append(CoCoStateFlowTraceEntry.Transition(
                        _graphInstanceId,
                        stagedStep.TickFrame,
                        layer.Compiled.LayerId,
                        layer.Compiled.Transitions[transitionIndex].TransitionId,
                        CoCoStateFlowTransitionRole.Candidate,
                        previousContext));
                }
            }

            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                if (!layer.CandidateWinnerTransitionId.IsValid)
                {
                    continue;
                }

                trace.Append(CoCoStateFlowTraceEntry.Transition(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    layer.Compiled.LayerId,
                    layer.CandidateWinnerTransitionId,
                    CoCoStateFlowTransitionRole.Winner,
                    previousContext));
            }
        }

        internal void AppendCommittedPathTrace(
            CoCoStateFlowTraceBuffer trace,
            in CoCoTickFrame tickFrame,
            CoCoStateFlowTraceFrameReference frame)
        {
            if (trace == null)
            {
                return;
            }

            for (int layerIndex = 0; layerIndex < CommittedTraceLayerCount; layerIndex++)
            {
                CoCoLayerId layerId = GetCommittedTraceLayerId(layerIndex);
                int pathCount = GetCommittedTracePathCount(layerIndex);
                for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
                {
                    trace.Append(CoCoStateFlowTraceEntry.Path(
                        _graphInstanceId,
                        tickFrame,
                        layerId,
                        GetCommittedTracePathStateId(layerIndex, pathIndex),
                        frame));
                }
            }
        }

        private bool ExecuteLayers(
            in CoCoTickFrame tickFrame,
            ICoCoIntentFrame intents,
            in CoCoContextFrameReadView previousContext,
            out CoCoDiagnostic diagnostic)
        {
            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                Array.Clear(_requestedTransitionsScratch, 0, layer.Compiled.Transitions.Count);
                IReadOnlyList<int> sourcePath =
                    layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices;
                for (int depth = 0; depth < sourcePath.Count; depth++)
                {
                    StateRuntime state = layer.States[sourcePath[depth]];
                    state.CandidateLocalSeconds += tickFrame.DeltaTime;
                }

                if (layer.CommittedEnterStartDepth >= 0)
                {
                    for (int depth = layer.CommittedEnterStartDepth; depth < sourcePath.Count; depth++)
                    {
                        StateRuntime state = layer.States[sourcePath[depth]];
                        if (state.Logic is ICoCoStateEnter &&
                            !InvokeState(
                                StateCallbackPhase.Enter,
                                layerIndex,
                                sourcePath[depth],
                                depth,
                                false,
                                tickFrame,
                                intents,
                                previousContext,
                                out diagnostic))
                        {
                            return false;
                        }
                    }
                }

                for (int depth = 0; depth < sourcePath.Count; depth++)
                {
                    int stateIndex = sourcePath[depth];
                    StateRuntime state = layer.States[stateIndex];
                    if (!(state.Logic is ICoCoStateUpdate))
                    {
                        diagnostic = StateError("Every State logic must provide Update.");
                        return false;
                    }

                    if (!InvokeState(
                            StateCallbackPhase.Update,
                            layerIndex,
                            stateIndex,
                            depth,
                            stateIndex == layer.CommittedLeafIndex,
                            tickFrame,
                            intents,
                            previousContext,
                            out diagnostic))
                    {
                        return false;
                    }
                }

                if (!TryChooseWinner(
                        layerIndex,
                        tickFrame,
                        intents,
                        previousContext,
                        out int winnerIndex,
                        out diagnostic))
                {
                    return false;
                }

                if (winnerIndex < 0)
                {
                    layer.CandidateLeafIndex = layer.CommittedLeafIndex;
                    layer.CandidateEnterStartDepth = -1;
                    continue;
                }

                CoCoCompiledTransition winner = layer.Compiled.Transitions[winnerIndex];
                IReadOnlyList<int> targetPath =
                    layer.Compiled.States[winner.TargetStateIndex].RootPathStateIndices;
                int commonCount = CountCommonPrefix(sourcePath, targetPath);
                if (winner.SourceStateIndex == winner.TargetStateIndex)
                {
                    commonCount = Math.Max(0, sourcePath.Count - 1);
                }

                for (int depth = sourcePath.Count - 1; depth >= commonCount; depth--)
                {
                    int stateIndex = sourcePath[depth];
                    StateRuntime state = layer.States[stateIndex];
                    if (state.Logic is ICoCoStateExit &&
                        !InvokeState(
                            StateCallbackPhase.Exit,
                            layerIndex,
                            stateIndex,
                            depth,
                            false,
                            tickFrame,
                            intents,
                            previousContext,
                            out diagnostic))
                    {
                        return false;
                    }
                }

                for (int depth = commonCount; depth < targetPath.Count; depth++)
                {
                    StateRuntime state = layer.States[targetPath[depth]];
                    state.Factory.ResetMemory(state.CandidateMemory);
                    state.CaptureCandidateMemoryFingerprint();
                    if (!TryTakeCandidateActivation(out CoCoActivationId activationId))
                    {
                        diagnostic = StateError("State activation identity capacity is exhausted.");
                        return false;
                    }

                    state.CandidateActivationId = activationId;
                    state.CandidateLocalSeconds = 0d;
                    state.CandidateActionProgress = 0d;
                }

                layer.CandidateLeafIndex = winner.TargetStateIndex;
                layer.CandidateEnterStartDepth = commonCount;
                layer.CandidateWinnerTransitionId = winner.TransitionId;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool InvokeState(
            StateCallbackPhase phase,
            int layerIndex,
            int stateIndex,
            int pathDepth,
            bool canRequestTransition,
            in CoCoTickFrame tickFrame,
            ICoCoIntentFrame intents,
            in CoCoContextFrameReadView previousContext,
            out CoCoDiagnostic diagnostic)
        {
            LayerRuntime layer = _layers[layerIndex];
            StateRuntime state = layer.States[stateIndex];
            if (!state.IsCandidateMemoryFingerprintCurrent)
            {
                diagnostic = StateError(
                    "ActivationMemory changed outside its owning State callback.");
                return false;
            }

            if (!CoCoOperationWriteRank.TryCreate(layerIndex, pathDepth, out CoCoOperationWriteRank rank))
            {
                diagnostic = OperationError(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "State callback composition rank is invalid.");
                return false;
            }

            CoCoStateExecutionContext stateContext = state.ExecutionContext;
            if (!_stateCallbackOperationLease.TryBegin())
            {
                diagnostic = StateError("State callback Operation lease is already active.");
                return false;
            }

            double resultActionProgress;
            bool contextHasError;
            try
            {
                stateContext.Prepare(
                    this,
                    state.CandidateMemory,
                    state.Compiled.Config,
                    intents,
                    previousContext,
                    tickFrame,
                    _operationWriter,
                    rank,
                    state.AllowedOperationSections,
                    _stateCallbackOperationLease,
                    layer.Compiled.LayerId,
                    state.Compiled.StateId,
                    state.CandidateActivationId,
                    state.CommittedLocalSeconds,
                    state.CandidateLocalSeconds,
                    state.CommittedActionProgress,
                    state.CandidateActionProgress,
                    canRequestTransition,
                    state.Compiled.Descriptor.ProvidesActionProgress);
                _executingLayerIndex = layerIndex;
                _executingStateIndex = stateIndex;
                _acceptsTransitionRequests = canRequestTransition;
                switch (phase)
                {
                    case StateCallbackPhase.Enter:
                        ((ICoCoStateEnter)state.Logic).OnEnter(stateContext);
                        break;
                    case StateCallbackPhase.Update:
                        ((ICoCoStateUpdate)state.Logic).Update(stateContext);
                        break;
                    case StateCallbackPhase.Exit:
                        ((ICoCoStateExit)state.Logic).OnExit(stateContext);
                        break;
                    default:
                        throw new InvalidOperationException("State callback phase is invalid.");
                }

                resultActionProgress = stateContext.ResultActionProgress;
                contextHasError = stateContext.HasError ||
                                  _stateCallbackOperationLease.HasError;
            }
            finally
            {
                stateContext.Clear();
                _stateCallbackOperationLease.End();
                _acceptsTransitionRequests = false;
                _executingLayerIndex = -1;
                _executingStateIndex = -1;
            }

            if (_disposeRequested)
            {
                diagnostic = LifecycleError("Dispose was requested during State callback execution.");
                return false;
            }

            if (IsFaulted)
            {
                diagnostic = _fault.Diagnostic;
                return false;
            }

            state.CandidateActionProgress = resultActionProgress;
            if (contextHasError)
            {
                diagnostic = StateError(
                    "State callback used an invalid Memory type, ActionProgress value, Transition Handle, or Operation write.");
                return false;
            }

            state.CaptureCandidateMemoryFingerprint();

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryChooseWinner(
            int layerIndex,
            in CoCoTickFrame tickFrame,
            ICoCoIntentFrame intents,
            in CoCoContextFrameReadView previousContext,
            out int winnerIndex,
            out CoCoDiagnostic diagnostic)
        {
            LayerRuntime layer = _layers[layerIndex];
            StateRuntime leaf = layer.States[layer.CommittedLeafIndex];
            winnerIndex = -1;
            int winningPriority = int.MinValue;
            for (int transitionIndex = 0;
                 transitionIndex < layer.Compiled.Transitions.Count;
                 transitionIndex++)
            {
                if (!_requestedTransitionsScratch[transitionIndex])
                {
                    continue;
                }

                CoCoCompiledTransition transition = layer.Compiled.Transitions[transitionIndex];
                if (transition.SourceStateIndex != layer.CommittedLeafIndex ||
                    !WindowMatches(transition.Window, leaf))
                {
                    continue;
                }

                ConditionRuntime[] conditions = layer.ConditionsByTransition[transitionIndex];
                bool accepted = true;
                for (int conditionIndex = 0; conditionIndex < conditions.Length; conditionIndex++)
                {
                    ConditionRuntime condition = conditions[conditionIndex];
                    _conditionContext.Prepare(
                        tickFrame,
                        intents,
                        previousContext,
                        condition.Compiled.Config,
                        layer.Compiled.LayerId,
                        leaf.Compiled.StateId,
                        transition.TransitionId,
                        leaf.CommittedLocalSeconds,
                        leaf.CandidateLocalSeconds,
                        leaf.CommittedActionProgress,
                        leaf.CandidateActionProgress);
                    if (!condition.Evaluator.Evaluate(_conditionContext))
                    {
                        accepted = false;
                        break;
                    }
                }

                if (accepted && (winnerIndex < 0 || transition.Priority > winningPriority))
                {
                    winningPriority = transition.Priority;
                    winnerIndex = transitionIndex;
                }

                if (accepted)
                {
                    layer.CandidateAcceptedTransitions[transitionIndex] = true;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryPrepareCandidateState(out CoCoDiagnostic diagnostic)
        {
            _candidateNextActivationValue = _nextActivationValue;
            try
            {
                for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
                {
                    LayerRuntime layer = _layers[layerIndex];
                    for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
                    {
                        if (!layer.States[stateIndex].TryPrepareCandidate())
                        {
                            diagnostic = StateError(
                                "ActivationMemory copy changed committed state or produced a non-equivalent candidate.");
                            return false;
                        }
                    }

                    layer.CandidateLeafIndex = layer.CommittedLeafIndex;
                    layer.CandidateEnterStartDepth = layer.CommittedEnterStartDepth;
                    layer.CandidateWinnerTransitionId = default;
                    Array.Clear(
                        layer.CandidateAcceptedTransitions,
                        0,
                        layer.CandidateAcceptedTransitions.Length);
                }
            }
            catch (Exception)
            {
                diagnostic = StateError("ActivationMemory copy failed while preparing the candidate Tick.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool TryPrepareRestoredLayer(
            LayerRuntime layer,
            int activeLeafIndex,
            out CoCoDiagnostic diagnostic)
        {
            IReadOnlyList<int> path =
                layer.Compiled.States[activeLeafIndex].RootPathStateIndices;
            int activeCount = 0;
            int enterPendingStart = -1;
            for (int stateIndex = 0; stateIndex < layer.States.Length; stateIndex++)
            {
                StateRuntime state = layer.States[stateIndex];
                int pathDepth = FindPathDepth(path, stateIndex);
                bool expectedActive = pathDepth >= 0;
                if (state.RestoreIsOnActivePath != expectedActive ||
                    state.RestoreEnterPending && !expectedActive)
                {
                    diagnostic = RestoreError(
                        "Restored Graph active markers do not form one compiled Root-to-Leaf path.");
                    return false;
                }

                if (!expectedActive)
                {
                    continue;
                }

                activeCount++;
                if (state.RestoreEnterPending)
                {
                    if (enterPendingStart < 0)
                    {
                        enterPendingStart = pathDepth;
                    }
                }
                else if (enterPendingStart >= 0)
                {
                    diagnostic = RestoreError(
                        "Restored EnterPending markers must be one active-path suffix.");
                    return false;
                }
            }

            if (activeCount != path.Count)
            {
                diagnostic = RestoreError(
                    "Restored Graph active markers do not cover the exact compiled path.");
                return false;
            }

            layer.CandidateLeafIndex = activeLeafIndex;
            layer.CandidateEnterStartDepth = enterPendingStart;
            layer.CandidateWinnerTransitionId = default;
            Array.Clear(
                layer.CandidateAcceptedTransitions,
                0,
                layer.CandidateAcceptedTransitions.Length);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool ValidateIntentFrame(
            in CoCoTickFrame tickFrame,
            ICoCoIntentFrame intents,
            out CoCoDiagnostic diagnostic)
        {
            if (_graph.IntentRequirements.Count == 0)
            {
                if (intents == null)
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }
            }
            else if (intents == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "This StateGraph requires a frozen IntentFrame for every Tick.");
                return false;
            }

            if (!intents.IsFrozen ||
                intents.GraphInstanceId != _graphInstanceId ||
                intents.LayoutId != _graph.IntentRequirements.LayoutId ||
                intents.Header.TickFrame != tickFrame)
            {
                diagnostic = Error(
                    CoCoDiagnosticDomain.Intent,
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "IntentFrame must be frozen for this GraphInstance, layout, and exact Tick.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryTakeActivation(out CoCoActivationId activationId)
        {
            if (_nextActivationValue == 0UL || _nextActivationValue == ulong.MaxValue)
            {
                activationId = default;
                return false;
            }

            if (!CoCoActivationId.TryCreate(_nextActivationValue, out activationId))
            {
                return false;
            }

            _nextActivationValue++;
            return true;
        }

        private bool TryTakeCandidateActivation(out CoCoActivationId activationId)
        {
            if (_candidateNextActivationValue == 0UL ||
                _candidateNextActivationValue == ulong.MaxValue ||
                !CoCoActivationId.TryCreate(_candidateNextActivationValue, out activationId))
            {
                activationId = default;
                return false;
            }

            _candidateNextActivationValue++;
            return true;
        }

        private bool Owns(in CoCoStagedGraphStep stagedStep) =>
            ReferenceEquals(stagedStep.Runtime, this) &&
            stagedStep.Token != 0UL &&
            stagedStep.Token == _activeStageToken;

        private bool Owns(in CoCoPreparedGraphCommit preparedCommit) =>
            ReferenceEquals(preparedCommit.Runtime, this) &&
            IsPreparedCommitTokenCurrent(preparedCommit.Token);

        private void CancelOutstandingStep(bool latchFault, CoCoDiagnostic diagnostic)
        {
            if (HasStagedStep)
            {
                _finalizedOperationFrame.Cancel();
                _clock.Cancel(_transitionHandleOwner, _stagedTickFrame);
                ClearStagedStep();
            }

            if (HasPreparedRestore)
            {
                _preparedClockRestore.Cancel();
                _preparedClockRestore = default;
                _activeRestoreToken = 0UL;
            }

            if (latchFault && diagnostic.IsError)
            {
                LatchFault(diagnostic);
            }
        }

        private void ClearStagedStep()
        {
            _activePreparedCommitToken = 0UL;
            _activeStageToken = 0UL;
            _stagedTickFrame = default;
            _finalizedOperationFrame = default;
            _operationWriter = default;
        }

        private void EndFailedStep(
            in CoCoTickFrame tickFrame,
            bool cancelOperation,
            CoCoDiagnostic diagnostic)
        {
            if (cancelOperation)
            {
                _operationWriter.Cancel();
            }

            _clock.Cancel(_transitionHandleOwner, tickFrame);
            _isExecutingStep = false;
            if (_disposeRequested)
            {
                _disposeRequested = false;
                DisposeAtSafeBoundary();
                return;
            }

            LatchFault(diagnostic);
        }

        private void DisposeAtSafeBoundary()
        {
            if (_lifecycle == CoCoRuntimeLifecycleState.Running ||
                _lifecycle == CoCoRuntimeLifecycleState.Suspended)
            {
                TransitionLifecycleTo(CoCoRuntimeLifecycleState.Stopped);
            }

            if (_lifecycle == CoCoRuntimeLifecycleState.Created ||
                _lifecycle == CoCoRuntimeLifecycleState.Stopped)
            {
                TransitionLifecycleTo(CoCoRuntimeLifecycleState.Disposed);
            }

            _isDisposed = _lifecycle == CoCoRuntimeLifecycleState.Disposed;
        }

        private void TransitionLifecycleTo(CoCoRuntimeLifecycleState nextState)
        {
            if (!_lifecycle.CanTransitionTo(nextState))
            {
                throw new InvalidOperationException(
                    $"Illegal StateGraph Runtime lifecycle transition: {_lifecycle} -> {nextState}.");
            }

            _lifecycle = nextState;
        }

        private bool TryValidateCandidateMemory(out CoCoDiagnostic diagnostic)
        {
            try
            {
                for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
                {
                    StateRuntime[] states = _layers[layerIndex].States;
                    for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                    {
                        if (!states[stateIndex].IsCandidateMemoryFingerprintCurrent)
                        {
                            diagnostic = StateError(
                                "ActivationMemory changed outside its owning State callback.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception)
            {
                diagnostic = StateError("ActivationMemory fingerprint evaluation failed.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateCommittedMemoryIntegrity(
            ICoCoStateGraphCommitGuard commitGuard,
            out CoCoDiagnostic diagnostic)
        {
            try
            {
                for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
                {
                    StateRuntime[] states = _layers[layerIndex].States;
                    for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                    {
                        if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                        {
                            diagnostic = LifecycleError(
                                "Committed ActivationMemory validation was cancelled by its Host before fingerprint evaluation.");
                            return false;
                        }

                        bool fingerprintIsCurrent =
                            states[stateIndex].IsCommittedMemoryFingerprintCurrent;

                        if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                        {
                            diagnostic = LifecycleError(
                                "Committed ActivationMemory validation was cancelled by its Host after fingerprint evaluation.");
                            return false;
                        }

                        if (!fingerprintIsCurrent)
                        {
                            diagnostic = RestoreError(
                                "Restore preparation observed mutated committed ActivationMemory.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception)
            {
                diagnostic = commitGuard != null && commitGuard.IsCommitCancellationRequested
                    ? LifecycleError(
                        "Committed ActivationMemory validation was cancelled by its Host during fingerprint evaluation.")
                    : RestoreError(
                        "Committed ActivationMemory fingerprint evaluation failed during restore preparation.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateStagedMemoryIntegrity(out CoCoDiagnostic diagnostic)
        {
            try
            {
                for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
                {
                    StateRuntime[] states = _layers[layerIndex].States;
                    for (int stateIndex = 0; stateIndex < states.Length; stateIndex++)
                    {
                        StateRuntime state = states[stateIndex];
                        if (!state.IsCommittedMemoryFingerprintCurrent ||
                            !state.IsCandidateMemoryFingerprintCurrent)
                        {
                            diagnostic = StateError(
                                "Staged or committed ActivationMemory changed outside the Runtime transaction.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception)
            {
                diagnostic = StateError("ActivationMemory fingerprint evaluation failed.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void LatchFault(CoCoDiagnostic diagnostic)
        {
            if (!_fault.IsFaulted)
            {
                _fault = new CoCoRuntimeFault(
                    diagnostic.IsError
                        ? diagnostic
                        : LifecycleError("StateGraph Runtime faulted without an explicit diagnostic."));
            }
        }

        private static void ReleaseRuntimeInstanceClaims(
            object owner,
            List<object> claimedInstances)
        {
            for (int index = claimedInstances.Count - 1; index >= 0; index--)
            {
                object instance = claimedInstances[index];
                if (instance is CoCoStateLogic logic)
                {
                    logic.ReleaseRuntimeOwner(owner);
                }
                else if (instance is CoCoStateCondition condition)
                {
                    condition.ReleaseRuntimeOwner(owner);
                }
                else if (instance is CoCoActivationMemory memory)
                {
                    memory.ReleaseRuntimeOwner(owner);
                }
            }
        }

        private static bool ValidateFactories(
            CoCoCompiledStateGraph graph,
            CoCoStateGraphLogicBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
            {
                CoCoCompiledStateLayer layer = graph.Layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    CoCoStateDescriptor descriptor = layer.States[stateIndex].Descriptor;
                    if (!bindings.TryGetStateFactory(descriptor.DescriptorId, out ICoCoStateRuntimeFactory factory) ||
                        factory.LogicType != descriptor.LogicType ||
                        factory.ActivationMemoryType != descriptor.ActivationMemoryType)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticDomain.Registry,
                            CoCoDiagnosticCode.DescriptorTypeMismatch,
                            "State runtime bindings do not exactly match the compiled Graph.");
                        return false;
                    }
                }

                for (int transitionIndex = 0;
                     transitionIndex < layer.Transitions.Count;
                     transitionIndex++)
                {
                    CoCoCompiledTransition transition = layer.Transitions[transitionIndex];
                    for (int conditionIndex = 0;
                         conditionIndex < transition.Conditions.Count;
                         conditionIndex++)
                    {
                        CoCoConditionDescriptor descriptor = transition.Conditions[conditionIndex].Descriptor;
                        if (!bindings.TryGetConditionFactory(
                                descriptor.DescriptorId,
                                out ICoCoConditionRuntimeFactory factory) ||
                            factory.ConditionType != descriptor.ConditionType)
                        {
                            diagnostic = Error(
                                CoCoDiagnosticDomain.Registry,
                                CoCoDiagnosticCode.DescriptorTypeMismatch,
                                "Condition runtime bindings do not exactly match the compiled Graph.");
                            return false;
                        }
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool ValidateOperationFrame(
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId,
            CoCoOperationFrame operationFrame,
            out CoCoDiagnostic diagnostic)
        {
            CoCoOperationSectionRegistry registry = operationFrame.Registry;
            CoCoGraphOperationProvidesManifest manifest = graph.OperationProvides;
            if (registry == null ||
                operationFrame.GraphInstanceId != graphInstanceId ||
                !registry.IsFrozen ||
                registry.LayoutId != manifest.LayoutId ||
                registry.Count != manifest.Count)
            {
                diagnostic = OperationError(
                    CoCoDiagnosticCode.MissingOperationSection,
                    "OperationFrame layout does not exactly match the compiled Graph manifest.");
                return false;
            }

            for (int provideIndex = 0; provideIndex < manifest.Provides.Count; provideIndex++)
            {
                CoCoGraphOperationProvideRequirement provide = manifest.Provides[provideIndex];
                bool matched = false;
                for (int sectionIndex = 0; sectionIndex < registry.Sections.Count; sectionIndex++)
                {
                    CoCoOperationSectionDescriptor section = registry.Sections[sectionIndex];
                    if (section.SectionId == provide.SectionId &&
                        section.Mode == provide.Mode &&
                        section.SectionType == provide.SectionType &&
                        section.Shape != null &&
                        provide.Shape != null &&
                        section.Shape.ShapeFingerprint == provide.Shape.ShapeFingerprint &&
                        section.Shape.Equals(provide.Shape))
                    {
                        matched = true;
                        break;
                    }
                }

                if (!matched)
                {
                    diagnostic = OperationError(
                        CoCoDiagnosticCode.MissingOperationSection,
                        "OperationFrame is missing a compiled Graph Operation Section.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static LayerRuntime BuildLayer(
            object owner,
            CoCoGraphInstanceId graphInstanceId,
            CoCoCompiledStateLayer compiledLayer,
            CoCoStateGraphLogicBindings bindings,
            CoCoOperationSectionRegistry operationRegistry,
            int layerIndex,
            List<object> claimedInstances)
        {
            var states = new StateRuntime[compiledLayer.States.Count];
            for (int stateIndex = 0; stateIndex < compiledLayer.States.Count; stateIndex++)
            {
                CoCoCompiledState compiledState = compiledLayer.States[stateIndex];
                bindings.TryGetStateFactory(
                    compiledState.Descriptor.DescriptorId,
                    out ICoCoStateRuntimeFactory factory);
                var handles = new CoCoTransitionHandle[compiledState.OutgoingTransitionCount];
                for (int handleIndex = 0; handleIndex < handles.Length; handleIndex++)
                {
                    int transitionIndex = compiledState.FirstOutgoingTransitionIndex + handleIndex;
                    CoCoCompiledTransition transition = compiledLayer.Transitions[transitionIndex];
                    handles[handleIndex] = new CoCoTransitionHandle(
                        owner,
                        layerIndex,
                        transitionIndex,
                        transition.TransitionId,
                        compiledState.StateId,
                        compiledLayer.States[transition.TargetStateIndex].StateId,
                        transition.Priority);
                }

                var factoryContext = new CoCoStateFactoryContext(
                    graphInstanceId,
                    compiledLayer.LayerId,
                    layerIndex,
                    compiledState.StateId,
                    compiledState.RootPathStateIndices.Count - 1,
                    compiledState.Config,
                    handles);
                CoCoStateLogic logic = factory.CreateLogic(factoryContext);
                CoCoActivationMemory memory0 = factory.CreateMemory();
                CoCoActivationMemory memory1 = factory.CreateMemory();
                if (logic.GetType() != compiledState.Descriptor.LogicType ||
                    !(logic is ICoCoStateUpdate) ||
                    memory0.GetType() != compiledState.Descriptor.ActivationMemoryType ||
                    memory1.GetType() != compiledState.Descriptor.ActivationMemoryType)
                {
                    throw new InvalidOperationException("State runtime factory returned a mismatched instance.");
                }

                if (!logic.TryClaimRuntimeOwner(owner))
                {
                    throw new InvalidOperationException(
                        "State runtime factory reused a Logic instance.");
                }

                claimedInstances.Add(logic);
                if (!memory0.TryClaimRuntimeOwner(owner))
                {
                    throw new InvalidOperationException(
                        "State runtime factory reused an ActivationMemory instance.");
                }

                claimedInstances.Add(memory0);
                if (!memory1.TryClaimRuntimeOwner(owner))
                {
                    throw new InvalidOperationException(
                        "State runtime factory reused an ActivationMemory instance.");
                }

                claimedInstances.Add(memory1);

                factory.ResetMemory(memory0);
                factory.ResetMemory(memory1);
                bool[] allowedOperationSections = BuildAllowedOperationSections(
                    compiledState.Descriptor.OperationProvides,
                    operationRegistry);
                states[stateIndex] = new StateRuntime(
                    compiledState,
                    factory,
                    logic,
                    memory0,
                    memory1,
                    handles,
                    allowedOperationSections);
            }

            var conditionsByTransition = new ConditionRuntime[compiledLayer.Transitions.Count][];
            for (int transitionIndex = 0;
                 transitionIndex < compiledLayer.Transitions.Count;
                 transitionIndex++)
            {
                CoCoCompiledTransition transition = compiledLayer.Transitions[transitionIndex];
                var conditions = new ConditionRuntime[transition.Conditions.Count];
                for (int conditionIndex = 0; conditionIndex < conditions.Length; conditionIndex++)
                {
                    CoCoCompiledCondition compiled = transition.Conditions[conditionIndex];
                    bindings.TryGetConditionFactory(
                        compiled.Descriptor.DescriptorId,
                        out ICoCoConditionRuntimeFactory factory);
                    var context = new CoCoConditionFactoryContext(
                        graphInstanceId,
                        compiledLayer.LayerId,
                        transition.TransitionId,
                        compiled.AuthoringIndex,
                        compiled.Config);
                    CoCoStateCondition condition = factory.CreateCondition(context);
                    if (condition.GetType() != compiled.Descriptor.ConditionType ||
                        !(condition is ICoCoStateConditionEvaluator evaluator))
                    {
                        throw new InvalidOperationException("Condition runtime factory returned a mismatched instance.");
                    }

                    if (!condition.TryClaimRuntimeOwner(owner))
                    {
                        throw new InvalidOperationException(
                            "Condition runtime factory reused a Condition instance.");
                    }

                    claimedInstances.Add(condition);

                    conditions[conditionIndex] = new ConditionRuntime(compiled, evaluator);
                }

                conditionsByTransition[transitionIndex] = conditions;
            }

            return new LayerRuntime(compiledLayer, states, conditionsByTransition);
        }

        private static bool[] BuildAllowedOperationSections(
            IReadOnlyList<CoCoOperationSectionId> operationProvides,
            CoCoOperationSectionRegistry registry)
        {
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "State runtime setup requires an Operation Section Registry.");
            }

            var allowed = new bool[registry.Count];
            for (int provideIndex = 0; provideIndex < operationProvides.Count; provideIndex++)
            {
                CoCoOperationSectionId sectionId = operationProvides[provideIndex];
                int denseIndex = -1;
                for (int sectionIndex = 0; sectionIndex < registry.Sections.Count; sectionIndex++)
                {
                    CoCoOperationSectionDescriptor section = registry.Sections[sectionIndex];
                    if (section.SectionId == sectionId)
                    {
                        denseIndex = section.DenseIndex;
                        break;
                    }
                }

                if (denseIndex < 0 || denseIndex >= allowed.Length || allowed[denseIndex])
                {
                    throw new InvalidOperationException(
                        "State OperationProvides could not be resolved exactly against the Runtime Registry.");
                }

                allowed[denseIndex] = true;
            }

            return allowed;
        }

        private static int ResolveInitialLeaf(CoCoCompiledStateLayer layer, int stateIndex)
        {
            int current = stateIndex;
            int remaining = layer.States.Count;
            while (!layer.States[current].IsLeaf && remaining-- > 0)
            {
                current = layer.States[current].InitialChildStateIndex;
            }

            if (current < 0 || current >= layer.States.Count || !layer.States[current].IsLeaf)
            {
                throw new InvalidOperationException("Compiled Layer has no valid initial leaf.");
            }

            return current;
        }

        private static int CountCommonPrefix(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            int count = Math.Min(left.Count, right.Count);
            int index = 0;
            while (index < count && left[index] == right[index])
            {
                index++;
            }

            return index;
        }

        private static int FindPathDepth(IReadOnlyList<int> path, int stateIndex)
        {
            for (int depth = 0; depth < path.Count; depth++)
            {
                if (path[depth] == stateIndex)
                {
                    return depth;
                }
            }

            return -1;
        }

        private static bool WindowMatches(CoCoTransitionWindow window, StateRuntime leaf)
        {
            switch (window.Mode)
            {
                case CoCoTransitionWindowMode.Always:
                    return true;
                case CoCoTransitionWindowMode.LocalSeconds:
                    return CrossesWindow(
                        leaf.CommittedLocalSeconds,
                        leaf.CandidateLocalSeconds,
                        window.StartInclusive,
                        window.EndExclusive);
                case CoCoTransitionWindowMode.ActionProgress:
                    return CrossesWindow(
                        leaf.CommittedActionProgress,
                        leaf.CandidateActionProgress,
                        window.StartInclusive,
                        window.EndExclusive);
                default:
                    return false;
            }
        }

        private static bool CrossesWindow(double previous, double current, double start, double end) =>
            previous < end && current >= start;

        private static CoCoDiagnostic StateError(string message) =>
            Error(CoCoDiagnosticDomain.State, CoCoDiagnosticCode.DescriptorTypeMismatch, message);

        private static CoCoDiagnostic OperationError(CoCoDiagnosticCode code, string message) =>
            Error(CoCoDiagnosticDomain.Operation, code, message);

        private static CoCoDiagnostic LifecycleError(string message) =>
            Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        private static CoCoDiagnostic RestoreError(string message) =>
            Error(
                CoCoDiagnosticDomain.Restore,
                CoCoDiagnosticCode.InvalidGraphRestore,
                message);

        private static CoCoDiagnostic Error(
            CoCoDiagnosticDomain domain,
            CoCoDiagnosticCode code,
            string message) => CoCoDiagnostic.Error(domain, code, message);

        private sealed class LayerRuntime
        {
            public LayerRuntime(
                CoCoCompiledStateLayer compiled,
                StateRuntime[] states,
                ConditionRuntime[][] conditionsByTransition)
            {
                Compiled = compiled;
                States = states;
                ConditionsByTransition = conditionsByTransition;
                CandidateAcceptedTransitions = new bool[compiled.Transitions.Count];
                CommittedLeafIndex = -1;
                CandidateLeafIndex = -1;
                CommittedEnterStartDepth = -1;
                CandidateEnterStartDepth = -1;
            }

            public CoCoCompiledStateLayer Compiled { get; }
            public StateRuntime[] States { get; }
            public ConditionRuntime[][] ConditionsByTransition { get; }
            public bool[] CandidateAcceptedTransitions { get; }
            public int CommittedLeafIndex { get; set; }
            public int CandidateLeafIndex { get; set; }
            public int CommittedEnterStartDepth { get; set; }
            public int CandidateEnterStartDepth { get; set; }
            public CoCoTransitionId CommittedWinnerTransitionId { get; set; }
            public CoCoTransitionId CandidateWinnerTransitionId { get; set; }
        }

        private sealed class StateRuntime
        {
            private bool _committedUsesMemory1;

            public StateRuntime(
                CoCoCompiledState compiled,
                ICoCoStateRuntimeFactory factory,
                CoCoStateLogic logic,
                CoCoActivationMemory memory0,
                CoCoActivationMemory memory1,
                CoCoTransitionHandle[] handles,
                bool[] allowedOperationSections)
            {
                Compiled = compiled;
                Factory = factory;
                Logic = logic;
                Memory0 = memory0;
                Memory1 = memory1;
                Handles = handles;
                AllowedOperationSections = allowedOperationSections;
                ExecutionContext = new CoCoStateExecutionContext();
            }

            public CoCoCompiledState Compiled { get; }
            public ICoCoStateRuntimeFactory Factory { get; }
            public CoCoStateLogic Logic { get; }
            public CoCoActivationMemory Memory0 { get; }
            public CoCoActivationMemory Memory1 { get; }
            public CoCoTransitionHandle[] Handles { get; }
            public bool[] AllowedOperationSections { get; }
            public CoCoStateExecutionContext ExecutionContext { get; }
            public CoCoActivationMemory CommittedMemory => _committedUsesMemory1 ? Memory1 : Memory0;
            public CoCoActivationMemory CandidateMemory => _committedUsesMemory1 ? Memory0 : Memory1;
            public CoCoActivationId CommittedActivationId { get; set; }
            public CoCoActivationId CandidateActivationId { get; set; }
            public double CommittedLocalSeconds { get; set; }
            public double CandidateLocalSeconds { get; set; }
            public double CommittedActionProgress { get; set; }
            public double CandidateActionProgress { get; set; }
            public ulong CommittedMemoryFingerprint { get; private set; }
            public ulong CandidateMemoryFingerprint { get; private set; }
            public bool RestoreIsOnActivePath { get; private set; }
            public bool RestoreEnterPending { get; private set; }

            public bool TryInitializeMemoryFingerprints()
            {
                CommittedMemoryFingerprint = Factory.GetMemoryFingerprint(CommittedMemory);
                CandidateMemoryFingerprint = Factory.GetMemoryFingerprint(CandidateMemory);
                return CommittedMemoryFingerprint == CandidateMemoryFingerprint;
            }

            public bool TryPrepareCandidate()
            {
                if (!IsCommittedMemoryFingerprintCurrent)
                {
                    return false;
                }

                Factory.CopyMemory(CommittedMemory, CandidateMemory);
                if (!IsCommittedMemoryFingerprintCurrent)
                {
                    return false;
                }

                CandidateMemoryFingerprint = Factory.GetMemoryFingerprint(CandidateMemory);
                if (CandidateMemoryFingerprint != CommittedMemoryFingerprint)
                {
                    return false;
                }

                CandidateActivationId = CommittedActivationId;
                CandidateLocalSeconds = CommittedLocalSeconds;
                CandidateActionProgress = CommittedActionProgress;
                RestoreIsOnActivePath = false;
                RestoreEnterPending = false;
                return true;
            }

            public bool TryPrepareRestoreCandidate(
                in CoCoStateGraphRestoreState restoreState)
            {
                ulong fingerprint = Factory.GetMemoryFingerprint(CandidateMemory);
                if (fingerprint != restoreState.MemoryFingerprint)
                {
                    return false;
                }

                CandidateMemoryFingerprint = fingerprint;
                CandidateActivationId = restoreState.ActivationId;
                CandidateLocalSeconds = restoreState.LocalSeconds;
                CandidateActionProgress = restoreState.ActionProgress;
                RestoreIsOnActivePath = restoreState.IsOnActivePath;
                RestoreEnterPending = restoreState.EnterPending;
                return true;
            }

            public bool IsCommittedMemoryFingerprintCurrent =>
                Factory.GetMemoryFingerprint(CommittedMemory) == CommittedMemoryFingerprint;

            public bool IsCandidateMemoryFingerprintCurrent =>
                Factory.GetMemoryFingerprint(CandidateMemory) == CandidateMemoryFingerprint;

            public void CaptureCandidateMemoryFingerprint()
            {
                CandidateMemoryFingerprint = Factory.GetMemoryFingerprint(CandidateMemory);
            }

            public void CommitCandidate()
            {
                _committedUsesMemory1 = !_committedUsesMemory1;
                CommittedMemoryFingerprint = CandidateMemoryFingerprint;
                CommittedActivationId = CandidateActivationId;
                CommittedLocalSeconds = CandidateLocalSeconds;
                CommittedActionProgress = CandidateActionProgress;
                RestoreIsOnActivePath = false;
                RestoreEnterPending = false;
            }
        }

        private readonly struct ConditionRuntime
        {
            public ConditionRuntime(
                CoCoCompiledCondition compiled,
                ICoCoStateConditionEvaluator evaluator)
            {
                Compiled = compiled;
                Evaluator = evaluator;
            }

            public CoCoCompiledCondition Compiled { get; }
            public ICoCoStateConditionEvaluator Evaluator { get; }
        }

        private enum StateCallbackPhase
        {
            Enter = 1,
            Update = 2,
            Exit = 3
        }
    }
}
