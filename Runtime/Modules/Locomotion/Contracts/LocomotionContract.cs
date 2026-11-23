using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.Locomotion.Contracts
{
    /// <summary>
    /// Per-tick locomotion request lane. Every field is a declare-this-tick
    /// value: the state logic rewrites what it wants each tick, untouched
    /// fields are zero/no-op, and the frame resets at tick end. Mirrors the
    /// proven CharacterLocomotion semantics (SetMovementVelocity /
    /// SetForcedVelocity / Jump / Launch / SetGravityEnable /
    /// SetGravityScale / SetRotation / SetRotationInstant) plus a logic
    /// teleport override (respawn / cutscene placement).
    /// </summary>
    [OperationSection(OperationSectionMode.Continuous)]
    public interface ILocomotionSection : IOperationSection
    {
        /// <summary>Desired horizontal velocity, world X. Final-conclusion
        /// semantics: the state logic has already applied every multiplier.
        /// Velocity, not a displacement.</summary>
        float MoveX { get; set; }

        /// <summary>Desired horizontal velocity, world Z.</summary>
        float MoveZ { get; set; }

        /// <summary>Forced horizontal velocity, world X — overrides Move
        /// when nonzero (knockback and dashes ignore multipliers).</summary>
        float ForcedX { get; set; }

        /// <summary>Forced horizontal velocity, world Z.</summary>
        float ForcedZ { get; set; }

        /// <summary>Gravity integration toggle. false = hover
        /// (VerticalVelocity is held at zero).</summary>
        bool UseGravity { get; set; }

        /// <summary>Gravity scale for this tick (slow-fall, heavy state).
        /// Final-conclusion value.</summary>
        float GravityScale { get; set; }

        /// <summary>One-shot jump request; applied only when grounded.</summary>
        bool JumpRequested { get; set; }

        /// <summary>true = apply VerticalImpulse unconditionally (launch);
        /// false = grounded-gated (jump).</summary>
        bool LaunchForced { get; set; }

        /// <summary>Vertical impulse value for Jump/Launch.</summary>
        float VerticalImpulse { get; set; }

        /// <summary>Desired facing direction, world X. Zero vector =
        /// keep current rotation.</summary>
        float LookX { get; set; }

        /// <summary>Desired facing direction, world Z.</summary>
        float LookZ { get; set; }

        /// <summary>true = snap rotation this tick instead of smooth-damp.</summary>
        bool InstantRotation { get; set; }

        /// <summary>Logic teleport (respawn / placement): this tick's
        /// position becomes the Teleport values directly, bypassing the
        /// delta path; the next tick resumes normal delta movement.</summary>
        bool TeleportRequested { get; set; }

        float TeleportX { get; set; }
        float TeleportY { get; set; }
        float TeleportZ { get; set; }

        float TeleportRotationX { get; set; }
        float TeleportRotationY { get; set; }
        float TeleportRotationZ { get; set; }
        float TeleportRotationW { get; set; }

        void Jump(float verticalImpulse);
        void Launch(float verticalImpulse);
    }

    /// <summary>
    /// The complete locomotion slot. Integration state (phase matters,
    /// snapshot core, evolved by the pure step) plus fact registers
    /// (delta accumulators, phase-free: the engine's actual output,
    /// sampled back after each move — position authority lives in the
    /// engine, this records what it decided).
    /// </summary>
    public struct LocomotionState
    {
        // —— Integration state (phase matters; snapshot core) ——
        public float VerticalVelocity;
        public float RotationVelocity;
        public float Rotation;

        // —— Fact registers (engine output, sampled; single writer) ——
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float RotationQX;
        public float RotationQY;
        public float RotationQZ;
        public float RotationQW;
        public bool IsGrounded;
    }

    /// <summary>
    /// Stable package ids for the locomotion section, operator, and slot.
    /// </summary>
    public static class LocoContractIds
    {
        private const ulong High = 0x434F434F4C4F434FUL; // "COCOLOC"

        public const ulong SectionSemanticFingerprint = 0x4C4F434F00001001UL;

        static LocoContractIds()
        {
            if (!OperationSectionId.TryCreate(
                    High,
                    1UL,
                    out OperationSectionId sectionId) ||
                !OperatorId.TryCreate(
                    High,
                    2UL,
                    out OperatorId operatorId) ||
                !StateBlockId.TryCreate(
                    High,
                    3UL,
                    out StateBlockId blockId) ||
                !StateSlotId.TryCreate(
                    High,
                    4UL,
                    out StateSlotId slotId))
            {
                throw new InvalidOperationException(
                    "Locomotion contract ids must be valid.");
            }

            SectionId = sectionId;
            OperatorId = operatorId;
            StateBlockId = blockId;
            StateSlotId = slotId;
        }

        public static OperationSectionId SectionId { get; }
        public static OperatorId OperatorId { get; }
        public static StateBlockId StateBlockId { get; }
        public static StateSlotId StateSlotId { get; }
    }

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
        private readonly OperationSectionReader _reader;
        private readonly OperationSectionField<float>[] _floats;
        private readonly OperationSectionField<bool>[] _bools;

        internal LocomotionSectionView(
            OperationSectionReader reader,
            OperationSectionField<float>[] floats,
            OperationSectionField<bool>[] bools)
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
        IOperationSectionViewFactory<ILocomotionSection>
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

        private OperationSectionField<float>[] _floats;
        private OperationSectionField<bool>[] _bools;

        public ILocomotionSection Create(
            in OperationSectionViewContext<ILocomotionSection> context)
        {
            _floats = ResolveFields<float>(context, FloatDenseIndices);
            _bools = ResolveFields<bool>(context, BoolDenseIndices);
            return new LocomotionSectionView(
                context.Reader,
                _floats,
                _bools);
        }

        private static OperationSectionField<TValue>[] ResolveFields<TValue>(
            in OperationSectionViewContext<ILocomotionSection> context,
            int[] denseIndices)
            where TValue : unmanaged
        {
            var fields = new OperationSectionField<TValue>[denseIndices.Length];
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

        public static StateSlotId StateSlot => LocoContractIds.StateSlotId;

        public static bool TryCreateSectionRequirement(
            out OperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic) =>
            OperationSectionRequirement.TryCreate<ILocomotionSection>(
                LocoContractIds.SectionId,
                OperationSectionMode.Continuous,
                out requirement,
                out diagnostic);
    }
}
