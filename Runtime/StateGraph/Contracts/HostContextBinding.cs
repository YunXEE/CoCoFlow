using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 场景组件声明自己负责的 Context 槽，Host 核对声明并在一次捕获期间提供限定的写入口。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>记录项目场景组件需要产生的槽、值类型及语义指纹。</summary>
    internal readonly struct HostContextValueRequirement :
        IEquatable<HostContextValueRequirement>
    {
        internal HostContextValueRequirement(
            StateSlotId slotId,
            Type valueType)
        {
            SlotId = slotId;
            ValueType = valueType;
        }

        internal StateSlotId SlotId { get; }
        internal Type ValueType { get; }
        internal bool IsValid => SlotId.IsValid &&
                                 ValueType != null &&
                                 StateFlowTypeRules.IsReferenceFreeValueType(ValueType);

        public bool Equals(HostContextValueRequirement other) =>
            SlotId == other.SlotId && ValueType == other.ValueType;

        public override bool Equals(object obj) =>
            obj is HostContextValueRequirement other && Equals(other);

        public override int GetHashCode() =>
            unchecked((SlotId.GetHashCode() * 397) ^ (ValueType?.GetHashCode() ?? 0));
    }

    /// <summary>描述场景组件负责捕获的 Context 块与槽。</summary>
    public sealed class HostContextBindingDescriptor
    {
        private readonly HostContextValueRequirement[] _values;
        private readonly IReadOnlyList<HostContextValueRequirement> _readOnlyValues;
        private readonly bool _isValid;

        public Type BindingType { get; }
        public ulong SemanticFingerprint { get; }
        public int ValueCount => _values.Length;
        public bool IsValid => _isValid;
        internal IReadOnlyList<HostContextValueRequirement> Values => _readOnlyValues;

        #region Internal Logic

        internal HostContextBindingDescriptor(
            Type bindingType,
            ulong semanticFingerprint,
            HostContextValueRequirement[] values)
        {
            BindingType = bindingType;
            SemanticFingerprint = semanticFingerprint;
            _values = values ?? Array.Empty<HostContextValueRequirement>();
            _readOnlyValues = Array.AsReadOnly(_values);
            _isValid = bindingType != null &&
                       typeof(IHostContextBinding).IsAssignableFrom(bindingType) &&
                       semanticFingerprint != 0UL &&
                       _values.Length > 0;
        }

        internal bool Produces<TValue>(StateSlotId slotId)
            where TValue : unmanaged
        {
            for (int index = 0; index < _values.Length; index++)
            {
                if (_values[index].SlotId == slotId &&
                    _values[index].ValueType == typeof(TValue))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion
    }

    /// <summary>为项目组件收集槽声明并生成可核对的绑定描述。</summary>
    public sealed class HostContextBindingDescriptorBuilder
    {
        private readonly List<HostContextValueRequirement> _values =
            new List<HostContextValueRequirement>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;

        #region Public API

        /// <summary>增加由本 Host 绑定产生的类型化槽声明。冻结后、标识无效或声明冲突时返回 false。</summary>
        public bool TryProduce<TValue>(
            StateSlotId slotId,
            out CoCoDiagnostic diagnostic)
            where TValue : unmanaged
        {
            var requirement = new HostContextValueRequirement(slotId, typeof(TValue));
            if (_isFrozen || !requirement.IsValid)
            {
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidStateSlot,
                    _isFrozen
                        ? "Actor Context binding descriptor is already frozen."
                        : "An Actor Context value requires a valid Slot id and reference-free value type.");
                return false;
            }

            for (int index = 0; index < _values.Count; index++)
            {
                if (_values[index].SlotId == slotId)
                {
                    diagnostic = Error(
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "An Actor Context binding cannot produce one Slot more than once.");
                    return false;
                }
            }

            _values.Add(requirement);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>将全部槽声明和语义指纹固化为该绑定的描述符，声明不合法时返回 false 和诊断。</summary>
        public bool TryFreeze<TBinding>(
            ulong semanticFingerprint,
            out HostContextBindingDescriptor descriptor,
            out CoCoDiagnostic diagnostic)
            where TBinding : class, IHostContextBinding
        {
            if (_isFrozen || semanticFingerprint == 0UL || _values.Count == 0)
            {
                descriptor = null;
                diagnostic = Error(
                    _isFrozen ? CoCoDiagnosticCode.RegistryFrozen : CoCoDiagnosticCode.InvalidStateBlock,
                    _isFrozen
                        ? "Actor Context binding descriptor may only be frozen once."
                        : "An Actor Context binding requires a non-zero semantic fingerprint and at least one Slot.");
                return false;
            }

            _isFrozen = true;
            descriptor = new HostContextBindingDescriptor(
                typeof(TBinding),
                semanticFingerprint,
                _values.ToArray());
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);

        #endregion
    }

    /// <summary>由场景组件实现，向 Host 声明负责的槽并在捕获时提供值。</summary>
    public interface IHostContextBinding
    {
        HostContextBindingDescriptor Descriptor { get; }

        /// <summary>在 Host 的场景捕获阶段写入本绑定声明的槽，返回 false 时整步拒绝该候选。</summary>
        bool TryCapture(
            in HostContextCaptureContext context,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>由 Host 检查本次捕获的槽所有权并接收类型化写入。</summary>
    internal interface IHostContextValueSink
    {
        bool IsActive(
            ulong token,
            IHostContextBinding binding);

        void RejectWrite(
            ulong token,
            IHostContextBinding binding);

        bool TryWrite<TValue>(
            ulong token,
            IHostContextBinding binding,
            StateSlotId slotId,
            in TValue value)
            where TValue : unmanaged;
    }

    /// <summary>限定场景组件在当前捕获期间可以写入的声明槽。</summary>
    public readonly struct HostContextWriter
    {
        private readonly IHostContextBinding _binding;
        private readonly HostContextBindingDescriptor _descriptor;
        private readonly IHostContextValueSink _sink;
        private readonly ulong _token;

        public bool IsValid => _binding != null &&
                               _descriptor != null &&
                               _descriptor.IsValid &&
                               _sink != null &&
                               _sink.IsActive(_token, _binding);

        #region Public API

        /// <summary>向本绑定声明的类型化槽暂存值，租约失效、槽归属或值类型不符时返回 false。</summary>
        public bool TryWrite<TValue>(
            StateSlot<TValue> slot,
            in TValue value)
            where TValue : unmanaged
        {
            return slot.IsValid
                ? TryWrite(slot.SlotId, value)
                : RejectWrite();
        }

        /// <summary>向本绑定声明的类型化槽暂存值，租约失效、槽归属或值类型不符时返回 false。</summary>
        public bool TryWrite<TValue>(
            StateSlotId slotId,
            in TValue value)
            where TValue : unmanaged
        {
            if (!IsValid || !_descriptor.Produces<TValue>(slotId))
            {
                return RejectWrite();
            }

            if (_sink.TryWrite(_token, _binding, slotId, value))
            {
                return true;
            }

            return RejectWrite();
        }

        #endregion

        #region Internal Logic

        internal HostContextWriter(
            IHostContextBinding binding,
            HostContextBindingDescriptor descriptor,
            IHostContextValueSink sink,
            ulong token)
        {
            _binding = binding;
            _descriptor = descriptor;
            _sink = sink;
            _token = token;
        }

        private bool RejectWrite()
        {
            _sink?.RejectWrite(_token, _binding);
            return false;
        }

        #endregion
    }

    /// <summary>向场景组件提供本次捕获的实例、Tick 与上一提交视图。</summary>
    public readonly struct HostContextCaptureContext
    {

        #region Public API

        public GraphInstanceId GraphInstanceId { get; }
        public TickFrame TickFrame { get; }
        public ContextFrameReadView PreviousContext { get; }
        public HostContextWriter Writer { get; }
        public bool IsValid => GraphInstanceId.IsValid &&
                               TickFrame.IsValid &&
                               PreviousContext.IsValid &&
                               Writer.IsValid;

        #endregion

        #region Internal Logic

        internal HostContextCaptureContext(
            GraphInstanceId graphInstanceId,
            in TickFrame tickFrame,
            ContextFrameReadView previousContext,
            HostContextWriter writer)
        {
            GraphInstanceId = graphInstanceId;
            TickFrame = tickFrame;
            PreviousContext = previousContext;
            Writer = writer;
        }

        #endregion
    }
}
