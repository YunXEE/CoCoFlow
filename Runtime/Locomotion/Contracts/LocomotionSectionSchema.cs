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
    /// View over the locomotion section fields; the supplied access owns read or write routing.
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

        public float MoveX { get => F(0); set => _reader.Write(_floats[0], value); }
        public float MoveZ { get => F(1); set => _reader.Write(_floats[1], value); }
        public float ForcedX { get => F(2); set => _reader.Write(_floats[2], value); }
        public float ForcedZ { get => F(3); set => _reader.Write(_floats[3], value); }
        public bool UseGravity { get => B(0); set => _reader.Write(_bools[0], value); }
        public float GravityScale { get => F(4); set => _reader.Write(_floats[4], value); }
        public bool JumpRequested { get => B(1); set => _reader.Write(_bools[1], value); }
        public bool LaunchForced { get => B(2); set => _reader.Write(_bools[2], value); }
        public float VerticalImpulse { get => F(5); set => _reader.Write(_floats[5], value); }
        public float LookX { get => F(6); set => _reader.Write(_floats[6], value); }
        public float LookZ { get => F(7); set => _reader.Write(_floats[7], value); }
        public bool InstantRotation { get => B(3); set => _reader.Write(_bools[3], value); }
        public bool TeleportRequested { get => B(4); set => _reader.Write(_bools[4], value); }
        public float TeleportX { get => F(8); set => _reader.Write(_floats[8], value); }
        public float TeleportY { get => F(9); set => _reader.Write(_floats[9], value); }
        public float TeleportZ { get => F(10); set => _reader.Write(_floats[10], value); }
        public float TeleportRotationX { get => F(11); set => _reader.Write(_floats[11], value); }
        public float TeleportRotationY { get => F(12); set => _reader.Write(_floats[12], value); }
        public float TeleportRotationZ { get => F(13); set => _reader.Write(_floats[13], value); }
        public float TeleportRotationW { get => F(14); set => _reader.Write(_floats[14], value); }

        public void Jump(float verticalImpulse)
        {
            JumpRequested = true;
            VerticalImpulse = verticalImpulse;
        }

        public void Launch(float verticalImpulse)
        {
            LaunchForced = true;
            VerticalImpulse = verticalImpulse;
        }
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
