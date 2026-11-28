using System.Collections.Generic;
using System.Text;
using CoCoFlow.Runtime.StateGraph;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraph
{
    internal static class StateGraphHostRestoreChain
    {
        #region Public API

        internal readonly struct RestoreChainNode
        {
            internal RestoreChainNode(
                MonoBehaviour component,
                bool isRoot,
                bool implementsContract,
                bool isRepeat,
                bool isInsideBoundary,
                bool isDestroyed)
            {
                Component = component;
                IsRoot = isRoot;
                ImplementsContract = implementsContract;
                IsRepeat = isRepeat;
                IsInsideBoundary = isInsideBoundary;
                IsDestroyed = isDestroyed;
            }

            internal MonoBehaviour Component { get; }
            internal bool IsRoot { get; }
            internal bool ImplementsContract { get; }
            internal bool IsRepeat { get; }
            internal bool IsInsideBoundary { get; }
            internal bool IsDestroyed { get; }
        }

        /// <summary>
        /// 从根走查下游链（无深度上限，与 Runtime 校验器一致；环防护经已访问集）
        /// 在首个断点（失销 / 未实现契约 / 重复节点 / 越界节点）处停止并标记。
        /// 失销判定与 Runtime 一致：ReferenceEquals 遍历 + Unity 伪装 null
        /// 检查（TemporalContracts 对失销节点显式拒绝，不静默截断）。
        /// </summary>
        internal static void BuildRestoreChainPreview(
            MonoBehaviour root,
            StateGraphHost host,
            List<RestoreChainNode> nodes)
        {
            nodes.Clear();
            if (ReferenceEquals(root, null))
            {
                return;
            }

            var seen = new HashSet<MonoBehaviour>();
            MonoBehaviour current = root;
            while (!ReferenceEquals(current, null))
            {
                bool isDestroyed = current == null; // Unity 伪装 null：真实引用存在但已失销
                bool implementsContract =
                    !isDestroyed && current is IHostRestoreBinding;
                bool isRepeat = !isDestroyed && !seen.Add(current);
                bool isInsideBoundary =
                    !isDestroyed &&
                    HostComponentBoundary.For(host).Contains(current);
                nodes.Add(new RestoreChainNode(
                    current,
                    ReferenceEquals(current, root),
                    implementsContract,
                    isRepeat,
                    isInsideBoundary,
                    isDestroyed));
                if (isDestroyed || !implementsContract || isRepeat ||
                    !isInsideBoundary)
                {
                    return;
                }

                current = (current as IHostRestoreDecorator)
                    ?.DownstreamRestoreBinding;
            }
        }

        /// <summary>链上首个断点提示（无断点返回 null）：未实现契约 / 环 / 越界。</summary>
        internal static BindingHint? BuildRestoreChainBreakHint(
            IReadOnlyList<RestoreChainNode> nodes)
        {
            if (nodes == null)
            {
                return null;
            }

            for (int index = 0; index < nodes.Count; index++)
            {
                RestoreChainNode node = nodes[index];
                if (node.ImplementsContract && !node.IsRepeat && node.IsInsideBoundary)
                {
                    continue;
                }

                if (node.IsDestroyed)
                {
                    return new BindingHint(
                        BindingHintKind.Error,
                        node.Component,
                        "a destroyed object breaks the chain — the runtime " +
                            "rejects destroyed restore bindings",
                        "链中存在已失销对象——运行时拒绝失销的恢复绑定");
                }

                if (!node.ImplementsContract)
                {
                    return new BindingHint(
                        BindingHintKind.Error,
                        node.Component,
                        node.Component.name + " breaks the chain — it implements no " +
                            "IHostRestoreBinding",
                        node.Component.name + " 中断链——它未实现 " +
                            "IHostRestoreBinding");
                }

                if (!node.IsInsideBoundary)
                {
                    return new BindingHint(
                        BindingHintKind.Error,
                        node.Component,
                        node.Component.name + " is outside the Host boundary — every " +
                            "chain node must stay inside the same Host boundary or " +
                            "the runtime rejects the chain",
                        node.Component.name + " 位于 Host 边界之外——链上每个节点" +
                            "必须留在同一 Host 边界内，否则运行时拒绝该链");
                }

                return new BindingHint(
                    BindingHintKind.Error,
                    node.Component,
                    node.Component.name + " appears twice in the chain — restore " +
                        "chains must be acyclic",
                    node.Component.name + " 在链中出现两次——恢复链必须无环");
            }

            return null;
        }

        // ----- 自动连接候选（D5：边界内扫描 + 层级排序） -----

        /// <summary>
        /// 收集 Host 边界内全部 IHostRestoreBinding 组件并按
        /// 层级路径（其次类型名）排序——自动连接的候选链。
        /// </summary>
        internal static void CollectRestoreChainCandidates(
            StateGraphHost host,
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
                    component is IHostRestoreBinding && !(component is IOperator) &&
                    HostComponentBoundary.For(host).Contains(component))
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
        internal readonly struct RestoreWirePlan
        {
            internal RestoreWirePlan(
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
        /// IHostRestoreDecorator 并具备可写的 downstreamRestoreBinding
        /// 序列化字段；尾节点如具备该字段则列入清空目标。任何目标缺失立即失败。
        /// </summary>
        internal static bool TryBuildRestoreWirePlan(
            StateGraphHost host,
            IReadOnlyList<MonoBehaviour> chain,
            out RestoreWirePlan plan,
            out BindingHint failure)
        {
            plan = default;
            if (chain == null || chain.Count == 0)
            {
                failure = new BindingHint(
                    BindingHintKind.Warning,
                    null,
                    "no IHostRestoreBinding components found inside the Host " +
                        "boundary — nothing to wire",
                    "Host 边界内没有找到 IHostRestoreBinding 组件——" +
                        "无可连接");
                return false;
            }

            var seen = new HashSet<MonoBehaviour>();
            for (int index = 0; index < chain.Count; index++)
            {
                MonoBehaviour component = chain[index];
                if (component == null)
                {
                    failure = new BindingHint(
                        BindingHintKind.Warning,
                        null,
                        "restore chain candidate disappeared while wiring — abort, " +
                            "nothing was written",
                        "连接过程中候选组件失销——中止，未写入任何内容");
                    return false;
                }

                if (!(component is IHostRestoreBinding))
                {
                    failure = new BindingHint(
                        BindingHintKind.Warning,
                        component,
                        component.name + " does not implement " +
                            "IHostRestoreBinding — abort, nothing was written",
                        component.name + " 未实现 IHostRestoreBinding——" +
                            "中止，未写入任何内容");
                    return false;
                }

                if (!HostComponentBoundary.For(host).Contains(component))
                {
                    failure = new BindingHint(
                        BindingHintKind.Warning,
                        component,
                        component.name + " is outside the Host boundary — abort, " +
                            "nothing was written",
                        component.name + " 位于 Host 边界之外——中止，未写入任何内容");
                    return false;
                }

                if (!seen.Add(component))
                {
                    failure = new BindingHint(
                        BindingHintKind.Warning,
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
                if (!(upstream is IHostRestoreDecorator))
                {
                    failure = new BindingHint(
                        BindingHintKind.Warning,
                        upstream,
                        upstream.name + " sits before another binding but implements " +
                            "no IHostRestoreDecorator — the chain cannot " +
                            "link, abort, nothing was written",
                        upstream.name + " 位于另一绑定之前但未实现 " +
                            "IHostRestoreDecorator——无法成链，中止，未写入任何内容");
                    return false;
                }

                if (FindDownstreamProperty(upstream) == null)
                {
                    failure = new BindingHint(
                        BindingHintKind.Warning,
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
            plan = new RestoreWirePlan(
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
                .FindProperty(StateGraphHostBindingRules.DownstreamPropertyName);
        }

        #endregion

        #region Internal Logic

        private static void SortByHierarchy(List<MonoBehaviour> components)
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

        #endregion
    }
}
