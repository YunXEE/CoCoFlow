using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.Animation.Contracts
{
    [OperationSection(OperationSectionMode.Continuous)]
    public interface IAnimParameterOperationSection : IOperationSection
    {
        AnimParameterCommand Slot00 { get; set; }
        AnimParameterCommand Slot01 { get; set; }
        AnimParameterCommand Slot02 { get; set; }
        AnimParameterCommand Slot03 { get; set; }
        AnimParameterCommand Slot04 { get; set; }
        AnimParameterCommand Slot05 { get; set; }
        AnimParameterCommand Slot06 { get; set; }
        AnimParameterCommand Slot07 { get; set; }
        AnimParameterCommand Slot08 { get; set; }
        AnimParameterCommand Slot09 { get; set; }
        AnimParameterCommand Slot10 { get; set; }
        AnimParameterCommand Slot11 { get; set; }
        AnimParameterCommand Slot12 { get; set; }
        AnimParameterCommand Slot13 { get; set; }
        AnimParameterCommand Slot14 { get; set; }
        AnimParameterCommand Slot15 { get; set; }
    }

    [OperationSection(OperationSectionMode.Discrete)]
    public interface IAnimTriggerOperationSection : IOperationSection
    {
        AnimTriggerCommand Slot00 { get; set; }
        AnimTriggerCommand Slot01 { get; set; }
        AnimTriggerCommand Slot02 { get; set; }
        AnimTriggerCommand Slot03 { get; set; }
        AnimTriggerCommand Slot04 { get; set; }
        AnimTriggerCommand Slot05 { get; set; }
        AnimTriggerCommand Slot06 { get; set; }
        AnimTriggerCommand Slot07 { get; set; }
    }

    public sealed class AnimParameterOperationSectionView : IAnimParameterOperationSection
    {
        private readonly OperationSectionReader _reader;
        private readonly OperationSectionField<AnimParameterCommand>[] _fields;

        internal AnimParameterOperationSectionView(
            OperationSectionReader reader,
            OperationSectionField<AnimParameterCommand>[] fields)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public AnimParameterCommand Slot00 { get => Read(0); set => _reader.Write(_fields[0], value); }
        public AnimParameterCommand Slot01 { get => Read(1); set => _reader.Write(_fields[1], value); }
        public AnimParameterCommand Slot02 { get => Read(2); set => _reader.Write(_fields[2], value); }
        public AnimParameterCommand Slot03 { get => Read(3); set => _reader.Write(_fields[3], value); }
        public AnimParameterCommand Slot04 { get => Read(4); set => _reader.Write(_fields[4], value); }
        public AnimParameterCommand Slot05 { get => Read(5); set => _reader.Write(_fields[5], value); }
        public AnimParameterCommand Slot06 { get => Read(6); set => _reader.Write(_fields[6], value); }
        public AnimParameterCommand Slot07 { get => Read(7); set => _reader.Write(_fields[7], value); }
        public AnimParameterCommand Slot08 { get => Read(8); set => _reader.Write(_fields[8], value); }
        public AnimParameterCommand Slot09 { get => Read(9); set => _reader.Write(_fields[9], value); }
        public AnimParameterCommand Slot10 { get => Read(10); set => _reader.Write(_fields[10], value); }
        public AnimParameterCommand Slot11 { get => Read(11); set => _reader.Write(_fields[11], value); }
        public AnimParameterCommand Slot12 { get => Read(12); set => _reader.Write(_fields[12], value); }
        public AnimParameterCommand Slot13 { get => Read(13); set => _reader.Write(_fields[13], value); }
        public AnimParameterCommand Slot14 { get => Read(14); set => _reader.Write(_fields[14], value); }
        public AnimParameterCommand Slot15 { get => Read(15); set => _reader.Write(_fields[15], value); }

        private AnimParameterCommand Read(int index) => _reader.Read(_fields[index]);
    }

    public sealed class AnimTriggerOperationSectionView : IAnimTriggerOperationSection
    {
        private readonly OperationSectionReader _reader;
        private readonly OperationSectionField<AnimTriggerCommand>[] _fields;

        internal AnimTriggerOperationSectionView(
            OperationSectionReader reader,
            OperationSectionField<AnimTriggerCommand>[] fields)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public AnimTriggerCommand Slot00 { get => Read(0); set => _reader.Write(_fields[0], value); }
        public AnimTriggerCommand Slot01 { get => Read(1); set => _reader.Write(_fields[1], value); }
        public AnimTriggerCommand Slot02 { get => Read(2); set => _reader.Write(_fields[2], value); }
        public AnimTriggerCommand Slot03 { get => Read(3); set => _reader.Write(_fields[3], value); }
        public AnimTriggerCommand Slot04 { get => Read(4); set => _reader.Write(_fields[4], value); }
        public AnimTriggerCommand Slot05 { get => Read(5); set => _reader.Write(_fields[5], value); }
        public AnimTriggerCommand Slot06 { get => Read(6); set => _reader.Write(_fields[6], value); }
        public AnimTriggerCommand Slot07 { get => Read(7); set => _reader.Write(_fields[7], value); }

        private AnimTriggerCommand Read(int index) => _reader.Read(_fields[index]);
    }

    public sealed class AnimParameterOperationSectionViewFactory :
        IOperationSectionViewFactory<IAnimParameterOperationSection>
    {
        private OperationSectionField<AnimParameterCommand>[] _fields;

        public OperationSectionHandle<IAnimParameterOperationSection> Handle { get; private set; }

        public IAnimParameterOperationSection Create(
            in OperationSectionViewContext<IAnimParameterOperationSection> context)
        {
            OperationSectionField<AnimParameterCommand>[] fields =
                AnimOperationFieldResolver.Resolve<
                    IAnimParameterOperationSection,
                    AnimParameterCommand>(
                    context,
                    AnimContractLimits.ParameterLaneCount);
            Handle = context.Handle;
            _fields = fields;
            return new AnimParameterOperationSectionView(context.Reader, fields);
        }

        public bool TryGetField(
            int lane,
            out OperationSectionField<AnimParameterCommand> field)
        {
            if (_fields == null || lane < 0 || lane >= _fields.Length)
            {
                field = default;
                return false;
            }

            field = _fields[lane];
            return field.IsValid;
        }
    }

    public sealed class AnimTriggerOperationSectionViewFactory :
        IOperationSectionViewFactory<IAnimTriggerOperationSection>
    {
        private OperationSectionField<AnimTriggerCommand>[] _fields;

        public OperationSectionHandle<IAnimTriggerOperationSection> Handle { get; private set; }

        public IAnimTriggerOperationSection Create(
            in OperationSectionViewContext<IAnimTriggerOperationSection> context)
        {
            OperationSectionField<AnimTriggerCommand>[] fields =
                AnimOperationFieldResolver.Resolve<
                    IAnimTriggerOperationSection,
                    AnimTriggerCommand>(
                    context,
                    AnimContractLimits.TriggerLaneCount);
            Handle = context.Handle;
            _fields = fields;
            return new AnimTriggerOperationSectionView(context.Reader, fields);
        }

        public bool TryGetField(
            int lane,
            out OperationSectionField<AnimTriggerCommand> field)
        {
            if (_fields == null || lane < 0 || lane >= _fields.Length)
            {
                field = default;
                return false;
            }

            field = _fields[lane];
            return field.IsValid;
        }
    }

    /// <summary>
    /// Per-project factory instances. The same instances must be supplied to project binding.
    /// </summary>
    public sealed class AnimOperationSchema
    {
        public AnimOperationSchema()
        {
            Parameters = new AnimParameterOperationSectionViewFactory();
            Triggers = new AnimTriggerOperationSectionViewFactory();
        }

        public AnimParameterOperationSectionViewFactory Parameters { get; }
        public AnimTriggerOperationSectionViewFactory Triggers { get; }

        public static bool TryCreateParameterRequirement(
            out OperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic) =>
            OperationSectionRequirement.TryCreate<IAnimParameterOperationSection>(
                AnimContractIds.ParameterSectionId,
                OperationSectionMode.Continuous,
                out requirement,
                out diagnostic);

        public static bool TryCreateTriggerRequirement(
            out OperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic) =>
            OperationSectionRequirement.TryCreate<IAnimTriggerOperationSection>(
                AnimContractIds.TriggerSectionId,
                OperationSectionMode.Discrete,
                out requirement,
                out diagnostic);

    }

    internal static class AnimOperationFieldResolver
    {
        public static OperationSectionField<TValue>[] Resolve<TSection, TValue>(
            in OperationSectionViewContext<TSection> context,
            int fieldCount)
            where TSection : class, IOperationSection
            where TValue : unmanaged
        {
            if (!context.IsValid || fieldCount <= 0)
            {
                throw new InvalidOperationException(
                    "Animation Operation Section view context is invalid.");
            }

            var fields = new OperationSectionField<TValue>[fieldCount];
            for (int index = 0; index < fields.Length; index++)
            {
                if (!context.TryGetField(index, out fields[index]))
                {
                    throw new InvalidOperationException(
                        "Animation Operation Section field could not be pre-resolved.");
                }
            }

            return fields;
        }
    }
}
