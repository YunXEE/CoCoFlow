using System;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.Serialization;

// Unity 导入历史图时通过这些旧身份还原配置，快照构建器在冻结前将其转换为当前节点配置。
namespace CoCoFlow.Runtime.StateGraph
{

    /// <summary>允许历史托管配置在冻结前转换为当前可读的节点配置。</summary>
    internal interface ILegacyGraphConfig<out T>
    {
        T Current { get; }
    }

    /// <summary>接收已发布程序集身份下的无字段配置并恢复为空节点配置。</summary>
    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.StateGraph", "CoCoFlow.Runtime.StateGraph", "EmptyStateConfig")]
    internal sealed class ReleasedEmptyStateConfig : StateConfig, ILegacyGraphConfig<StateConfig>
    {
        [NonSerialized] private EmptyStateConfig _current;
        public StateConfig Current => _current ??= new EmptyStateConfig();
    }

    /// <summary>接收已发布程序集身份下的标准字段覆盖，并保留历史条目和值。</summary>
    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.StateGraph", "CoCoFlow.Runtime.StateGraph", "FieldConfig")]
    internal sealed class ReleasedFieldConfig : LegacyFieldConfig { }

    /// <summary>让旧图中名称迁移前的空配置可以重建为当前配置。</summary>
    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "EmptyStateConfig")]
    internal sealed class LegacyEmptyStateConfig : StateConfig, ILegacyGraphConfig<StateConfig>
    {
        [NonSerialized] private EmptyStateConfig _current;
        public StateConfig Current => _current ??= new EmptyStateConfig();
    }

    /// <summary>从较早的托管类型身份重建标准节点配置的条目。</summary>
    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.Core", "CoCoFlow.Runtime.Core.StateGraph", "CoCoFieldConfig")]
    internal class LegacyFieldConfig : StateConfig, ILegacyGraphConfig<StateConfig>
    {
        [FormerlySerializedAs(nameof(FieldConfig.Values)), SerializeField]
        private string[] values = Array.Empty<string>();
        [NonSerialized] private FieldConfig _current;
        public StateConfig Current => _current ??= new FieldConfig { Values = values };
    }

    /// <summary>接收保留旧模块前缀的配置类型名，使已保存的字段值可再次冻结。</summary>
    [Serializable]
    [MovedFrom(false, "CoCoFlow.Runtime.StateGraph", "CoCoFlow.Runtime.StateGraph", "CoCoFieldConfig")]
    internal sealed class LegacyPrefixedFieldConfig : LegacyFieldConfig { }

}
