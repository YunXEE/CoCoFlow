using System;
using System.Collections.Generic;

// Unity 资产冻结成不依赖引擎的图输入，编译器读取节点关系、转换窗口及事件声明。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>选择始终可请求、按本地时间或按动作进度校验的转换窗口。</summary>
    public enum TransitionWindowMode
    {
        None = 0,
        Always = 1,
        LocalSeconds = 2,
        ActionProgress = 3
    }

    /// <summary>描述已声明边在当前激活中允许参与仲裁的时间或进度区间。</summary>
    public readonly struct TransitionWindow : IEquatable<TransitionWindow>
    {

        #region Public API

        public static TransitionWindow Always =>
            new TransitionWindow(TransitionWindowMode.Always, 0d, 0d);

        public TransitionWindowMode Mode { get; }
        public double StartInclusive { get; }
        public double EndExclusive { get; }
        public bool IsValid => (Mode == TransitionWindowMode.Always &&
                                StartInclusive == 0d &&
                                EndExclusive == 0d) ||
                               ((Mode == TransitionWindowMode.LocalSeconds ||
                                 Mode == TransitionWindowMode.ActionProgress) &&
                                IsFinite(StartInclusive) &&
                                IsFinite(EndExclusive) &&
                                StartInclusive >= 0d &&
                                StartInclusive < EndExclusive &&
                                (Mode != TransitionWindowMode.ActionProgress || EndExclusive <= 1d));

        /// <summary>依据窗口模式创建起止区间，值非有限或边界不符合模式时返回 false。</summary>
        public static bool TryCreate(
            TransitionWindowMode mode,
            double startInclusive,
            double endExclusive,
            out TransitionWindow window)
        {
            if (mode == TransitionWindowMode.Always)
            {
                window = Always;
                return true;
            }

            window = new TransitionWindow(mode, startInclusive, endExclusive);
            if (window.IsValid)
            {
                return true;
            }

            window = default;
            return false;
        }

        /// <summary>按<c>Mode</c>、<c>StartInclusive</c>、<c>EndExclusive</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(TransitionWindow other)
        {
            return Mode == other.Mode &&
                   StartInclusive.Equals(other.StartInclusive) &&
                   EndExclusive.Equals(other.EndExclusive);
        }

        /// <summary>仅当对象属于同类型并且<c>Mode</c>、<c>StartInclusive</c>、<c>EndExclusive</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is TransitionWindow other && Equals(other);

        /// <summary>为哈希集合计算与<c>Mode</c>、<c>StartInclusive</c>、<c>EndExclusive</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = (int)Mode;
                hashCode = (hashCode * 397) ^ StartInclusive.GetHashCode();
                hashCode = (hashCode * 397) ^ EndExclusive.GetHashCode();
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>Mode</c>、<c>StartInclusive</c>、<c>EndExclusive</c>，相等时返回 true。</summary>
        public static bool operator ==(TransitionWindow left, TransitionWindow right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>Mode</c>、<c>StartInclusive</c>、<c>EndExclusive</c>，存在差异时返回 true。</summary>
        public static bool operator !=(TransitionWindow left, TransitionWindow right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        private TransitionWindow(
            TransitionWindowMode mode,
            double startInclusive,
            double endExclusive)
        {
            Mode = mode;
            StartInclusive = startInclusive;
            EndExclusive = endExclusive;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        #endregion
    }

    /// <summary>保存尚待编译的出边端点、源内优先级和时间窗口。</summary>
    public sealed class TransitionSource
    {

        #region Public API

        public TransitionId TransitionId { get; }
        public StateId SourceStateId { get; }
        public StateId TargetStateId { get; }
        public int Priority { get; }
        public TransitionWindow Window { get; }

        #endregion

        #region Internal Logic

        internal TransitionSource(
            TransitionId transitionId,
            StateId sourceStateId,
            StateId targetStateId,
            int priority,
            TransitionWindow window)
        {
            TransitionId = transitionId;
            SourceStateId = sourceStateId;
            TargetStateId = targetStateId;
            Priority = priority;
            Window = window;
        }

        #endregion
    }

    /// <summary>提供单个节点的层级关系、Logic 描述符及冻结配置。</summary>
    public sealed class StateSource
    {

        #region Public API

        public StateId StateId { get; }
        public StateId ParentStateId { get; }
        public StateId InitialChildStateId { get; }
        public StateDescriptorId DescriptorId { get; }
        public FrozenConfigSnapshot Config { get; }

        #endregion

        #region Internal Logic

        internal StateSource(
            StateId stateId,
            StateId parentStateId,
            StateId initialChildStateId,
            StateDescriptorId descriptorId,
            FrozenConfigSnapshot config)
        {
            StateId = stateId;
            ParentStateId = parentStateId;
            InitialChildStateId = initialChildStateId;
            DescriptorId = descriptorId;
            Config = config;
        }

        #endregion
    }

    /// <summary>提供一层的节点与转换定义以及初始节点和可选 AnyState。</summary>
    public sealed class StateLayerSource
    {
        private readonly IReadOnlyList<StateSource> _states;
        private readonly IReadOnlyList<TransitionSource> _transitions;

        public LayerId LayerId { get; }
        public StateId InitialStateId { get; }
        public IReadOnlyList<StateSource> States => _states;
        public IReadOnlyList<TransitionSource> Transitions => _transitions;
        public StateSource AnyState { get; }

        #region Internal Logic

        internal StateLayerSource(
            LayerId layerId,
            StateId initialStateId,
            IReadOnlyList<StateSource> states,
            IReadOnlyList<TransitionSource> transitions)
        {
            LayerId = layerId;
            InitialStateId = initialStateId;
            _states = GraphSourceCollections.Clone(states);
            _transitions = GraphSourceCollections.Clone(transitions);
        }

        internal StateLayerSource(
            LayerId layerId,
            StateId initialStateId,
            IReadOnlyList<StateSource> states,
            IReadOnlyList<TransitionSource> transitions,
            StateSource anyState)
            : this(layerId, initialStateId, states, transitions)
        {
            AnyState = anyState;
        }

        #endregion
    }

    /// <summary>表示图资产提交给编译器的一项显式事件转输入声明。</summary>
    public sealed class EventToIntentDeclarationSource
    {

        #region Public API

        public EventTypeId EventTypeId { get; }
        public IntentId ProvidedIntentId { get; }

        #endregion

        #region Internal Logic

        internal EventToIntentDeclarationSource(
            EventTypeId eventTypeId,
            IntentId providedIntentId)
        {
            EventTypeId = eventTypeId;
            ProvidedIntentId = providedIntentId;
        }

        #endregion
    }

    /// <summary>包含一份已冻结的图输入、格式版本与内容指纹。</summary>
    public sealed class StateGraphSource
    {
        private readonly IReadOnlyList<StateLayerSource> _layers;
        private readonly IReadOnlyList<EventToIntentDeclarationSource> _eventAdapterDeclarations;

        public uint SchemaVersion { get; }
        public ulong ContentFingerprint { get; }
        public GraphId GraphId { get; }
        public IReadOnlyList<StateLayerSource> Layers => _layers;
        public IReadOnlyList<EventToIntentDeclarationSource> EventAdapterDeclarations =>
            _eventAdapterDeclarations;

        #region Internal Logic

        internal StateGraphSource(
            uint schemaVersion,
            ulong contentFingerprint,
            GraphId graphId,
            IReadOnlyList<StateLayerSource> layers,
            IReadOnlyList<EventToIntentDeclarationSource> eventAdapterDeclarations)
        {
            SchemaVersion = schemaVersion;
            ContentFingerprint = contentFingerprint;
            GraphId = graphId;
            _layers = GraphSourceCollections.Clone(layers);
            _eventAdapterDeclarations =
                GraphSourceCollections.Clone(eventAdapterDeclarations) ??
                Array.AsReadOnly(new EventToIntentDeclarationSource[0]);
        }

        #endregion
    }

    /// <summary>将外部给定的源列表复制成编译期间稳定的只读集合。</summary>
    internal static class GraphSourceCollections
    {
        public static IReadOnlyList<T> Clone<T>(IReadOnlyList<T> source)
        {
            if (source == null)
            {
                return null;
            }

            var copy = new T[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                copy[index] = source[index];
            }

            return Array.AsReadOnly(copy);
        }
    }
}
