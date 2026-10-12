using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Locomotion.Contracts
{
    /// <summary>
    /// Engine-free, callback-scoped locomotion author. Each call writes requested
    /// values into the declared Section; only the real Operator moves the Actor.
    /// </summary>
    public readonly struct LocomotionOperationAuthor
    {
        private readonly CoCoStateOperationWriter _writer;
        public LocomotionOperationAuthor(CoCoStateOperationWriter writer) => _writer = writer;
        public bool IsValid => _writer.IsValid;

        public bool Move(float x, float z) =>
            Write(LocomotionSectionFields.MoveX, x) && Write(LocomotionSectionFields.MoveZ, z);
        public bool Force(float x, float z) =>
            Write(LocomotionSectionFields.ForcedX, x) && Write(LocomotionSectionFields.ForcedZ, z);
        public bool Look(float x, float z, bool instant = false) =>
            Write(LocomotionSectionFields.LookX, x) && Write(LocomotionSectionFields.LookZ, z) &&
            Write(LocomotionSectionFields.InstantRotation, instant);
        public bool Gravity(bool enabled, float scale = 1f) =>
            Write(LocomotionSectionFields.UseGravity, enabled) && Write(LocomotionSectionFields.GravityScale, scale);
        public bool Jump(float impulse, bool forced = false) =>
            Write(LocomotionSectionFields.VerticalImpulse, impulse) &&
            Write(LocomotionSectionFields.LaunchForced, forced) && Write(LocomotionSectionFields.JumpRequested, true);
        public bool Teleport(float x, float y, float z, float rotationX, float rotationY, float rotationZ, float rotationW) =>
            Write(LocomotionSectionFields.TeleportX, x) && Write(LocomotionSectionFields.TeleportY, y) &&
            Write(LocomotionSectionFields.TeleportZ, z) && Write(LocomotionSectionFields.TeleportRotationX, rotationX) &&
            Write(LocomotionSectionFields.TeleportRotationY, rotationY) && Write(LocomotionSectionFields.TeleportRotationZ, rotationZ) &&
            Write(LocomotionSectionFields.TeleportRotationW, rotationW) && Write(LocomotionSectionFields.TeleportRequested, true);

        private bool Write<TValue>(int index, in TValue value) where TValue : unmanaged =>
            _writer.Write(_writer.ResolveField<ILocomotionSection, TValue>(index), value);
    }
}
