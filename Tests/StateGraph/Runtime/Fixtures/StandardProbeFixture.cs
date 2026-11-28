using CoCoFlow.Runtime.Input.Contracts;

using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    [State("StandardProbe")]
    public sealed class StandardProbeLogic : StateLogic, IStateUpdate
    {
        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        public static List<RawInputRecord> Received;

        public void Update(StateExecutionContext context)
        {
            Movement.MoveZ = 2f;
            Movement.Launch(5f);
            if (Received != null)
            {
                for (int index = 0; index < Input.Count; index++)
                {
                    if (Input.TryGet(index, out RawInputRecord record))
                    {
                        Received.Add(record);
                    }
                }
            }
        }
    }
    [State("PropertyProbe")]
    public sealed class PropertyProbeLogic : StateLogic, IStateUpdate
    {
        [Intent] public InputIntentContract Input { get; set; }
        [Operation] public ILocomotionOperation Movement { get; set; }
        public static PropertyProbeLogic Last;

        public void Update(StateExecutionContext context)
        {
            Last = this;
            Movement.MoveZ = 2f;
            Movement.Launch(5f);
            if (StandardProbeLogic.Received == null) return;
            for (int index = 0; index < Input.Count; index++)
            {
                if (Input.TryGet(index, out RawInputRecord record)) StandardProbeLogic.Received.Add(record);
            }
        }
    }

}
