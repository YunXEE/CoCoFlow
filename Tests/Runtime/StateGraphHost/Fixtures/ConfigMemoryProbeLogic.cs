using System.Collections.Generic;
using CoCoFlow.Runtime.StateGraph;

[State("ConfigMemoryProbeLogic")]
public sealed class ConfigMemoryProbeLogic : StateLogic, IStateUpdate
{
    [Config] public float Speed = 6f;
    [Memory] public float Distance;
    [Memory] public bool ImpulseSent;
    public static readonly List<ConfigMemoryProbeLogic> Seen = new List<ConfigMemoryProbeLogic>();
    public static int Impulses;

    public void Update(StateExecutionContext context)
    {
        if (!Seen.Contains(this)) Seen.Add(this);
        Distance += Speed * (float)context.TickFrame.DeltaTime;
        if (ImpulseSent || Speed == 0f) return;
        ImpulseSent = true;
        Impulses++;
    }
}

[State("ConfigOnlyProbe")]
public sealed class ConfigOnlyProbe : StateLogic, IStateUpdate
{
    [Config] public float Speed = 6f;
    public static int Instances;
    public ConfigOnlyProbe() { Instances++; }
    public void Update(StateExecutionContext context) { }
}

[State("MemoryOnlyProbe")]
public sealed class MemoryOnlyProbe : StateLogic, IStateUpdate
{
    [Memory] public bool Sent = true;
    public void Update(StateExecutionContext context) { }
}
