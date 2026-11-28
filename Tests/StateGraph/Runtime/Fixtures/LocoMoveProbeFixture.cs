using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    [State("LocoMoveProbe")]
    public sealed class LocoMoveProbeLogic : StateLogic, IStateUpdate
    {
        [Operation] public ILocomotionOperation Movement { get; set; }
        public static float? LastDelta;

        public void Update(StateExecutionContext context)
        {
            Movement.MoveZ = 2f;
            Movement.UseGravity = true;
            LastDelta = 2f;
        }
    }
}
