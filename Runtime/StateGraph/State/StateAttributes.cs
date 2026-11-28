using System;

// 玩法在 Logic、字段及属性上声明状态名、进度、输入和配置。标准 Provider 在启动前扫描这些标记。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>指示标准绑定在回调前将该步的输入载荷注入属性。</summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = false)]
    public sealed class IntentAttribute : Attribute
    {
    }

    /// <summary>以项目声明的状态名生成稳定描述符，供资产节点选择该 Logic。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class StateAttribute : Attribute
    {
        /// <summary>以给定名称声明状态，标准注册据此派生描述符。传入 null 会抛出异常。</summary>
        public StateAttribute(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public string Name { get; }
        public bool ProvidesActionProgress { get; set; }
    }

    /// <summary>将公开实例字段作为节点配置，编译时冻结并在 Host 创建 Logic 时应用。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class StateConfigAttribute : Attribute
    {
    }

    /// <summary>把公开的非托管字段纳入激活记忆，在提交或恢复时复制其值。</summary>
    [AttributeUsage(AttributeTargets.Field, Inherited = false)]
    public class StateMemoryAttribute : Attribute
    {
    }

    /// <summary>供项目在节点可覆盖的公开 Logic 字段上声明配置。</summary>
    public sealed class ConfigAttribute : StateConfigAttribute { }

    /// <summary>标记随激活记忆捕获和还原的非托管玩法字段。</summary>
    public sealed class MemoryAttribute : StateMemoryAttribute { }
}
