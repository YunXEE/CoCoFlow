using System;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Locomotion.Contracts
{
    /// <summary>
    /// Stable dense field indices for ILocomotionSection. Operation Section
    /// shapes sort properties by ordinal name; state scripts use these names
    /// instead of duplicating numeric layout knowledge.
    /// </summary>
    public static class LocomotionSectionFields
    {
        public const int ForcedX = 0;
        public const int ForcedZ = 1;
        public const int GravityScale = 2;
        public const int InstantRotation = 3;
        public const int JumpRequested = 4;
        public const int LaunchForced = 5;
        public const int LookX = 6;
        public const int LookZ = 7;
        public const int MoveX = 8;
        public const int MoveZ = 9;
        public const int TeleportRequested = 10;
        public const int TeleportRotationW = 11;
        public const int TeleportRotationX = 12;
        public const int TeleportRotationY = 13;
        public const int TeleportRotationZ = 14;
        public const int TeleportX = 15;
        public const int TeleportY = 16;
        public const int TeleportZ = 17;
        public const int UseGravity = 18;
        public const int VerticalImpulse = 19;
    }

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
        private static readonly int[] FloatDenseIndices =
        {
            LocomotionSectionFields.MoveX,
            LocomotionSectionFields.MoveZ,
            LocomotionSectionFields.ForcedX,
            LocomotionSectionFields.ForcedZ,
            LocomotionSectionFields.GravityScale,
            LocomotionSectionFields.VerticalImpulse,
            LocomotionSectionFields.LookX,
            LocomotionSectionFields.LookZ,
            LocomotionSectionFields.TeleportX,
            LocomotionSectionFields.TeleportY,
            LocomotionSectionFields.TeleportZ,
            LocomotionSectionFields.TeleportRotationX,
            LocomotionSectionFields.TeleportRotationY,
            LocomotionSectionFields.TeleportRotationZ,
            LocomotionSectionFields.TeleportRotationW,
        };

        private static readonly int[] BoolDenseIndices =
        {
            LocomotionSectionFields.UseGravity,
            LocomotionSectionFields.JumpRequested,
            LocomotionSectionFields.LaunchForced,
            LocomotionSectionFields.InstantRotation,
            LocomotionSectionFields.TeleportRequested,
        };

        private CoCoOperationSectionField<float>[] _floats;
        private CoCoOperationSectionField<bool>[] _bools;

        public ILocomotionSection Create(
            in CoCoOperationSectionViewContext<ILocomotionSection> context)
        {
            _floats = ResolveFields<float>(context, FloatDenseIndices);
            _bools = ResolveFields<bool>(context, BoolDenseIndices);
            return new LocomotionSectionView(
                context.Reader,
                _floats,
                _bools);
        }

        private static CoCoOperationSectionField<TValue>[] ResolveFields<TValue>(
            in CoCoOperationSectionViewContext<ILocomotionSection> context,
            int[] denseIndices)
            where TValue : unmanaged
        {
            var fields = new CoCoOperationSectionField<TValue>[denseIndices.Length];
            for (int index = 0; index < fields.Length; index++)
            {
                if (!context.TryGetField(denseIndices[index], out fields[index]))
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
