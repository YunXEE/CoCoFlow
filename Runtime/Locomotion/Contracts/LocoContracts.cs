using System;
using CoCoFlow.Runtime.Core;

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
    public interface ILocomotionSection : ICoCoOperationSection
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
            if (!CoCoOperationSectionId.TryCreate(
                    High,
                    1UL,
                    out CoCoOperationSectionId sectionId) ||
                !CoCoOperatorId.TryCreate(
                    High,
                    2UL,
                    out CoCoOperatorId operatorId) ||
                !CoCoStateBlockId.TryCreate(
                    High,
                    3UL,
                    out CoCoStateBlockId blockId) ||
                !CoCoStateSlotId.TryCreate(
                    High,
                    4UL,
                    out CoCoStateSlotId slotId))
            {
                throw new InvalidOperationException(
                    "Locomotion contract ids must be valid.");
            }

            SectionId = sectionId;
            OperatorId = operatorId;
            StateBlockId = blockId;
            StateSlotId = slotId;
        }

        public static CoCoOperationSectionId SectionId { get; }
        public static CoCoOperatorId OperatorId { get; }
        public static CoCoStateBlockId StateBlockId { get; }
        public static CoCoStateSlotId StateSlotId { get; }
    }
}
