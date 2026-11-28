using System;
using System.Collections;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// Host 驱动每个实例的回调、选边与候选记忆，并在 Operator 和 Context 都准备好后决定提交。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>标记实例步进失败的阶段与诊断，供 Host 决定是否继续。</summary>
    public readonly struct RuntimeFault : IEquatable<RuntimeFault>
    {

        #region Public API

        /// <summary>与本次操作关联的诊断。</summary>
        public CoCoDiagnostic Diagnostic { get; }
        /// <summary>已锁存运行时故障时为真。</summary>
        public bool IsFaulted => Diagnostic.IsError;

        /// <summary>按<c>Diagnostic</c>比较两个值，全部相等时返回 true。</summary>
        /// <param name="other">待比较的同类值。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public bool Equals(RuntimeFault other) => Diagnostic == other.Diagnostic;
        /// <summary>仅当对象属于同类型并且<c>Diagnostic</c>相等时返回 true。</summary>
        /// <param name="obj">待比较的对象。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public override bool Equals(object obj) => obj is RuntimeFault other && Equals(other);
        /// <summary>为哈希集合计算与<c>Diagnostic</c>的相等判定一致的散列值。</summary>
        /// <returns>与相等判定一致的哈希值。</returns>
        public override int GetHashCode() => Diagnostic.GetHashCode();
        /// <summary>比较两端的<c>Diagnostic</c>，相等时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator ==(RuntimeFault left, RuntimeFault right) => left.Equals(right);
        /// <summary>比较两端的<c>Diagnostic</c>，存在差异时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator !=(RuntimeFault left, RuntimeFault right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal RuntimeFault(CoCoDiagnostic diagnostic)
        {
            Diagnostic = diagnostic;
        }

        #endregion
    }

    /// <summary>以从根到当前叶的顺序公开本层活动节点。</summary>
    public sealed class ActivePath : IReadOnlyList<StateId>
    {
        private readonly StateGraphInstance _runtime;
        private readonly int _layerIndex;

        /// <summary>对应的图层身份。</summary>
        public LayerId LayerId { get; }
        /// <summary>当前可读取的条目数量。</summary>
        public int Count => _runtime.GetCommittedPathCount(_layerIndex);
        /// <summary>当前活动路径的叶节点。</summary>
        public StateId ActiveLeaf => _runtime.GetCommittedLeafId(_layerIndex);

        #region Public API

        /// <summary>读取活动路径上给定深度的状态。</summary>
        public StateId this[int index] => _runtime.GetCommittedPathStateId(_layerIndex, index);

        /// <summary>按根到叶的路径顺序提供节点迭代器。</summary>
        /// <returns>从根到活动叶依次遍历的枚举器。</returns>
        public Enumerator GetEnumerator() => new Enumerator(this);
        /// <summary>按根到叶的路径顺序提供节点迭代器。</summary>
        IEnumerator<StateId> IEnumerable<StateId>.GetEnumerator() => GetEnumerator();
        /// <summary>按根到叶的路径顺序提供节点迭代器。</summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>顺序遍历该活动路径已有的节点，不重新计算图状态。</summary>
        public struct Enumerator : IEnumerator<StateId>
        {
            private readonly ActivePath _path;
            private int _index;

            /// <summary>枚举器当前位置的活动状态。</summary>
            public StateId Current => _path[_index];
            object IEnumerator.Current => Current;

            #region Public API

            /// <summary>前进到下一个活动节点，还有可读节点时返回 true。</summary>
            /// <returns>成功移动到另一个活动节点时为真。</returns>
            public bool MoveNext() => ++_index < _path.Count;
            /// <summary>将迭代位置恢复到第一个节点之前，允许再次遍历当前活动路径。</summary>
            public void Reset() => _index = -1;
            /// <summary>结束本次遍历。枚举器不持有需要释放的图实例资源。</summary>
            public void Dispose()
            {
            }

            #endregion

            #region Internal Logic

            internal Enumerator(ActivePath path)
            {
                _path = path;
                _index = -1;
            }

            #endregion
        }

        #endregion

        #region Internal Logic

        internal ActivePath(StateGraphInstance runtime, int layerIndex, LayerId layerId)
        {
            _runtime = runtime;
            _layerIndex = layerIndex;
            LayerId = layerId;
        }

        #endregion
    }

    /// <summary>标识一次尚待 Host 决定的图步进及其候选输出。</summary>
    internal readonly struct StagedGraphStep : IEquatable<StagedGraphStep>
    {
        private readonly StateGraphInstance _runtime;
        private readonly ulong _token;
        private readonly FinalizedOperationFrame _finalizedOperationFrame;

        internal StagedGraphStep(
            StateGraphInstance runtime,
            ulong token,
            in TickFrame tickFrame,
            FinalizedOperationFrame operationFrame)
        {
            _runtime = runtime;
            _token = token;
            _finalizedOperationFrame = operationFrame;
            TickFrame = tickFrame;
        }

        public TickFrame TickFrame { get; }
        public StagedOperationFrame OperationFrame =>
            _finalizedOperationFrame.ReadView;
        public bool IsValid => _runtime != null && _runtime.IsStagedTokenCurrent(_token);

        internal StateGraphInstance Runtime => _runtime;
        internal ulong Token => _token;
        internal FinalizedOperationFrame FinalizedOperationFrame => _finalizedOperationFrame;

        public bool Equals(StagedGraphStep other) =>
            ReferenceEquals(_runtime, other._runtime) &&
            _token == other._token &&
            TickFrame == other.TickFrame;

        public override bool Equals(object obj) => obj is StagedGraphStep other && Equals(other);
        public override int GetHashCode() =>
            unchecked(((_runtime?.GetHashCode() ?? 0) * 397) ^ _token.GetHashCode());
        public static bool operator ==(StagedGraphStep left, StagedGraphStep right) => left.Equals(right);
        public static bool operator !=(StagedGraphStep left, StagedGraphStep right) => !left.Equals(right);
    }

    /// <summary>保存已校验待提交的图候选，保持其与具体实例和代次的关联。</summary>
    internal readonly struct PreparedGraphCommit
    {
        private readonly StateGraphInstance _runtime;
        private readonly ulong _token;

        internal PreparedGraphCommit(
            StateGraphInstance runtime,
            ulong token,
            in TickFrame tickFrame)
        {
            _runtime = runtime;
            _token = token;
            TickFrame = tickFrame;
        }

        internal TickFrame TickFrame { get; }
        internal bool IsValid =>
            _runtime != null && _runtime.IsPreparedCommitTokenCurrent(_token);
        internal StateGraphInstance Runtime => _runtime;
        internal ulong Token => _token;

        internal void CommitNoFail()
        {
            _runtime.CommitPreparedStepNoFail();
        }
    }

    /// <summary>保存重建活动路径、记忆和时钟所需的图恢复状态。</summary>
    internal readonly struct StateGraphRestoreState
    {
        internal StateGraphRestoreState(
            LayerId layerId,
            StateId stateId,
            bool isOnActivePath,
            ActivationId activationId,
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

        internal LayerId LayerId { get; }
        internal StateId StateId { get; }
        internal bool IsOnActivePath { get; }
        internal ActivationId ActivationId { get; }
        internal double LocalSeconds { get; }
        internal double ActionProgress { get; }
        internal bool EnterPending { get; }
        internal ulong MemoryFingerprint { get; }
        internal bool IsValid { get; }
    }

    /// <summary>让实例事务在同一屏障准备、提交或取消 Context。</summary>
    internal interface IStateGraphContextRuntime
    {
        bool TryBeginGraphCapture(
            in StagedGraphStep stagedStep,
            ContextFrameReadView previous,
            in PreparedContextCommit prepared,
            ulong token,
            out CoCoDiagnostic diagnostic);

        bool TryCaptureState(
            int orderedStateIndex,
            StateActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            out CoCoDiagnostic diagnostic);

        bool TryValidateInitialStateDefault(
            int orderedStateIndex,
            StateActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            ContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic);

        bool TryCompleteGraphCapture(out CoCoDiagnostic diagnostic);
        void CancelCapture();

        bool TryValidateRestore(
            ContextRestoreReadView source,
            out ulong nextActivationValue,
            out CoCoDiagnostic diagnostic);

        bool TryPrepareStateRestore(
            int orderedStateIndex,
            ContextRestoreReadView source,
            StateActivationMemory candidateMemory,
            out StateGraphRestoreState state,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>绑定已准备的图恢复候选，供 Host 确认或撤销。</summary>
    internal readonly struct PreparedGraphRestore
    {
        private readonly StateGraphInstance _runtime;
        private readonly ulong _token;

        internal PreparedGraphRestore(
            StateGraphInstance runtime,
            ulong token,
            in TickFrame tickFrame)
        {
            _runtime = runtime;
            _token = token;
            TickFrame = tickFrame;
        }

        internal TickFrame TickFrame { get; }
        internal bool IsValid =>
            _runtime != null && _runtime.IsPreparedRestoreTokenCurrent(_token);

        internal void ApplyNoFail()
        {
            _runtime.ApplyPreparedRestoreNoFail();
        }

        internal bool Cancel() =>
            _runtime != null && _runtime.CancelPreparedRestore(_token);
    }

    /// <summary>允许整步持有者在发布候选前确认其仍可提交。</summary>
    internal interface IStateGraphCommitGuard
    {
        bool IsCommitCancellationRequested { get; }
    }

    /// <summary>关联一次时间恢复所需要的图和 Context 候选。</summary>
    internal readonly struct PreparedTemporalRecovery
    {
        private readonly StateGraphInstance _runtime;
        private readonly ulong _token;

        internal PreparedTemporalRecovery(
            StateGraphInstance runtime,
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

    /// <summary>为一个实例维护图层、激活记忆和候选步进，并在整步决定后发布结果。</summary>
    internal sealed class StateGraphInstance : IDisposable, IStateTransitionRequests
    {
        bool IStateTransitionRequests.TryGetStateLogicType(
            StateId stateId,
            out Type logicType)
        {
            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                IReadOnlyList<CompiledState> states =
                    _layers[layerIndex].Compiled.States;
                for (int stateIndex = 0; stateIndex < states.Count; stateIndex++)
                {
                    if (states[stateIndex].StateId == stateId)
                    {
                        logicType = states[stateIndex].Descriptor.LogicType;
                        return logicType != null;
                    }
                }
            }

            logicType = null;
            return false;
        }

        private readonly CompiledStateGraph _graph;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly StateGraphLogicBindings _bindings;
        private readonly OperationFrame _operationFrame;
        private readonly InstanceClock _clock;
        private readonly object _transitionHandleOwner;
        private readonly LayerRuntime[] _layers;
        private StateEventInbox _eventInbox;
        private IStateEventOutbox _eventOutbox;

        private readonly ActivePath[] _activePaths;
        private readonly StateCallbackOperationLease _stateCallbackOperationLease;
        private readonly bool[] _requestedTransitionsScratch;
        private RuntimeLifecycleState _lifecycle;
        private RuntimeFault _fault;
        private OperationFrameWriter _operationWriter;
        private FinalizedOperationFrame _finalizedOperationFrame;
        private TickFrame _stagedTickFrame;
        private ulong _nextActivationValue;
        private ulong _candidateNextActivationValue;
        private ulong _stageGeneration;
        private ulong _activeStageToken;
        private ulong _activePreparedCommitToken;
        private ulong _restoreGeneration;
        private ulong _activeRestoreToken;
        private ulong _temporalRecoveryGeneration;
        private ulong _activeTemporalRecoveryToken;
        private PreparedInstanceClockRestore _preparedClockRestore;
        private int _executingLayerIndex;
        private int _executingStateIndex;
        private bool _acceptsTransitionRequests;
        private bool _isExecutingStep;
        private bool _disposeRequested;
        private bool _isDisposed;

        internal void AttachEvents(StateEventInbox inbox, IStateEventOutbox outbox)
        {
            _eventInbox = inbox;
            _eventOutbox = outbox;
        }

        private StateGraphInstance(
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            StateGraphLogicBindings bindings,
            OperationFrame operationFrame,
            InstanceClock clock,
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
            _activePaths = new ActivePath[layers.Length];
            for (int index = 0; index < layers.Length; index++)
            {
                _activePaths[index] = new ActivePath(this, index, layers[index].Compiled.LayerId);
            }

            _stateCallbackOperationLease = new StateCallbackOperationLease();
            _requestedTransitionsScratch = new bool[maximumTransitionCount];
            _lifecycle = RuntimeLifecycleState.Created;
            _nextActivationValue = 1UL;
            _executingLayerIndex = -1;
            _executingStateIndex = -1;
        }

        public CompiledStateGraph Graph => _graph;
        public GraphInstanceId GraphInstanceId => _graphInstanceId;
        public RuntimeLifecycleState Lifecycle => _lifecycle;
        public RuntimeFault Fault => _fault;
        public bool IsFaulted => _fault.IsFaulted;
        public bool HasStagedStep => _activeStageToken != 0UL;
        internal bool HasPreparedRestore => _activeRestoreToken != 0UL;
        public InstanceClock Clock => _clock;
        public IReadOnlyList<ActivePath> ActivePaths => _activePaths;

        internal static bool TryCreate(
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            StateGraphLogicBindings bindings,
            OperationFrame operationFrame,
            InstanceClock clock,
            out StateGraphInstance runtime,
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
                    CompiledStateLayer compiledLayer = graph.Layers[layerIndex];
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

                runtime = new StateGraphInstance(
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
                    "StateGraph setup requires fresh, isolated Logic, Memory, Clock, and OperationFrame instances.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryStart(out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != RuntimeLifecycleState.Created || _isDisposed || IsFaulted)
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
                                "StateActivationMemory reset produced inconsistent committed and candidate Banks.");
                            LatchFault(diagnostic);
                            return false;
                        }
                    }

                    int initialLeaf = ResolveInitialLeaf(layer.Compiled, layer.Compiled.InitialStateIndex);
                    layer.CommittedLeafIndex = initialLeaf;
                    layer.CommittedEnterStartDepth = 0;
                    IReadOnlyList<int> path = layer.Compiled.States[initialLeaf].RootPathStateIndices;

                    int activationCount = path.Count + (layer.Compiled.HasAnyState ? 1 : 0);
                    for (int depth = 0; depth < activationCount; depth++)
                    {
                        int stateIndex = depth < path.Count ? path[depth] : layer.Compiled.AnyStateIndex;
                        StateRuntime state = layer.States[stateIndex];
                        if (!TryTakeActivation(out ActivationId activationId))
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
                diagnostic = StateError("StateActivationMemory reset failed while starting the StateGraph Runtime.");
                LatchFault(diagnostic);
                return false;
            }

            TransitionLifecycleTo(RuntimeLifecycleState.Running);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TrySuspend(out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != RuntimeLifecycleState.Running ||
                HasStagedStep ||
                HasPreparedRestore ||
                IsFaulted ||
                _isExecutingStep ||
                _disposeRequested)
            {
                diagnostic = LifecycleError("Suspend requires a healthy Running Runtime at a resolved Tick boundary.");
                return false;
            }

            TransitionLifecycleTo(RuntimeLifecycleState.Suspended);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryResume(out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != RuntimeLifecycleState.Suspended ||
                IsFaulted ||
                HasPreparedRestore)
            {
                diagnostic = LifecycleError("Resume requires a healthy Suspended Runtime.");
                return false;
            }

            TransitionLifecycleTo(RuntimeLifecycleState.Running);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryStop(out CoCoDiagnostic diagnostic)
        {
            if ((_lifecycle != RuntimeLifecycleState.Running &&
                 _lifecycle != RuntimeLifecycleState.Suspended) ||
                _isExecutingStep ||
                _disposeRequested ||
                _isDisposed)
            {
                diagnostic = LifecycleError(
                    "Stop requires a Running or Suspended StateGraph Runtime at a safe boundary.");
                return false;
            }

            CancelOutstandingStep(false, default);
            TransitionLifecycleTo(RuntimeLifecycleState.Stopped);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPreviewNextTick(
            double deltaTime,
            double instanceTimeScale,
            out TickFrame tickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (_lifecycle != RuntimeLifecycleState.Running ||
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
                instanceTimeScale,
                out tickFrame,
                out diagnostic);
        }

        internal bool TryStageStep(
            in TickFrame tickFrame,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
            out StagedGraphStep stagedStep,
            out CoCoDiagnostic diagnostic)
        {
            stagedStep = default;
            if (_lifecycle != RuntimeLifecycleState.Running ||
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
                diagnostic = StateError("State execution threw while staging the Tick.");
            }
            finally
            {
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
            stagedStep = new StagedGraphStep(
                this,
                _activeStageToken,
                tickFrame,
                _finalizedOperationFrame);
            _isExecutingStep = false;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public ActivePath GetActivePath(int layerIndex)
        {
            if (layerIndex < 0 || layerIndex >= _activePaths.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(layerIndex));
            }

            return _activePaths[layerIndex];
        }

        public bool TryGetActivePath(LayerId layerId, out ActivePath activePath)
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

        bool IStateTransitionRequests.TryRequestTransition(TransitionHandle handle, StateId stateId)
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
            if ((_executingStateIndex != layer.CommittedLeafIndex &&
                 _executingStateIndex != layer.Compiled.AnyStateIndex) ||
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
            IStateGraphContextRuntime contextRuntime,
            in StagedGraphStep stagedStep,
            ContextFrameReadView previousContext,
            in PreparedContextCommit preparedContext,
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
            IStateGraphContextRuntime contextRuntime,
            ContextFrameReadView defaults,
            IStateGraphCommitGuard commitGuard,
            out CoCoDiagnostic diagnostic)
        {
            if (contextRuntime == null || _lifecycle != RuntimeLifecycleState.Running ||
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
                        "Initial Graph capture mutated committed StateActivationMemory outside Runtime authority.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryValidateRestore(
            IStateGraphContextRuntime contextRuntime,
            ContextRestoreReadView source,
            in TickFrame resumedTickFrame,
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
            IStateGraphContextRuntime contextRuntime,
            ContextRestoreReadView source,
            in TickFrame resumedTickFrame,
            out PreparedGraphRestore preparedRestore,
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
                    out PreparedInstanceClockRestore preparedClock,
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
                                out StateGraphRestoreState restoreState,
                                out diagnostic) ||
                            !restoreState.IsValid ||
                            restoreState.LayerId != layer.Compiled.LayerId ||
                            restoreState.StateId != state.Compiled.StateId ||
                            !state.TryPrepareRestoreCandidate(restoreState))
                        {
                            if (!diagnostic.IsError)
                            {
                                diagnostic = RestoreError(
                                    "A Graph State restore record or prepared StateActivationMemory is inconsistent.");
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
                preparedRestore = new PreparedGraphRestore(
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
                    "Graph restore preparation threw while rebuilding candidate StateActivationMemory.");
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
            IStateGraphContextRuntime contextRuntime,
            ContextRestoreReadView source,
            in TickFrame resumedTickFrame,
            out ulong nextActivationValue,
            out CoCoDiagnostic diagnostic)
        {
            nextActivationValue = 0UL;
            if (contextRuntime == null ||
                (_lifecycle != RuntimeLifecycleState.Running &&
                 _lifecycle != RuntimeLifecycleState.Suspended) ||
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
            in StagedGraphStep stagedStep,
            IStateGraphCommitGuard commitGuard,
            out PreparedGraphCommit preparedCommit,
            out CoCoDiagnostic diagnostic)
        {
            preparedCommit = default;
            if (_lifecycle != RuntimeLifecycleState.Running ||
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
            preparedCommit = new PreparedGraphCommit(
                this,
                _activePreparedCommitToken,
                _stagedTickFrame);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void CommitPreparedStep(in PreparedGraphCommit preparedCommit)
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
            in StagedGraphStep stagedStep,
            out CoCoDiagnostic diagnostic) =>
            TryAcceptStagedStep(stagedStep, null, out diagnostic);

        internal bool TryAcceptStagedStep(
            in StagedGraphStep stagedStep,
            IStateGraphCommitGuard commitGuard,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryPrepareStagedCommit(
                    stagedStep,
                    commitGuard,
                    out PreparedGraphCommit preparedCommit,
                    out diagnostic))
            {
                return false;
            }

            preparedCommit.CommitNoFail();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryCancelStagedStep(
            in StagedGraphStep stagedStep,
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
            in StagedGraphStep stagedStep,
            CoCoDiagnostic reason,
            bool latchFault,
            out CoCoDiagnostic diagnostic) =>
            TryRejectStagedStep(stagedStep, reason, latchFault, null, out diagnostic);

        internal bool TryRejectStagedStep(
            in StagedGraphStep stagedStep,
            CoCoDiagnostic reason,
            bool latchFault,
            IStateGraphCommitGuard commitGuard,
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
                _lifecycle == RuntimeLifecycleState.Stopped ||
                _lifecycle == RuntimeLifecycleState.Disposed ||
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
            out PreparedTemporalRecovery prepared,
            out CoCoDiagnostic diagnostic)
        {
            prepared = default;
            if (!_fault.IsFaulted ||
                (_lifecycle != RuntimeLifecycleState.Running &&
                 _lifecycle != RuntimeLifecycleState.Suspended) ||
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
            prepared = new PreparedTemporalRecovery(
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

        internal bool TryCaptureCommittedDebugState(
            out StateGraphCommittedDebugState state,
            out CoCoDiagnostic diagnostic)
        {
            state = null;
            if (_isDisposed ||
                (_lifecycle != RuntimeLifecycleState.Running &&
                 _lifecycle != RuntimeLifecycleState.Suspended) ||
                _isExecutingStep ||
                _activeStageToken != 0UL ||
                _activePreparedCommitToken != 0UL ||
                _activeRestoreToken != 0UL ||
                _activeTemporalRecoveryToken != 0UL ||
                _clock.HasStagedTick)
            {
                diagnostic = LifecycleError(
                    "Committed debugger state requires one live idle Runtime with no candidate authority.");
                return false;
            }

            var layers = new StateGraphCommittedDebugLayer[_layers.Length];
            for (int layerIndex = 0; layerIndex < _layers.Length; layerIndex++)
            {
                LayerRuntime layer = _layers[layerIndex];
                if (layer.CommittedLeafIndex < 0 ||
                    layer.CommittedLeafIndex >= layer.States.Length)
                {
                    diagnostic = StateError(
                        "Committed debugger state found a Layer without a valid active leaf.");
                    return false;
                }

                IReadOnlyList<int> path =
                    layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices;
                var activeStates = new StateGraphCommittedDebugActiveState[path.Count];
                for (int pathIndex = 0; pathIndex < path.Count; pathIndex++)
                {
                    int stateIndex = path[pathIndex];
                    StateRuntime active = layer.States[stateIndex];
                    activeStates[pathIndex] = new StateGraphCommittedDebugActiveState(
                        active.Compiled.StateId,
                        active.CommittedActivationId,
                        active.CommittedLocalSeconds,
                        active.CommittedActionProgress);
                }

                layers[layerIndex] = new StateGraphCommittedDebugLayer(
                    layer.Compiled.LayerId,
                    layer.CommittedWinnerTransitionId,
                    activeStates);
            }

            state = new StateGraphCommittedDebugState(
                _graph.SchemaVersion,
                _graph.ContentFingerprint,
                _graph.GraphId,
                _graph.CatalogFingerprint,
                _graphInstanceId,
                _clock.TimelineId,
                _clock.ClockDomainId,
                _clock.TimelineEpoch,
                _clock.Tick,
                _clock.ExecutionSequence,
                _clock.Seconds,
                layers);
            diagnostic = CoCoDiagnostic.None;
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

        internal StateId GetCommittedLeafId(int layerIndex)
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

        internal StateId GetCommittedPathStateId(int layerIndex, int pathIndex)
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

        internal LayerId GetCommittedTraceLayerId(int layerIndex) =>
            _layers[layerIndex].Compiled.LayerId;

        internal int GetCommittedTracePathCount(int layerIndex)
        {
            LayerRuntime layer = _layers[layerIndex];
            return layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices.Count;
        }

        internal StateId GetCommittedTracePathStateId(int layerIndex, int pathIndex)
        {
            LayerRuntime layer = _layers[layerIndex];
            IReadOnlyList<int> path =
                layer.Compiled.States[layer.CommittedLeafIndex].RootPathStateIndices;
            return layer.Compiled.States[path[pathIndex]].StateId;
        }

        internal TransitionId GetCommittedTraceWinnerTransitionId(int layerIndex) =>
            _layers[layerIndex].CommittedWinnerTransitionId;

        internal void AppendCommittedStateTrace(
            StateFlowTraceBuffer trace,
            in TickFrame tickFrame)
        {
            if (trace == null)
            {
                return;
            }

            for (int layerIndex = 0; layerIndex < CommittedTraceLayerCount; layerIndex++)
            {
                LayerId layerId = GetCommittedTraceLayerId(layerIndex);
                int pathCount = GetCommittedTracePathCount(layerIndex);
                for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
                {
                    trace.Append(StateFlowTraceEntry.Path(
                        _graphInstanceId,
                        tickFrame,
                        layerId,
                        GetCommittedTracePathStateId(layerIndex, pathIndex)));
                }

                TransitionId winner =
                    GetCommittedTraceWinnerTransitionId(layerIndex);
                if (winner.IsValid)
                {
                    trace.Append(StateFlowTraceEntry.Transition(
                        _graphInstanceId,
                        tickFrame,
                        layerId,
                        winner));
                }
            }
        }

        internal void AppendStagedTransitionTrace(
            StateFlowTraceBuffer trace,
            in StagedGraphStep stagedStep,
            StateFlowTraceFrameReference previousContext)
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

                    trace.Append(StateFlowTraceEntry.Transition(
                        _graphInstanceId,
                        stagedStep.TickFrame,
                        layer.Compiled.LayerId,
                        layer.Compiled.Transitions[transitionIndex].TransitionId,
                        StateFlowTransitionRole.Candidate,
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

                trace.Append(StateFlowTraceEntry.Transition(
                    _graphInstanceId,
                    stagedStep.TickFrame,
                    layer.Compiled.LayerId,
                    layer.CandidateWinnerTransitionId,
                    StateFlowTransitionRole.Winner,
                    previousContext));
            }
        }

        internal void AppendCommittedPathTrace(
            StateFlowTraceBuffer trace,
            in TickFrame tickFrame,
            StateFlowTraceFrameReference frame)
        {
            if (trace == null)
            {
                return;
            }

            for (int layerIndex = 0; layerIndex < CommittedTraceLayerCount; layerIndex++)
            {
                LayerId layerId = GetCommittedTraceLayerId(layerIndex);
                int pathCount = GetCommittedTracePathCount(layerIndex);
                for (int pathIndex = 0; pathIndex < pathCount; pathIndex++)
                {
                    trace.Append(StateFlowTraceEntry.Path(
                        _graphInstanceId,
                        tickFrame,
                        layerId,
                        GetCommittedTracePathStateId(layerIndex, pathIndex),
                        frame));
                }
            }
        }

        private bool ExecuteLayers(
            in TickFrame tickFrame,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
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
                        if (state.Logic is IStateEnter &&
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
                    if (!(state.Logic is IStateUpdate))
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

                if (layer.Compiled.HasAnyState)
                {

                    StateRuntime anyState = layer.States[layer.Compiled.AnyStateIndex];
                    anyState.CandidateLocalSeconds += tickFrame.DeltaTime;
                    if (!InvokeState(
                            StateCallbackPhase.Update,
                            layerIndex,
                            layer.Compiled.AnyStateIndex,
                            sourcePath.Count,
                            true,
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

                CompiledTransition winner = layer.Compiled.Transitions[winnerIndex];
                IReadOnlyList<int> targetPath =
                    layer.Compiled.States[winner.TargetStateIndex].RootPathStateIndices;
                int commonCount = CountCommonPrefix(sourcePath, targetPath);
                if (layer.CommittedLeafIndex == winner.TargetStateIndex)
                {
                    commonCount = Math.Max(0, sourcePath.Count - 1);
                }

                for (int depth = sourcePath.Count - 1; depth >= commonCount; depth--)
                {
                    int stateIndex = sourcePath[depth];
                    StateRuntime state = layer.States[stateIndex];
                    if (state.Logic is IStateExit &&
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
                    if (!TryTakeCandidateActivation(out ActivationId activationId))
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
            in TickFrame tickFrame,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
            out CoCoDiagnostic diagnostic)
        {
            LayerRuntime layer = _layers[layerIndex];
            StateRuntime state = layer.States[stateIndex];
            if (!state.IsCandidateMemoryFingerprintCurrent)
            {
                diagnostic = StateError(
                    "StateActivationMemory changed outside its owning State callback.");
                return false;
            }

            if (!OperationWriteRank.TryCreate(layerIndex, pathDepth, out OperationWriteRank rank))
            {
                diagnostic = OperationError(
                    CoCoDiagnosticCode.InvalidOperationContract,
                    "State callback composition rank is invalid.");
                return false;
            }

            StateExecutionContext stateContext = state.ExecutionContext;
            if (!_stateCallbackOperationLease.TryBegin())
            {
                diagnostic = StateError("State callback Operation lease is already active.");
                return false;
            }

            double resultActionProgress;
            bool contextHasError;
            try
            {
                stateContext.AttachEvents(_eventInbox, _eventOutbox);
                stateContext.Prepare(
                    this,
                    state.CandidateMemory,
                    state.Compiled.Config,
                    intents,
                    previousContext,
                    tickFrame,
                    _operationWriter,
                    rank,
                    state.AllowedOperations,
                    _stateCallbackOperationLease,
                    layer.Compiled.LayerId,
                    state.Compiled.StateId,
                    layer.Compiled.States[layer.CommittedLeafIndex].StateId,
                    state.CandidateActivationId,
                    state.CommittedLocalSeconds,
                    state.CandidateLocalSeconds,
                    state.CommittedActionProgress,
                    state.CandidateActionProgress,
                    canRequestTransition,
                    state.Compiled.Descriptor.ProvidesActionProgress);
                state.PropertyBindings.Refresh(intents);
                (state.CandidateMemory as IFieldMemory)?.Load(state.Logic);
                _executingLayerIndex = layerIndex;
                _executingStateIndex = stateIndex;
                _acceptsTransitionRequests = canRequestTransition;
                switch (phase)
                {
                    case StateCallbackPhase.Enter:
                        ((IStateEnter)state.Logic).OnEnter(stateContext);
                        break;
                    case StateCallbackPhase.Update:
                        ((IStateUpdate)state.Logic).Update(stateContext);
                        break;
                    case StateCallbackPhase.Exit:
                        ((IStateExit)state.Logic).OnExit(stateContext);
                        break;
                    default:
                        throw new InvalidOperationException("State callback phase is invalid.");
                }

                (state.CandidateMemory as IFieldMemory)?.Store(state.Logic);
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
            in TickFrame tickFrame,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
            out int winnerIndex,
            out CoCoDiagnostic diagnostic)
        {
            LayerRuntime layer = _layers[layerIndex];
            if (layer.Compiled.HasAnyState)
            {
                if (!TryChooseWinnerForState(
                        layerIndex,
                        layer.Compiled.AnyStateIndex,
                        tickFrame,
                        intents,
                        previousContext,
                        out winnerIndex,
                        out diagnostic))
                {
                    return false;
                }

                if (winnerIndex >= 0)
                {
                    return true;
                }
            }

            return TryChooseWinnerForState(
                layerIndex,
                layer.CommittedLeafIndex,
                tickFrame,
                intents,
                previousContext,
                out winnerIndex,
                out diagnostic);
        }

        private bool TryChooseWinnerForState(
            int layerIndex,
            int sourceStateIndex,
            in TickFrame tickFrame,
            IIntentFrame intents,
            in ContextFrameReadView previousContext,
            out int winnerIndex,
            out CoCoDiagnostic diagnostic)
        {
            LayerRuntime layer = _layers[layerIndex];
            StateRuntime source = layer.States[sourceStateIndex];
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

                CompiledTransition transition = layer.Compiled.Transitions[transitionIndex];
                if (transition.SourceStateIndex != sourceStateIndex ||
                    !WindowMatches(transition.Window, source))
                {
                    continue;
                }

                if (winnerIndex < 0 || transition.Priority > winningPriority)
                {
                    winningPriority = transition.Priority;
                    winnerIndex = transitionIndex;
                }

                layer.CandidateAcceptedTransitions[transitionIndex] = true;
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
                                "StateActivationMemory copy changed committed state or produced a non-equivalent candidate.");
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
                diagnostic = StateError("StateActivationMemory copy failed while preparing the candidate Tick.");
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
                if (state.Compiled.IsAnyState && !state.CandidateActivationId.IsValid)
                {
                    diagnostic = RestoreError("Restored AnyState requires its persistent activation identity.");
                    return false;
                }

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
            in TickFrame tickFrame,
            IIntentFrame intents,
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

        private bool TryTakeActivation(out ActivationId activationId)
        {
            if (_nextActivationValue == 0UL || _nextActivationValue == ulong.MaxValue)
            {
                activationId = default;
                return false;
            }

            if (!ActivationId.TryCreate(_nextActivationValue, out activationId))
            {
                return false;
            }

            _nextActivationValue++;
            return true;
        }

        private bool TryTakeCandidateActivation(out ActivationId activationId)
        {
            if (_candidateNextActivationValue == 0UL ||
                _candidateNextActivationValue == ulong.MaxValue ||
                !ActivationId.TryCreate(_candidateNextActivationValue, out activationId))
            {
                activationId = default;
                return false;
            }

            _candidateNextActivationValue++;
            return true;
        }

        private bool Owns(in StagedGraphStep stagedStep) =>
            ReferenceEquals(stagedStep.Runtime, this) &&
            stagedStep.Token != 0UL &&
            stagedStep.Token == _activeStageToken;

        private bool Owns(in PreparedGraphCommit preparedCommit) =>
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
            in TickFrame tickFrame,
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
            if (_lifecycle == RuntimeLifecycleState.Running ||
                _lifecycle == RuntimeLifecycleState.Suspended)
            {
                TransitionLifecycleTo(RuntimeLifecycleState.Stopped);
            }

            if (_lifecycle == RuntimeLifecycleState.Created ||
                _lifecycle == RuntimeLifecycleState.Stopped)
            {
                TransitionLifecycleTo(RuntimeLifecycleState.Disposed);
            }

            _isDisposed = _lifecycle == RuntimeLifecycleState.Disposed;
        }

        private void TransitionLifecycleTo(RuntimeLifecycleState nextState)
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
                                "StateActivationMemory changed outside its owning State callback.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception)
            {
                diagnostic = StateError("StateActivationMemory fingerprint evaluation failed.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateCommittedMemoryIntegrity(
            IStateGraphCommitGuard commitGuard,
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
                                "Committed StateActivationMemory validation was cancelled by its Host before fingerprint evaluation.");
                            return false;
                        }

                        bool fingerprintIsCurrent =
                            states[stateIndex].IsCommittedMemoryFingerprintCurrent;

                        if (commitGuard != null && commitGuard.IsCommitCancellationRequested)
                        {
                            diagnostic = LifecycleError(
                                "Committed StateActivationMemory validation was cancelled by its Host after fingerprint evaluation.");
                            return false;
                        }

                        if (!fingerprintIsCurrent)
                        {
                            diagnostic = RestoreError(
                                "Restore preparation observed mutated committed StateActivationMemory.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception)
            {
                diagnostic = commitGuard != null && commitGuard.IsCommitCancellationRequested
                    ? LifecycleError(
                        "Committed StateActivationMemory validation was cancelled by its Host during fingerprint evaluation.")
                    : RestoreError(
                        "Committed StateActivationMemory fingerprint evaluation failed during restore preparation.");
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
                                "Staged or committed StateActivationMemory changed outside the Runtime transaction.");
                            return false;
                        }
                    }
                }
            }
            catch (Exception)
            {
                diagnostic = StateError("StateActivationMemory fingerprint evaluation failed.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private void LatchFault(CoCoDiagnostic diagnostic)
        {
            if (!_fault.IsFaulted)
            {
                _fault = new RuntimeFault(
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
                if (instance is StateLogic logic)
                {
                    logic.ReleaseRuntimeOwner(owner);
                }
                else if (instance is StateActivationMemory memory)
                {
                    memory.ReleaseRuntimeOwner(owner);
                }
            }
        }

        private static bool ValidateFactories(
            CompiledStateGraph graph,
            StateGraphLogicBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
            {
                CompiledStateLayer layer = graph.Layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    StateDescriptor descriptor = layer.States[stateIndex].Descriptor;
                    if (!bindings.TryGetStateFactory(descriptor.DescriptorId, out IStateRuntimeFactory factory) ||
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

            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool ValidateOperationFrame(
            CompiledStateGraph graph,
            GraphInstanceId graphInstanceId,
            OperationFrame operationFrame,
            out CoCoDiagnostic diagnostic)
        {
            OperationRegistry registry = operationFrame.Registry;
            GraphOperationProvisionManifest manifest = graph.OperationProvides;
            if (registry == null ||
                operationFrame.GraphInstanceId != graphInstanceId ||
                !registry.IsFrozen ||
                registry.LayoutId != manifest.LayoutId ||
                registry.Count != manifest.Count)
            {
                diagnostic = OperationError(
                    CoCoDiagnosticCode.MissingOperationContract,
                    "OperationFrame layout does not exactly match the compiled Graph manifest.");
                return false;
            }

            for (int provideIndex = 0; provideIndex < manifest.Provides.Count; provideIndex++)
            {
                GraphOperationProvision provide = manifest.Provides[provideIndex];
                bool matched = false;
                for (int operationIndex = 0; operationIndex < registry.Operations.Count; operationIndex++)
                {
                    OperationDescriptor section = registry.Operations[operationIndex];
                    if (section.OperationId == provide.OperationId &&
                        section.Mode == provide.Mode &&
                        section.OperationType == provide.OperationType &&
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
                        CoCoDiagnosticCode.MissingOperationContract,
                        "OperationFrame is missing a compiled Graph Operation Section.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static LayerRuntime BuildLayer(
            object owner,
            GraphInstanceId graphInstanceId,
            CompiledStateLayer compiledLayer,
            StateGraphLogicBindings bindings,
            OperationRegistry operationRegistry,
            int layerIndex,
            List<object> claimedInstances)
        {
            var states = new StateRuntime[compiledLayer.States.Count];
            for (int stateIndex = 0; stateIndex < compiledLayer.States.Count; stateIndex++)
            {
                CompiledState compiledState = compiledLayer.States[stateIndex];
                bindings.TryGetStateFactory(
                    compiledState.Descriptor.DescriptorId,
                    out IStateRuntimeFactory factory);
                var handles = new TransitionHandle[compiledState.OutgoingTransitionCount];
                for (int handleIndex = 0; handleIndex < handles.Length; handleIndex++)
                {
                    int transitionIndex = compiledState.FirstOutgoingTransitionIndex + handleIndex;
                    CompiledTransition transition = compiledLayer.Transitions[transitionIndex];
                    handles[handleIndex] = new TransitionHandle(
                        owner,
                        layerIndex,
                        transitionIndex,
                        transition.TransitionId,
                        compiledState.StateId,
                        compiledLayer.States[transition.TargetStateIndex].StateId,
                        transition.Priority);
                }

                var factoryContext = new StateFactoryContext(
                    graphInstanceId,
                    compiledLayer.LayerId,
                    layerIndex,
                    compiledState.StateId,
                    compiledState.RootPathStateIndices.Count - 1,
                    compiledState.Config,
                    handles);
                StateLogic logic = factory.CreateLogic(factoryContext);
                StateActivationMemory memory0 = factory.CreateMemory();
                StateActivationMemory memory1 = factory.CreateMemory();
                if (logic.GetType() != compiledState.Descriptor.LogicType ||
                    !(logic is IStateUpdate) ||
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
                        "State runtime factory reused an StateActivationMemory instance.");
                }

                claimedInstances.Add(memory0);
                if (!memory1.TryClaimRuntimeOwner(owner))
                {
                    throw new InvalidOperationException(
                        "State runtime factory reused an StateActivationMemory instance.");
                }

                claimedInstances.Add(memory1);

                factory.ResetMemory(memory0);
                factory.ResetMemory(memory1);
                bool[] allowedOperations = BuildAllowedOperations(
                    compiledState.Descriptor.OperationProvides,
                    operationRegistry);
                states[stateIndex] = new StateRuntime(
                    compiledState,
                    factory,
                    logic,
                    memory0,
                    memory1,
                    handles,
                    allowedOperations,
                    operationRegistry);
                states[stateIndex].ExecutionContext
                    .AttachOutgoingTransitions(handles);
            }

            return new LayerRuntime(compiledLayer, states);
        }

        private static bool[] BuildAllowedOperations(
            IReadOnlyList<OperationId> operationProvides,
            OperationRegistry registry)
        {
            if (registry == null)
            {
                throw new InvalidOperationException(
                    "State runtime setup requires an Operation Section Registry.");
            }

            var allowed = new bool[registry.Count];
            for (int provideIndex = 0; provideIndex < operationProvides.Count; provideIndex++)
            {
                OperationId operationId = operationProvides[provideIndex];
                int denseIndex = -1;
                for (int operationIndex = 0; operationIndex < registry.Operations.Count; operationIndex++)
                {
                    OperationDescriptor section = registry.Operations[operationIndex];
                    if (section.OperationId == operationId)
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

        private static int ResolveInitialLeaf(CompiledStateLayer layer, int stateIndex)
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

        private static bool WindowMatches(TransitionWindow window, StateRuntime leaf)
        {
            switch (window.Mode)
            {
                case TransitionWindowMode.Always:
                    return true;
                case TransitionWindowMode.LocalSeconds:
                    return CrossesWindow(
                        leaf.CommittedLocalSeconds,
                        leaf.CandidateLocalSeconds,
                        window.StartInclusive,
                        window.EndExclusive);
                case TransitionWindowMode.ActionProgress:
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

        /// <summary>保存单个运行层的活动路径和转换请求，供该层步进与仲裁使用。</summary>
        private sealed class LayerRuntime
        {
            public LayerRuntime(
                CompiledStateLayer compiled,
                StateRuntime[] states)
            {
                Compiled = compiled;
                States = states;
                CandidateAcceptedTransitions = new bool[compiled.Transitions.Count];
                CommittedLeafIndex = -1;
                CandidateLeafIndex = -1;
                CommittedEnterStartDepth = -1;
                CandidateEnterStartDepth = -1;
            }

            public CompiledStateLayer Compiled { get; }
            public StateRuntime[] States { get; }
            public bool[] CandidateAcceptedTransitions { get; }
            public int CommittedLeafIndex { get; set; }
            public int CandidateLeafIndex { get; set; }
            public int CommittedEnterStartDepth { get; set; }
            public int CandidateEnterStartDepth { get; set; }
            public TransitionId CommittedWinnerTransitionId { get; set; }
            public TransitionId CandidateWinnerTransitionId { get; set; }
        }

        /// <summary>保存节点的 Logic、激活记忆与本次回调所需的运行数据。</summary>
        private sealed class StateRuntime
        {
            private bool _committedUsesMemory1;

            public StateRuntime(
                CompiledState compiled,
                IStateRuntimeFactory factory,
                StateLogic logic,
                StateActivationMemory memory0,
                StateActivationMemory memory1,
                TransitionHandle[] handles,
                bool[] allowedOperations,
                OperationRegistry operationRegistry)
            {
                Compiled = compiled;
                Factory = factory;
                Logic = logic;
                Memory0 = memory0;
                Memory1 = memory1;
                Handles = handles;
                AllowedOperations = allowedOperations;
                ExecutionContext = new StateExecutionContext();
                PropertyBindings = new StatePropertyBindings(logic, operationRegistry, ExecutionContext);
            }

            public CompiledState Compiled { get; }
            public IStateRuntimeFactory Factory { get; }
            public StateLogic Logic { get; }
            public StateActivationMemory Memory0 { get; }
            public StateActivationMemory Memory1 { get; }
            public TransitionHandle[] Handles { get; }
            public bool[] AllowedOperations { get; }
            public StateExecutionContext ExecutionContext { get; }
            public StatePropertyBindings PropertyBindings { get; }
            public StateActivationMemory CommittedMemory => _committedUsesMemory1 ? Memory1 : Memory0;
            public StateActivationMemory CandidateMemory => _committedUsesMemory1 ? Memory0 : Memory1;
            public ActivationId CommittedActivationId { get; set; }
            public ActivationId CandidateActivationId { get; set; }
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
                in StateGraphRestoreState restoreState)
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

        /// <summary>区分实例正在调用的进入、更新或退出阶段。</summary>
        private enum StateCallbackPhase
        {
            Enter = 1,
            Update = 2,
            Exit = 3
        }
    }
}
