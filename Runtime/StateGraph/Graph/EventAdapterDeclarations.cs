using System;

// 资产快照交付 Event-to-Intent 声明，编译器按事件域、类型和目标 Intent 校验并保存结果。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>唯一标识图中一个由事件转为 Intent 的显式声明。</summary>
    public readonly struct EventToIntentDeclarationKey :
        IEquatable<EventToIntentDeclarationKey>,
        IComparable<EventToIntentDeclarationKey>
    {

        #region Public API

        public EventTypeId EventTypeId { get; }
        public IntentId ProvidedIntentId { get; }
        public bool IsValid => EventTypeId.IsValid && ProvidedIntentId.IsValid;

        /// <summary>用事件与目标输入的有效标识组成显式适配声明键，无效标识时返回 false。</summary>
        public static bool TryCreate(
            EventTypeId eventTypeId,
            IntentId providedIntentId,
            out EventToIntentDeclarationKey key)
        {
            if (!eventTypeId.IsValid || !providedIntentId.IsValid)
            {
                key = default;
                return false;
            }

            key = new EventToIntentDeclarationKey(eventTypeId, providedIntentId);
            return true;
        }

        /// <summary>先按事件、再按输入标识比较声明键，负数、零和正数分别表示先于、相同和后于。</summary>
        public int CompareTo(EventToIntentDeclarationKey other)
        {
            int comparison = EventTypeId.High.CompareTo(other.EventTypeId.High);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = EventTypeId.Low.CompareTo(other.EventTypeId.Low);
            if (comparison != 0)
            {
                return comparison;
            }

            comparison = ProvidedIntentId.High.CompareTo(other.ProvidedIntentId.High);
            return comparison != 0
                ? comparison
                : ProvidedIntentId.Low.CompareTo(other.ProvidedIntentId.Low);
        }

        /// <summary>按<c>EventTypeId</c>、<c>ProvidedIntentId</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(EventToIntentDeclarationKey other) =>
            EventTypeId == other.EventTypeId && ProvidedIntentId == other.ProvidedIntentId;

        /// <summary>仅当对象属于同类型并且<c>EventTypeId</c>、<c>ProvidedIntentId</c>相等时返回 true。</summary>
        public override bool Equals(object obj) =>
            obj is EventToIntentDeclarationKey other && Equals(other);

        /// <summary>为哈希集合计算与<c>EventTypeId</c>、<c>ProvidedIntentId</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode() =>
            unchecked((EventTypeId.GetHashCode() * 397) ^ ProvidedIntentId.GetHashCode());

        /// <summary>将事件标识与所提供的输入标识一同输出，供编译诊断定位显式声明。</summary>
        public override string ToString() => $"{EventTypeId}:{ProvidedIntentId}";

        /// <summary>比较两端的<c>EventTypeId</c>、<c>ProvidedIntentId</c>，相等时返回 true。</summary>
        public static bool operator ==(
            EventToIntentDeclarationKey left,
            EventToIntentDeclarationKey right) => left.Equals(right);

        /// <summary>比较两端的<c>EventTypeId</c>、<c>ProvidedIntentId</c>，存在差异时返回 true。</summary>
        public static bool operator !=(
            EventToIntentDeclarationKey left,
            EventToIntentDeclarationKey right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        private EventToIntentDeclarationKey(
            EventTypeId eventTypeId,
            IntentId providedIntentId)
        {
            EventTypeId = eventTypeId;
            ProvidedIntentId = providedIntentId;
        }

        #endregion
    }

    /// <summary>保存编译器已与目录匹配的事件转输入声明。</summary>
    public sealed class CompiledEventToIntentDeclaration
    {

        #region Public API

        public EventDomainId EventDomainId { get; }
        public EventTypeId EventTypeId { get; }
        public Type EventPayloadType { get; }
        public IntentId ProvidedIntentId { get; }
        public Type ProvidedIntentType { get; }

        #endregion

        #region Internal Logic

        internal CompiledEventToIntentDeclaration(
            IGraphEventToIntentDeclarationRegistration registration)
        {
            EventDomainId = registration.EventDomainId;
            EventTypeId = registration.EventTypeId;
            EventPayloadType = registration.EventPayloadType;
            ProvidedIntentId = registration.ProvidedIntentId;
            ProvidedIntentType = registration.ProvidedIntentType;
        }

        #endregion
    }

    /// <summary>向图编译器提供声明键、事件载荷和目标注册信息。</summary>
    internal interface IGraphEventToIntentDeclarationRegistration
    {
        EventToIntentDeclarationKey Key { get; }
        EventDomainId EventDomainId { get; }
        EventTypeId EventTypeId { get; }
        Type EventPayloadType { get; }
        IntentId ProvidedIntentId { get; }
        Type ProvidedIntentType { get; }
    }

    /// <summary>关联指定事件和 Intent 的类型及已注册声明。</summary>
    internal sealed class GraphEventToIntentDeclarationRegistration :
        IGraphEventToIntentDeclarationRegistration
    {
        internal GraphEventToIntentDeclarationRegistration(
            EventToIntentDeclarationKey key,
            EventDomainId eventDomainId,
            Type eventPayloadType,
            Type providedIntentType)
        {
            Key = key;
            EventDomainId = eventDomainId;
            EventPayloadType = eventPayloadType;
            ProvidedIntentType = providedIntentType;
        }

        public EventToIntentDeclarationKey Key { get; }
        public EventDomainId EventDomainId { get; }
        public EventTypeId EventTypeId => Key.EventTypeId;
        public Type EventPayloadType { get; }
        public IntentId ProvidedIntentId => Key.ProvidedIntentId;
        public Type ProvidedIntentType { get; }
    }
}
