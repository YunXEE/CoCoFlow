using System;
using CoCoFlow.Runtime.Core;

// 实例把候选边、胜出边、提交和故障写入有界跟踪缓冲，调试器按过滤条件复制近期记录。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>标明跟踪项所属的候选、选边、提交或故障阶段。</summary>
    [Flags]
    public enum StateFlowTraceKind
    {
        /// <summary>尚无指定值。</summary>
        None = 0,
        /// <summary>记录步进的时钟边界。</summary>
        Tick = 1 << 0,
        /// <summary>记录候选转换或选中的转换。</summary>
        Transition = 1 << 1,
        /// <summary>记录本步产生的操作。</summary>
        Operation = 1 << 2,
        /// <summary>记录 Operator 的执行结果。</summary>
        OperatorOutcome = 1 << 3,
        /// <summary>记录 Context 提交。</summary>
        ContextCommit = 1 << 4,
        /// <summary>记录事件分配的序号。</summary>
        EventSequence = 1 << 5,
        /// <summary>记录已发布的事件。</summary>
        EventPublished = 1 << 6,
        /// <summary>与本次操作关联的诊断。</summary>
        Diagnostic = 1 << 7,
        /// <summary>候选已取消。</summary>
        Cancelled = 1 << 8,
        /// <summary>记录一层已提交的活动路径。</summary>
        ActivePath = 1 << 9,
        /// <summary>包含所有已定义的跟踪种类。</summary>
        All = Tick | Transition | Operation | OperatorOutcome |
              ContextCommit | EventSequence | EventPublished | Diagnostic | Cancelled |
              ActivePath
    }

    /// <summary>区分参与仲裁的出边及其最终选中情况。</summary>
    public enum StateFlowTransitionRole
    {
        /// <summary>尚无指定值。</summary>
        None = 0,
        /// <summary>本次参与选择的候选转换。</summary>
        Candidate = 1,
        /// <summary>本次选中的转换。</summary>
        Winner = 2
    }

    /// <summary>记录跟踪项所指帧的标识，供调试器关联已提交状态。</summary>
    public readonly struct StateFlowTraceFrameReference : IEquatable<StateFlowTraceFrameReference>
    {

        #region Public API

        /// <summary>跟踪项所引用帧的身份。</summary>
        public StateFlowFrameIdentity Identity { get; }
        /// <summary>解释该帧的布局标识。</summary>
        public FrameLayoutId LayoutId { get; }
        /// <summary>帧布局的版本。</summary>
        public uint LayoutVersion { get; }
        /// <summary>冻结布局的结构指纹。</summary>
        public ulong LayoutSchemaHash { get; }
        /// <summary>此帧提交时的修订号。</summary>
        public ContextRevision Revision { get; }
        /// <summary>跟踪引用指向已提交帧时为真。</summary>
        public bool HasCommittedFrame { get; }

        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => LayoutId.IsValid &&
                               LayoutVersion > 0U &&
                               LayoutSchemaHash != 0UL &&
                               (HasCommittedFrame
                                   ? Identity.IsValid &&
                                     Identity.Kind == StateFlowFrameKind.Context &&
                                     Revision.IsValid
                                   : !Identity.IsValid && !Revision.IsValid);

        /// <summary>从仍然有效的 Context 视图提取跟踪用帧身份，缺少已提交帧时返回 false。</summary>
        /// <param name="context">要提取帧身份的当前 Context 读视图。</param>
        /// <param name="reference">成功时为提取的帧引用。</param>
        /// <returns>输入有效并成功生成结果时为真。</returns>
        public static bool TryCreate(
            ContextFrameReadView context,
            out StateFlowTraceFrameReference reference)
        {
            if (!context.IsValid || context.Layout == null)
            {
                reference = default;
                return false;
            }

            ContextFrameLayout layout = context.Layout;
            if (context.HasCommittedFrame)
            {
                StateFlowFrameHeader header = context.Header;
                if (!header.IsValid ||
                    !header.HasExactLayoutIdentity ||
                    header.Identity.Kind != StateFlowFrameKind.Context ||
                    header.LayoutId != layout.LayoutId ||
                    header.LayoutVersion != layout.Version ||
                    header.LayoutSchemaHash != layout.SchemaHash ||
                    !context.Revision.IsValid)
                {
                    reference = default;
                    return false;
                }

                reference = new StateFlowTraceFrameReference(
                    header.Identity,
                    header.LayoutId,
                    header.LayoutVersion,
                    header.LayoutSchemaHash,
                    context.Revision,
                    true);
                return true;
            }

            reference = new StateFlowTraceFrameReference(
                default,
                layout.LayoutId,
                layout.Version,
                layout.SchemaHash,
                default,
                false);
            return reference.IsValid;
        }

        /// <summary>按<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>Revision</c>比较两个值，全部相等时返回 true。</summary>
        /// <param name="other">待比较的同类值。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public bool Equals(StateFlowTraceFrameReference other)
        {
            return Identity == other.Identity &&
                   LayoutId == other.LayoutId &&
                   LayoutVersion == other.LayoutVersion &&
                   LayoutSchemaHash == other.LayoutSchemaHash &&
                   Revision == other.Revision &&
                   HasCommittedFrame == other.HasCommittedFrame;
        }

        /// <summary>仅当对象属于同类型并且<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>Revision</c>相等时返回 true。</summary>
        /// <param name="obj">待比较的对象。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public override bool Equals(object obj) =>
            obj is StateFlowTraceFrameReference other && Equals(other);

        /// <summary>为哈希集合计算与<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>Revision</c>的相等判定一致的散列值。</summary>
        /// <returns>与相等判定一致的哈希值。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Identity.GetHashCode();
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)LayoutVersion;
                hashCode = (hashCode * 397) ^ LayoutSchemaHash.GetHashCode();
                hashCode = (hashCode * 397) ^ Revision.GetHashCode();
                hashCode = (hashCode * 397) ^ HasCommittedFrame.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>Revision</c>，相等时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator ==(
            StateFlowTraceFrameReference left,
            StateFlowTraceFrameReference right) => left.Equals(right);

        /// <summary>比较两端的<c>Identity</c>、<c>LayoutId</c>、<c>LayoutVersion</c>、<c>LayoutSchemaHash</c>、<c>Revision</c>，存在差异时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator !=(
            StateFlowTraceFrameReference left,
            StateFlowTraceFrameReference right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private StateFlowTraceFrameReference(
            StateFlowFrameIdentity identity,
            FrameLayoutId layoutId,
            uint layoutVersion,
            ulong layoutSchemaHash,
            ContextRevision revision,
            bool hasCommittedFrame)
        {
            Identity = identity;
            LayoutId = layoutId;
            LayoutVersion = layoutVersion;
            LayoutSchemaHash = layoutSchemaHash;
            Revision = revision;
            HasCommittedFrame = hasCommittedFrame;
        }

        internal static bool TryCreateCommitted(
            GraphInstanceId graphInstanceId,
            ContextFrameLayout layout,
            in TickFrame tickFrame,
            ContextRevision revision,
            out StateFlowTraceFrameReference reference)
        {
            if (layout == null ||
                !revision.IsValid ||
                !StateFlowFrameHeader.TryCreate(
                    graphInstanceId,
                    layout,
                    StateFlowFrameKind.Context,
                    tickFrame,
                    out StateFlowFrameHeader header))
            {
                reference = default;
                return false;
            }

            reference = new StateFlowTraceFrameReference(
                header.Identity,
                header.LayoutId,
                header.LayoutVersion,
                header.LayoutSchemaHash,
                revision,
                true);
            return reference.IsValid;
        }

        #endregion
    }

    /// <summary>描述一次带实例、节点或转换定位信息的跟踪事件。</summary>
    public readonly struct StateFlowTraceEntry : IEquatable<StateFlowTraceEntry>
    {

        #region Public API

        /// <summary>本跟踪记录的种类。</summary>
        public StateFlowTraceKind Kind { get; }
        /// <summary>生成本记录的图实例标识。</summary>
        public GraphInstanceId GraphInstanceId { get; }
        /// <summary>生成此记录时的时钟快照。</summary>
        public TickFrame TickFrame { get; }
        /// <summary>提交之前的修订号。</summary>
        public ContextRevision PreviousRevision { get; }
        /// <summary>本次提交后的修订号。</summary>
        public ContextRevision NewRevision { get; }
        /// <summary>对应的图层身份。</summary>
        public LayerId LayerId { get; }
        /// <summary>对应的状态身份。</summary>
        public StateId StateId { get; }
        /// <summary>对应的出边身份。</summary>
        public TransitionId TransitionId { get; }
        /// <summary>对应的操作标识。</summary>
        public OperationId OperationId { get; }
        /// <summary>对应的 Operator 标识。</summary>
        public OperatorId OperatorId { get; }
        /// <summary>本条记录关联的 Operator 执行结果。</summary>
        public OperatorOutcomeStatus OperatorOutcome { get; }
        /// <summary>本次记录覆盖的首个事件序号。</summary>
        public EventSequence FirstEventSequence { get; }
        /// <summary>本次记录覆盖的末个事件序号。</summary>
        public EventSequence LastEventSequence { get; }
        /// <summary>诊断所属模块。</summary>
        public CoCoDiagnosticDomain DiagnosticDomain { get; }
        /// <summary>本条记录携带的诊断代码。</summary>
        public CoCoDiagnosticCode DiagnosticCode { get; }
        /// <summary>这条记录对应的帧身份。</summary>
        public StateFlowTraceFrameReference Frame { get; }
        /// <summary>本步使用的先前 Context 帧引用。</summary>
        public StateFlowTraceFrameReference PreviousContext { get; }
        /// <summary>本次转换记录为候选或选中项。</summary>
        public StateFlowTransitionRole TransitionRole { get; }
        /// <summary>此值已初始化且满足该类型的身份或代次约束时为真。</summary>
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               IsSingleKind(Kind) &&
                               HasValidContextReferences() &&
                               HasValidKindIdentity();

        /// <summary>按<c>Kind</c>、<c>GraphInstanceId</c>、<c>TickFrame</c>、<c>PreviousRevision</c>、<c>NewRevision</c>比较两个值，全部相等时返回 true。</summary>
        /// <param name="other">待比较的同类值。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public bool Equals(StateFlowTraceEntry other)
        {
            return Kind == other.Kind &&
                   GraphInstanceId == other.GraphInstanceId &&
                   TickFrame == other.TickFrame &&
                   PreviousRevision == other.PreviousRevision &&
                   NewRevision == other.NewRevision &&
                   LayerId == other.LayerId &&
                   StateId == other.StateId &&
                   TransitionId == other.TransitionId &&
                   OperationId == other.OperationId &&
                   OperatorId == other.OperatorId &&
                   OperatorOutcome == other.OperatorOutcome &&
                   FirstEventSequence == other.FirstEventSequence &&
                   LastEventSequence == other.LastEventSequence &&
                   DiagnosticDomain == other.DiagnosticDomain &&
                   DiagnosticCode == other.DiagnosticCode &&
                   Frame == other.Frame &&
                   PreviousContext == other.PreviousContext &&
                   TransitionRole == other.TransitionRole;
        }

        /// <summary>仅当对象属于同类型并且<c>Kind</c>、<c>GraphInstanceId</c>、<c>TickFrame</c>、<c>PreviousRevision</c>、<c>NewRevision</c>相等时返回 true。</summary>
        /// <param name="obj">待比较的对象。</param>
        /// <returns>两者代表同一值时为真。</returns>
        public override bool Equals(object obj) => obj is StateFlowTraceEntry other && Equals(other);

        /// <summary>为哈希集合计算与<c>Kind</c>、<c>GraphInstanceId</c>、<c>TickFrame</c>、<c>PreviousRevision</c>、<c>NewRevision</c>的相等判定一致的散列值。</summary>
        /// <returns>与相等判定一致的哈希值。</returns>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^ GraphInstanceId.GetHashCode();
                hashCode = (hashCode * 397) ^ TickFrame.GetHashCode();
                hashCode = (hashCode * 397) ^ LayerId.GetHashCode();
                hashCode = (hashCode * 397) ^ StateId.GetHashCode();
                hashCode = (hashCode * 397) ^ TransitionId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperationId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperatorId.GetHashCode();
                hashCode = (hashCode * 397) ^ FirstEventSequence.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)DiagnosticCode;
                hashCode = (hashCode * 397) ^ Frame.GetHashCode();
                hashCode = (hashCode * 397) ^ PreviousContext.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)TransitionRole;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>Kind</c>、<c>GraphInstanceId</c>、<c>TickFrame</c>、<c>PreviousRevision</c>、<c>NewRevision</c>，相等时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator ==(StateFlowTraceEntry left, StateFlowTraceEntry right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>Kind</c>、<c>GraphInstanceId</c>、<c>TickFrame</c>、<c>PreviousRevision</c>、<c>NewRevision</c>，存在差异时返回 true。</summary>
        /// <param name="left">左侧比较值。</param>
        /// <param name="right">右侧比较值。</param>
        public static bool operator !=(StateFlowTraceEntry left, StateFlowTraceEntry right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        internal StateFlowTraceEntry(
            StateFlowTraceKind kind,
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            ContextRevision previousRevision,
            ContextRevision newRevision,
            StateId stateId,
            TransitionId transitionId,
            OperationId operationId,
            OperatorId operatorId,
            OperatorOutcomeStatus operatorOutcome,
            EventSequence firstEventSequence,
            EventSequence lastEventSequence,
            CoCoDiagnosticDomain diagnosticDomain,
            CoCoDiagnosticCode diagnosticCode,
            LayerId layerId = default,
            StateFlowTraceFrameReference frame = default,
            StateFlowTraceFrameReference previousContext = default,
            StateFlowTransitionRole transitionRole = StateFlowTransitionRole.None)
        {
            Kind = kind;
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousRevision = previousRevision;
            NewRevision = newRevision;
            StateId = stateId;
            TransitionId = transitionId;
            OperationId = operationId;
            OperatorId = operatorId;
            OperatorOutcome = operatorOutcome;
            FirstEventSequence = firstEventSequence;
            LastEventSequence = lastEventSequence;
            DiagnosticDomain = diagnosticDomain;
            DiagnosticCode = diagnosticCode;
            LayerId = layerId;
            Frame = frame;
            PreviousContext = previousContext;
            TransitionRole = transitionRole;
        }

        internal static StateFlowTraceEntry Tick(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Tick,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Transition(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            LayerId layerId,
            TransitionId transitionId,
            StateFlowTransitionRole transitionRole = StateFlowTransitionRole.Winner,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Transition,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                transitionId,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                layerId,
                previousContext: previousContext,
                transitionRole: transitionRole);

        internal static StateFlowTraceEntry Path(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            LayerId layerId,
            StateId stateId,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.ActivePath,
                graphInstanceId,
                tickFrame,
                default,
                default,
                stateId,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                layerId,
                frame: frame);

        internal static StateFlowTraceEntry Operation(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            OperationId operationId,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Operation,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                operationId,
                default,
                default,
                default,
                default,
                default,
                default,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Operator(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            OperatorId operatorId,
            OperatorOutcomeStatus outcome,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.OperatorOutcome,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                operatorId,
                outcome,
                default,
                default,
                default,
                default,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Commit(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            ContextRevision previousRevision,
            ContextRevision newRevision,
            StateFlowTraceFrameReference previousContext = default,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.ContextCommit,
                graphInstanceId,
                tickFrame,
                previousRevision,
                newRevision,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                frame: frame,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Sequence(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            EventSequence first,
            EventSequence last,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.EventSequence,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                first,
                last,
                default,
                default,
                frame: frame);

        internal static StateFlowTraceEntry Published(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            EventSequence sequence,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.EventPublished,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                sequence,
                sequence,
                default,
                default,
                frame: frame);

        internal static StateFlowTraceEntry Diagnostic(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            CoCoDiagnostic diagnostic,
            StateFlowTraceFrameReference previousContext = default,
            StateFlowTraceFrameReference frame = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Diagnostic,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                diagnostic.Domain,
                diagnostic.Code,
                frame: frame,
                previousContext: previousContext);

        internal static StateFlowTraceEntry Cancelled(
            GraphInstanceId graphInstanceId,
            TickFrame tickFrame,
            CoCoDiagnostic diagnostic,
            StateFlowTraceFrameReference previousContext = default) =>
            new StateFlowTraceEntry(
                StateFlowTraceKind.Cancelled,
                graphInstanceId,
                tickFrame,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                diagnostic.Domain,
                diagnostic.Code,
                previousContext: previousContext);

        private static bool IsSingleKind(StateFlowTraceKind kind)
        {
            int value = (int)kind;
            return value > 0 && (value & (value - 1)) == 0 &&
                   (kind & StateFlowTraceKind.All) == kind;
        }

        private bool HasValidKindIdentity()
        {
            switch (Kind)
            {
                case StateFlowTraceKind.Tick:
                    return TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.ActivePath:
                    return LayerId.IsValid &&
                           StateId.IsValid &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.Transition:
                    return LayerId.IsValid &&
                           TransitionId.IsValid &&
                           (TransitionRole == StateFlowTransitionRole.Candidate ||
                            TransitionRole == StateFlowTransitionRole.Winner);
                case StateFlowTraceKind.Operation:
                    return OperationId.IsValid && TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.OperatorOutcome:
                    return OperatorId.IsValid &&
                           OperatorOutcome >= OperatorOutcomeStatus.Succeeded &&
                           OperatorOutcome <= OperatorOutcomeStatus.ClaimDenied &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.ContextCommit:
                    return NewRevision.IsValid &&
                           TransitionRole == StateFlowTransitionRole.None &&
                           (!Frame.IsValid ||
                            (Frame.HasCommittedFrame && Frame.Revision == NewRevision)) &&
                           (!PreviousContext.IsValid ||
                            (PreviousContext.HasCommittedFrame
                                ? PreviousContext.Revision == PreviousRevision
                                : !PreviousRevision.IsValid));
                case StateFlowTraceKind.EventSequence:
                    return FirstEventSequence.IsValid &&
                           LastEventSequence.IsValid &&
                           FirstEventSequence.Value <= LastEventSequence.Value &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.EventPublished:
                    return FirstEventSequence.IsValid &&
                           FirstEventSequence == LastEventSequence &&
                           TransitionRole == StateFlowTransitionRole.None;
                case StateFlowTraceKind.Diagnostic:
                case StateFlowTraceKind.Cancelled:
                    return DiagnosticDomain != CoCoDiagnosticDomain.None &&
                           DiagnosticCode != CoCoDiagnosticCode.None &&
                           TransitionRole == StateFlowTransitionRole.None;
                default:
                    return false;
            }
        }

        private bool HasValidContextReferences()
        {
            return (Frame == default || Frame.IsValid) &&
                   (PreviousContext == default || PreviousContext.IsValid);
        }

        #endregion
    }

    /// <summary>指定复制跟踪记录时需匹配的实例、元素或事件类别。</summary>
    public readonly struct StateFlowTraceFilter
    {

        #region Public API

        /// <summary>包含所有已定义的跟踪种类。</summary>
        public static StateFlowTraceFilter All => new StateFlowTraceFilter(StateFlowTraceKind.All);

        /// <summary>纳入查询的跟踪种类集合。</summary>
        public StateFlowTraceKind Kinds { get; }
        /// <summary>需要匹配的图实例标识。</summary>
        public GraphInstanceId GraphInstanceId { get; }
        /// <summary>对应的 Operator 标识。</summary>
        public OperatorId OperatorId { get; }
        /// <summary>对应的操作标识。</summary>
        public OperationId OperationId { get; }
        /// <summary>本条记录携带的诊断代码。</summary>
        public CoCoDiagnosticCode DiagnosticCode { get; }
        /// <summary>对应的图层身份。</summary>
        public LayerId LayerId { get; }
        /// <summary>对应的状态身份。</summary>
        public StateId StateId { get; }
        /// <summary>对应的出边身份。</summary>
        public TransitionId TransitionId { get; }
        /// <summary>记录调用方指定的跟踪筛选条件，供复制近期记录时匹配。</summary>
        /// <param name="kinds">接受的跟踪记录种类。</param>
        /// <param name="graphInstanceId">要匹配的图实例标识。</param>
        /// <param name="operatorId">需要匹配的 Operator 标识。</param>
        /// <param name="operationId">需要匹配的操作标识。</param>
        /// <param name="diagnosticCode">查询需要匹配的诊断代码。</param>
        /// <param name="layerId">要匹配的图层标识。</param>
        /// <param name="stateId">需要匹配的状态标识。</param>
        /// <param name="transitionId">要查找或匹配的出边标识。</param>
        public StateFlowTraceFilter(
            StateFlowTraceKind kinds,
            GraphInstanceId graphInstanceId = default,
            OperatorId operatorId = default,
            OperationId operationId = default,
            CoCoDiagnosticCode diagnosticCode = CoCoDiagnosticCode.None,
            LayerId layerId = default,
            StateId stateId = default,
            TransitionId transitionId = default)
        {
            Kinds = kinds;
            GraphInstanceId = graphInstanceId;
            OperatorId = operatorId;
            OperationId = operationId;
            DiagnosticCode = diagnosticCode;
            LayerId = layerId;
            StateId = stateId;
            TransitionId = transitionId;
        }

        #endregion

        #region Internal Logic

        internal bool Matches(in StateFlowTraceEntry entry)
        {
            StateFlowTraceKind kinds = Kinds == StateFlowTraceKind.None
                ? StateFlowTraceKind.All
                : Kinds;
            return entry.IsValid &&
                   (kinds & entry.Kind) != 0 &&
                   (!GraphInstanceId.IsValid || entry.GraphInstanceId == GraphInstanceId) &&
                   (!OperatorId.IsValid || entry.OperatorId == OperatorId) &&
                   (!OperationId.IsValid || entry.OperationId == OperationId) &&
                   (!LayerId.IsValid || entry.LayerId == LayerId) &&
                   (!StateId.IsValid || entry.StateId == StateId) &&
                   (!TransitionId.IsValid || entry.TransitionId == TransitionId) &&
                   (DiagnosticCode == CoCoDiagnosticCode.None || entry.DiagnosticCode == DiagnosticCode);
        }

        #endregion
    }

    /// <summary>将符合过滤条件的近期跟踪项复制到调用方提供的存储。</summary>
    public interface IStateFlowTrace
    {
        /// <summary>此存储可接纳的最大条目数。</summary>
        int Capacity { get; }
        /// <summary>当前可读取的条目数量。</summary>
        int Count { get; }
        /// <summary>此跟踪缓冲累计接收的记录数。</summary>
        ulong TotalWritten { get; }
        /// <summary>按过滤条件将近期跟踪项写入调用方缓冲区，返回实际复制数。</summary>
        /// <param name="destination">接收符合过滤条件的跟踪项的缓冲区。</param>
        /// <param name="filter">决定哪些记录需要复制的过滤条件。</param>
        /// <returns>复制到目标缓冲区的实际记录数。</returns>
        int CopyLatestTo(Span<StateFlowTraceEntry> destination, StateFlowTraceFilter filter = default);
    }

    /// <summary>在有界缓冲内记录步进过程，供调试器读取近期条目。</summary>
    internal sealed class StateFlowTraceBuffer : IStateFlowTrace
    {
        private readonly StateFlowTraceEntry[] _entries;
        private int _next;

        public StateFlowTraceBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Trace capacity must be positive.");
            }

            _entries = new StateFlowTraceEntry[capacity];
        }

        public int Capacity => _entries.Length;
        public int Count { get; private set; }
        public ulong TotalWritten { get; private set; }

        internal bool Append(in StateFlowTraceEntry entry)
        {
            if (!entry.IsValid)
            {
                return false;
            }

            _entries[_next] = entry;
            _next++;
            if (_next == _entries.Length)
            {
                _next = 0;
            }

            if (Count < _entries.Length)
            {
                Count++;
            }

            if (TotalWritten < ulong.MaxValue)
            {
                TotalWritten++;
            }

            return true;
        }

        public int CopyLatestTo(
            Span<StateFlowTraceEntry> destination,
            StateFlowTraceFilter filter = default)
        {
            int written = 0;
            for (int offset = 0; offset < Count && written < destination.Length; offset++)
            {
                int index = _next - 1 - offset;
                if (index < 0)
                {
                    index += _entries.Length;
                }

                StateFlowTraceEntry entry = _entries[index];
                if (filter.Matches(entry))
                {
                    destination[written++] = entry;
                }
            }

            for (int left = 0, right = written - 1; left < right; left++, right--)
            {
                StateFlowTraceEntry swap = destination[left];
                destination[left] = destination[right];
                destination[right] = swap;
            }

            return written;
        }

        internal void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _next = 0;
            Count = 0;
        }
    }
}
