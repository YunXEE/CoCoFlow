using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>
    /// Writes a fixed forward velocity into the locomotion section via the
    /// typed field resolver. It deliberately consumes no Intent so the test
    /// also proves that an Operation-only graph needs no Intent source.
    /// </summary>
    [State("LocoMoveProbe")]
    [OperationProvide(typeof(ILocomotionSection))]
    public sealed class LocoMoveProbeLogic : StateLogic, IStateUpdate
    {
        public static float? LastDelta;

        public void Update(StateExecutionContext context)
        {
            var moveZ = context.Operations.ResolveField<ILocomotionSection, float>(
                LocomotionSectionFields.MoveZ);
            var gravity = context.Operations.ResolveField<ILocomotionSection, bool>(
                LocomotionSectionFields.UseGravity);
            if (!moveZ.IsValid || !gravity.IsValid)
            {
                LastDelta = null;
                return;
            }

            _ = context.Operations.Write(moveZ, 2f);   // MoveZ = 2 m/s
            _ = context.Operations.Write(gravity, true);
            LastDelta = 2f;
        }
    }

    /// <summary>
    /// Declares both Sections consumed by AnimAutoOperator. The State does
    /// not need to write commands for the registration test; its descriptor
    /// is sufficient to freeze both Sections into the compiled manifest.
    /// </summary>
    [State("AnimOperationProbe")]
    [OperationProvide(typeof(IAnimParameterOperationSection))]
    [OperationProvide(typeof(IAnimTriggerOperationSection))]
    public sealed class AnimOperationProbeLogic :
        StateLogic,
        IStateUpdate
    {
        public void Update(StateExecutionContext context)
        {
        }
    }
}
