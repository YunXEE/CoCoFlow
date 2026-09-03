using System.Collections.Generic;
using System.Linq;

namespace CoCoFlow.Editor.Core
{
    /// <summary>
    /// 单个 CoCoFlow 模块的启用条件目录条目（自窗口私有嵌套迁出；
    /// 实例属性名与可见性保持，测试反射绑定——方案 v4 §2.1/§2.4）。
    /// </summary>
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

        /// <summary>英文描述（测试断言绑定的原始值，不变）。</summary>
        public string Description { get; }

        /// <summary>中文描述（渲染投影侧；不参与任何英文断言）。</summary>
        public string DescriptionZh { get; }
    }

    /// <summary>
    /// 17 模块目录与模块状态计算（数据层；窗口只做投影）。
    /// </summary>
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
                    "CoCoFlow.Runtime.StateGraphHost"
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
                    "CoCoFlow.Runtime.StateGraphHost",
                    "CoCoFlow.Runtime.Modules.Map.Temporal"
                },
                "Map-first availability and retention decorator; it does not replay Map state.",
                "Map 优先的可用性与保留装饰器；不重放 Map 状态。"),
            new ModuleDefinition(
                "Animation",
                new string[0],
                new[]
                {
                    "CoCoFlow.Runtime.Animation.Contracts",
                    "CoCoFlow.Runtime.Modules.Animation"
                },
                "Animator Controller parameter/trigger and manual Playable Operators.",
                "Animator Controller 参数/触发器与手动 Playable Operator。"),
            new ModuleDefinition(
                "Animation (DOTween)",
                new[] { CoCoFlowUtility.DotweenDefine },
                new[]
                {
                    "DOTween",
                    "CoCoFlow.Runtime.Modules.Animation",
                    "CoCoFlow.Runtime.Modules.Animation.DOTween"
                },
                "Optional Operator-owned manual modulation; never advances global tweens.",
                "可选的 Operator 持有的手动调制；从不推进全局 tween。"),
            new ModuleDefinition(
                "Animation (UniTask)",
                new[] { CoCoFlowUtility.UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Modules.Animation",
                    "CoCoFlow.Runtime.Modules.Animation.UniTask"
                },
                "Optional playback-token waiter; cancellation does not stop playback.",
                "可选的播放令牌等待器；取消不停止播放。"),
            new ModuleDefinition(
                "UI",
                new[] { CoCoFlowUtility.UniTaskDefine, CoCoFlowUtility.DotweenDefine, CoCoFlowUtility.UniTaskDotweenDefine },
                new[] { "UniTask", "DOTween.Modules", "UniTask.DOTween", "Unity.TextMeshPro", "CoCoFlow.Runtime.Content" },
                "DOTween animated UI module.",
                "基于 DOTween 动画的 UI 模块。")
        };

        /// <summary>双语文本对（原始数据侧；渲染层按语言取一侧）。</summary>
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

        /// <summary>单模块状态快照（数据层计算，视图只投影）。</summary>
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
                    // 旧格式保留：每个缺失 define 携带状态后缀（单目标下缺失即 off）。
                    partsEn.Add("Defines: " + string.Join("; ", MissingDefines.Select(d => d + " off")));
                    partsZh.Add("宏：" + string.Join("；", MissingDefines.Select(d => d + " 未启用")));
                }

                return new BilingualText(
                    "Disabled or partial. " + string.Join(" | ", partsEn),
                    "禁用或部分禁用。" + string.Join("｜", partsZh));
            }
        }

        /// <summary>
        /// 计算 17 模块状态（define 缺失按 active target 的 MissingDefineTargets 判定，
        /// 与旧实现的逐模块计算口径一致——invariant #11/#13）。
        /// </summary>
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
