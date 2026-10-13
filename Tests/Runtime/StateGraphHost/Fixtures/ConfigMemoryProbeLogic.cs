using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

[CoCoState("ConfigMemoryProbeLogic")]
public sealed class ConfigMemoryProbeLogic : CoCoStateLogic, ICoCoStateUpdate
{
    [Config] public float Speed = 6f;
    [Memory] public float Distance;
    [Memory] public bool ImpulseSent;
    public static readonly List<ConfigMemoryProbeLogic> Seen = new List<ConfigMemoryProbeLogic>();
    public static int Impulses;

    public void Update(CoCoStateExecutionContext context)
    {
        if (!Seen.Contains(this)) Seen.Add(this);
        Distance += Speed * (float)context.TickFrame.DeltaTime;
        if (ImpulseSent || Speed == 0f) return;
        ImpulseSent = true;
        Impulses++;
    }
}
