using System;
using System.Collections.Generic;
using System.Text;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    /// <summary>装配期提示级别（N6：authoring hints，非启动权威）。</summary>
    internal enum CoCoBindingHintKind
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    /// <summary>
    /// 一条装配期提示：级别 + 定位目标 + 双语文案。
    /// 规则层给出结构化结果；文案在这里成对烘焙，渲染层只取
    /// <see cref="LocalizedText"/>。通过提示不宣称启动必成功（启动权威
    /// Runtime-deferred：唯一性全集、精确泛型、Descriptor、图覆盖）。
    /// </summary>
    internal readonly struct CoCoBindingHint
    {
        internal CoCoBindingHint(
            CoCoBindingHintKind kind,
            MonoBehaviour target,
            string english,
            string chinese)
        {
            Kind = kind;
            Target = target;
            English = english;
            Chinese = chinese;
        }

        internal CoCoBindingHintKind Kind { get; }
        internal MonoBehaviour Target { get; }
        internal string English { get; }
        internal string Chinese { get; }
        internal string LocalizedText => CoCoEditorLocalization.Text(English, Chinese);
    }

    /// <summary>
    /// Host 绑定装配规则（D3/D11）：只读、无状态、镜像 Runtime 启动纪律中
    /// 静态可判定的子集；不写任何序列化数据。
    /// </summary>
    internal static class CoCoStateGraphHostBindingRules
    {
        internal const int MaximumRestoreChainDepth = 32;

        internal const string DownstreamPropertyName = "downstreamRestoreBinding";

        // ----- 接口识别 -----

        internal static bool IsIntentFrameSource(MonoBehaviour component)
        {
            return CoCoStateGraphHostBindingCandidates.IsIntentSource(component);
        }

        internal static List<Type> GetIntentPayloadTypes(Type type)
        {
            var payloadTypes = new List<Type>();
            Type[] interfaces = type.GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type contract = interfaces[index];
                if (contract.IsGenericType &&
                    contract.GetGenericTypeDefinition() ==
                        typeof(ICoCoIntentFrameSource<>))
                {
                    payloadTypes.Add(contract.GetGenericArguments()[0]);
                }
            }

            return payloadTypes;
        }

        internal static bool IsOperator(MonoBehaviour component)
        {
            return component != null && component is ICoCoOperator;
        }

        internal static bool IsActorContextBinding(MonoBehaviour component)
        {
            return component != null && component is ICoCoActorContextBinding;
        }

        internal static bool IsContextRestoreBinding(MonoBehaviour component)
        {
            return component != null && component is ICoCoContextRestoreBinding;
        }

        // ----- 描述（列表行 desc，双语成对） -----

        internal static void DescribeIntentSource(
            MonoBehaviour component,
            out string english,
            out string chinese)
        {
            if (component == null)
            {
                english = "empty reference — the slot stays unbound at startup";
                chinese = "空引用——启动时该槽位保持未绑定";
                return;
            }

            List<Type> payloadTypes = GetIntentPayloadTypes(component.GetType());
            if (payloadTypes.Count == 0)
            {
                english = component.GetType().Name +
                    " implements no ICoCoIntentFrameSource<T> interface";
                chinese = component.GetType().Name +
                    " 未实现任何 ICoCoIntentFrameSource<T> 接口";
                return;
            }

            var payloadNames = new StringBuilder();
            for (int index = 0; index < payloadTypes.Count; index++)
            {
                if (index > 0)
                {
                    payloadNames.Append(", ");
                }

                payloadNames.Append(payloadTypes[index].Name);
            }

            english = component.GetType().Name + " (" + payloadNames + ")";
            chinese = english;
        }

        internal static void DescribeOperator(
            MonoBehaviour component,
            out string english,
            out string chinese)
        {
            if (component == null)
            {
                english = "empty reference — the slot stays unbound at startup";
                chinese = "空引用——启动时该槽位保持未绑定";
                return;
            }

            english = component.GetType().Name;
            chinese = english;
        }

        // ----- 单项提示（不含重复；重复在数组层检测） -----

        /// <summary>
        /// Intent Source 装配提示：空引用（Info）、无接口（Error）、
        /// 越界（Warning；Runtime 在清单解析时拒绝越界且要求唯一）。
        /// </summary>
        internal static CoCoBindingHint? BuildIntentSourceHint(
            CoCoStateGraphHost host,
            MonoBehaviour component)
        {
            if (component == null)
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Info,
                    null,
                    "empty Intent Source slot — the slot stays unbound at startup",
                    "Intent Source 空槽位——启动时保持未绑定");
            }

            if (!IsIntentFrameSource(component))
            {
                DescribeIntentSource(component, out string english, out string chinese);
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Error,
                    component,
                    english + " — it will be rejected at startup",
                    chinese + "——启动时将被拒绝");
            }

            if (!CoCoStateGraphHostBoundary.Contains(host, component))
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Warning,
                    component,
                    component.name + " is outside the Host boundary — the runtime " +
                        "requires manifest-bound Intent Sources inside the Host subtree.",
                    component.name + " 位于 Host 边界之外——运行时要求清单绑定的 " +
                        "Intent Source 位于 Host 子树内。");
            }

            return null;
        }

        /// <summary>Operator 装配提示：空引用（Error；Runtime 对任何 null Operator 条目整体拒绝启动）、无接口（Error）、越界（Warning）。</summary>
        internal static CoCoBindingHint? BuildOperatorHint(
            CoCoStateGraphHost host,
            MonoBehaviour component)
        {
            if (component == null)
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Error,
                    null,
                    "empty Operator slot — the runtime rejects every Host Operator " +
                        "entry before Running, so the Host cannot start",
                    "Operator 空槽位——运行时在 Running 前逐条校验 Operator 数组，" +
                        "Host 无法启动");
            }

            if (!IsOperator(component))
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Error,
                    component,
                    component.GetType().Name + " implements no ICoCoOperator " +
                        "interface — it will be rejected at startup",
                    component.GetType().Name + " 未实现 ICoCoOperator 接口——" +
                        "启动时将被拒绝");
            }

            if (!CoCoStateGraphHostBoundary.Contains(host, component))
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Warning,
                    component,
                    component.name + " is outside the Host boundary — move it onto " +
                        "the Host object or one of its children, the runtime rejects " +
                        "it at startup",
                    component.name + " 位于 Host 边界之外——请移到 Host 物体或其" +
                        "子物体上，运行时启动会拒绝越界组件");
            }

            return null;
        }

        /// <summary>Actor Context 装配提示（单引用浅验证，D3）。</summary>
        internal static CoCoBindingHint? BuildActorContextHint(
            CoCoStateGraphHost host,
            MonoBehaviour component)
        {
            if (component == null)
            {
                return null;
            }

            if (!IsActorContextBinding(component))
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Error,
                    component,
                    component.name + " does not implement ICoCoActorContextBinding " +
                        "— startup rejects the Actor Context binding",
                    component.name + " 未实现 ICoCoActorContextBinding——启动时" +
                        "将拒绝该 Actor Context 绑定");
            }

            if (!CoCoStateGraphHostBoundary.Contains(host, component))
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Warning,
                    component,
                    component.name + " is outside the Host boundary — direct " +
                        "Actor-owned slots require one live binding inside the Host " +
                        "boundary",
                    component.name + " 位于 Host 边界之外——Actor 直有槽位要求" +
                        "绑定位于 Host 边界内");
            }

            return null;
        }

        /// <summary>Restore 根装配提示（D5：语义保持）。</summary>
        internal static CoCoBindingHint? BuildRestoreRootHint(
            CoCoStateGraphHost host,
            MonoBehaviour root)
        {
            if (root == null)
            {
                return null;
            }

            if (!(root is ICoCoContextRestoreBinding))
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Error,
                    root,
                    root.name + " does not implement ICoCoContextRestoreBinding — " +
                        "restore projection will be silently skipped",
                    root.name + " 未实现 ICoCoContextRestoreBinding——恢复投影" +
                        "将被静默跳过");
            }

            if (!CoCoStateGraphHostBoundary.Contains(host, root))
            {
                return new CoCoBindingHint(
                    CoCoBindingHintKind.Warning,
                    root,
                    root.name + " is outside the Host boundary — the Temporal " +
                        "controller drops it silently at startup. Move it into the " +
                        "Host subtree or pick another component",
                    root.name + " 位于 Host 边界之外——Temporal 控制器启动时会" +
                        "静默丢弃它。请移入 Host 子树或另选组件");
            }

            return null;
        }

        /// <summary>数组内重复引用（第二次及以后出现者；Runtime 要求组件唯一）。</summary>
        internal static List<MonoBehaviour> FindDuplicateReferences(
            IReadOnlyList<MonoBehaviour> references)
        {
            var duplicates = new List<MonoBehaviour>();
            if (references == null)
            {
                return duplicates;
            }

            var seen = new HashSet<MonoBehaviour>();
            for (int index = 0; index < references.Count; index++)
            {
                MonoBehaviour component = references[index];
                if (component == null)
                {
                    continue;
                }

                if (!seen.Add(component))
                {
                    duplicates.Add(component);
                }
            }

            return duplicates;
        }

        // ----- Restore 链走查（D5：预览 + 自动连接共用） -----

        internal readonly struct CoCoRestoreChainNode
        {
            internal CoCoRestoreChainNode(
                MonoBehaviour component,
                bool isRoot,
                bool implementsContract,
                bool isRepeat,
                bool isInsideBoundary)
            {
                Component = component;
                IsRoot = isRoot;
                ImplementsContract = implementsContract;
                IsRepeat = isRepeat;
                IsInsideBoundary = isInsideBoundary;
            }

            internal MonoBehaviour Component { get; }
            internal bool IsRoot { get; }
            internal bool ImplementsContract { get; }
            internal bool IsRepeat { get; }
            internal bool IsInsideBoundary { get; }
        }

        /// <summary>
        /// 从根走查下游链（深度上限 32，环防护）；在首个断点
        /// （未实现契约 / 重复节点 / 越界节点）处停止并标记该节点。
        /// Runtime 对链上每个节点强制同 Host 边界（TemporalContracts）。
        /// </summary>
        internal static void BuildRestoreChainPreview(
            MonoBehaviour root,
            CoCoStateGraphHost host,
            List<CoCoRestoreChainNode> nodes)
        {
            nodes.Clear();
            if (root == null)
            {
                return;
            }

            var seen = new HashSet<MonoBehaviour>();
            MonoBehaviour current = root;
            int guard = 0;
            while (current != null && guard++ < MaximumRestoreChainDepth)
            {
                bool implementsContract = current is ICoCoContextRestoreBinding;
                bool isRepeat = !seen.Add(current);
                bool isInsideBoundary =
                    CoCoStateGraphHostBoundary.Contains(host, current);
                nodes.Add(new CoCoRestoreChainNode(
                    current,
                    current == root,
                    implementsContract,
                    isRepeat,
                    isInsideBoundary));
                if (!implementsContract || isRepeat || !isInsideBoundary)
                {
                    return;
                }

                current = (current as ICoCoTemporalDecoratorBinding)
                    ?.DownstreamRestoreBinding;
            }
        }

        /// <summary>链上首个断点提示（无断点返回 null）：未实现契约 / 环 / 越界。</summary>
        internal static CoCoBindingHint? BuildRestoreChainBreakHint(
            IReadOnlyList<CoCoRestoreChainNode> nodes)
        {
            if (nodes == null)
            {
                return null;
            }

            for (int index = 0; index < nodes.Count; index++)
            {
                CoCoRestoreChainNode node = nodes[index];
                if (node.ImplementsContract && !node.IsRepeat && node.IsInsideBoundary)
                {
                    continue;
                }

                if (!node.ImplementsContract)
                {
                    return new CoCoBindingHint(
                        CoCoBindingHintKind.Error,
                        node.Component,
                        node.Component.name + " breaks the chain — it implements no " +
                            "ICoCoContextRestoreBinding",
                        node.Component.name + " 中断链——它未实现 " +
                            "ICoCoContextRestoreBinding");
                }

                if (!node.IsInsideBoundary)
                {
                    return new CoCoBindingHint(
                        CoCoBindingHintKind.Error,
                        node.Component,
                        node.Component.name + " is outside the Host boundary — every " +
                            "chain node must stay inside the same Host boundary or " +
                            "the runtime rejects the chain",
                        node.Component.name + " 位于 Host 边界之外——链上每个节点" +
                            "必须留在同一 Host 边界内，否则运行时拒绝该链");
                }

                return new CoCoBindingHint(
                    CoCoBindingHintKind.Error,
                    node.Component,
                    node.Component.name + " appears twice in the chain — restore " +
                        "chains must be acyclic",
                    node.Component.name + " 在链中出现两次——恢复链必须无环");
            }

            return null;
        }

        // ----- 自动连接候选（D5：边界内扫描 + 层级排序） -----

        /// <summary>
        /// 收集 Host 边界内全部 ICoCoContextRestoreBinding 组件并按
        /// 层级路径（其次类型名）排序——自动连接的候选链。
        /// </summary>
        internal static void CollectRestoreChainCandidates(
            CoCoStateGraphHost host,
            List<MonoBehaviour> chain)
        {
            chain.Clear();
            if (host == null)
            {
                return;
            }

            MonoBehaviour[] components = host.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < components.Length; index++)
            {
                MonoBehaviour component = components[index];
                if (component != null &&
                    component is ICoCoContextRestoreBinding &&
                    CoCoStateGraphHostBoundary.Contains(host, component))
                {
                    chain.Add(component);
                }
            }

            SortByHierarchy(chain);
        }

        /// <summary>
        /// 自动连接写入计划（B2）：root 赋值 + 逐对 downstream 链接 + 尾节点清空。
        /// 由 <see cref="TryBuildRestoreWirePlan"/> 在零写入前提下完整解析。
        /// </summary>
        internal readonly struct CoCoRestoreWirePlan
        {
            internal CoCoRestoreWirePlan(
                MonoBehaviour root,
                List<MonoBehaviour> upstreams,
                List<MonoBehaviour> downstreams,
                MonoBehaviour tailToClear)
            {
                Root = root;
                Upstreams = upstreams;
                Downstreams = downstreams;
                TailToClear = tailToClear;
            }

            internal MonoBehaviour Root { get; }
            internal IReadOnlyList<MonoBehaviour> Upstreams { get; }
            internal IReadOnlyList<MonoBehaviour> Downstreams { get; }
            internal MonoBehaviour TailToClear { get; }
        }

        /// <summary>
        /// 自动连接前置校验与写入目标解析（B2：失败零写入）：
        /// 非空、全部实现契约、无重复、全在边界内，且每个非尾节点实现
        /// ICoCoTemporalDecoratorBinding 并具备可写的 downstreamRestoreBinding
        /// 序列化字段；尾节点如具备该字段则列入清空目标。任何目标缺失立即失败。
        /// </summary>
        internal static bool TryBuildRestoreWirePlan(
            CoCoStateGraphHost host,
            IReadOnlyList<MonoBehaviour> chain,
            out CoCoRestoreWirePlan plan,
            out CoCoBindingHint failure)
        {
            plan = default;
            if (chain == null || chain.Count == 0)
            {
                failure = new CoCoBindingHint(
                    CoCoBindingHintKind.Warning,
                    null,
                    "no ICoCoContextRestoreBinding components found inside the Host " +
                        "boundary — nothing to wire",
                    "Host 边界内没有找到 ICoCoContextRestoreBinding 组件——" +
                        "无可连接");
                return false;
            }

            var seen = new HashSet<MonoBehaviour>();
            for (int index = 0; index < chain.Count; index++)
            {
                MonoBehaviour component = chain[index];
                if (component == null)
                {
                    failure = new CoCoBindingHint(
                        CoCoBindingHintKind.Warning,
                        null,
                        "restore chain candidate disappeared while wiring — abort, " +
                            "nothing was written",
                        "连接过程中候选组件失销——中止，未写入任何内容");
                    return false;
                }

                if (!(component is ICoCoContextRestoreBinding))
                {
                    failure = new CoCoBindingHint(
                        CoCoBindingHintKind.Warning,
                        component,
                        component.name + " does not implement " +
                            "ICoCoContextRestoreBinding — abort, nothing was written",
                        component.name + " 未实现 ICoCoContextRestoreBinding——" +
                            "中止，未写入任何内容");
                    return false;
                }

                if (!CoCoStateGraphHostBoundary.Contains(host, component))
                {
                    failure = new CoCoBindingHint(
                        CoCoBindingHintKind.Warning,
                        component,
                        component.name + " is outside the Host boundary — abort, " +
                            "nothing was written",
                        component.name + " 位于 Host 边界之外——中止，未写入任何内容");
                    return false;
                }

                if (!seen.Add(component))
                {
                    failure = new CoCoBindingHint(
                        CoCoBindingHintKind.Warning,
                        component,
                        component.name + " appears twice in the candidates — abort, " +
                            "nothing was written",
                        component.name + " 在候选中出现两次——中止，未写入任何内容");
                    return false;
                }
            }

            var upstreams = new List<MonoBehaviour>(chain.Count - 1);
            var downstreams = new List<MonoBehaviour>(chain.Count - 1);
            for (int index = 0; index + 1 < chain.Count; index++)
            {
                MonoBehaviour upstream = chain[index];
                MonoBehaviour downstream = chain[index + 1];
                if (!(upstream is ICoCoTemporalDecoratorBinding))
                {
                    failure = new CoCoBindingHint(
                        CoCoBindingHintKind.Warning,
                        upstream,
                        upstream.name + " sits before another binding but implements " +
                            "no ICoCoTemporalDecoratorBinding — the chain cannot " +
                            "link, abort, nothing was written",
                        upstream.name + " 位于另一绑定之前但未实现 " +
                            "ICoCoTemporalDecoratorBinding——无法成链，中止，未写入任何内容");
                    return false;
                }

                if (FindDownstreamProperty(upstream) == null)
                {
                    failure = new CoCoBindingHint(
                        CoCoBindingHintKind.Warning,
                        upstream,
                        upstream.name + " has no writable downstreamRestoreBinding " +
                            "field — the chain cannot link, abort, nothing was " +
                            "written",
                        upstream.name + " 没有可写的 downstreamRestoreBinding " +
                            "字段——无法成链，中止，未写入任何内容");
                    return false;
                }

                upstreams.Add(upstream);
                downstreams.Add(downstream);
            }

            MonoBehaviour tail = chain[chain.Count - 1];
            MonoBehaviour tailToClear =
                chain.Count == 1 || FindDownstreamProperty(tail) != null ? tail : null;
            plan = new CoCoRestoreWirePlan(
                chain[0],
                upstreams,
                downstreams,
                tailToClear);
            failure = default;
            return true;
        }

        /// <summary>downstream 序列化字段查找（写入 seam；只读查询）。</summary>
        internal static SerializedProperty FindDownstreamProperty(
            MonoBehaviour component)
        {
            return new SerializedObject(component)
                .FindProperty(DownstreamPropertyName);
        }

        // ----- 场景候选（菜单用；方案 v3 §2.2：边界过滤 + 最近宿主 + 已分配排除） -----

        /// <summary>
        /// Intent Source 候选：复用 CoCoStateGraphHostBindingCandidates
        /// （边界过滤 + 嵌套宿主排除 + 已分配排除；Runtime 对清单槽位
        /// 强制同边界，越界候选会被启动拒绝，不列入）。
        /// </summary>
        internal static void CollectIntentSourceCandidates(
            CoCoStateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            CoCoStateGraphHostBindingCandidates.FindIntentSources(
                host,
                assigned,
                results);
        }

        /// <summary>Operator 候选：同边界规则（Runtime 启动拒绝越界 Operator）。</summary>
        internal static void CollectOperatorCandidates(
            CoCoStateGraphHost host,
            IReadOnlyList<MonoBehaviour> assigned,
            List<MonoBehaviour> results)
        {
            CoCoStateGraphHostBindingCandidates.FindOperators(
                host,
                assigned,
                results);
        }

        /// <summary>
        /// Actor Context 候选：边界内实现 ICoCoActorContextBinding 的组件
        /// （Runtime 要求 Actor 绑定位于边界内），排除已分配。
        /// </summary>
        internal static void CollectActorContextCandidates(
            CoCoStateGraphHost host,
            MonoBehaviour assigned,
            List<MonoBehaviour> results)
        {
            results.Clear();
            if (host == null)
            {
                return;
            }

            MonoBehaviour[] components = host.GetComponentsInChildren<MonoBehaviour>(true);
            for (int index = 0; index < components.Length; index++)
            {
                MonoBehaviour component = components[index];
                if (component != null &&
                    component != assigned &&
                    component is ICoCoActorContextBinding &&
                    CoCoStateGraphHostBoundary.Contains(host, component))
                {
                    results.Add(component);
                }
            }
        }

        // ----- 排序（层级路径，其次类型名；语义保持） -----

        internal static void SortByHierarchy(List<MonoBehaviour> components)
        {
            components.Sort((left, right) =>
            {
                string leftPath = BuildHierarchyPath(left.transform);
                string rightPath = BuildHierarchyPath(right.transform);
                int order = string.CompareOrdinal(leftPath, rightPath);
                if (order != 0)
                {
                    return order;
                }

                return string.CompareOrdinal(
                    left.GetType().Name,
                    right.GetType().Name);
            });
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            var path = new StringBuilder(transform.name);
            Transform parent = transform.parent;
            while (parent != null)
            {
                path.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            return path.ToString();
        }
    }
}
