using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 输入来源按 Tick 采样，归并器处理同一载荷的贡献。玩法通过冻结的 Intent 帧读取结果。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>将一个步长中同类来源的贡献归并为 State 可读取的输入。</summary>
    public interface IIntentReducer<TIntent>
    {
        /// <summary>将一个来源的贡献合入当前输入，返回传给下一个来源或本步 Logic 的结果。</summary>
        TIntent Reduce(in TIntent current, in TIntent candidate);
    }

    /// <summary>为实例创建独立的 Intent 归并器。</summary>
    public interface IIntentReducerFactory<TIntent, TReducer>
        where TReducer : unmanaged, IIntentReducer<TIntent>
    {
        /// <summary>为指定图实例创建归并器，跨实例的归并状态由各自工厂提供。</summary>
        TReducer Create(GraphInstanceId graphInstanceId);
    }

    /// <summary>在 Host 指定的 Tick 采集一个类型化输入值。</summary>
    public interface IIntentFrameSource<TIntent>
    {
        /// <summary>来源在步进开始时采样一次，返回 true 提供本步贡献，返回 false 表示本次没有值。</summary>
        bool TrySample(in TickFrame tickFrame, out TIntent intent);
    }

    /// <summary>提供对同一步冻结 Intent 的存在性、句柄和按类型查询。</summary>
    public interface IIntentFrame
    {
        GraphInstanceId GraphInstanceId { get; }
        StateFlowFrameHeader Header { get; }
        FrameLayoutId LayoutId { get; }
        bool IsFrozen { get; }

        /// <summary>按本帧布局的类型化句柄取已归并的输入，值缺失或句柄无效时返回 false。</summary>
        bool TryGet<TIntent>(
            IntentHandle<TIntent> handle,
            out TIntent value);

        /// <summary>判断该类型化输入句柄在本帧是否有值。调用者可区分未提供与提供默认值。</summary>
        bool IsPresent<TIntent>(IntentHandle<TIntent> handle);

        /// <summary>返回本帧同类型中第一个有值的输入槽，找不到时返回 false。</summary>
        bool TryFirst<TIntent>(out TIntent value);
    }

    /// <summary>标明归并器接受来源贡献后的处理结果。</summary>
    public enum IntentContributionResult
    {
        None = 0,
        Accepted = 1,
        ArbiterNotCollecting = 2,
        InvalidHandle = 3,
        DuplicateContribution = 4,
        CapacityExceeded = 5
    }

    /// <summary>区分来源在本次采样中有值、无值或失败。</summary>
    public enum IntentSourceSampleResult
    {
        None = 0,
        Contributed = 1,
        NoValue = 2,
        AlreadySampled = 3,
        ArbiterNotCollecting = 4,
        InvalidBinding = 5,
        ContributionRejected = 6
    }

    /// <summary>记录 Intent 的稳定身份、载荷类型与允许的贡献容量。</summary>
    public readonly struct IntentDescriptor : IEquatable<IntentDescriptor>
    {

        #region Public API

        public IntentId IntentId { get; }
        public Type ValueType { get; }
        public int DenseIndex { get; }
        public int MaxContributions { get; }
        public bool IsValid => IntentId.IsValid &&
                               ValueType != null &&
                               DenseIndex >= 0 &&
                               MaxContributions > 0;

        /// <summary>按<c>IntentId</c>、<c>ValueType</c>、<c>DenseIndex</c>、<c>MaxContributions</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(IntentDescriptor other)
        {
            return IntentId == other.IntentId &&
                   ValueType == other.ValueType &&
                   DenseIndex == other.DenseIndex &&
                   MaxContributions == other.MaxContributions;
        }

        /// <summary>仅当对象属于同类型并且<c>IntentId</c>、<c>ValueType</c>、<c>DenseIndex</c>、<c>MaxContributions</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is IntentDescriptor other && Equals(other);

        /// <summary>为哈希集合计算与<c>IntentId</c>、<c>ValueType</c>、<c>DenseIndex</c>、<c>MaxContributions</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = IntentId.GetHashCode();
                hashCode = (hashCode * 397) ^ (ValueType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ DenseIndex;
                hashCode = (hashCode * 397) ^ MaxContributions;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>IntentId</c>、<c>ValueType</c>、<c>DenseIndex</c>、<c>MaxContributions</c>，相等时返回 true。</summary>
        public static bool operator ==(IntentDescriptor left, IntentDescriptor right) => left.Equals(right);
        /// <summary>比较两端的<c>IntentId</c>、<c>ValueType</c>、<c>DenseIndex</c>、<c>MaxContributions</c>，存在差异时返回 true。</summary>
        public static bool operator !=(IntentDescriptor left, IntentDescriptor right) => !left.Equals(right);

        #endregion

        #region Internal Logic

        internal IntentDescriptor(
            IntentId intentId,
            Type valueType,
            int denseIndex,
            int maxContributions)
        {
            IntentId = intentId;
            ValueType = valueType;
            DenseIndex = denseIndex;
            MaxContributions = maxContributions;
        }

        #endregion
    }

    /// <summary>将某类输入定位到所属帧布局的索引。</summary>
    public readonly struct IntentHandle<TIntent> : IEquatable<IntentHandle<TIntent>>
    {
        private readonly IntentFrameLayout _owner;

        public FrameLayoutId LayoutId { get; }
        public IntentId IntentId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               IntentId.IsValid &&
                               DenseIndex >= 0;

        #region Public API

        /// <summary>按<c>owner</c>、<c>LayoutId</c>、<c>IntentId</c>、<c>DenseIndex</c>比较两个值，全部相等时返回 true。</summary>
        public bool Equals(IntentHandle<TIntent> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   IntentId == other.IntentId &&
                   DenseIndex == other.DenseIndex;
        }

        /// <summary>仅当对象属于同类型并且<c>owner</c>、<c>LayoutId</c>、<c>IntentId</c>、<c>DenseIndex</c>相等时返回 true。</summary>
        public override bool Equals(object obj) => obj is IntentHandle<TIntent> other && Equals(other);

        /// <summary>为哈希集合计算与<c>owner</c>、<c>LayoutId</c>、<c>IntentId</c>、<c>DenseIndex</c>的相等判定一致的散列值。</summary>
        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ IntentId.GetHashCode();
                hashCode = (hashCode * 397) ^ DenseIndex;
                return hashCode;
            }
        }

        /// <summary>比较两端的<c>owner</c>、<c>LayoutId</c>、<c>IntentId</c>、<c>DenseIndex</c>，相等时返回 true。</summary>
        public static bool operator ==(IntentHandle<TIntent> left, IntentHandle<TIntent> right) =>
            left.Equals(right);

        /// <summary>比较两端的<c>owner</c>、<c>LayoutId</c>、<c>IntentId</c>、<c>DenseIndex</c>，存在差异时返回 true。</summary>
        public static bool operator !=(IntentHandle<TIntent> left, IntentHandle<TIntent> right) =>
            !left.Equals(right);

        #endregion

        #region Internal Logic

        internal IntentHandle(
            IntentFrameLayout owner,
            IntentId intentId,
            int denseIndex)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            IntentId = intentId;
            DenseIndex = denseIndex;
        }

        internal bool IsOwnedBy(IntentFrameLayout owner) => ReferenceEquals(_owner, owner);

        #endregion
    }

    /// <summary>在项目程序集中声明用于标准注册的 Intent 配置。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
    public sealed class IntentRegistrationAttribute : Attribute
    {
        /// <summary>将输入载荷与其归并或来源注册信息关联，供标准 Provider 扫描程序集时读取。</summary>
        public IntentRegistrationAttribute(Type registrationType)
        {
            RegistrationType = registrationType ?? throw new ArgumentNullException(nameof(registrationType));
        }
        public Type RegistrationType { get; }
    }
}
