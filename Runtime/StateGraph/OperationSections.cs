using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>
    /// Marks a data contract that a StateGraph can place in an OperationFrame.
    /// Section interfaces inherit this interface directly; setters and data methods are optional.
    /// </summary>
    public interface IOperationSection
    {
    }

    [AttributeUsage(AttributeTargets.Interface, Inherited = false)]
    public sealed class OperationSectionAttribute : Attribute
    {
        public OperationSectionAttribute(OperationSectionMode mode) => Mode = mode;
        public OperationSectionMode Mode { get; }
    }

    /// <summary>Associates a Section with its compiler-generated View factory.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class OperationViewFactoryAttribute : Attribute
    {
        public OperationViewFactoryAttribute(Type section, Type factory)
        {
            Section = section;
            Factory = factory;
        }

        public Type Section { get; }
        public Type Factory { get; }
    }

    internal sealed class GeneratedOperationViewFactory<TSection> : IOperationSectionViewFactory<TSection>
        where TSection : class, IOperationSection
    {
        private readonly IOperationSectionViewFactory<TSection> _factory;

        internal GeneratedOperationViewFactory()
        {
            Type section = typeof(TSection);
            foreach (OperationViewFactoryAttribute entry in
                     section.Assembly.GetCustomAttributes(typeof(OperationViewFactoryAttribute), false))
            {
                if (entry.Section != section) continue;
                _factory = (IOperationSectionViewFactory<TSection>)Activator.CreateInstance(entry.Factory);
                return;
            }

            throw new InvalidOperationException($"No generated View for Operation Section {section.FullName}.");
        }

        public TSection Create(in OperationSectionViewContext<TSection> context) => _factory.Create(context);
    }

    public enum OperationSectionMode
    {
        None = 0,
        Continuous = 1,
        Discrete = 2
    }

    public sealed class OperationSectionFieldShape :
        IEquatable<OperationSectionFieldShape>
    {
        internal OperationSectionFieldShape(
            int denseIndex,
            string name,
            Type valueType,
            int byteOffset,
            int byteSize)
        {
            DenseIndex = denseIndex;
            Name = name;
            ValueType = valueType;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        public int DenseIndex { get; }
        public string Name { get; }
        public Type ValueType { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }

        public bool Equals(OperationSectionFieldShape other)
        {
            return other != null &&
                   DenseIndex == other.DenseIndex &&
                   string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                   ValueType == other.ValueType &&
                   ByteOffset == other.ByteOffset &&
                   ByteSize == other.ByteSize;
        }

        public override bool Equals(object obj) =>
            obj is OperationSectionFieldShape other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = DenseIndex;
                hashCode = (hashCode * 397) ^ (Name?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (ValueType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ ByteOffset;
                hashCode = (hashCode * 397) ^ ByteSize;
                return hashCode;
            }
        }
    }

    /// <summary>
    /// The complete immutable storage contract for one Operation Section interface.
    /// The fingerprint is a deterministic fast identity; compatibility still compares every field.
    /// </summary>
    public sealed class OperationSectionShape : IEquatable<OperationSectionShape>
    {
        private readonly OperationSectionFieldShape[] _fields;
        private readonly IReadOnlyList<OperationSectionFieldShape> _readOnlyFields;

        internal OperationSectionShape(
            int byteSize,
            OperationSectionFieldShape[] fields)
        {
            ByteSize = byteSize;
            _fields = (OperationSectionFieldShape[])fields.Clone();
            _readOnlyFields = Array.AsReadOnly(_fields);
            ShapeFingerprint = OperationSectionShapeFingerprint.Compute(byteSize, _fields);
        }

        public int ByteSize { get; }
        public int FieldCount => _fields.Length;
        public IReadOnlyList<OperationSectionFieldShape> Fields => _readOnlyFields;
        public ulong ShapeFingerprint { get; }
        public bool IsValid => ByteSize > 0 &&
                               _fields.Length > 0 &&
                               ShapeFingerprint != 0UL;

        /// <summary>
        /// Validates and freezes the complete shape of a Section interface at setup time.
        /// This non-generic entry point lets Editor/build tooling reuse the runtime contract
        /// without constructing closed generic methods.
        /// </summary>
        public static bool TryCreate(
            Type sectionType,
            out OperationSectionShape shape,
            out CoCoDiagnostic diagnostic)
        {
            return OperationSectionContract.TryCreateShape(
                sectionType,
                out shape,
                out diagnostic);
        }

        public bool Equals(OperationSectionShape other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null ||
                ShapeFingerprint != other.ShapeFingerprint ||
                ByteSize != other.ByteSize ||
                _fields.Length != other._fields.Length)
            {
                return false;
            }

            for (int index = 0; index < _fields.Length; index++)
            {
                if (!_fields[index].Equals(other._fields[index]))
                {
                    return false;
                }
            }

            return true;
        }

        public override bool Equals(object obj) =>
            obj is OperationSectionShape other && Equals(other);

        public override int GetHashCode() => ShapeFingerprint.GetHashCode();
    }

    public readonly struct OperationSectionEntryHeader : IEquatable<OperationSectionEntryHeader>
    {
        private OperationSectionEntryHeader(
            bool enabled,
            ActivationId activationId,
            OperationSequence operationSequence)
        {
            Enabled = enabled;
            ActivationId = activationId;
            OperationSequence = operationSequence;
        }

        public bool Enabled { get; }
        public ActivationId ActivationId { get; }
        public OperationSequence OperationSequence { get; }

        internal static OperationSectionEntryHeader Continuous() =>
            new OperationSectionEntryHeader(true, default, default);

        internal static OperationSectionEntryHeader Discrete(
            ActivationId activationId,
            OperationSequence operationSequence) =>
            new OperationSectionEntryHeader(true, activationId, operationSequence);

        public bool Equals(OperationSectionEntryHeader other)
        {
            return Enabled == other.Enabled &&
                   ActivationId == other.ActivationId &&
                   OperationSequence == other.OperationSequence;
        }

        public override bool Equals(object obj) =>
            obj is OperationSectionEntryHeader other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = Enabled.GetHashCode();
                hashCode = (hashCode * 397) ^ ActivationId.GetHashCode();
                hashCode = (hashCode * 397) ^ OperationSequence.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(
            OperationSectionEntryHeader left,
            OperationSectionEntryHeader right) => left.Equals(right);

        public static bool operator !=(
            OperationSectionEntryHeader left,
            OperationSectionEntryHeader right) => !left.Equals(right);
    }

    /// <summary>
    /// A setup-time declaration used by Operators and Graph compilation.
    /// Runtime code resolves it to a dense handle before Running begins.
    /// </summary>
    public readonly struct OperationSectionRequirement : IEquatable<OperationSectionRequirement>
    {
        private readonly Type _sectionType;
        private readonly OperationSectionShape _shape;

        private OperationSectionRequirement(
            OperationSectionId sectionId,
            OperationSectionMode mode,
            Type sectionType,
            OperationSectionShape shape)
        {
            SectionId = sectionId;
            Mode = mode;
            _sectionType = sectionType;
            _shape = shape;
        }

        public OperationSectionId SectionId { get; }
        public OperationSectionMode Mode { get; }
        public Type SectionType => _sectionType;
        public OperationSectionShape Shape => _shape;
        public bool IsValid => SectionId.IsValid &&
                               OperationSectionContract.IsDefinedMode(Mode) &&
                               _sectionType != null &&
                               _shape != null &&
                               _shape.IsValid;

        public static bool TryCreate<TSection>(
            OperationSectionId sectionId,
            OperationSectionMode mode,
            out OperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TSection : class, IOperationSection
        {
            if (!sectionId.IsValid)
            {
                requirement = default;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "Operation SectionId must be valid.");
                return false;
            }

            if (!OperationSectionContract.IsDefinedMode(mode))
            {
                requirement = default;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section mode must be Continuous or Discrete.");
                return false;
            }

            if (!OperationSectionShape.TryCreate(
                    typeof(TSection),
                    out OperationSectionShape shape,
                    out diagnostic))
            {
                requirement = default;
                return false;
            }

            requirement = new OperationSectionRequirement(
                sectionId,
                mode,
                typeof(TSection),
                shape);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool Equals(OperationSectionRequirement other)
        {
            return SectionId == other.SectionId &&
                   Mode == other.Mode &&
                   _sectionType == other._sectionType &&
                   Equals(_shape, other._shape);
        }

        public override bool Equals(object obj) =>
            obj is OperationSectionRequirement other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = SectionId.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Mode;
                hashCode = (hashCode * 397) ^ (_sectionType?.GetHashCode() ?? 0);
                hashCode = (hashCode * 397) ^ (_shape?.GetHashCode() ?? 0);
                return hashCode;
            }
        }

        public static bool operator ==(
            OperationSectionRequirement left,
            OperationSectionRequirement right) => left.Equals(right);

        public static bool operator !=(
            OperationSectionRequirement left,
            OperationSectionRequirement right) => !left.Equals(right);
    }

    public readonly struct OperationSectionHandle<TSection> :
        IEquatable<OperationSectionHandle<TSection>>
        where TSection : class, IOperationSection
    {
        private readonly OperationSectionRegistry _owner;

        internal OperationSectionHandle(
            OperationSectionRegistry owner,
            OperationSectionId sectionId,
            int denseIndex)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            SectionId = sectionId;
            DenseIndex = denseIndex;
        }

        public FrameLayoutId LayoutId { get; }
        public OperationSectionId SectionId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               SectionId.IsValid &&
                               DenseIndex >= 0;

        internal bool IsOwnedBy(OperationSectionRegistry owner) => ReferenceEquals(_owner, owner);

        public bool Equals(OperationSectionHandle<TSection> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   SectionId == other.SectionId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) =>
            obj is OperationSectionHandle<TSection> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ SectionId.GetHashCode();
                hashCode = (hashCode * 397) ^ DenseIndex;
                return hashCode;
            }
        }

        public static bool operator ==(
            OperationSectionHandle<TSection> left,
            OperationSectionHandle<TSection> right) => left.Equals(right);

        public static bool operator !=(
            OperationSectionHandle<TSection> left,
            OperationSectionHandle<TSection> right) => !left.Equals(right);
    }

    /// <summary>
    /// A pre-resolved field address. It is intentionally free of Type and string metadata.
    /// </summary>
    public readonly struct OperationSectionField<TValue> :
        IEquatable<OperationSectionField<TValue>>
        where TValue : unmanaged
    {
        private readonly OperationSectionRegistry _owner;

        internal OperationSectionField(
            OperationSectionRegistry owner,
            int sectionIndex,
            int fieldIndex,
            int byteOffset,
            int byteSize)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            SectionIndex = sectionIndex;
            FieldIndex = fieldIndex;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        public FrameLayoutId LayoutId { get; }
        public int SectionIndex { get; }
        public int FieldIndex { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               SectionIndex >= 0 &&
                               FieldIndex >= 0 &&
                               ByteOffset >= 0 &&
                               ByteSize > 0;

        internal bool IsOwnedBy(OperationSectionRegistry owner) => ReferenceEquals(_owner, owner);

        public bool Equals(OperationSectionField<TValue> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   SectionIndex == other.SectionIndex &&
                   FieldIndex == other.FieldIndex &&
                   ByteOffset == other.ByteOffset &&
                   ByteSize == other.ByteSize;
        }

        public override bool Equals(object obj) =>
            obj is OperationSectionField<TValue> other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _owner?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ LayoutId.GetHashCode();
                hashCode = (hashCode * 397) ^ SectionIndex;
                hashCode = (hashCode * 397) ^ FieldIndex;
                hashCode = (hashCode * 397) ^ ByteOffset;
                hashCode = (hashCode * 397) ^ ByteSize;
                return hashCode;
            }
        }

        public static bool operator ==(
            OperationSectionField<TValue> left,
            OperationSectionField<TValue> right) => left.Equals(right);

        public static bool operator !=(
            OperationSectionField<TValue> left,
            OperationSectionField<TValue> right) => !left.Equals(right);
    }

    public sealed class OperationSectionFieldDescriptor
    {
        internal OperationSectionFieldDescriptor(
            int denseIndex,
            string name,
            Type valueType,
            int byteOffset,
            int byteSize)
        {
            DenseIndex = denseIndex;
            Name = name;
            ValueType = valueType;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
        }

        public int DenseIndex { get; }
        public string Name { get; }
        public Type ValueType { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
    }

    public sealed class OperationSectionDescriptor
    {
        private readonly IReadOnlyList<OperationSectionFieldDescriptor> _readOnlyFields;

        internal OperationSectionDescriptor(
            OperationSectionId sectionId,
            OperationSectionMode mode,
            Type sectionType,
            OperationSectionShape shape,
            int denseIndex,
            int byteOffset,
            int byteSize,
            OperationSectionFieldDescriptor[] fields)
        {
            SectionId = sectionId;
            Mode = mode;
            SectionType = sectionType;
            Shape = shape;
            DenseIndex = denseIndex;
            ByteOffset = byteOffset;
            ByteSize = byteSize;
            _readOnlyFields = Array.AsReadOnly(fields);
        }

        public OperationSectionId SectionId { get; }
        public OperationSectionMode Mode { get; }
        public Type SectionType { get; }
        public OperationSectionShape Shape { get; }
        public int DenseIndex { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
        public IReadOnlyList<OperationSectionFieldDescriptor> Fields => _readOnlyFields;
    }

    /// <summary>
    /// Creates a sealed section view during GraphInstance setup. Factories are never invoked per Tick.
    /// </summary>
    public interface IOperationSectionViewFactory<TSection>
        where TSection : class, IOperationSection
    {
        TSection Create(in OperationSectionViewContext<TSection> context);
    }

    public readonly struct OperationSectionViewContext<TSection>
        where TSection : class, IOperationSection
    {
        private readonly OperationSectionRegistry _registry;

        internal OperationSectionViewContext(
            OperationSectionRegistry registry,
            OperationSectionReader reader,
            OperationSectionHandle<TSection> handle)
        {
            _registry = registry;
            Reader = reader;
            Handle = handle;
        }

        public OperationSectionReader Reader { get; }
        public OperationSectionHandle<TSection> Handle { get; }
        public bool IsValid => _registry != null && Reader != null && Handle.IsValid;

        public bool TryGetField<TValue>(
            int denseFieldIndex,
            out OperationSectionField<TValue> field)
            where TValue : unmanaged
        {
            if (_registry == null)
            {
                field = default;
                return false;
            }

            return _registry.TryResolveField(Handle, denseFieldIndex, out field);
        }
    }

    internal interface IOperationSectionWriteTarget
    {
        void Write<TValue>(OperationSectionField<TValue> field, in TValue value)
            where TValue : unmanaged;
        void Enable<TSection>() where TSection : class, IOperationSection;
    }

    /// <summary>
    /// Accesses the existing frame for Operator reads or the callback writer for Logic writes.
    /// </summary>
    public sealed class OperationSectionReader
    {
        private readonly OperationFrame _frame;
        private readonly IOperationSectionWriteTarget _writeTarget;
        private readonly Action _enable;

        internal OperationSectionReader(OperationFrame frame)
        {
            _frame = frame;
        }

        internal OperationSectionReader(IOperationSectionWriteTarget writeTarget, Action enable)
        {
            _writeTarget = writeTarget;
            _enable = enable;
        }

        public void Write<TValue>(OperationSectionField<TValue> field, in TValue value)
            where TValue : unmanaged
        {
            _enable?.Invoke();
            _writeTarget.Write(field, value);
        }

        public bool TryRead<TValue>(
            OperationSectionField<TValue> field,
            out TValue value)
            where TValue : unmanaged
        {
            if (_frame == null)
            {
                value = default;
                return false;
            }

            return _frame.TryRead(field, out value);
        }

        public TValue Read<TValue>(OperationSectionField<TValue> field)
            where TValue : unmanaged
        {
            return _frame != null && _frame.TryRead(field, out TValue value)
                ? value
                : default;
        }
    }

    public sealed class OperationSectionRegistryBuilder
    {
        private readonly Dictionary<Type, Registration> _byType = new Dictionary<Type, Registration>();
        private readonly Dictionary<OperationSectionId, Registration> _byId =
            new Dictionary<OperationSectionId, Registration>();
        private readonly List<Registration> _registrations = new List<Registration>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;
        public int Count => _registrations.Count;

        public bool TryRegister<TSection>(
            OperationSectionId sectionId,
            OperationSectionMode mode,
            IOperationSectionViewFactory<TSection> viewFactory,
            out OperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TSection : class, IOperationSection
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "Operation Section Registry is already frozen.");
                return false;
            }

            if (viewFactory == null)
            {
                requirement = default;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section read-view factory is required.");
                return false;
            }

            if (!OperationSectionRequirement.TryCreate<TSection>(
                    sectionId,
                    mode,
                    out requirement,
                    out diagnostic))
            {
                return false;
            }

            Type sectionType = typeof(TSection);
            if (_byType.TryGetValue(sectionType, out Registration sameType))
            {
                if (sameType.Requirement == requirement &&
                    ReferenceEquals(sameType.Factory.FactoryInstance, viewFactory))
                {
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                requirement = default;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "The same Operation Section interface cannot be registered with another identity, mode, or view factory instance.");
                return false;
            }

            if (_byId.ContainsKey(sectionId))
            {
                requirement = default;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "Operation SectionId is already assigned to another interface.");
                return false;
            }

            var registration = new Registration(
                requirement,
                new ViewFactoryRegistration<TSection>(viewFactory));
            _registrations.Add(registration);
            _byType.Add(sectionType, registration);
            _byId.Add(sectionId, registration);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryFreeze(
            FrameLayoutId layoutId,
            out OperationSectionRegistry registry,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                registry = null;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "Operation Section Registry may only be frozen once.");
                return false;
            }

            if (!layoutId.IsValid)
            {
                registry = null;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "OperationFrame LayoutId must be valid.");
                return false;
            }

            Registration[] registrations = _registrations.ToArray();
            Array.Sort(registrations, RegistrationComparer.Instance);

            var descriptors = new OperationSectionDescriptor[registrations.Length];
            var factories = new IViewFactoryRegistration[registrations.Length];
            int layoutSize = 0;
            for (int sectionIndex = 0; sectionIndex < registrations.Length; sectionIndex++)
            {
                Registration registration = registrations[sectionIndex];
                OperationSectionShape shape = registration.Requirement.Shape;
                var fields = new OperationSectionFieldDescriptor[shape.FieldCount];
                for (int fieldIndex = 0; fieldIndex < shape.FieldCount; fieldIndex++)
                {
                    OperationSectionFieldShape field = shape.Fields[fieldIndex];
                    fields[fieldIndex] = new OperationSectionFieldDescriptor(
                        field.DenseIndex,
                        field.Name,
                        field.ValueType,
                        field.ByteOffset,
                        field.ByteSize);
                }

                if (layoutSize > int.MaxValue - shape.ByteSize)
                {
                    registry = null;
                    diagnostic = OperationSectionContract.Error(
                        CoCoDiagnosticCode.InvalidFrameLayout,
                        "OperationFrame storage exceeds the supported arena size.");
                    return false;
                }

                descriptors[sectionIndex] = new OperationSectionDescriptor(
                    registration.Requirement.SectionId,
                    registration.Requirement.Mode,
                    registration.Requirement.SectionType,
                    shape,
                    sectionIndex,
                    layoutSize,
                    shape.ByteSize,
                    fields);
                factories[sectionIndex] = registration.Factory;
                layoutSize += shape.ByteSize;
            }

            registry = new OperationSectionRegistry(layoutId, layoutSize, descriptors, factories);
            if (!registry.TryPrewarmViews(out diagnostic))
            {
                registry = null;
                return false;
            }

            _isFrozen = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private sealed class Registration
        {
            public Registration(
                OperationSectionRequirement requirement,
                IViewFactoryRegistration factory)
            {
                Requirement = requirement;
                Factory = factory;
            }

            public OperationSectionRequirement Requirement { get; }
            public IViewFactoryRegistration Factory { get; }
        }

        private sealed class RegistrationComparer : IComparer<Registration>
        {
            public static readonly RegistrationComparer Instance = new RegistrationComparer();

            public int Compare(Registration left, Registration right)
            {
                int high = left.Requirement.SectionId.High.CompareTo(right.Requirement.SectionId.High);
                return high != 0
                    ? high
                    : left.Requirement.SectionId.Low.CompareTo(right.Requirement.SectionId.Low);
            }
        }
    }

    public sealed class OperationSectionRegistry
    {
        private readonly OperationSectionDescriptor[] _descriptors;
        private readonly IReadOnlyList<OperationSectionDescriptor> _readOnlyDescriptors;
        private readonly IViewFactoryRegistration[] _factories;

        internal OperationSectionRegistry(
            FrameLayoutId layoutId,
            int byteSize,
            OperationSectionDescriptor[] descriptors,
            IViewFactoryRegistration[] factories)
        {
            LayoutId = layoutId;
            ByteSize = byteSize;
            _descriptors = descriptors;
            _readOnlyDescriptors = Array.AsReadOnly(descriptors);
            _factories = factories;
        }

        public FrameLayoutId LayoutId { get; }
        public int ByteSize { get; }
        public int Count => _descriptors.Length;
        public IReadOnlyList<OperationSectionDescriptor> Sections => _readOnlyDescriptors;
        public bool IsFrozen => LayoutId.IsValid;

        /// <summary>
        /// Resolves a field by Section type and its stable dense field index.
        /// Standard State logics use contract-owned field constants instead
        /// of constructor-injected handles. Section types are unique per
        /// registry.
        /// </summary>
        /// <summary>
        /// Resolves a section handle by interface type for enabling a
        /// discrete section from a standard state logic (no injected
        /// requirement needed). Section types are unique per registry.
        /// </summary>
        public bool TryResolveTypedHandle<TSection>(
            out OperationSectionHandle<TSection> handle)
            where TSection : class, IOperationSection
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                if (_descriptors[index].SectionType != typeof(TSection))
                {
                    continue;
                }

                handle = new OperationSectionHandle<TSection>(
                    this,
                    _descriptors[index].SectionId,
                    index);
                return true;
            }

            handle = default;
            return false;
        }

        public bool TryResolveTypedField<TSection, TValue>(
            int fieldIndex,
            out OperationSectionField<TValue> field)
            where TSection : class, IOperationSection
            where TValue : unmanaged
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                if (_descriptors[index].SectionType != typeof(TSection))
                {
                    continue;
                }

                var handle = new OperationSectionHandle<TSection>(
                    this,
                    _descriptors[index].SectionId,
                    index);
                return TryResolveField(handle, fieldIndex, out field);
            }

            field = default;
            return false;
        }

        public bool TryResolve<TSection>(
            OperationSectionRequirement requirement,
            out OperationSectionHandle<TSection> handle)
            where TSection : class, IOperationSection
        {
            if (!requirement.IsValid ||
                requirement.SectionType != typeof(TSection) ||
                !TryFind(requirement, out int sectionIndex))
            {
                handle = default;
                return false;
            }

            OperationSectionDescriptor descriptor = _descriptors[sectionIndex];
            handle = new OperationSectionHandle<TSection>(
                this,
                descriptor.SectionId,
                sectionIndex);
            return true;
        }

        internal bool TryResolveDenseIndex(
            OperationSectionRequirement requirement,
            out int denseIndex)
        {
            if (!requirement.IsValid || !TryFind(requirement, out denseIndex))
            {
                denseIndex = -1;
                return false;
            }

            return true;
        }

        public bool TryResolveField<TSection, TValue>(
            OperationSectionHandle<TSection> handle,
            int denseFieldIndex,
            out OperationSectionField<TValue> field)
            where TSection : class, IOperationSection
            where TValue : unmanaged
        {
            if (!ValidateHandle(handle) ||
                denseFieldIndex < 0 ||
                denseFieldIndex >= _descriptors[handle.DenseIndex].Fields.Count)
            {
                field = default;
                return false;
            }

            OperationSectionDescriptor section = _descriptors[handle.DenseIndex];
            OperationSectionFieldDescriptor descriptor = section.Fields[denseFieldIndex];
            if (descriptor.ValueType != typeof(TValue))
            {
                field = default;
                return false;
            }

            field = new OperationSectionField<TValue>(
                this,
                handle.DenseIndex,
                denseFieldIndex,
                section.ByteOffset + descriptor.ByteOffset,
                descriptor.ByteSize);
            return true;
        }

        public bool TryValidateProvides(
            IReadOnlyList<OperationSectionRequirement> graphProvides,
            out CoCoDiagnostic diagnostic)
        {
            if (graphProvides == null)
            {
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.MissingOperationSection,
                    "Graph Operation Provides must be declared.");
                return false;
            }

            if (graphProvides.Count != _descriptors.Length)
            {
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.MissingOperationSection,
                    "Graph Operation Provides must cover every Section in the OperationFrame layout.");
                return false;
            }

            for (int index = 0; index < graphProvides.Count; index++)
            {
                OperationSectionRequirement current = graphProvides[index];
                if (!current.IsValid || !TryFind(current, out _))
                {
                    diagnostic = OperationSectionContract.Error(
                        CoCoDiagnosticCode.MissingOperationSection,
                        "Graph Operation Provides contains an unregistered Section.");
                    return false;
                }

                for (int previous = 0; previous < index; previous++)
                {
                    if (graphProvides[previous] == current)
                    {
                        diagnostic = OperationSectionContract.Error(
                            CoCoDiagnosticCode.DuplicateIdentifier,
                            "Graph Operation Provides contains the same Section more than once.");
                        return false;
                    }
                }
            }

            for (int sectionIndex = 0; sectionIndex < _descriptors.Length; sectionIndex++)
            {
                OperationSectionDescriptor descriptor = _descriptors[sectionIndex];
                bool covered = false;
                for (int provideIndex = 0; provideIndex < graphProvides.Count; provideIndex++)
                {
                    OperationSectionRequirement provided = graphProvides[provideIndex];
                    if (provided.SectionId == descriptor.SectionId &&
                        provided.Mode == descriptor.Mode &&
                        provided.SectionType == descriptor.SectionType &&
                        HasExactShape(provided.Shape, descriptor.Shape))
                    {
                        covered = true;
                        break;
                    }
                }

                if (!covered)
                {
                    diagnostic = OperationSectionContract.Error(
                        CoCoDiagnosticCode.MissingOperationSection,
                        "Graph Operation Provides does not cover the complete OperationFrame layout.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool ValidateHandle<TSection>(OperationSectionHandle<TSection> handle)
            where TSection : class, IOperationSection
        {
            return handle.IsValid &&
                   handle.IsOwnedBy(this) &&
                   handle.LayoutId == LayoutId &&
                   handle.DenseIndex < _descriptors.Length &&
                   _descriptors[handle.DenseIndex].SectionId == handle.SectionId &&
                   _descriptors[handle.DenseIndex].SectionType == typeof(TSection);
        }

        internal bool ValidateField<TValue>(OperationSectionField<TValue> field)
            where TValue : unmanaged
        {
            if (!field.IsValid ||
                !field.IsOwnedBy(this) ||
                field.LayoutId != LayoutId ||
                field.SectionIndex >= _descriptors.Length)
            {
                return false;
            }

            OperationSectionDescriptor section = _descriptors[field.SectionIndex];
            if (field.FieldIndex >= section.Fields.Count)
            {
                return false;
            }

            OperationSectionFieldDescriptor descriptor = section.Fields[field.FieldIndex];
            return field.ByteOffset == section.ByteOffset + descriptor.ByteOffset &&
                   field.ByteSize == descriptor.ByteSize &&
                   descriptor.ValueType == typeof(TValue);
        }

        internal object CreateWriteView(Type sectionType, IOperationSectionWriteTarget target)
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                if (_descriptors[index].SectionType == sectionType)
                    return _factories[index].CreateWriteView(this, target, _descriptors[index]);
            }
            throw new InvalidOperationException("No registered Operation Section: " + sectionType);
        }

        internal object[] CreateViews(OperationFrame frame)
        {
            var views = new object[_descriptors.Length];
            var reader = new OperationSectionReader(frame);
            for (int index = 0; index < _descriptors.Length; index++)
            {
                object view = _factories[index].Create(this, reader, _descriptors[index]);
                if (view == null || !view.GetType().IsSealed)
                {
                    throw new InvalidOperationException(
                        "Operation Section factory returned an invalid read view.");
                }

                views[index] = view;
            }

            return views;
        }

        internal OperationSectionMode GetMode(int sectionIndex) => _descriptors[sectionIndex].Mode;

        private bool TryFind(OperationSectionRequirement requirement, out int sectionIndex)
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                OperationSectionDescriptor descriptor = _descriptors[index];
                if (descriptor.SectionId == requirement.SectionId &&
                    descriptor.Mode == requirement.Mode &&
                    descriptor.SectionType == requirement.SectionType &&
                    HasExactShape(requirement.Shape, descriptor.Shape))
                {
                    sectionIndex = index;
                    return true;
                }
            }

            sectionIndex = -1;
            return false;
        }

        private static bool HasExactShape(
            OperationSectionShape provided,
            OperationSectionShape registered)
        {
            return provided != null &&
                   registered != null &&
                   provided.ShapeFingerprint == registered.ShapeFingerprint &&
                   provided.Equals(registered);
        }

        internal bool TryPrewarmViews(out CoCoDiagnostic diagnostic)
        {
            var reader = new OperationSectionReader(null);
            for (int index = 0; index < _descriptors.Length; index++)
            {
                object view;
                try
                {
                    view = _factories[index].Create(this, reader, _descriptors[index]);
                }
                catch (Exception)
                {
                    diagnostic = OperationSectionContract.Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section read-view factory failed during prewarm.");
                    return false;
                }

                if (view == null || !view.GetType().IsSealed)
                {
                    diagnostic = OperationSectionContract.Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section read-view factory must return a sealed view.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public readonly struct OperationSectionEntry<TSection>
        where TSection : class, IOperationSection
    {
        internal OperationSectionEntry(
            OperationSectionEntryHeader header,
            TSection view)
        {
            Header = header;
            View = view;
        }

        public OperationSectionEntryHeader Header { get; }
        public TSection View { get; }
    }

    /// <summary>
    /// Framework-owned precedence for one Operation contribution.
    /// A larger Layer index wins first; within one Layer, a deeper active path wins.
    /// </summary>
    public readonly struct OperationWriteRank : IEquatable<OperationWriteRank>
    {
        private readonly bool _isValid;

        private OperationWriteRank(int layerIndex, int pathDepth)
        {
            _isValid = true;
            LayerIndex = layerIndex;
            PathDepth = pathDepth;
        }

        public int LayerIndex { get; }
        public int PathDepth { get; }
        public bool IsValid => _isValid && LayerIndex >= 0 && PathDepth >= 0;

        internal static OperationWriteRank Base => new OperationWriteRank(0, 0);

        public static bool TryCreate(
            int layerIndex,
            int pathDepth,
            out OperationWriteRank rank)
        {
            if (layerIndex < 0 || pathDepth < 0)
            {
                rank = default;
                return false;
            }

            rank = new OperationWriteRank(layerIndex, pathDepth);
            return true;
        }

        internal bool IsLowerThan(in OperationWriteRank other)
        {
            return LayerIndex < other.LayerIndex ||
                   (LayerIndex == other.LayerIndex && PathDepth < other.PathDepth);
        }

        public bool Equals(OperationWriteRank other) =>
            _isValid == other._isValid &&
            LayerIndex == other.LayerIndex &&
            PathDepth == other.PathDepth;

        public override bool Equals(object obj) =>
            obj is OperationWriteRank other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = _isValid.GetHashCode();
                hashCode = (hashCode * 397) ^ LayerIndex;
                hashCode = (hashCode * 397) ^ PathDepth;
                return hashCode;
            }
        }

        public static bool operator ==(
            OperationWriteRank left,
            OperationWriteRank right) => left.Equals(right);

        public static bool operator !=(
            OperationWriteRank left,
            OperationWriteRank right) => !left.Equals(right);
    }

    /// <summary>
    /// Read-only finalized OperationFrame surface consumed by Operators.
    /// </summary>
    public interface IOperationFrame
    {
        StateFlowFrameHeader Header { get; }
        OperationSectionRegistry Registry { get; }

        bool TryGet<TSection>(
            OperationSectionHandle<TSection> handle,
            out OperationSectionEntry<TSection> entry)
            where TSection : class, IOperationSection;
    }

    /// <summary>
    /// A fixed-layout, GraphInstance-owned OperationFrame arena.
    /// Begin, ranked writes, finalize and commit/cancel all reuse preallocated storage.
    /// </summary>
    internal sealed class OperationFrame
    {
        private readonly OperationSectionRegistry _registry;
        private readonly GraphInstanceId _graphInstanceId;
        private readonly byte[] _storage;
        private readonly OperationSectionEntryHeader[] _entryHeaders;
        private readonly OperationWriteRank[] _fieldRanks;
        private readonly bool[] _fieldHasContribution;
        private readonly OperationWriteRank[] _discreteRanks;
        private readonly bool[] _discreteHasContribution;
        private readonly ActivationId[] _discreteActivationIds;
        private readonly ulong[] _committedSequences;
        private readonly ulong[] _pendingSequences;
        private readonly object[] _views;
        private TimelineEpoch _committedSequenceEpoch;
        private TimelineEpoch _pendingSequenceEpoch;
        private TickFrame _lastCommittedTickFrame;
        private StateFlowFrameHeader _header;
        private ulong _writeToken;
        private object _runtimeOwner;
        private bool _hasCommittedSequenceEpoch;
        private bool _hasCommittedTickFrame;
        private bool _isWriting;
        private bool _isFinalized;

        private OperationFrame(
            OperationSectionRegistry registry,
            GraphInstanceId graphInstanceId)
        {
            _registry = registry;
            _graphInstanceId = graphInstanceId;
            _storage = new byte[registry.ByteSize];
            _entryHeaders = new OperationSectionEntryHeader[registry.Count];
            _fieldRanks = new OperationWriteRank[registry.ByteSize];
            _fieldHasContribution = new bool[registry.ByteSize];
            _discreteRanks = new OperationWriteRank[registry.Count];
            _discreteHasContribution = new bool[registry.Count];
            _discreteActivationIds = new ActivationId[registry.Count];
            _committedSequences = new ulong[registry.Count];
            _pendingSequences = new ulong[registry.Count];
            _views = registry.CreateViews(this);
        }

        public OperationSectionRegistry Registry => _registry;
        public GraphInstanceId GraphInstanceId => _graphInstanceId;

        public static bool TryCreate(
            OperationSectionRegistry registry,
            GraphInstanceId graphInstanceId,
            IReadOnlyList<OperationSectionRequirement> graphProvides,
            out OperationFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (registry == null || !registry.IsFrozen)
            {
                frame = null;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "OperationFrame requires a frozen Section Registry.");
                return false;
            }

            if (!graphInstanceId.IsValid)
            {
                frame = null;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "OperationFrame GraphInstanceId must be valid.");
                return false;
            }

            if (!registry.TryValidateProvides(graphProvides, out diagnostic))
            {
                frame = null;
                return false;
            }

            try
            {
                frame = new OperationFrame(registry, graphInstanceId);
            }
            catch (Exception)
            {
                frame = null;
                diagnostic = OperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section view creation failed during GraphInstance setup.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryBegin(TickFrame tickFrame, out OperationFrameWriter writer) =>
            TryBegin(null, tickFrame, out writer);

        internal bool TryBegin(
            object runtimeOwner,
            TickFrame tickFrame,
            out OperationFrameWriter writer)
        {
            if ((_runtimeOwner != null && !ReferenceEquals(_runtimeOwner, runtimeOwner)) ||
                _isWriting ||
                _isFinalized ||
                (_hasCommittedTickFrame &&
                 !StateFlowTickOrder.IsStrictlyAfter(
                     tickFrame,
                     _lastCommittedTickFrame)) ||
                !StateFlowFrameHeader.TryCreate(
                    _graphInstanceId,
                    _registry.LayoutId,
                    StateFlowFrameKind.Operation,
                    tickFrame,
                    out StateFlowFrameHeader header))
            {
                writer = default;
                return false;
            }

            if (!TryPrepareSequenceEpoch(tickFrame.TimelineEpoch))
            {
                writer = default;
                return false;
            }

            Array.Clear(_storage, 0, _storage.Length);
            Array.Clear(_fieldHasContribution, 0, _fieldHasContribution.Length);
            Array.Clear(_discreteHasContribution, 0, _discreteHasContribution.Length);
            Array.Clear(_discreteActivationIds, 0, _discreteActivationIds.Length);
            for (int index = 0; index < _entryHeaders.Length; index++)
            {
                _entryHeaders[index] = _registry.GetMode(index) == OperationSectionMode.Continuous
                    ? OperationSectionEntryHeader.Continuous()
                    : default;
            }

            _header = header;
            _isWriting = true;
            _isFinalized = false;
            _writeToken = _writeToken == ulong.MaxValue ? 1UL : _writeToken + 1UL;
            writer = new OperationFrameWriter(this, _writeToken);
            return true;
        }

        internal bool TryClaimRuntimeOwner(object owner, GraphInstanceId graphInstanceId)
        {
            lock (this)
            {
                if (owner == null ||
                    !graphInstanceId.IsValid ||
                    graphInstanceId != _graphInstanceId ||
                    _runtimeOwner != null ||
                    _isWriting ||
                    _isFinalized ||
                    _hasCommittedTickFrame ||
                    _hasCommittedSequenceEpoch)
                {
                    return false;
                }

                _runtimeOwner = owner;
                return true;
            }
        }

        internal void ReleaseRuntimeOwner(object owner)
        {
            lock (this)
            {
                if (ReferenceEquals(_runtimeOwner, owner))
                {
                    _runtimeOwner = null;
                }
            }
        }

        internal bool TryGet<TSection>(
            ulong token,
            OperationSectionHandle<TSection> handle,
            out OperationSectionEntry<TSection> entry)
            where TSection : class, IOperationSection
        {
            if (!IsFinalized(token) || !_registry.ValidateHandle(handle))
            {
                entry = default;
                return false;
            }

            entry = new OperationSectionEntry<TSection>(
                _entryHeaders[handle.DenseIndex],
                (TSection)_views[handle.DenseIndex]);
            return true;
        }

        internal bool TryGetHeader(
            ulong token,
            int denseIndex,
            out OperationSectionEntryHeader header)
        {
            if (!IsFinalized(token) ||
                denseIndex < 0 ||
                denseIndex >= _entryHeaders.Length)
            {
                header = default;
                return false;
            }

            header = _entryHeaders[denseIndex];
            return true;
        }

        internal bool TryRead<TValue>(
            OperationSectionField<TValue> field,
            out TValue value)
            where TValue : unmanaged
        {
            if (!_isFinalized || !_registry.ValidateField(field))
            {
                value = default;
                return false;
            }

            value = OperationSectionBinary.Read<TValue>(
                _storage,
                field.ByteOffset,
                field.ByteSize);
            return true;
        }

        internal bool TryWrite<TValue>(
            ulong token,
            OperationWriteRank rank,
            OperationSectionField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            if (!CanWrite(token) ||
                !rank.IsValid ||
                !_registry.ValidateField(field))
            {
                return false;
            }

            int fieldAddress = field.ByteOffset;
            if (_fieldHasContribution[fieldAddress] &&
                rank.IsLowerThan(_fieldRanks[fieldAddress]))
            {
                return true;
            }

            OperationSectionBinary.Write(_storage, field.ByteOffset, field.ByteSize, value);
            _fieldRanks[fieldAddress] = rank;
            _fieldHasContribution[fieldAddress] = true;
            return true;
        }

        internal bool TryEnableDiscrete<TSection>(
            ulong token,
            OperationWriteRank rank,
            OperationSectionHandle<TSection> handle,
            ActivationId activationId)
            where TSection : class, IOperationSection
        {
            if (!CanWrite(token) ||
                !rank.IsValid ||
                !activationId.IsValid ||
                !_registry.ValidateHandle(handle) ||
                _registry.GetMode(handle.DenseIndex) != OperationSectionMode.Discrete)
            {
                return false;
            }

            int sectionIndex = handle.DenseIndex;
            if (_discreteHasContribution[sectionIndex] &&
                rank.IsLowerThan(_discreteRanks[sectionIndex]))
            {
                return true;
            }

            _discreteRanks[sectionIndex] = rank;
            _discreteHasContribution[sectionIndex] = true;
            _discreteActivationIds[sectionIndex] = activationId;
            return true;
        }

        internal bool TryFinalize(
            ulong token,
            out FinalizedOperationFrame finalized)
        {
            if (!CanWrite(token))
            {
                finalized = default;
                return false;
            }

            for (int index = 0; index < _discreteHasContribution.Length; index++)
            {
                if (_discreteHasContribution[index] &&
                    _pendingSequences[index] == ulong.MaxValue)
                {
                    finalized = default;
                    return false;
                }
            }

            for (int index = 0; index < _discreteHasContribution.Length; index++)
            {
                if (!_discreteHasContribution[index])
                {
                    continue;
                }

                ulong next = _pendingSequences[index] + 1UL;
                if (!OperationSequence.TryCreate(next, out OperationSequence sequence))
                {
                    finalized = default;
                    return false;
                }

                _pendingSequences[index] = next;
                _entryHeaders[index] = OperationSectionEntryHeader.Discrete(
                    _discreteActivationIds[index],
                    sequence);
            }

            _isWriting = false;
            _isFinalized = true;
            finalized = new FinalizedOperationFrame(this, token);
            return true;
        }

        internal bool IsCommitReady(ulong token) => IsFinalized(token);

        internal void CommitPreparedNoFail()
        {
            CommitPendingSequenceEpoch();
            _lastCommittedTickFrame = _header.TickFrame;
            _hasCommittedTickFrame = true;
            ClearActiveTransaction();
        }

        internal void CommitPrepared(ulong token)
        {
            if (!IsCommitReady(token))
            {
                throw new InvalidOperationException(
                    "The finalized OperationFrame token is no longer ready.");
            }

            CommitPreparedNoFail();
        }

        internal bool Commit(ulong token)
        {
            if (!IsCommitReady(token))
            {
                return false;
            }

            CommitPrepared(token);
            return true;
        }

        internal bool CancelPrepared(ulong token)
        {
            if (!CanWrite(token))
            {
                return false;
            }

            ClearActiveTransaction();
            return true;
        }

        internal bool CancelFinalized(ulong token)
        {
            if (!IsFinalized(token))
            {
                return false;
            }

            ClearActiveTransaction();
            return true;
        }

        internal bool IsWriting(ulong token) => CanWrite(token);

        internal bool IsFinalized(ulong token) =>
            _isFinalized && token != 0UL && token == _writeToken;

        internal StateFlowFrameHeader GetFinalizedHeader(ulong token) =>
            IsFinalized(token) ? _header : default;

        private bool TryPrepareSequenceEpoch(TimelineEpoch epoch)
        {
            if (_hasCommittedSequenceEpoch && epoch == _committedSequenceEpoch)
            {
                Array.Copy(_committedSequences, _pendingSequences, _committedSequences.Length);
                _pendingSequenceEpoch = epoch;
                return true;
            }

            // Restore/rewind resumes in a strictly newer TimelineEpoch. Returning to an older
            // committed epoch is unsupported because it could restart a per-Section sequence.
            if (_hasCommittedSequenceEpoch && epoch.Value < _committedSequenceEpoch.Value)
            {
                return false;
            }

            Array.Clear(_pendingSequences, 0, _pendingSequences.Length);
            _pendingSequenceEpoch = epoch;
            return true;
        }

        private void CommitPendingSequenceEpoch()
        {
            _committedSequenceEpoch = _pendingSequenceEpoch;
            _hasCommittedSequenceEpoch = true;
            Array.Copy(_pendingSequences, _committedSequences, _pendingSequences.Length);
            _pendingSequenceEpoch = default;
        }

        private void ClearActiveTransaction()
        {
            _isWriting = false;
            _isFinalized = false;
            _header = default;
            _pendingSequenceEpoch = default;
        }

        private bool CanWrite(ulong token) => _isWriting && token != 0UL && token == _writeToken;
    }

    /// <summary>
    /// Read-only finalized OperationFrame candidate. Commit advances Tick and discrete Sequences;
    /// Cancel abandons the candidate without consuming either.
    /// </summary>
    internal readonly struct FinalizedOperationFrame : IOperationFrame
    {
        private readonly OperationFrame _frame;
        private readonly ulong _token;

        internal FinalizedOperationFrame(OperationFrame frame, ulong token)
        {
            _frame = frame;
            _token = token;
        }

        public StateFlowFrameHeader Header =>
            _frame != null ? _frame.GetFinalizedHeader(_token) : default;

        public OperationSectionRegistry Registry => _frame?.Registry;
        public bool IsValid => _frame != null && _frame.IsFinalized(_token);

        public bool TryGet<TSection>(
            OperationSectionHandle<TSection> handle,
            out OperationSectionEntry<TSection> entry)
            where TSection : class, IOperationSection
        {
            if (_frame == null)
            {
                entry = default;
                return false;
            }

            return _frame.TryGet(_token, handle, out entry);
        }

        internal bool TryGetHeader(
            OperationSectionRequirement requirement,
            out OperationSectionEntryHeader header)
        {
            if (_frame == null ||
                !_frame.Registry.TryResolveDenseIndex(requirement, out int denseIndex))
            {
                header = default;
                return false;
            }

            return _frame.TryGetHeader(_token, denseIndex, out header);
        }

        internal bool TryGetHeader(
            int denseIndex,
            out OperationSectionEntryHeader header)
        {
            if (_frame == null)
            {
                header = default;
                return false;
            }

            return _frame.TryGetHeader(_token, denseIndex, out header);
        }

        internal bool Commit() => _frame != null && _frame.Commit(_token);
        internal bool Cancel() => _frame != null && _frame.CancelFinalized(_token);

        internal bool IsCommitReady => _frame != null && _frame.IsCommitReady(_token);

        internal void CommitPrepared()
        {
            _frame.CommitPrepared(_token);
        }

        internal void CommitPreparedNoFail()
        {
            _frame.CommitPreparedNoFail();
        }
    }

    /// <summary>
    /// Read-only, allocation-free view of a finalized Operation candidate.
    /// Unlike <see cref="FinalizedOperationFrame"/>, this surface cannot commit or
    /// cancel the candidate and is safe to expose to producer callbacks.
    /// </summary>
    public readonly struct StagedOperationFrame : IOperationFrame
    {
        private readonly FinalizedOperationFrame _frame;

        internal StagedOperationFrame(FinalizedOperationFrame frame)
        {
            _frame = frame;
        }

        public StateFlowFrameHeader Header => _frame.Header;
        public OperationSectionRegistry Registry => _frame.Registry;
        public bool IsValid => _frame.IsValid;

        public bool TryGet<TSection>(
            OperationSectionHandle<TSection> handle,
            out OperationSectionEntry<TSection> entry)
            where TSection : class, IOperationSection =>
            _frame.TryGet(handle, out entry);
    }

    internal readonly struct OperationFrameWriter
    {
        private readonly OperationFrame _frame;
        private readonly ulong _token;

        internal OperationFrameWriter(OperationFrame frame, ulong token)
        {
            _frame = frame;
            _token = token;
        }

        public bool IsValid => _frame != null && _frame.IsWriting(_token);

        internal bool TryResolveTypedHandle<TSection>(
            out OperationSectionHandle<TSection> handle)
            where TSection : class, IOperationSection
        {
            handle = default;
            return _frame != null &&
                   _frame.Registry.TryResolveTypedHandle(out handle);
        }

        internal OperationSectionField<TValue> ResolveField<TSection, TValue>(
            int fieldIndex)
            where TSection : class, IOperationSection
            where TValue : unmanaged
        {
            return _frame != null &&
                   _frame.Registry.TryResolveTypedField<TSection, TValue>(
                       fieldIndex,
                       out OperationSectionField<TValue> field)
                ? field
                : default;
        }

        public bool Write<TValue>(
            OperationSectionField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            return Write(OperationWriteRank.Base, field, value);
        }

        public bool Write<TValue>(
            OperationWriteRank rank,
            OperationSectionField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            return _frame != null && _frame.TryWrite(_token, rank, field, value);
        }

        public bool EnableDiscrete<TSection>(
            OperationSectionHandle<TSection> handle,
            ActivationId activationId)
            where TSection : class, IOperationSection
        {
            return EnableDiscrete(OperationWriteRank.Base, handle, activationId);
        }

        public bool EnableDiscrete<TSection>(
            OperationWriteRank rank,
            OperationSectionHandle<TSection> handle,
            ActivationId activationId)
            where TSection : class, IOperationSection
        {
            return _frame != null &&
                   _frame.TryEnableDiscrete(_token, rank, handle, activationId);
        }

        internal bool TryFinalize(out FinalizedOperationFrame finalized)
        {
            if (_frame == null)
            {
                finalized = default;
                return false;
            }

            return _frame.TryFinalize(_token, out finalized);
        }

        internal bool Cancel() => _frame != null && _frame.CancelPrepared(_token);
    }

    internal interface IViewFactoryRegistration
    {
        object FactoryInstance { get; }

        object CreateWriteView(OperationSectionRegistry registry,
            IOperationSectionWriteTarget target, OperationSectionDescriptor descriptor);

        object Create(
            OperationSectionRegistry registry,
            OperationSectionReader reader,
            OperationSectionDescriptor descriptor);
    }

    internal sealed class ViewFactoryRegistration<TSection> : IViewFactoryRegistration
        where TSection : class, IOperationSection
    {
        private readonly IOperationSectionViewFactory<TSection> _factory;

        public ViewFactoryRegistration(IOperationSectionViewFactory<TSection> factory)
        {
            _factory = factory;
        }

        public object FactoryInstance => _factory;

        public object CreateWriteView(OperationSectionRegistry registry,
            IOperationSectionWriteTarget target, OperationSectionDescriptor descriptor)
        {
            Action enable = descriptor.Mode == OperationSectionMode.Discrete
                ? (Action)target.Enable<TSection> : null;
            return Create(registry, new OperationSectionReader(target, enable), descriptor);
        }

        public object Create(
            OperationSectionRegistry registry,
            OperationSectionReader reader,
            OperationSectionDescriptor descriptor)
        {
            var handle = new OperationSectionHandle<TSection>(
                registry,
                descriptor.SectionId,
                descriptor.DenseIndex);
            var context = new OperationSectionViewContext<TSection>(registry, reader, handle);
            return _factory.Create(context);
        }
    }

    internal static class OperationSectionContract
    {
        public static bool IsDefinedMode(OperationSectionMode mode) =>
            mode == OperationSectionMode.Continuous || mode == OperationSectionMode.Discrete;

        public static bool TryCreateShape(
            Type sectionType,
            out OperationSectionShape shape,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryValidateIdentity(sectionType, out diagnostic))
            {
                shape = null;
                return false;
            }

            if (!TryValidateMembers(sectionType, out PropertyInfo[] properties, out diagnostic))
            {
                shape = null;
                return false;
            }

            var fields = new OperationSectionFieldShape[properties.Length];
            int sectionSize = 0;
            for (int fieldIndex = 0; fieldIndex < properties.Length; fieldIndex++)
            {
                PropertyInfo property = properties[fieldIndex];
                if (!TryGetManagedSize(property.PropertyType, out int fieldSize))
                {
                    shape = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section field layout could not be determined.");
                    return false;
                }

                if (fieldSize <= 0 || sectionSize > int.MaxValue - fieldSize)
                {
                    shape = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidFrameLayout,
                        "Operation Section field storage exceeds the supported arena size.");
                    return false;
                }

                fields[fieldIndex] = new OperationSectionFieldShape(
                    fieldIndex,
                    property.Name,
                    property.PropertyType,
                    sectionSize,
                    fieldSize);
                sectionSize += fieldSize;
            }

            shape = new OperationSectionShape(sectionSize, fields);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public static bool TryValidateIdentity(
            Type sectionType,
            out CoCoDiagnostic diagnostic)
        {
            if (sectionType == null ||
                !sectionType.IsInterface ||
                sectionType == typeof(IOperationSection))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section must be a dedicated interface.");
                return false;
            }

            Type[] inherited = sectionType.GetInterfaces();
            if (inherited.Length != 1 || !IsOperationSectionMarker(inherited[0]))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section must inherit only IOperationSection directly.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool IsOperationSectionMarker(Type type)
        {
            Type marker = typeof(IOperationSection);
            return type == marker ||
                   (type != null &&
                    string.Equals(type.FullName, marker.FullName, StringComparison.Ordinal) &&
                    string.Equals(
                        type.Assembly.GetName().Name,
                        marker.Assembly.GetName().Name,
                        StringComparison.Ordinal));
        }

        public static bool TryValidateMembers(
            Type sectionType,
            out PropertyInfo[] properties,
            out CoCoDiagnostic diagnostic)
        {
            if (sectionType == null)
            {
                properties = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section type is required.");
                return false;
            }

            const BindingFlags declaredMembers = BindingFlags.Instance |
                                                 BindingFlags.Static |
                                                 BindingFlags.Public |
                                                 BindingFlags.NonPublic |
                                                 BindingFlags.DeclaredOnly;
            if (sectionType.GetEvents(declaredMembers).Length != 0 ||
                sectionType.GetFields(declaredMembers).Length != 0)
            {
                properties = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section cannot declare events or fields.");
                return false;
            }

            properties = sectionType.GetProperties(declaredMembers);
            if (properties.Length == 0)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section must declare at least one getter property.");
                return false;
            }

            Array.Sort(properties, PropertyComparer.Instance);
            var accessors = new HashSet<MethodInfo>();
            for (int index = 0; index < properties.Length; index++)
            {
                PropertyInfo property = properties[index];
                MethodInfo getter = property.GetGetMethod(true);
                if (getter == null ||
                    !getter.IsPublic ||
                    getter.IsStatic ||
                    !getter.IsAbstract ||
                    property.GetIndexParameters().Length != 0 ||
                    property.PropertyType.IsByRef ||
                    Nullable.GetUnderlyingType(property.PropertyType) != null ||
                    !StateFlowTypeRules.IsReferenceFreeValueType(property.PropertyType))
                {
                    properties = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section properties must be public abstract unmanaged properties.");
                    return false;
                }

                accessors.Add(getter);
                if (property.GetSetMethod(true) is MethodInfo setter) accessors.Add(setter);
            }

            MethodInfo[] methods = sectionType.GetMethods(declaredMembers);
            for (int index = 0; index < methods.Length; index++)
            {
                MethodInfo method = methods[index];
                bool dataMethod = method.IsPublic && method.IsAbstract && !method.IsStatic &&
                    method.ReturnType == typeof(void) &&
                    Array.TrueForAll(method.GetParameters(), parameter =>
                        StateFlowTypeRules.IsReferenceFreeValueType(parameter.ParameterType));
                if (!accessors.Contains(method) && !dataMethod)
                {
                    properties = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section methods must be void data operations.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public static bool TryGetManagedSize(Type valueType, out int size)
        {
            GCHandle pinned = default;
            try
            {
                if (valueType == null ||
                    !StateFlowTypeRules.IsReferenceFreeValueType(valueType))
                {
                    size = 0;
                    return false;
                }

                // The distance between adjacent elements is the CLR-managed size used by
                // MemoryMarshal during Tick reads and writes. This non-generic setup path avoids
                // constructing arbitrary closed generic methods, which is not AOT-safe.
                Array values = Array.CreateInstance(valueType, 2);
                pinned = GCHandle.Alloc(values, GCHandleType.Pinned);
                long first = Marshal.UnsafeAddrOfPinnedArrayElement(values, 0).ToInt64();
                long second = Marshal.UnsafeAddrOfPinnedArrayElement(values, 1).ToInt64();
                long stride = second - first;
                if (stride <= 0L || stride > int.MaxValue)
                {
                    size = 0;
                    return false;
                }

                size = (int)stride;
                return true;
            }
            catch (Exception)
            {
                size = 0;
                return false;
            }
            finally
            {
                if (pinned.IsAllocated)
                {
                    pinned.Free();
                }
            }
        }

        public static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Operation, code, message);

        private sealed class PropertyComparer : IComparer<PropertyInfo>
        {
            public static readonly PropertyComparer Instance = new PropertyComparer();

            public int Compare(PropertyInfo left, PropertyInfo right) =>
                string.CompareOrdinal(left.Name, right.Name);
        }
    }

    internal static class OperationSectionShapeFingerprint
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(
            int byteSize,
            IReadOnlyList<OperationSectionFieldShape> fields)
        {
            ulong hash = OffsetBasis;
            Add(ref hash, 1U);
            Add(ref hash, byteSize);
            Add(ref hash, fields.Count);
            for (int index = 0; index < fields.Count; index++)
            {
                OperationSectionFieldShape field = fields[index];
                Add(ref hash, field.DenseIndex);
                Add(ref hash, field.Name);
                Add(ref hash, field.ValueType);
                Add(ref hash, field.ByteOffset);
                Add(ref hash, field.ByteSize);
            }

            return hash == 0UL ? OffsetBasis : hash;
        }

        private static void Add(ref ulong hash, Type valueType)
        {
            if (valueType.IsGenericType)
            {
                Type definition = valueType.GetGenericTypeDefinition();
                Add(ref hash, GraphTypeIdentity.AssemblyName(definition));
                Add(ref hash, GraphTypeIdentity.FullName(definition));
                Type[] arguments = valueType.GetGenericArguments();
                Add(ref hash, arguments.Length);
                for (int index = 0; index < arguments.Length; index++)
                {
                    Add(ref hash, arguments[index]);
                }

                return;
            }

            Add(ref hash, GraphTypeIdentity.AssemblyName(valueType));
            Add(ref hash, GraphTypeIdentity.FullName(valueType));
        }

        private static void Add(ref ulong hash, string value)
        {
            if (value == null)
            {
                Add(ref hash, -1);
                return;
            }

            Add(ref hash, value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                AddByte(ref hash, (byte)character);
                AddByte(ref hash, (byte)(character >> 8));
            }
        }

        private static void Add(ref ulong hash, int value) =>
            Add(ref hash, unchecked((uint)value));

        private static void Add(ref ulong hash, uint value)
        {
            AddByte(ref hash, (byte)value);
            AddByte(ref hash, (byte)(value >> 8));
            AddByte(ref hash, (byte)(value >> 16));
            AddByte(ref hash, (byte)(value >> 24));
        }

        private static void AddByte(ref ulong hash, byte value)
        {
            hash ^= value;
            hash *= Prime;
        }
    }

    internal static class OperationSectionBinary
    {
        public static TValue Read<TValue>(byte[] storage, int offset, int size)
            where TValue : unmanaged
        {
            TValue value = default;
            Span<byte> destination = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateSpan(ref value, 1));
            new ReadOnlySpan<byte>(storage, offset, size).CopyTo(destination);
            return value;
        }

        public static void Write<TValue>(byte[] storage, int offset, int size, in TValue value)
            where TValue : unmanaged
        {
            TValue copy = value;
            ReadOnlySpan<byte> source = MemoryMarshal.AsBytes(
                MemoryMarshal.CreateReadOnlySpan(ref copy, 1));
            source.CopyTo(new Span<byte>(storage, offset, size));
        }
    }
}
