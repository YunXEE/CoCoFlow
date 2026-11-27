using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using System.Reflection;
using System.Runtime.InteropServices;

// 图状态生产方和 Operator 从框架提供的视图读取候选活动路径、输出和上一份已提交的 Context。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>表示一个节点在提交帧中的激活身份、路径标志、时间和项目状态值。</summary>
    public readonly struct GraphStateRecord<TState> : IEquatable<GraphStateRecord<TState>>
        where TState : unmanaged
    {

        #region Public API

        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public bool IsOnActivePath { get; }
        public ActivationId ActivationId { get; }
        public double LocalSeconds { get; }
        public double ActionProgress { get; }
        public bool EnterPending { get; }
        public ulong MemoryFingerprint { get; }
        public TState State { get; }
        public bool IsActive => IsOnActivePath;
        public bool IsValid => LayerId.IsValid &&
                               StateId.IsValid &&
                               IsFiniteNonNegative(LocalSeconds) &&
                               ActionProgress >= 0d &&
                               ActionProgress <= 1d &&
                               (!EnterPending ||
                                LocalSeconds == 0d && ActionProgress == 0d) &&
                               (IsOnActivePath
                                   ? ActivationId.IsValid
                                   : !EnterPending &&
                                     (ActivationId.IsValid ||
                                      LocalSeconds == 0d && ActionProgress == 0d));

        /// <summary>用层、节点、激活及时间值构造候选状态记录，字段组合非法时返回 false。</summary>
        public static bool TryCreate(
            LayerId layerId,
            StateId stateId,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in TState state,
            out GraphStateRecord<TState> record)
        {
            record = new GraphStateRecord<TState>(
                layerId,
                stateId,
                isOnActivePath,
                activationId,
                localSeconds,
                actionProgress,
                enterPending,
                memoryFingerprint,
                state);
            if (record.IsValid)
            {
                return true;
            }

            record = default;
            return false;
        }

        /// <summary>为指定层和节点生成未激活的状态记录，标识无效时返回 false。</summary>
        public static bool TryCreateInactive(
            LayerId layerId,
            StateId stateId,
            ulong memoryFingerprint,
            in TState state,
            out GraphStateRecord<TState> record) =>
            TryCreate(
                layerId,
                stateId,
                false,
                default,
                0d,
                0d,
                false,
                memoryFingerprint,
                state,
                out record);

        /// <summary>按<c>LayerId</c>、<c>StateId</c>、<c>IsOnActivePath</c>、<c>ActivationId</c>、<c>LocalSeconds</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(GraphStateRecord<TState> other)
        {
            return LayerId == other.LayerId &&
                   StateId == other.StateId &&
                   IsOnActivePath == other.IsOnActivePath &&
                   ActivationId == other.ActivationId &&
                   LocalSeconds.Equals(other.LocalSeconds) &&
                   ActionProgress.Equals(other.ActionProgress) &&
                   EnterPending == other.EnterPending &&
                   MemoryFingerprint == other.MemoryFingerprint &&
                   EqualityComparer<TState>.Default.Equals(State, other.State);
        }

        /// <summary>仅当对象属于同类型并且<c>LayerId</c>、<c>StateId</c>、<c>IsOnActivePath</c>、<c>ActivationId</c>、<c>LocalSeconds</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is GraphStateRecord<TState> other && Equals(other);

        /// <summary>为哈希集合计算与<c>LayerId</c>、<c>StateId</c>、<c>IsOnActivePath</c>、<c>ActivationId</c>、<c>LocalSeconds</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = LayerId.GetHashCode();
                hashCode = (hashCode * 397) ^ StateId.GetHashCode();
                hashCode = (hashCode * 397) ^ IsOnActivePath.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ LocalSeconds.GetHashCode();
                hashCode = (hashCode * 397) ^ ActionProgress.GetHashCode();
                hashCode = (hashCode * 397) ^ EnterPending.GetHashCode();
                hashCode = (hashCode * 397) ^ MemoryFingerprint.GetHashCode();
                hashCode = (hashCode * 397) ^ EqualityComparer<TState>.Default.GetHashCode(State);
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>LayerId</c>、<c>StateId</c>、<c>IsOnActivePath</c>、<c>ActivationId</c>、<c>LocalSeconds</c>，相等时返回 true。</summary>
        public static bool operator ==(
            GraphStateRecord<TState> left,
            GraphStateRecord<TState> right) => left.Equals(right);

        /// <summary>比较两端的<c>LayerId</c>、<c>StateId</c>、<c>IsOnActivePath</c>、<c>ActivationId</c>、<c>LocalSeconds</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            GraphStateRecord<TState> left,
            GraphStateRecord<TState> right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private GraphStateRecord(
            LayerId layerId,
            StateId stateId,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in TState state)
        {
            LayerId = layerId;
            StateId = stateId;
            IsOnActivePath = isOnActivePath;
            ActivationId = activationId;
            LocalSeconds = localSeconds;
            ActionProgress = actionProgress;
            EnterPending = enterPending;
            MemoryFingerprint = memoryFingerprint;
            State = state;
        }

        private static bool IsFiniteNonNegative(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;

        #endregion
    }

    /// <summary>在激活记忆与 Context 中可保存的状态值之间执行捕获和恢复。</summary>
    public interface IActivationMemoryStateBinding<TMemory, TState>
        where TMemory : StateActivationMemory
        where TState : unmanaged
    {
        ulong SemanticFingerprint { get; }

        /// <summary>从当前激活记忆捕获可保存的状态值。无法捕获时返回 false 并给出诊断。</summary>
        bool TryCapture(
            TMemory memory,
            out TState state,
            out CoCoDiagnostic diagnostic);

        /// <summary>把保存的状态解入候选记忆，准备失败时返回 false 和诊断，由调用方放弃该次恢复。</summary>
        bool TryPrepareRestore(
            in TState state,
            TMemory candidateMemory,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>从本步候选图状态生成一个声明过的 Context 槽值。</summary>
    public interface IGraphContextValueProducer<TValue>
        where TValue : unmanaged
    {
        ulong SemanticFingerprint { get; }

        /// <summary>根据当前候选的状态及上一提交生成一个槽值，生成失败时返回 false 并提供诊断。</summary>
        bool TryProduce(
            in GraphContextCaptureContext context,
            out TValue value,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>保存一项 Operator 占用的持有者及其激活身份，供下一步或恢复使用。</summary>
    public readonly struct OperatorClaimState : IEquatable<OperatorClaimState>
    {

        #region Public API

        public OperatorClaimId ClaimId { get; }
        public OperationId OperationId { get; }
        public OperatorId OwnerOperatorId { get; }
        public ActivationId ActivationId { get; }
        public bool IsHeld => OwnerOperatorId.IsValid && ActivationId.IsValid;
        public bool IsValid => ClaimId.IsValid &&
                               OperationId.IsValid &&
                               (IsHeld ||
                                (!OwnerOperatorId.IsValid && !ActivationId.IsValid));

        /// <summary>为指定占用合同取得当前无持有者的状态值。</summary>
        public static OperatorClaimState Unheld(
            OperatorClaimId claimId,
            OperationId operationId)
        {
            if (!claimId.IsValid)
            {
                throw new ArgumentException("ClaimId must be valid.", nameof(claimId));
            }

            if (!operationId.IsValid)
            {
                throw new ArgumentException("OperationId must be valid.", nameof(operationId));
            }

            return new OperatorClaimState(claimId, operationId, default, default);
        }

        /// <summary>把占用关联到具体 Operator 和激活，必要标识无效时返回 false。</summary>
        public static bool TryCreateHeld(
            OperatorClaimId claimId,
            OperationId operationId,
            OperatorId ownerOperatorId,
            ActivationId activationId,
            out OperatorClaimState state)
        {
            state = new OperatorClaimState(
                claimId,
                operationId,
                ownerOperatorId,
                activationId);
            if (state.IsValid && state.IsHeld)
            {
                return true;
            }

            state = default;
            return false;
        }

        /// <summary>按<c>ClaimId</c>、<c>OperationId</c>、<c>OwnerOperatorId</c>、<c>ActivationId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(OperatorClaimState other) =>
            ClaimId == other.ClaimId &&
            OperationId == other.OperationId &&
            OwnerOperatorId == other.OwnerOperatorId &&
            ActivationId == other.ActivationId;

        /// <summary>仅当对象属于同类型并且<c>ClaimId</c>、<c>OperationId</c>、<c>OwnerOperatorId</c>、<c>ActivationId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is OperatorClaimState other && Equals(other);

        /// <summary>为哈希集合计算与<c>ClaimId</c>、<c>OperationId</c>、<c>OwnerOperatorId</c>、<c>ActivationId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = ClaimId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperationId.GetHashCode();
                hashCode = (hashCode * 397) ^ OwnerOperatorId.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>ClaimId</c>、<c>OperationId</c>、<c>OwnerOperatorId</c>、<c>ActivationId</c>，相等时返回 true。</summary>
        public static bool operator ==(
            OperatorClaimState left,
            OperatorClaimState right) => left.Equals(right);

        /// <summary>比较两端的<c>ClaimId</c>、<c>OperationId</c>、<c>OwnerOperatorId</c>、<c>ActivationId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            OperatorClaimState left,
            OperatorClaimState right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private OperatorClaimState(
            OperatorClaimId claimId,
            OperationId operationId,
            OperatorId ownerOperatorId,
            ActivationId activationId)
        {
            ClaimId = claimId;
            OperationId = operationId;
            OwnerOperatorId = ownerOperatorId;
            ActivationId = activationId;
        }

        #endregion
    }

    /// <summary>在候选令牌有效时提供活动叶与图状态记录。</summary>
    internal interface IStagedGraphReadSource
    {
        bool IsActive(ulong token);

        bool TryGetActiveLeaf(
            ulong token,
            LayerId layerId,
            out StateId stateId);

        bool TryGetState<TState>(
            ulong token,
            StateId stateId,
            out GraphStateRecord<TState> state)
            where TState : unmanaged;
    }

    /// <summary>让 Context 生产方在准备阶段读取同一候选的图状态。</summary>
    public readonly struct StagedGraphReadView
    {
        private readonly IStagedGraphReadSource _source;
        private readonly ulong _token;

        public bool IsValid => _source != null && _source.IsActive(_token);

        #region Public API

        /// <summary>读取候选中指定层的当前活动叶，视图失效或层不存在时返回 false。</summary>
        public bool TryGetActiveLeaf(
            LayerId layerId,
            out StateId stateId)
        {
            if (!IsValid || !layerId.IsValid)
            {
                stateId = default;
                return false;
            }

            return _source.TryGetActiveLeaf(_token, layerId, out stateId) && stateId.IsValid;
        }

        /// <summary>读取候选中指定节点的状态记录，视图失效或类型、节点不匹配时返回 false。</summary>
        public bool TryGetState<TState>(
            StateId stateId,
            out GraphStateRecord<TState> state)
            where TState : unmanaged
        {
            if (!IsValid || !stateId.IsValid)
            {
                state = default;
                return false;
            }

            return _source.TryGetState(_token, stateId, out state) && state.IsValid;
        }

        #endregion

        #region Internal Logic

        internal StagedGraphReadView(
            IStagedGraphReadSource source,
            ulong token)
        {
            _source = source;
            _token = token;
        }

        #endregion
    }

    /// <summary>向图槽生产方提供当前实例、上一提交及本步的图与 Operation 视图。</summary>
    public readonly struct GraphContextCaptureContext
    {

        #region Public API

        public GraphInstanceId GraphInstanceId { get; }
        public TickFrame TickFrame { get; }
        public ContextFrameReadView PreviousContext { get; }
        public StagedGraphReadView StagedGraph { get; }
        public StagedOperationFrame OperationFrame { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               StagedGraph.IsValid &&
                               OperationFrame.IsValid;

        /// <summary>读取候选中该类型化操作的条目，候选或句柄无效时返回 false。</summary>
        public bool TryGetOperation<TOperation>(
            OperationRequirement requirement,
            out OperationEntry<TOperation> entry)
            where TOperation : class, IOperation
        {
            if (!IsValid ||
                !requirement.IsValid ||
                requirement.OperationType != typeof(TOperation) ||
                OperationFrame.Registry == null ||
                !OperationFrame.Registry.TryResolve(
                    requirement,
                    out OperationHandle<TOperation> handle))
            {
                entry = default;
                return false;
            }

            return OperationFrame.TryGet(handle, out entry);
        }

        #endregion

        #region Internal Logic

        internal GraphContextCaptureContext(
            GraphInstanceId graphInstanceId,
            in TickFrame tickFrame,
            ContextFrameReadView previousContext,
            StagedGraphReadView stagedGraph,
            StagedOperationFrame operationFrame)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousContext = previousContext;
            StagedGraph = stagedGraph;
            OperationFrame = operationFrame;
        }

        #endregion
    }

    /// <summary>区分帧头描述的输入、输出和 Context 数据。</summary>
    public enum StateFlowFrameKind
    {
        None = 0,
        Intent = 1,
        Operation = 2,
        Context = 3
    }

    /// <summary>标识一份帧所属的图实例、时间线、执行序号与修订。</summary>
    public readonly struct StateFlowFrameIdentity : IEquatable<StateFlowFrameIdentity>
    {
        /// <summary>记录实例、时间线代次、执行序号和帧修订，供读写方核对帧归属。</summary>
        public StateFlowFrameIdentity(
            GraphInstanceId graphInstanceId,
            TimelineEpoch timelineEpoch,
            TimelineTick tick,
            ExecutionSequence executionSequence,
            StateFlowFrameKind kind)
        {
            GraphInstanceId = graphInstanceId;
            TimelineEpoch = timelineEpoch;
            Tick = tick;
            ExecutionSequence = executionSequence;
            Kind = kind;
        }

        public GraphInstanceId GraphInstanceId { get; }
        public TimelineEpoch TimelineEpoch { get; }
        public TimelineTick Tick { get; }
        public ExecutionSequence ExecutionSequence { get; }
        public StateFlowFrameKind Kind { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               Kind >= StateFlowFrameKind.Intent &&
                               Kind <= StateFlowFrameKind.Context;

        /// <summary>按<c>GraphInstanceId</c>、<c>TimelineEpoch</c>、<c>Tick</c>、<c>ExecutionSequence</c>、<c>Kind</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateFlowFrameIdentity other)
        {
            return GraphInstanceId == other.GraphInstanceId &&
                   TimelineEpoch == other.TimelineEpoch &&
                   Tick == other.Tick &&
                   ExecutionSequence == other.ExecutionSequence &&
                   Kind == other.Kind;
        }

        /// <summary>仅当对象属于同类型并且<c>GraphInstanceId</c>、<c>TimelineEpoch</c>、<c>Tick</c>、<c>ExecutionSequence</c>、<c>Kind</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateFlowFrameIdentity other && Equals(other);

        /// <summary>为哈希集合计算与<c>GraphInstanceId</c>、<c>TimelineEpoch</c>、<c>Tick</c>、<c>ExecutionSequence</c>、<c>Kind</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = GraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ TimelineEpoch.GetHashCode();
                hashCode = (hashCode * 397) ^ Tick.GetHashCode();
                hashCode = (hashCode * 397) ^ ExecutionSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Kind;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>GraphInstanceId</c>、<c>TimelineEpoch</c>、<c>Tick</c>、<c>ExecutionSequence</c>、<c>Kind</c>，相等时返回 true。</summary>
        public static bool operator ==(StateFlowFrameIdentity left, StateFlowFrameIdentity right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>GraphInstanceId</c>、<c>TimelineEpoch</c>、<c>Tick</c>、<c>ExecutionSequence</c>、<c>Kind</c>，存在差异时返回 true。</summary>
        public static bool operator !=(StateFlowFrameIdentity left, StateFlowFrameIdentity right) =>
            !left.Equals(right);
    }

    /// <summary>将步进身份与帧种类、布局标识关联，供读写两端校验。</summary>
    public readonly struct StateFlowFrameHeader : IEquatable<StateFlowFrameHeader>
    {

        #region Public API

        public StateFlowFrameIdentity Identity { get; }
        public FrameLayoutId LayoutId { get; }
        public uint LayoutVersion { get; }
        public ulong LayoutSchemaHash { get; }
        public TickFrame TickFrame { get; }
        public bool IsValid => Identity.IsValid && LayoutId.IsValid && TickFrame.IsValid;
        public bool HasExactLayoutIdentity => LayoutVersion > 0U && LayoutSchemaHash != 0UL;

        /// <summary>将实例、帧种类与布局绑定到这次步进，无效时间身份或布局不能生成有效帧头。</summary>
        public static bool TryCreate(
            GraphInstanceId graphInstanceId,
            FrameLayoutId layoutId,
            StateFlowFrameKind kind,
            TickFrame tickFrame,
            out StateFlowFrameHeader header)
        {
            var identity = new StateFlowFrameIdentity(
                graphInstanceId,
                tickFrame.TimelineEpoch,
                tickFrame.Tick,
                tickFrame.ExecutionSequence,
                kind);
            if (!identity.IsValid || !layoutId.IsValid || !tickFrame.IsValid)
            {
                header = default;
                return false;
            }

            header = new StateFlowFrameHeader(identity, layoutId, 0U, 0UL, tickFrame);
            return true;
        }

        /// <summary>将实例、帧种类与布局绑定到这次步进，无效时间身份或布局不能生成有效帧头。</summary>
        public static bool TryCreate(
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            StateFlowFrameKind kind,
            TickFrame tickFrame,
            out StateFlowFrameHeader header)
        {
            if (layout == null ||
                !TryCreate(graphInstanceId, layout.LayoutId, kind, tickFrame, out StateFlowFrameHeader basic))
            {
                header = default;
                return false;
            }

            header = new StateFlowFrameHeader(
                basic.Identity,
                layout.LayoutId,
                layout.Version,
                layout.SchemaHash,
                tickFrame);
            return true;
        }

        /// <summary>按<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>TickFrame</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(StateFlowFrameHeader other)
        {
            return Identity == other.Identity &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   TickFrame == other.TickFrame;
        }

        /// <summary>仅当对象属于同类型并且<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>TickFrame</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is StateFlowFrameHeader other && Equals(other);
        /// <summary>为哈希集合计算与<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>TickFrame</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Identity.GetHashCode();
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)LayoutVersion;
                hashCode = (hashCode * 397) ^ LayoutSchemaHash.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>TickFrame</c>，相等时返回 true。</summary>
        public static bool operator ==(StateFlowFrameHeader left, StateFlowFrameHeader right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>TickFrame</c>，存在差异时返回 true。</summary>
        public static bool operator !=(StateFlowFrameHeader left, StateFlowFrameHeader right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        private StateFlowFrameHeader(
            StateFlowFrameIdentity identity,
            FrameLayoutId layoutId,
            uint layoutVersion,
            ulong layoutSchemaHash,
            TickFrame tickFrame)
        {
            Identity = identity;
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            LayoutSchemaHash = layoutSchemaHash;
            TickFrame = tickFrame;
        }

        #endregion
    }

    /// <summary>记录某一实例已经成功提交的 Context 修订号。</summary>
    public readonly struct ContextRevision : IEquatable<ContextRevision>
    {
        /// <summary>将给定的版本值保存在 Context 修订中，供连续提交与历史读取对照。</summary>
        public ContextRevision(ulong value)
        {
            Value = value;
        }

        public ulong Value { get; }
        public bool IsValid => Value != 0UL;

        /// <summary>按<c>Value</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(ContextRevision other) => Value == other.Value;
        /// <summary>仅当对象属于同类型并且<c>Value</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is ContextRevision other && Equals(other);
        /// <summary>为哈希集合计算与<c>Value</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => Value.GetHashCode();

        /// <summary>比较两端的<c>Value</c>，相等时返回 true。</summary>
        public static bool operator ==(ContextRevision left, ContextRevision right) => left.Equals(right);
        /// <summary>比较两端的<c>Value</c>，存在差异时返回 true。</summary>
        public static bool operator !=(ContextRevision left, ContextRevision right) => !left.Equals(right);
    }

    /// <summary>区分正常提交的 Context 和从已有记录恢复的 Context。</summary>
    public enum ContextFrameOriginKind
    {
        None = 0,
        Commit = 1,
        Restore = 2
    }

    /// <summary>保存 Context 的生成方式及恢复所依据的帧身份。</summary>
    public readonly struct ContextFrameOrigin : IEquatable<ContextFrameOrigin>
    {

        #region Public API

        public ContextFrameOriginKind Kind { get; }
        public GraphInstanceId SourceGraphInstanceId { get; }
        public TimelineEpoch SourceTimelineEpoch { get; }
        public TimelineTick SourceTick { get; }
        public ContextRevision SourceRevision { get; }
        public bool IsRestore => Kind == ContextFrameOriginKind.Restore;
        public bool IsValid => Kind == ContextFrameOriginKind.Commit ||
                               (Kind == ContextFrameOriginKind.Restore &&
                                SourceGraphInstanceId.IsValid &&
                                SourceRevision.IsValid);

        /// <summary>为普通步进产生的 Context 标记提交来源。</summary>
        public static ContextFrameOrigin Commit() =>
            new ContextFrameOrigin(ContextFrameOriginKind.Commit, default, default, default, default);

        /// <summary>为恢复生成的 Context 标记其来源帧，供诊断与历史读取追溯。</summary>
        public static ContextFrameOrigin RestoreFrom(ContextFrame source)
        {
            if (!source.IsAlive)
            {
                return default;
            }

            return new ContextFrameOrigin(
                ContextFrameOriginKind.Restore,
                source.Header.Identity.GraphInstanceId,
                source.Header.Identity.TimelineEpoch,
                source.Header.Identity.Tick,
                source.Revision);
        }

        /// <summary>按<c>Kind</c>、<c>SourceGraphInstanceId</c>、<c>SourceTimelineEpoch</c>、<c>SourceTick</c>、<c>SourceRevision</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(ContextFrameOrigin other)
        {
            return Kind == other.Kind &&
                   SourceGraphInstanceId == other.SourceGraphInstanceId &&
                   SourceTimelineEpoch == other.SourceTimelineEpoch &&
                   SourceTick == other.SourceTick &&
                   SourceRevision == other.SourceRevision;
        }

        /// <summary>仅当对象属于同类型并且<c>Kind</c>、<c>SourceGraphInstanceId</c>、<c>SourceTimelineEpoch</c>、<c>SourceTick</c>、<c>SourceRevision</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is ContextFrameOrigin other && Equals(other);
        /// <summary>为哈希集合计算与<c>Kind</c>、<c>SourceGraphInstanceId</c>、<c>SourceTimelineEpoch</c>、<c>SourceTick</c>、<c>SourceRevision</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ SourceRevision.GetHashCode());

        #endregion

        #region Internal Logic

        private ContextFrameOrigin(
            ContextFrameOriginKind kind,
            GraphInstanceId sourceGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            ContextRevision sourceRevision)
        {
            Kind = kind;
            SourceGraphInstanceId = sourceGraphInstanceId;
            SourceTimelineEpoch = sourceTimelineEpoch;
            SourceTick = sourceTick;
            SourceRevision = sourceRevision;
        }

        internal static ContextFrameOrigin RestoreFrom(
            GraphInstanceId sourceGraphInstanceId,
            TimelineEpoch sourceTimelineEpoch,
            TimelineTick sourceTick,
            ContextRevision sourceRevision)
        {
            var origin = new ContextFrameOrigin(
                ContextFrameOriginKind.Restore,
                sourceGraphInstanceId,
                sourceTimelineEpoch,
                sourceTick,
                sourceRevision);
            return origin.IsValid ? origin : default;
        }

        #endregion
    }

    /// <summary>提供按类型化槽读取 Context 数据的公共合同。</summary>
    public interface IContextFrame
    {
        StateFlowFrameHeader Header { get; }
        ContextRevision Revision { get; }
        ContextFrameOrigin Origin { get; }
        /// <summary>读取该帧布局中类型化槽的值，调用方须持有有效帧及匹配的槽句柄。</summary>
        T Read<T>(StateSlot<T> slot) where T : unmanaged;
    }

    /// <summary>向回调提供指定代次的已提交槽值和帧头，帧存储本身由实例管理。</summary>
    internal interface ICommittedContextReadSource
    {
        StateFlowFrameHeader Header { get; }
        ContextRevision Revision { get; }
        ContextFrameOrigin Origin { get; }
        bool IsAlive(ulong generation);
        TValue Read<TValue>(StateSlot<TValue> slot) where TValue : unmanaged;
    }

    /// <summary>框架传给回调的上一提交视图，首次提交前读取布局默认值。</summary>
    public readonly struct ContextFrameReadView : IContextFrame, IEquatable<ContextFrameReadView>
    {
        #region Public API

        public StateFlowFrameHeader Header => HasCommittedFrame ? _source.Header : default;
        public ContextRevision Revision => HasCommittedFrame ? _source.Revision : default;
        public ContextFrameOrigin Origin => HasCommittedFrame ? _source.Origin : default;
        public ContextFrameLayout Layout => IsValid ? _layout : null;
        public bool HasCommittedFrame => _source != null && _source.IsAlive(_generation);
        public bool IsValid => _layout != null && (_source == null || _source.IsAlive(_generation));

        /// <summary>读上一成功提交的槽值，首步读取布局默认值。视图失效或槽属于其他布局时抛出异常。</summary>
        public TValue Read<TValue>(StateSlot<TValue> slot) where TValue : unmanaged
        {
            if (!IsValid)
                throw new InvalidOperationException("The ContextFrame read view is no longer valid.");
            return _source == null ? _layout.ReadDefault(slot) : _source.Read(slot);
        }

        /// <summary>两个视图对应同一布局、存储和代次时返回 true，包括同一布局的初始视图。</summary>
        public bool Equals(ContextFrameReadView other) =>
            ReferenceEquals(_layout, other._layout) && ReferenceEquals(_source, other._source) &&
            _generation == other._generation;

        /// <summary>检查对象是否为相同布局与存储代次的读取视图。</summary>
        public override bool Equals(object obj) => obj is ContextFrameReadView other && Equals(other);

        /// <summary>用布局、已提交来源和代次生成可用于字典的散列值。</summary>
        public override int GetHashCode() => unchecked(((_layout?.GetHashCode() ?? 0) * 397) ^
            (((_source?.GetHashCode() ?? 0) * 397) ^ _generation.GetHashCode()));

        /// <summary>两端指向同一布局及同一已提交来源和代次时返回 true。</summary>
        public static bool operator ==(ContextFrameReadView left, ContextFrameReadView right) => left.Equals(right);

        /// <summary>两端来自不同布局、存储或代次时返回 true。</summary>
        public static bool operator !=(ContextFrameReadView left, ContextFrameReadView right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private readonly ContextFrameLayout _layout;
        private readonly ICommittedContextReadSource _source;
        private readonly ulong _generation;

        internal ContextFrameReadView(ContextFrameLayout layout, ICommittedContextReadSource source = null,
            ulong generation = 0)
        {
            _layout = layout;
            _source = source;
            _generation = generation;
        }

        #endregion
    }
}
