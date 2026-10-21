using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    public enum ConfigCollectionMode { First, Second }

    [Serializable]
    public struct ConfigCollectionTiming
    {
        public decimal Weight;
    }

    [Serializable]
    public struct ConfigCollectionWindow
    {
        public float startSeconds;
        public double endSeconds;
        public bool isEnabled;
        public ConfigCollectionMode mode;
        public ConfigCollectionTiming timing;
    }

    [CoCoState("ConfigCollectionProbe")]
    public sealed class ConfigCollectionProbeLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        [Config] public float Speed = 6f;
        [Config] public ConfigCollectionWindow Window;
        [Config] public float[] Numbers = { 7f };
        [Config] public List<float> Samples = new List<float> { 8f };
        [Config] public ConfigCollectionWindow[] Windows = { new ConfigCollectionWindow { endSeconds = 9d } };
        [Config] public List<ConfigCollectionWindow> WindowList = new List<ConfigCollectionWindow>();
        [Config] public bool[] Flags = Array.Empty<bool>();
        [Config] public List<ConfigCollectionMode> Modes = new List<ConfigCollectionMode>();
        [Config] public int[] Optional;
        public static readonly List<ConfigCollectionProbeLogic> Seen = new List<ConfigCollectionProbeLogic>();

        public void Update(CoCoStateExecutionContext context)
        {
            if (!Seen.Contains(this)) Seen.Add(this);
        }
    }
}
