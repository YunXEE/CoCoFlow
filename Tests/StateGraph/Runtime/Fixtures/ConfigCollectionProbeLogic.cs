using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

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

    public struct ClipReference
    {
        public string Key;
    }

    public readonly struct MontageSegment
    {
        public readonly int Frames;
        public MontageSegment(int frames) => Frames = frames;
    }

    public interface IMontageLibrary
    {
        bool TryGet(string key, out MontageSegment segment);
    }

    [State("ConfigCollectionProbe")]
    public sealed class ConfigCollectionProbeLogic : StateLogic, IStateUpdate
    {
        [Config] public string ClipKey = "Idle";
        [Config] public string[] Names;
        [Config] public List<string> Aliases;
        [Config] public ClipReference Clip;
        public bool FoundSegment { get; private set; }
        public MontageSegment Segment { get; private set; }
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

        public void Update(StateExecutionContext context)
        {
            if (CoCoServices.TryGet<IMontageLibrary>(out var library))
            {
                FoundSegment = library.TryGet(ClipKey, out var segment);
                Segment = segment;
            }
            if (!Seen.Contains(this)) Seen.Add(this);
        }
    }
}
