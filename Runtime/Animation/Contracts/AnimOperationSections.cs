using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Animation.Contracts
{
    public interface IAnimParameterOperationSection : ICoCoOperationSection
    {
        AnimParameterCommand Slot00 { get; }
        AnimParameterCommand Slot01 { get; }
        AnimParameterCommand Slot02 { get; }
        AnimParameterCommand Slot03 { get; }
        AnimParameterCommand Slot04 { get; }
        AnimParameterCommand Slot05 { get; }
        AnimParameterCommand Slot06 { get; }
        AnimParameterCommand Slot07 { get; }
        AnimParameterCommand Slot08 { get; }
        AnimParameterCommand Slot09 { get; }
        AnimParameterCommand Slot10 { get; }
        AnimParameterCommand Slot11 { get; }
        AnimParameterCommand Slot12 { get; }
        AnimParameterCommand Slot13 { get; }
        AnimParameterCommand Slot14 { get; }
        AnimParameterCommand Slot15 { get; }
    }

    public interface IAnimTriggerOperationSection : ICoCoOperationSection
    {
        AnimTriggerCommand Slot00 { get; }
        AnimTriggerCommand Slot01 { get; }
        AnimTriggerCommand Slot02 { get; }
        AnimTriggerCommand Slot03 { get; }
        AnimTriggerCommand Slot04 { get; }
        AnimTriggerCommand Slot05 { get; }
        AnimTriggerCommand Slot06 { get; }
        AnimTriggerCommand Slot07 { get; }
    }

    public sealed class AnimParameterOperationSectionView : IAnimParameterOperationSection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<AnimParameterCommand>[] _fields;

        internal AnimParameterOperationSectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<AnimParameterCommand>[] fields)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public AnimParameterCommand Slot00 => Read(0);
        public AnimParameterCommand Slot01 => Read(1);
        public AnimParameterCommand Slot02 => Read(2);
        public AnimParameterCommand Slot03 => Read(3);
        public AnimParameterCommand Slot04 => Read(4);
        public AnimParameterCommand Slot05 => Read(5);
        public AnimParameterCommand Slot06 => Read(6);
        public AnimParameterCommand Slot07 => Read(7);
        public AnimParameterCommand Slot08 => Read(8);
        public AnimParameterCommand Slot09 => Read(9);
        public AnimParameterCommand Slot10 => Read(10);
        public AnimParameterCommand Slot11 => Read(11);
        public AnimParameterCommand Slot12 => Read(12);
        public AnimParameterCommand Slot13 => Read(13);
        public AnimParameterCommand Slot14 => Read(14);
        public AnimParameterCommand Slot15 => Read(15);

        private AnimParameterCommand Read(int index) => _reader.Read(_fields[index]);
    }

    public sealed class AnimTriggerOperationSectionView : IAnimTriggerOperationSection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<AnimTriggerCommand>[] _fields;

        internal AnimTriggerOperationSectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<AnimTriggerCommand>[] fields)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _fields = fields ?? throw new ArgumentNullException(nameof(fields));
        }

        public AnimTriggerCommand Slot00 => Read(0);
        public AnimTriggerCommand Slot01 => Read(1);
        public AnimTriggerCommand Slot02 => Read(2);
        public AnimTriggerCommand Slot03 => Read(3);
        public AnimTriggerCommand Slot04 => Read(4);
        public AnimTriggerCommand Slot05 => Read(5);
        public AnimTriggerCommand Slot06 => Read(6);
        public AnimTriggerCommand Slot07 => Read(7);

        private AnimTriggerCommand Read(int index) => _reader.Read(_fields[index]);
    }

    public sealed class AnimParameterOperationSectionViewFactory :
        ICoCoOperationSectionViewFactory<IAnimParameterOperationSection>
    {
        private CoCoOperationSectionField<AnimParameterCommand>[] _fields;

        public CoCoOperationSectionHandle<IAnimParameterOperationSection> Handle { get; private set; }

        public IAnimParameterOperationSection Create(
            in CoCoOperationSectionViewContext<IAnimParameterOperationSection> context)
        {
            CoCoOperationSectionField<AnimParameterCommand>[] fields =
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
            out CoCoOperationSectionField<AnimParameterCommand> field)
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
        ICoCoOperationSectionViewFactory<IAnimTriggerOperationSection>
    {
        private CoCoOperationSectionField<AnimTriggerCommand>[] _fields;

        public CoCoOperationSectionHandle<IAnimTriggerOperationSection> Handle { get; private set; }

        public IAnimTriggerOperationSection Create(
            in CoCoOperationSectionViewContext<IAnimTriggerOperationSection> context)
        {
            CoCoOperationSectionField<AnimTriggerCommand>[] fields =
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
            out CoCoOperationSectionField<AnimTriggerCommand> field)
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
            out CoCoOperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic) =>
            CoCoOperationSectionRequirement.TryCreate<IAnimParameterOperationSection>(
                AnimContractIds.ParameterSectionId,
                CoCoOperationSectionMode.Continuous,
                out requirement,
                out diagnostic);

        public static bool TryCreateTriggerRequirement(
            out CoCoOperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic) =>
            CoCoOperationSectionRequirement.TryCreate<IAnimTriggerOperationSection>(
                AnimContractIds.TriggerSectionId,
                CoCoOperationSectionMode.Discrete,
                out requirement,
                out diagnostic);

    }

    internal static class AnimOperationFieldResolver
    {
        public static CoCoOperationSectionField<TValue>[] Resolve<TSection, TValue>(
            in CoCoOperationSectionViewContext<TSection> context,
            int fieldCount)
            where TSection : class, ICoCoOperationSection
            where TValue : unmanaged
        {
            if (!context.IsValid || fieldCount <= 0)
            {
                throw new InvalidOperationException(
                    "Animation Operation Section view context is invalid.");
            }

            var fields = new CoCoOperationSectionField<TValue>[fieldCount];
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
