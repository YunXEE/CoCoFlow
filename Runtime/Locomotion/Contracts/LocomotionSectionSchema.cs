using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Locomotion.Contracts
{
    /// <summary>
    /// Read view over the locomotion section fields.
    /// </summary>
    public sealed class LocomotionSectionView : ILocomotionSection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<float>[] _floats;
        private readonly CoCoOperationSectionField<bool>[] _bools;

        internal LocomotionSectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<float>[] floats,
            CoCoOperationSectionField<bool>[] bools)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _floats = floats ?? throw new ArgumentNullException(nameof(floats));
            _bools = bools ?? throw new ArgumentNullException(nameof(bools));
        }

        private float F(int index) => _reader.Read(_floats[index]);
        private bool B(int index) => _reader.Read(_bools[index]);

        public float MoveX => F(0);
        public float MoveZ => F(1);
        public float ForcedX => F(2);
        public float ForcedZ => F(3);
        public bool UseGravity => B(0);
        public float GravityScale => F(4);
        public bool JumpRequested => B(1);
        public bool LaunchForced => B(2);
        public float VerticalImpulse => F(5);
        public float LookX => F(6);
        public float LookZ => F(7);
        public bool InstantRotation => B(3);
        public bool TeleportRequested => B(4);
        public float TeleportX => F(8);
        public float TeleportY => F(9);
        public float TeleportZ => F(10);
        public float TeleportRotationX => F(11);
        public float TeleportRotationY => F(12);
        public float TeleportRotationZ => F(13);
        public float TeleportRotationW => F(14);
    }

    /// <summary>
    /// View factory for the locomotion section (one instance per project
    /// binding; same shape as the animation section factories).
    /// </summary>
    public sealed class LocomotionSectionViewFactory :
        ICoCoOperationSectionViewFactory<ILocomotionSection>
    {
        private CoCoOperationSectionField<float>[] _floats;
        private CoCoOperationSectionField<bool>[] _bools;

        public ILocomotionSection Create(
            in CoCoOperationSectionViewContext<ILocomotionSection> context)
        {
            const int floatCount = 15;
            const int boolCount = 5;
            _floats = ResolveFields<float>(context, floatCount);
            _bools = ResolveFields<bool>(context, boolCount);
            return new LocomotionSectionView(
                context.Reader,
                _floats,
                _bools);
        }

        private static CoCoOperationSectionField<TValue>[] ResolveFields<TValue>(
            in CoCoOperationSectionViewContext<ILocomotionSection> context,
            int fieldCount)
            where TValue : unmanaged
        {
            var fields = new CoCoOperationSectionField<TValue>[fieldCount];
            for (int index = 0; index < fieldCount; index++)
            {
                if (!context.TryGetField(index, out fields[index]))
                {
                    throw new InvalidOperationException(
                        "Locomotion Section field could not be pre-resolved.");
                }
            }

            return fields;
        }
    }

    /// <summary>
    /// Per-project locomotion schema: section registration and requirement
    /// helpers (AnimOperationSchema shape).
    /// </summary>
    public sealed class LocomotionSectionSchema
    {
        public LocomotionSectionSchema()
        {
            Section = new LocomotionSectionViewFactory();
        }

        public LocomotionSectionViewFactory Section { get; }

        public static CoCoStateSlotId StateSlot => LocoContractIds.StateSlotId;

        public static bool TryCreateSectionRequirement(
            out CoCoOperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic) =>
            CoCoOperationSectionRequirement.TryCreate<ILocomotionSection>(
                LocoContractIds.SectionId,
                CoCoOperationSectionMode.Continuous,
                out requirement,
                out diagnostic);
    }
}
