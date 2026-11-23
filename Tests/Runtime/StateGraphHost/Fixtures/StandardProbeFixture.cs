
using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    [State("StandardProbe")]
    [IntentConsume(typeof(InputIntentData))]
    [OperationProvide(typeof(ILocomotionSection))]
    public sealed class StandardProbeLogic : StateLogic, IStateUpdate
    {
        public static List<RawInputRecord> Received;

        public void Update(StateExecutionContext context)
        {
            var writer = context.Operations;
            writer.Write(writer.ResolveField<ILocomotionSection, float>(LocomotionSectionFields.MoveZ), 2f);
            writer.Write(writer.ResolveField<ILocomotionSection, bool>(LocomotionSectionFields.LaunchForced), true);
            writer.Write(writer.ResolveField<ILocomotionSection, float>(LocomotionSectionFields.VerticalImpulse), 5f);
            if (context.Intents != null &&
                context.Intents.TryFirst(out InputIntentData intent) &&
                Received != null)
            {
                for (int index = 0; index < intent.Count; index++)
                {
                    if (intent.TryGet(index, out RawInputRecord record))
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
        [Intent] public InputIntentData Input { get; set; }
        [Operation] public ILocomotionSection Movement { get; set; }
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
