
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    [CoCoState("StandardProbe")]
    [CoCoIntentConsume(typeof(RawInputIntent))]
    [CoCoOperationProvide(typeof(ILocomotionSection))]
    public sealed class StandardProbeLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public static List<RawInputRecord> Received;

        public void Update(CoCoStateExecutionContext context)
        {
            var writer = context.Operations;
            writer.Write(writer.ResolveField<ILocomotionSection, float>(LocomotionSectionFields.MoveZ), 2f);
            writer.Write(writer.ResolveField<ILocomotionSection, bool>(LocomotionSectionFields.LaunchForced), true);
            writer.Write(writer.ResolveField<ILocomotionSection, float>(LocomotionSectionFields.VerticalImpulse), 5f);
            if (context.Intents != null &&
                context.Intents.TryFirst(out RawInputIntent intent) &&
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
    [CoCoState("PropertyProbe")]
    public sealed class PropertyProbeLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        [Intent] public RawInputIntent Input { get; set; }
        [Operation] public ILocomotionSection Movement { get; set; }
        public static PropertyProbeLogic Last;

        public void Update(CoCoStateExecutionContext context)
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
