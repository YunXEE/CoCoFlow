using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Marks a read-only data contract that a StateGraph can place in an OperationFrame.
    /// Section interfaces must inherit this interface directly and may only declare getter properties.
    /// </summary>
    public interface ICoCoOperationSection
    {
    }

    public enum CoCoOperationSectionMode
    {
        None = 0,
        Continuous = 1,
        Discrete = 2
    }

    public sealed class CoCoOperationSectionFieldShape :
        IEquatable<CoCoOperationSectionFieldShape>
    {
        internal CoCoOperationSectionFieldShape(
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

        public bool Equals(CoCoOperationSectionFieldShape other)
        {
            return other != null &&
                   DenseIndex == other.DenseIndex &&
                   string.Equals(Name, other.Name, StringComparison.Ordinal) &&
                   ValueType == other.ValueType &&
                   ByteOffset == other.ByteOffset &&
                   ByteSize == other.ByteSize;
        }

        public override bool Equals(object obj) =>
            obj is CoCoOperationSectionFieldShape other && Equals(other);

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
    public sealed class CoCoOperationSectionShape : IEquatable<CoCoOperationSectionShape>
    {
        private readonly CoCoOperationSectionFieldShape[] _fields;
        private readonly IReadOnlyList<CoCoOperationSectionFieldShape> _readOnlyFields;

        internal CoCoOperationSectionShape(
            int byteSize,
            CoCoOperationSectionFieldShape[] fields)
        {
            ByteSize = byteSize;
            _fields = (CoCoOperationSectionFieldShape[])fields.Clone();
            _readOnlyFields = Array.AsReadOnly(_fields);
            ShapeFingerprint = CoCoOperationSectionShapeFingerprint.Compute(byteSize, _fields);
        }

        public int ByteSize { get; }
        public int FieldCount => _fields.Length;
        public IReadOnlyList<CoCoOperationSectionFieldShape> Fields => _readOnlyFields;
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
            out CoCoOperationSectionShape shape,
            out CoCoDiagnostic diagnostic)
        {
            return CoCoOperationSectionContract.TryCreateShape(
                sectionType,
                out shape,
                out diagnostic);
        }

        public bool Equals(CoCoOperationSectionShape other)
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
            obj is CoCoOperationSectionShape other && Equals(other);

        public override int GetHashCode() => ShapeFingerprint.GetHashCode();
    }

    public readonly struct CoCoOperationSectionEntryHeader : IEquatable<CoCoOperationSectionEntryHeader>
    {
        private CoCoOperationSectionEntryHeader(
            bool enabled,
            CoCoActivationId activationId,
            CoCoOperationSequence operationSequence)
        {
            Enabled = enabled;
            ActivationId = activationId;
            OperationSequence = operationSequence;
        }

        public bool Enabled { get; }
        public CoCoActivationId ActivationId { get; }
        public CoCoOperationSequence OperationSequence { get; }

        internal static CoCoOperationSectionEntryHeader Continuous() =>
            new CoCoOperationSectionEntryHeader(true, default, default);

        internal static CoCoOperationSectionEntryHeader Discrete(
            CoCoActivationId activationId,
            CoCoOperationSequence operationSequence) =>
            new CoCoOperationSectionEntryHeader(true, activationId, operationSequence);

        public bool Equals(CoCoOperationSectionEntryHeader other)
        {
            return Enabled == other.Enabled &&
                   ActivationId == other.ActivationId &&
                   OperationSequence == other.OperationSequence;
        }

        public override bool Equals(object obj) =>
            obj is CoCoOperationSectionEntryHeader other && Equals(other);

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
            CoCoOperationSectionEntryHeader left,
            CoCoOperationSectionEntryHeader right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperationSectionEntryHeader left,
            CoCoOperationSectionEntryHeader right) => !left.Equals(right);
    }

    /// <summary>
    /// A setup-time declaration used by Operators and Graph compilation.
    /// Runtime code resolves it to a dense handle before Running begins.
    /// </summary>
    public readonly struct CoCoOperationSectionRequirement : IEquatable<CoCoOperationSectionRequirement>
    {
        private readonly Type _sectionType;
        private readonly CoCoOperationSectionShape _shape;

        private CoCoOperationSectionRequirement(
            CoCoOperationSectionId sectionId,
            CoCoOperationSectionMode mode,
            Type sectionType,
            CoCoOperationSectionShape shape)
        {
            SectionId = sectionId;
            Mode = mode;
            _sectionType = sectionType;
            _shape = shape;
        }

        public CoCoOperationSectionId SectionId { get; }
        public CoCoOperationSectionMode Mode { get; }
        public Type SectionType => _sectionType;
        public CoCoOperationSectionShape Shape => _shape;
        public bool IsValid => SectionId.IsValid &&
                               CoCoOperationSectionContract.IsDefinedMode(Mode) &&
                               _sectionType != null &&
                               _shape != null &&
                               _shape.IsValid;

        public static bool TryCreate<TSection>(
            CoCoOperationSectionId sectionId,
            CoCoOperationSectionMode mode,
            out CoCoOperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TSection : class, ICoCoOperationSection
        {
            if (!sectionId.IsValid)
            {
                requirement = default;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "Operation SectionId must be valid.");
                return false;
            }

            if (!CoCoOperationSectionContract.IsDefinedMode(mode))
            {
                requirement = default;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section mode must be Continuous or Discrete.");
                return false;
            }

            if (!CoCoOperationSectionShape.TryCreate(
                    typeof(TSection),
                    out CoCoOperationSectionShape shape,
                    out diagnostic))
            {
                requirement = default;
                return false;
            }

            requirement = new CoCoOperationSectionRequirement(
                sectionId,
                mode,
                typeof(TSection),
                shape);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool Equals(CoCoOperationSectionRequirement other)
        {
            return SectionId == other.SectionId &&
                   Mode == other.Mode &&
                   _sectionType == other._sectionType &&
                   Equals(_shape, other._shape);
        }

        public override bool Equals(object obj) =>
            obj is CoCoOperationSectionRequirement other && Equals(other);

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
            CoCoOperationSectionRequirement left,
            CoCoOperationSectionRequirement right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperationSectionRequirement left,
            CoCoOperationSectionRequirement right) => !left.Equals(right);
    }

    public readonly struct CoCoOperationSectionHandle<TSection> :
        IEquatable<CoCoOperationSectionHandle<TSection>>
        where TSection : class, ICoCoOperationSection
    {
        private readonly CoCoOperationSectionRegistry _owner;

        internal CoCoOperationSectionHandle(
            CoCoOperationSectionRegistry owner,
            CoCoOperationSectionId sectionId,
            int denseIndex)
        {
            _owner = owner;
            LayoutId = owner?.LayoutId ?? default;
            SectionId = sectionId;
            DenseIndex = denseIndex;
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public CoCoOperationSectionId SectionId { get; }
        public int DenseIndex { get; }
        public bool IsValid => _owner != null &&
                               LayoutId.IsValid &&
                               SectionId.IsValid &&
                               DenseIndex >= 0;

        internal bool IsOwnedBy(CoCoOperationSectionRegistry owner) => ReferenceEquals(_owner, owner);

        public bool Equals(CoCoOperationSectionHandle<TSection> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   SectionId == other.SectionId &&
                   DenseIndex == other.DenseIndex;
        }

        public override bool Equals(object obj) =>
            obj is CoCoOperationSectionHandle<TSection> other && Equals(other);

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
            CoCoOperationSectionHandle<TSection> left,
            CoCoOperationSectionHandle<TSection> right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperationSectionHandle<TSection> left,
            CoCoOperationSectionHandle<TSection> right) => !left.Equals(right);
    }

    /// <summary>
    /// A pre-resolved field address. It is intentionally free of Type and string metadata.
    /// </summary>
    public readonly struct CoCoOperationSectionField<TValue> :
        IEquatable<CoCoOperationSectionField<TValue>>
        where TValue : unmanaged
    {
        private readonly CoCoOperationSectionRegistry _owner;

        internal CoCoOperationSectionField(
            CoCoOperationSectionRegistry owner,
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

        public CoCoFrameLayoutId LayoutId { get; }
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

        internal bool IsOwnedBy(CoCoOperationSectionRegistry owner) => ReferenceEquals(_owner, owner);

        public bool Equals(CoCoOperationSectionField<TValue> other)
        {
            return ReferenceEquals(_owner, other._owner) &&
                   LayoutId == other.LayoutId &&
                   SectionIndex == other.SectionIndex &&
                   FieldIndex == other.FieldIndex &&
                   ByteOffset == other.ByteOffset &&
                   ByteSize == other.ByteSize;
        }

        public override bool Equals(object obj) =>
            obj is CoCoOperationSectionField<TValue> other && Equals(other);

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
            CoCoOperationSectionField<TValue> left,
            CoCoOperationSectionField<TValue> right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperationSectionField<TValue> left,
            CoCoOperationSectionField<TValue> right) => !left.Equals(right);
    }

    public sealed class CoCoOperationSectionFieldDescriptor
    {
        internal CoCoOperationSectionFieldDescriptor(
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

    public sealed class CoCoOperationSectionDescriptor
    {
        private readonly IReadOnlyList<CoCoOperationSectionFieldDescriptor> _readOnlyFields;

        internal CoCoOperationSectionDescriptor(
            CoCoOperationSectionId sectionId,
            CoCoOperationSectionMode mode,
            Type sectionType,
            CoCoOperationSectionShape shape,
            int denseIndex,
            int byteOffset,
            int byteSize,
            CoCoOperationSectionFieldDescriptor[] fields)
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

        public CoCoOperationSectionId SectionId { get; }
        public CoCoOperationSectionMode Mode { get; }
        public Type SectionType { get; }
        public CoCoOperationSectionShape Shape { get; }
        public int DenseIndex { get; }
        public int ByteOffset { get; }
        public int ByteSize { get; }
        public IReadOnlyList<CoCoOperationSectionFieldDescriptor> Fields => _readOnlyFields;
    }

    /// <summary>
    /// Creates a sealed section view during GraphInstance setup. Factories are never invoked per Tick.
    /// </summary>
    public interface ICoCoOperationSectionViewFactory<TSection>
        where TSection : class, ICoCoOperationSection
    {
        TSection Create(in CoCoOperationSectionViewContext<TSection> context);
    }

    public readonly struct CoCoOperationSectionViewContext<TSection>
        where TSection : class, ICoCoOperationSection
    {
        private readonly CoCoOperationSectionRegistry _registry;

        internal CoCoOperationSectionViewContext(
            CoCoOperationSectionRegistry registry,
            CoCoOperationSectionReader reader,
            CoCoOperationSectionHandle<TSection> handle)
        {
            _registry = registry;
            Reader = reader;
            Handle = handle;
        }

        public CoCoOperationSectionReader Reader { get; }
        public CoCoOperationSectionHandle<TSection> Handle { get; }
        public bool IsValid => _registry != null && Reader != null && Handle.IsValid;

        public bool TryGetField<TValue>(
            int denseFieldIndex,
            out CoCoOperationSectionField<TValue> field)
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

    /// <summary>
    /// Stable reader shared by every pre-created view owned by one OperationFrame.
    /// </summary>
    public sealed class CoCoOperationSectionReader
    {
        private readonly CoCoOperationFrame _frame;

        internal CoCoOperationSectionReader(CoCoOperationFrame frame)
        {
            _frame = frame;
        }

        public bool TryRead<TValue>(
            CoCoOperationSectionField<TValue> field,
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

        public TValue Read<TValue>(CoCoOperationSectionField<TValue> field)
            where TValue : unmanaged
        {
            return _frame != null && _frame.TryRead(field, out TValue value)
                ? value
                : default;
        }
    }

    public sealed class CoCoOperationSectionRegistryBuilder
    {
        private readonly Dictionary<Type, Registration> _byType = new Dictionary<Type, Registration>();
        private readonly Dictionary<CoCoOperationSectionId, Registration> _byId =
            new Dictionary<CoCoOperationSectionId, Registration>();
        private readonly List<Registration> _registrations = new List<Registration>();
        private bool _isFrozen;

        public bool IsFrozen => _isFrozen;
        public int Count => _registrations.Count;

        public bool TryRegister<TSection>(
            CoCoOperationSectionId sectionId,
            CoCoOperationSectionMode mode,
            ICoCoOperationSectionViewFactory<TSection> viewFactory,
            out CoCoOperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TSection : class, ICoCoOperationSection
        {
            if (_isFrozen)
            {
                requirement = default;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "Operation Section Registry is already frozen.");
                return false;
            }

            if (viewFactory == null)
            {
                requirement = default;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section read-view factory is required.");
                return false;
            }

            if (!CoCoOperationSectionRequirement.TryCreate<TSection>(
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
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "The same Operation Section interface cannot be registered with another identity, mode, or view factory instance.");
                return false;
            }

            if (_byId.ContainsKey(sectionId))
            {
                requirement = default;
                diagnostic = CoCoOperationSectionContract.Error(
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
            CoCoFrameLayoutId layoutId,
            out CoCoOperationSectionRegistry registry,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                registry = null;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "Operation Section Registry may only be frozen once.");
                return false;
            }

            if (!layoutId.IsValid)
            {
                registry = null;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "OperationFrame LayoutId must be valid.");
                return false;
            }

            Registration[] registrations = _registrations.ToArray();
            Array.Sort(registrations, RegistrationComparer.Instance);

            var descriptors = new CoCoOperationSectionDescriptor[registrations.Length];
            var factories = new IViewFactoryRegistration[registrations.Length];
            int layoutSize = 0;
            for (int sectionIndex = 0; sectionIndex < registrations.Length; sectionIndex++)
            {
                Registration registration = registrations[sectionIndex];
                CoCoOperationSectionShape shape = registration.Requirement.Shape;
                var fields = new CoCoOperationSectionFieldDescriptor[shape.FieldCount];
                for (int fieldIndex = 0; fieldIndex < shape.FieldCount; fieldIndex++)
                {
                    CoCoOperationSectionFieldShape field = shape.Fields[fieldIndex];
                    fields[fieldIndex] = new CoCoOperationSectionFieldDescriptor(
                        field.DenseIndex,
                        field.Name,
                        field.ValueType,
                        field.ByteOffset,
                        field.ByteSize);
                }

                if (layoutSize > int.MaxValue - shape.ByteSize)
                {
                    registry = null;
                    diagnostic = CoCoOperationSectionContract.Error(
                        CoCoDiagnosticCode.InvalidFrameLayout,
                        "OperationFrame storage exceeds the supported arena size.");
                    return false;
                }

                descriptors[sectionIndex] = new CoCoOperationSectionDescriptor(
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

            registry = new CoCoOperationSectionRegistry(layoutId, layoutSize, descriptors, factories);
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
                CoCoOperationSectionRequirement requirement,
                IViewFactoryRegistration factory)
            {
                Requirement = requirement;
                Factory = factory;
            }

            public CoCoOperationSectionRequirement Requirement { get; }
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

    public sealed class CoCoOperationSectionRegistry
    {
        private readonly CoCoOperationSectionDescriptor[] _descriptors;
        private readonly IReadOnlyList<CoCoOperationSectionDescriptor> _readOnlyDescriptors;
        private readonly IViewFactoryRegistration[] _factories;

        internal CoCoOperationSectionRegistry(
            CoCoFrameLayoutId layoutId,
            int byteSize,
            CoCoOperationSectionDescriptor[] descriptors,
            IViewFactoryRegistration[] factories)
        {
            LayoutId = layoutId;
            ByteSize = byteSize;
            _descriptors = descriptors;
            _readOnlyDescriptors = Array.AsReadOnly(descriptors);
            _factories = factories;
        }

        public CoCoFrameLayoutId LayoutId { get; }
        public int ByteSize { get; }
        public int Count => _descriptors.Length;
        public IReadOnlyList<CoCoOperationSectionDescriptor> Sections => _readOnlyDescriptors;
        public bool IsFrozen => LayoutId.IsValid;

        public bool TryResolve<TSection>(
            CoCoOperationSectionRequirement requirement,
            out CoCoOperationSectionHandle<TSection> handle)
            where TSection : class, ICoCoOperationSection
        {
            if (!requirement.IsValid ||
                requirement.SectionType != typeof(TSection) ||
                !TryFind(requirement, out int sectionIndex))
            {
                handle = default;
                return false;
            }

            CoCoOperationSectionDescriptor descriptor = _descriptors[sectionIndex];
            handle = new CoCoOperationSectionHandle<TSection>(
                this,
                descriptor.SectionId,
                sectionIndex);
            return true;
        }

        public bool TryResolveField<TSection, TValue>(
            CoCoOperationSectionHandle<TSection> handle,
            int denseFieldIndex,
            out CoCoOperationSectionField<TValue> field)
            where TSection : class, ICoCoOperationSection
            where TValue : unmanaged
        {
            if (!ValidateHandle(handle) ||
                denseFieldIndex < 0 ||
                denseFieldIndex >= _descriptors[handle.DenseIndex].Fields.Count)
            {
                field = default;
                return false;
            }

            CoCoOperationSectionDescriptor section = _descriptors[handle.DenseIndex];
            CoCoOperationSectionFieldDescriptor descriptor = section.Fields[denseFieldIndex];
            if (descriptor.ValueType != typeof(TValue))
            {
                field = default;
                return false;
            }

            field = new CoCoOperationSectionField<TValue>(
                this,
                handle.DenseIndex,
                denseFieldIndex,
                section.ByteOffset + descriptor.ByteOffset,
                descriptor.ByteSize);
            return true;
        }

        public bool TryValidateProvides(
            IReadOnlyList<CoCoOperationSectionRequirement> graphProvides,
            out CoCoDiagnostic diagnostic)
        {
            if (graphProvides == null)
            {
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.MissingOperationSection,
                    "Graph Operation Provides must be declared.");
                return false;
            }

            if (graphProvides.Count != _descriptors.Length)
            {
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.MissingOperationSection,
                    "Graph Operation Provides must cover every Section in the OperationFrame layout.");
                return false;
            }

            for (int index = 0; index < graphProvides.Count; index++)
            {
                CoCoOperationSectionRequirement current = graphProvides[index];
                if (!current.IsValid || !TryFind(current, out _))
                {
                    diagnostic = CoCoOperationSectionContract.Error(
                        CoCoDiagnosticCode.MissingOperationSection,
                        "Graph Operation Provides contains an unregistered Section.");
                    return false;
                }

                for (int previous = 0; previous < index; previous++)
                {
                    if (graphProvides[previous] == current)
                    {
                        diagnostic = CoCoOperationSectionContract.Error(
                            CoCoDiagnosticCode.DuplicateIdentifier,
                            "Graph Operation Provides contains the same Section more than once.");
                        return false;
                    }
                }
            }

            for (int sectionIndex = 0; sectionIndex < _descriptors.Length; sectionIndex++)
            {
                CoCoOperationSectionDescriptor descriptor = _descriptors[sectionIndex];
                bool covered = false;
                for (int provideIndex = 0; provideIndex < graphProvides.Count; provideIndex++)
                {
                    CoCoOperationSectionRequirement provided = graphProvides[provideIndex];
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
                    diagnostic = CoCoOperationSectionContract.Error(
                        CoCoDiagnosticCode.MissingOperationSection,
                        "Graph Operation Provides does not cover the complete OperationFrame layout.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool ValidateHandle<TSection>(CoCoOperationSectionHandle<TSection> handle)
            where TSection : class, ICoCoOperationSection
        {
            return handle.IsValid &&
                   handle.IsOwnedBy(this) &&
                   handle.LayoutId == LayoutId &&
                   handle.DenseIndex < _descriptors.Length &&
                   _descriptors[handle.DenseIndex].SectionId == handle.SectionId &&
                   _descriptors[handle.DenseIndex].SectionType == typeof(TSection);
        }

        internal bool ValidateField<TValue>(CoCoOperationSectionField<TValue> field)
            where TValue : unmanaged
        {
            if (!field.IsValid ||
                !field.IsOwnedBy(this) ||
                field.LayoutId != LayoutId ||
                field.SectionIndex >= _descriptors.Length)
            {
                return false;
            }

            CoCoOperationSectionDescriptor section = _descriptors[field.SectionIndex];
            if (field.FieldIndex >= section.Fields.Count)
            {
                return false;
            }

            CoCoOperationSectionFieldDescriptor descriptor = section.Fields[field.FieldIndex];
            return field.ByteOffset == section.ByteOffset + descriptor.ByteOffset &&
                   field.ByteSize == descriptor.ByteSize &&
                   descriptor.ValueType == typeof(TValue);
        }

        internal object[] CreateViews(CoCoOperationFrame frame)
        {
            var views = new object[_descriptors.Length];
            var reader = new CoCoOperationSectionReader(frame);
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

        internal CoCoOperationSectionMode GetMode(int sectionIndex) => _descriptors[sectionIndex].Mode;

        private bool TryFind(CoCoOperationSectionRequirement requirement, out int sectionIndex)
        {
            for (int index = 0; index < _descriptors.Length; index++)
            {
                CoCoOperationSectionDescriptor descriptor = _descriptors[index];
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
            CoCoOperationSectionShape provided,
            CoCoOperationSectionShape registered)
        {
            return provided != null &&
                   registered != null &&
                   provided.ShapeFingerprint == registered.ShapeFingerprint &&
                   provided.Equals(registered);
        }

        internal bool TryPrewarmViews(out CoCoDiagnostic diagnostic)
        {
            var reader = new CoCoOperationSectionReader(null);
            for (int index = 0; index < _descriptors.Length; index++)
            {
                object view;
                try
                {
                    view = _factories[index].Create(this, reader, _descriptors[index]);
                }
                catch (Exception)
                {
                    diagnostic = CoCoOperationSectionContract.Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section read-view factory failed during prewarm.");
                    return false;
                }

                if (view == null || !view.GetType().IsSealed)
                {
                    diagnostic = CoCoOperationSectionContract.Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section read-view factory must return a sealed view.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public readonly struct CoCoOperationSectionEntry<TSection>
        where TSection : class, ICoCoOperationSection
    {
        internal CoCoOperationSectionEntry(
            CoCoOperationSectionEntryHeader header,
            TSection view)
        {
            Header = header;
            View = view;
        }

        public CoCoOperationSectionEntryHeader Header { get; }
        public TSection View { get; }
    }

    /// <summary>
    /// Read-only OperationFrame surface consumed by Operators.
    /// Frame construction and mutation remain runtime-owned concerns on the concrete frame and its writer.
    /// </summary>
    public interface ICoCoOperationFrame
    {
        CoCoStateFlowFrameHeader Header { get; }
        CoCoOperationSectionRegistry Registry { get; }
        bool IsSealed { get; }

        bool TryGet<TSection>(
            CoCoOperationSectionHandle<TSection> handle,
            out CoCoOperationSectionEntry<TSection> entry)
            where TSection : class, ICoCoOperationSection;
    }

    /// <summary>
    /// A fixed-layout, GraphInstance-owned execution guide. Begin, write and seal all reuse preallocated storage.
    /// </summary>
    public sealed class CoCoOperationFrame : ICoCoOperationFrame
    {
        private readonly CoCoOperationSectionRegistry _registry;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly byte[] _storage;
        private readonly CoCoOperationSectionEntryHeader[] _entryHeaders;
        private readonly ulong[] _committedSequences;
        private readonly ulong[] _pendingSequences;
        private readonly object[] _views;
        private CoCoTimelineEpoch _committedSequenceEpoch;
        private CoCoTimelineEpoch _pendingSequenceEpoch;
        private CoCoTickFrame _lastSealedTickFrame;
        private CoCoStateFlowFrameHeader _header;
        private ulong _writeToken;
        private bool _hasCommittedSequenceEpoch;
        private bool _hasSealedTickFrame;
        private bool _pendingSequenceEpochIsNew;
        private bool _isWriting;
        private bool _isSealed;

        private CoCoOperationFrame(
            CoCoOperationSectionRegistry registry,
            CoCoGraphInstanceId graphInstanceId)
        {
            _registry = registry;
            _graphInstanceId = graphInstanceId;
            _storage = new byte[registry.ByteSize];
            _entryHeaders = new CoCoOperationSectionEntryHeader[registry.Count];
            _committedSequences = new ulong[registry.Count];
            _pendingSequences = new ulong[registry.Count];
            _views = registry.CreateViews(this);
        }

        public CoCoStateFlowFrameHeader Header => _header;
        public CoCoOperationSectionRegistry Registry => _registry;
        public bool IsSealed => _isSealed;

        public static bool TryCreate(
            CoCoOperationSectionRegistry registry,
            CoCoGraphInstanceId graphInstanceId,
            IReadOnlyList<CoCoOperationSectionRequirement> graphProvides,
            out CoCoOperationFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (registry == null || !registry.IsFrozen)
            {
                frame = null;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "OperationFrame requires a frozen Section Registry.");
                return false;
            }

            if (!graphInstanceId.IsValid)
            {
                frame = null;
                diagnostic = CoCoOperationSectionContract.Error(
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
                frame = new CoCoOperationFrame(registry, graphInstanceId);
            }
            catch (Exception)
            {
                frame = null;
                diagnostic = CoCoOperationSectionContract.Error(
                    CoCoDiagnosticCode.InvalidOperationSection,
                    "Operation Section view creation failed during GraphInstance setup.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryBegin(CoCoTickFrame tickFrame, out CoCoOperationFrameWriter writer)
        {
            if (_isWriting ||
                (_hasSealedTickFrame &&
                 !CoCoStateFlowTickOrder.IsStrictlyAfter(
                     tickFrame,
                     _lastSealedTickFrame)) ||
                !CoCoStateFlowFrameHeader.TryCreate(
                    _graphInstanceId,
                    _registry.LayoutId,
                    CoCoStateFlowFrameKind.Operation,
                    tickFrame,
                    out CoCoStateFlowFrameHeader header))
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
            for (int index = 0; index < _entryHeaders.Length; index++)
            {
                _entryHeaders[index] = _registry.GetMode(index) == CoCoOperationSectionMode.Continuous
                    ? CoCoOperationSectionEntryHeader.Continuous()
                    : default;
            }

            _header = header;
            _isWriting = true;
            _isSealed = false;
            _writeToken = _writeToken == ulong.MaxValue ? 1UL : _writeToken + 1UL;
            writer = new CoCoOperationFrameWriter(this, _writeToken);
            return true;
        }

        public bool TryGet<TSection>(
            CoCoOperationSectionHandle<TSection> handle,
            out CoCoOperationSectionEntry<TSection> entry)
            where TSection : class, ICoCoOperationSection
        {
            if (!_isSealed || !_registry.ValidateHandle(handle))
            {
                entry = default;
                return false;
            }

            entry = new CoCoOperationSectionEntry<TSection>(
                _entryHeaders[handle.DenseIndex],
                (TSection)_views[handle.DenseIndex]);
            return true;
        }

        internal bool TryRead<TValue>(
            CoCoOperationSectionField<TValue> field,
            out TValue value)
            where TValue : unmanaged
        {
            if (!_isSealed || !_registry.ValidateField(field))
            {
                value = default;
                return false;
            }

            value = CoCoOperationSectionBinary.Read<TValue>(
                _storage,
                field.ByteOffset,
                field.ByteSize);
            return true;
        }

        internal bool TryWrite<TValue>(
            ulong token,
            CoCoOperationSectionField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            if (!CanWrite(token) || !_registry.ValidateField(field))
            {
                return false;
            }

            CoCoOperationSectionBinary.Write(_storage, field.ByteOffset, field.ByteSize, value);
            return true;
        }

        internal bool TryEnableDiscrete<TSection>(
            ulong token,
            CoCoOperationSectionHandle<TSection> handle,
            CoCoActivationId activationId,
            out CoCoOperationSequence sequence)
            where TSection : class, ICoCoOperationSection
        {
            if (!CanWrite(token) ||
                !activationId.IsValid ||
                !_registry.ValidateHandle(handle) ||
                _registry.GetMode(handle.DenseIndex) != CoCoOperationSectionMode.Discrete ||
                _entryHeaders[handle.DenseIndex].Enabled ||
                _pendingSequences[handle.DenseIndex] == ulong.MaxValue)
            {
                sequence = default;
                return false;
            }

            ulong next = _pendingSequences[handle.DenseIndex] + 1UL;
            if (!CoCoOperationSequence.TryCreate(next, out sequence))
            {
                return false;
            }

            _pendingSequences[handle.DenseIndex] = next;
            _entryHeaders[handle.DenseIndex] = CoCoOperationSectionEntryHeader.Discrete(
                activationId,
                sequence);
            return true;
        }

        internal bool Seal(ulong token)
        {
            if (!CanWrite(token))
            {
                return false;
            }

            CommitPendingSequenceEpoch();
            _lastSealedTickFrame = _header.TickFrame;
            _hasSealedTickFrame = true;
            _isWriting = false;
            _isSealed = true;
            return true;
        }

        internal bool Cancel(ulong token)
        {
            if (!CanWrite(token))
            {
                return false;
            }

            _isWriting = false;
            _isSealed = false;
            _header = default;
            _pendingSequenceEpoch = default;
            _pendingSequenceEpochIsNew = false;
            return true;
        }

        private bool TryPrepareSequenceEpoch(CoCoTimelineEpoch epoch)
        {
            if (_hasCommittedSequenceEpoch && epoch == _committedSequenceEpoch)
            {
                Array.Copy(_committedSequences, _pendingSequences, _committedSequences.Length);
                _pendingSequenceEpoch = epoch;
                _pendingSequenceEpochIsNew = false;
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
            _pendingSequenceEpochIsNew = true;
            return true;
        }

        private void CommitPendingSequenceEpoch()
        {
            if (_pendingSequenceEpochIsNew)
            {
                _committedSequenceEpoch = _pendingSequenceEpoch;
                _hasCommittedSequenceEpoch = true;
            }

            Array.Copy(_pendingSequences, _committedSequences, _pendingSequences.Length);
            _pendingSequenceEpoch = default;
            _pendingSequenceEpochIsNew = false;
        }

        private bool CanWrite(ulong token) => _isWriting && token != 0UL && token == _writeToken;
    }

    public readonly struct CoCoOperationFrameWriter
    {
        private readonly CoCoOperationFrame _frame;
        private readonly ulong _token;

        internal CoCoOperationFrameWriter(CoCoOperationFrame frame, ulong token)
        {
            _frame = frame;
            _token = token;
        }

        public bool IsValid => _frame != null && _token != 0UL;

        public bool Write<TValue>(
            CoCoOperationSectionField<TValue> field,
            in TValue value)
            where TValue : unmanaged
        {
            return _frame != null && _frame.TryWrite(_token, field, value);
        }

        public bool EnableDiscrete<TSection>(
            CoCoOperationSectionHandle<TSection> handle,
            CoCoActivationId activationId,
            out CoCoOperationSequence sequence)
            where TSection : class, ICoCoOperationSection
        {
            if (_frame == null)
            {
                sequence = default;
                return false;
            }

            return _frame.TryEnableDiscrete(_token, handle, activationId, out sequence);
        }

        public bool Seal() => _frame != null && _frame.Seal(_token);
        public bool Cancel() => _frame != null && _frame.Cancel(_token);
    }

    internal interface IViewFactoryRegistration
    {
        object FactoryInstance { get; }

        object Create(
            CoCoOperationSectionRegistry registry,
            CoCoOperationSectionReader reader,
            CoCoOperationSectionDescriptor descriptor);
    }

    internal sealed class ViewFactoryRegistration<TSection> : IViewFactoryRegistration
        where TSection : class, ICoCoOperationSection
    {
        private readonly ICoCoOperationSectionViewFactory<TSection> _factory;

        public ViewFactoryRegistration(ICoCoOperationSectionViewFactory<TSection> factory)
        {
            _factory = factory;
        }

        public object FactoryInstance => _factory;

        public object Create(
            CoCoOperationSectionRegistry registry,
            CoCoOperationSectionReader reader,
            CoCoOperationSectionDescriptor descriptor)
        {
            var handle = new CoCoOperationSectionHandle<TSection>(
                registry,
                descriptor.SectionId,
                descriptor.DenseIndex);
            var context = new CoCoOperationSectionViewContext<TSection>(registry, reader, handle);
            return _factory.Create(context);
        }
    }

    internal static class CoCoOperationSectionContract
    {
        public static bool IsDefinedMode(CoCoOperationSectionMode mode) =>
            mode == CoCoOperationSectionMode.Continuous || mode == CoCoOperationSectionMode.Discrete;

        public static bool TryCreateShape(
            Type sectionType,
            out CoCoOperationSectionShape shape,
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

            var fields = new CoCoOperationSectionFieldShape[properties.Length];
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

                fields[fieldIndex] = new CoCoOperationSectionFieldShape(
                    fieldIndex,
                    property.Name,
                    property.PropertyType,
                    sectionSize,
                    fieldSize);
                sectionSize += fieldSize;
            }

            shape = new CoCoOperationSectionShape(sectionSize, fields);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public static bool TryValidateIdentity(
            Type sectionType,
            out CoCoDiagnostic diagnostic)
        {
            if (sectionType == null ||
                !sectionType.IsInterface ||
                sectionType == typeof(ICoCoOperationSection))
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
                    "Operation Section must inherit only ICoCoOperationSection directly.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static bool IsOperationSectionMarker(Type type)
        {
            Type marker = typeof(ICoCoOperationSection);
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
            var getters = new HashSet<MethodInfo>();
            for (int index = 0; index < properties.Length; index++)
            {
                PropertyInfo property = properties[index];
                MethodInfo getter = property.GetGetMethod(true);
                if (getter == null ||
                    !getter.IsPublic ||
                    getter.IsStatic ||
                    !getter.IsAbstract ||
                    property.GetSetMethod(true) != null ||
                    property.GetIndexParameters().Length != 0 ||
                    property.PropertyType.IsByRef ||
                    Nullable.GetUnderlyingType(property.PropertyType) != null ||
                    !CoCoStateFlowTypeRules.IsReferenceFreeValueType(property.PropertyType))
                {
                    properties = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section properties must be public abstract getter-only unmanaged values.");
                    return false;
                }

                getters.Add(getter);
            }

            MethodInfo[] methods = sectionType.GetMethods(declaredMembers);
            for (int index = 0; index < methods.Length; index++)
            {
                if (!getters.Contains(methods[index]))
                {
                    properties = null;
                    diagnostic = Error(
                        CoCoDiagnosticCode.InvalidOperationSection,
                        "Operation Section cannot declare methods beyond property getters.");
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
                    !CoCoStateFlowTypeRules.IsReferenceFreeValueType(valueType))
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

    internal static class CoCoOperationSectionShapeFingerprint
    {
        private const ulong OffsetBasis = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Compute(
            int byteSize,
            IReadOnlyList<CoCoOperationSectionFieldShape> fields)
        {
            ulong hash = OffsetBasis;
            Add(ref hash, 1U);
            Add(ref hash, byteSize);
            Add(ref hash, fields.Count);
            for (int index = 0; index < fields.Count; index++)
            {
                CoCoOperationSectionFieldShape field = fields[index];
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
                Add(ref hash, definition.Assembly.GetName().Name);
                Add(ref hash, definition.FullName);
                Type[] arguments = valueType.GetGenericArguments();
                Add(ref hash, arguments.Length);
                for (int index = 0; index < arguments.Length; index++)
                {
                    Add(ref hash, arguments[index]);
                }

                return;
            }

            Add(ref hash, valueType.Assembly.GetName().Name);
            Add(ref hash, valueType.FullName);
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

    internal static class CoCoOperationSectionBinary
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
