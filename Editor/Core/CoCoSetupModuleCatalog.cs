using System.Collections.Generic;
using System.Linq;

// Setup 窗口从这里读取已随包提供的模块、程序集名称和双语说明。
namespace CoCoFlow.Editor.Core
{
    /// <summary>将一个可安装模块与其程序集、依赖和 Setup 窗口说明关联。</summary>
    internal sealed class ModuleDefinition
    {
        public ModuleDefinition(
            string displayName,
            string[] requiredSupportDefines,
            string[] requiredAssemblies,
            string description,
            string descriptionZh)
        {
            DisplayName = displayName;
            RequiredSupportDefines = requiredSupportDefines;
            RequiredAssemblies = requiredAssemblies;
            Description = description;
            DescriptionZh = descriptionZh;
        }

        public string DisplayName { get; }
        public string[] RequiredSupportDefines { get; }
        public string[] RequiredAssemblies { get; }

                public string Description { get; }

                public string DescriptionZh { get; }
    }

    /// <summary>为 Setup 窗口提供随包模块的固定目录。</summary>
    internal static class CoCoSetupModuleCatalog
    {
        internal static readonly ModuleDefinition[] Modules =
        {
            new ModuleDefinition(
                "Core",
                new string[0],
                new string[0],
                "Always compiled.",
                "始终编译。"),
            new ModuleDefinition(
                "Input",
                new string[0],
                new[] { "Unity.InputSystem" },
                "Input System runtime module.",
                "Input System 运行时模块。"),
            new ModuleDefinition(
                "Input Prompt (UI)",
                new[] { CoCoFlowUtility.UniTaskDefine, CoCoFlowUtility.DotweenDefine, CoCoFlowUtility.UniTaskDotweenDefine },
                new[]
                {
                    "Unity.InputSystem",
                    "CoCoFlow.Runtime.Modules.Input",
                    "CoCoFlow.Runtime.Modules.Input.UI",
                    "CoCoFlow.Runtime.Modules.Localization.UI",
                    "CoCoFlow.Runtime.Modules.UI"
                },
                "Optional UI V2 binding display, device glyph, and localized prompt presentation.",
                "可选的 UI V2 绑定显示、设备图标与本地化提示呈现。"),
            new ModuleDefinition(
                "Localization",
                new string[0],
                new[]
                {
                    "Unity.Localization",
                    "CoCoFlow.Runtime.Modules.Localization"
                },
                "Official Unity Localization core integration and diagnostics.",
                "官方 Unity Localization 核心集成与诊断。"),
            new ModuleDefinition(
                "Localization (UI)",
                new[] { CoCoFlowUtility.UniTaskDefine, CoCoFlowUtility.DotweenDefine, CoCoFlowUtility.UniTaskDotweenDefine },
                new[]
                {
                    "Unity.Localization",
                    "Unity.TextMeshPro",
                    "CoCoFlow.Runtime.Modules.Localization.UI",
                    "CoCoFlow.Runtime.Modules.UI"
                },
                "Optional UI V2 localized text Widget.",
                "可选的 UI V2 本地化文本 Widget。"),
            new ModuleDefinition(
                "Camera",
                new string[0],
                new[] { CoCoFlowUtility.CinemachineAssemblyName },
                "Cinemachine runtime module.",
                "Cinemachine 运行时模块。"),
            new ModuleDefinition(
                "Content (Direct)",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[] { "UniTask" },
                "Direct Asset, Prefab Source, and additive Scene ownership.",
                "直连 Asset、Prefab Source 与附加 Scene 所有权。"),
            new ModuleDefinition(
                "Content (Addressables)",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "Unity.Addressables",
                    "CoCoFlow.Runtime.Content.Addressables"
                },
                "Optional Addressables backend; enabled by assembly version detection.",
                "可选 Addressables 后端；按程序集版本检测启用。"),
            new ModuleDefinition(
                "Pooling",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Content",
                    "CoCoFlow.Runtime.Pooling"
                },
                "Content-backed GameObject pooling using Unity's private pool implementation.",
                "基于 Content 的 GameObject 对象池，使用 Unity 私有池实现。"),
            new ModuleDefinition(
                "Pooling (Temporal)",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Pooling",
                    "CoCoFlow.Runtime.Pooling.Temporal",
                    "CoCoFlow.Runtime.StateGraph"
                },
                "Optional Host-scoped temporal retention sidecar; not world rollback.",
                "可选的 Host 作用域时序保留挂件；非世界回滚。"),
            new ModuleDefinition(
                "Map (Region Fidelity)",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Content",
                    "CoCoFlow.Runtime.Modules.Map"
                },
                "Compiled Region Profiles, scoped Demand Leases, per-Chunk fidelity, and transactional participants.",
                "编译版 Region Profile、作用域 Demand Lease、逐 Chunk 保真度与事务参与者。"),
            new ModuleDefinition(
                "Map (Pooling)",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Modules.Map",
                    "CoCoFlow.Runtime.Pooling",
                    "CoCoFlow.Runtime.Modules.Map.Pooling"
                },
                "Optional Region participant adapter with committed-node PoolScope ownership.",
                "可选的 Region 参与者适配器，含已提交节点的 PoolScope 所有权。"),
            new ModuleDefinition(
                "Map (Temporal)",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Modules.Map",
                    "CoCoFlow.Runtime.StateGraph",
                    "CoCoFlow.Runtime.Modules.Map.Temporal"
                },
                "Map-first availability and retention decorator; it does not replay Map state.",
                "Map 优先的可用性与保留装饰器；不重放 Map 状态。"),
            new ModuleDefinition(
                "UI",
                new[] { CoCoFlowUtility.UniTaskDefine, CoCoFlowUtility.DotweenDefine, CoCoFlowUtility.UniTaskDotweenDefine },
                new[] { "UniTask", "DOTween.Modules", "UniTask.DOTween", "Unity.TextMeshPro", "CoCoFlow.Runtime.Content" },
                "DOTween animated UI module.",
                "基于 DOTween 动画的 UI 模块。")
        };

        /// <summary>保存一组中英界面文字，供 Setup 按语言显示。</summary>
        internal readonly struct BilingualText
        {
            public BilingualText(string english, string simplifiedChinese)
            {
                English = english;
                SimplifiedChinese = simplifiedChinese;
            }

            public string English { get; }
            public string SimplifiedChinese { get; }
        }

        /// <summary>记录当前模块在 Setup 中的检查结果。</summary>
        internal sealed class ModuleStatus
        {
            public ModuleDefinition Definition { get; set; }
            public string[] MissingAssemblies { get; set; }
            public string[] MissingDefines { get; set; }
            public bool IsEnabled => MissingAssemblies.Length == 0 && MissingDefines.Length == 0;

            public BilingualText BuildMessage()
            {
                if (IsEnabled)
                {
                    return new BilingualText(
                        "Enabled. " + Definition.Description,
                        "已启用。" + Definition.DescriptionZh);
                }

                var partsEn = new List<string>();
                var partsZh = new List<string>();
                if (MissingAssemblies.Length > 0)
                {
                    var joined = string.Join(", ", MissingAssemblies);
                    partsEn.Add("Missing assemblies: " + joined);
                    partsZh.Add("缺失程序集：" + joined);
                }

                if (MissingDefines.Length > 0)
                {

                    partsEn.Add("Defines: " + string.Join("; ", MissingDefines.Select(d => d + " off")));
                    partsZh.Add("宏：" + string.Join("；", MissingDefines.Select(d => d + " 未启用")));
                }

                return new BilingualText(
                    "Disabled or partial. " + string.Join(" | ", partsEn),
                    "禁用或部分禁用。" + string.Join("｜", partsZh));
            }
        }

                internal static List<ModuleStatus> EvaluateModules(CoCoSetupDependencyStatus status)
        {
            var result = new List<ModuleStatus>(Modules.Length);
            foreach (var module in Modules)
            {
                var missingAssemblies = module.RequiredAssemblies
                    .Where(assembly => !status.AssemblyAvailable(assembly))
                    .ToArray();
                var missingDefines = module.RequiredSupportDefines
                    .Where(define => !status.DefinePresentOnActiveTarget(define))
                    .ToArray();
                result.Add(new ModuleStatus
                {
                    Definition = module,
                    MissingAssemblies = missingAssemblies,
                    MissingDefines = missingDefines
                });
            }

            return result;
        }
    }
}
