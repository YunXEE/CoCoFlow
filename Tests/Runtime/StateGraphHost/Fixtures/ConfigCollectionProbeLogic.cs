using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// 绑定测试通过本状态的配置字段接收节点覆盖，并以字符串键从项目服务查询片段。
namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>提供枚举型配置元素，供集合序列化和应用测试使用。</summary>
    public enum ConfigCollectionMode { First, Second }

    /// <summary>将时间值组成嵌套字段，供标准配置验证层级路径。</summary>
    [Serializable]
    public struct ConfigCollectionTiming
    {
        public decimal Weight;
    }

    /// <summary>在同一配置元素中组合区间与嵌套值，用于集合冻结和覆盖测试。</summary>
    [Serializable]
    public struct ConfigCollectionWindow
    {
        public float startSeconds;
        public double endSeconds;
        public bool isEnabled;
        public ConfigCollectionMode mode;
        public ConfigCollectionTiming timing;
    }

    /// <summary>将字符串片段键嵌入可配置结构体，供字段路径与集合元素往返。</summary>
    public struct ClipReference
    {
        public string Key;
    }

    /// <summary>保存测试片段库成功查找后返回的片段身份。</summary>
    public readonly struct MontageSegment
    {
        public readonly int Frames;
        /// <summary>保存项目服务查到的片段值，供节点用例检查实际查询结果。</summary>
        public MontageSegment(int frames) => Frames = frames;
    }

    /// <summary>由项目侧服务按节点提供的字符串键查询片段，未命中时返回失败。</summary>
    public interface IMontageLibrary
    {
        /// <summary>以原样字符串查找项目片段，找到时返回 true，未命中时由节点自行处理。</summary>
        bool TryGet(string key, out MontageSegment segment);
    }

    /// <summary>在状态回调内读取节点和集合配置，并用本节点的片段键查询项目服务。</summary>
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

        /// <summary>将本次实例暴露给测试观察器，并使用节点的配置键读取所注册的项目片段库。</summary>
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
