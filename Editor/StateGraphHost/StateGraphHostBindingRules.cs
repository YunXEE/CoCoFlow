using System;
using System.Collections.Generic;
using System.Text;
using CoCoFlow.Editor.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    /// <summary>装配期提示级别（N6：authoring hints，非启动权威）。</summary>
    internal enum BindingHintKind
    {
        Warning = 1,
        Error = 2
    }

    /// <summary>
    /// 一条装配期提示：级别 + 定位目标 + 双语文案。
    /// 规则层给出结构化结果；文案在这里成对烘焙，渲染层只取
    /// <see cref="LocalizedText"/>。通过提示不宣称启动必成功（启动权威
    /// Runtime-deferred：唯一性全集、精确泛型、Descriptor、图覆盖）。
    /// </summary>
    internal readonly struct BindingHint
    {
        internal BindingHint(
            BindingHintKind kind,
            MonoBehaviour target,
            string english,
            string chinese)
        {
            Kind = kind;
            Target = target;
            English = english;
            Chinese = chinese;
        }

        internal BindingHintKind Kind { get; }
        internal MonoBehaviour Target { get; }
        internal string English { get; }
        internal string Chinese { get; }
        internal string LocalizedText => CoCoEditorLocalization.Text(English, Chinese);
    }

    /// <summary>
    /// Host 绑定装配规则（D3/D11）：只读、无状态、镜像 Runtime 启动纪律中
    /// 静态可判定的子集；不写任何序列化数据。
    /// </summary>
    internal static class StateGraphHostBindingRules
    {
        #region Public API

        internal const string DownstreamPropertyName = "downstreamRestoreBinding";

        // ----- 接口识别 -----

        internal static bool IsIntentFrameSource(MonoBehaviour component)
        {
            if (component == null)
            {
                return false;
            }

            Type[] interfaces = component.GetType().GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type contract = interfaces[index];
                if (contract.IsGenericType &&
                    contract.GetGenericTypeDefinition() == typeof(IIntentFrameSource<>))
                {
                    return true;
                }
            }

            return false;
        }

        internal static bool IsOperator(MonoBehaviour component)
        {
            return component != null && component is IOperator;
        }

        internal static bool IsActorContextBinding(MonoBehaviour component)
        {
            return component != null && component is IHostContextBinding;
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
                    " implements no IIntentFrameSource<T> interface";
                chinese = component.GetType().Name +
                    " 未实现任何 IIntentFrameSource<T> 接口";
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
        /// Intent Source 装配提示：空引用（Error；Runtime 冻结要求每个索引恰好绑定
        /// 一次，null 条目导致无法启动）、无接口（Error）、越界（Warning）。
        /// </summary>
        internal static BindingHint? BuildIntentSourceHint(
            StateGraphHost host,
            MonoBehaviour component)
        {
            if (component == null)
            {
                return new BindingHint(
                    BindingHintKind.Error,
                    null,
                    "empty Intent Source slot — every Host Intent Source index " +
                        "must bind exactly once at startup; remove the entry or " +
                        "assign a component",
                    "Intent Source 空槽位——运行时冻结要求每个索引恰好绑定一次；" +
                        "请移除该条目或指定组件");
            }

            if (!IsIntentFrameSource(component))
            {
                DescribeIntentSource(component, out string english, out string chinese);
                return new BindingHint(
                    BindingHintKind.Error,
                    component,
                    english + " — it will be rejected at startup",
                    chinese + "——启动时将被拒绝");
            }

            if (!HostComponentBoundary.For(host).Contains(component))
            {
                return new BindingHint(
                    BindingHintKind.Warning,
                    component,
                    component.name + " is outside the Host boundary — the runtime " +
                        "requires manifest-bound Intent Sources inside the Host subtree.",
                    component.name + " 位于 Host 边界之外——运行时要求清单绑定的 " +
                        "Intent Source 位于 Host 子树内。");
            }

            return null;
        }

        /// <summary>Operator 装配提示：空引用（Error；Runtime 对任何 null Operator 条目整体拒绝启动）、无接口（Error）、越界（Warning）。</summary>
        internal static BindingHint? BuildOperatorHint(
            StateGraphHost host,
            MonoBehaviour component)
        {
            if (component == null)
            {
                return new BindingHint(
                    BindingHintKind.Error,
                    null,
                    "empty Operator slot — the runtime rejects every Host Operator " +
                        "entry before Running, so the Host cannot start",
                    "Operator 空槽位——运行时在 Running 前逐条校验 Operator 数组，" +
                        "Host 无法启动");
            }

            if (!IsOperator(component))
            {
                return new BindingHint(
                    BindingHintKind.Error,
                    component,
                    component.GetType().Name + " implements no IOperator " +
                        "interface — it will be rejected at startup",
                    component.GetType().Name + " 未实现 IOperator 接口——" +
                        "启动时将被拒绝");
            }

            if (!HostComponentBoundary.For(host).Contains(component))
            {
                return new BindingHint(
                    BindingHintKind.Warning,
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
        internal static BindingHint? BuildActorContextHint(
            StateGraphHost host,
            MonoBehaviour component)
        {
            if (component == null)
            {
                return null;
            }

            if (!IsActorContextBinding(component))
            {
                return new BindingHint(
                    BindingHintKind.Error,
                    component,
                    component.name + " does not implement IHostContextBinding " +
                        "— startup rejects the Actor Context binding",
                    component.name + " 未实现 IHostContextBinding——启动时" +
                        "将拒绝该 Actor Context 绑定");
            }

            if (!HostComponentBoundary.For(host).Contains(component))
            {
                return new BindingHint(
                    BindingHintKind.Warning,
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
        internal static BindingHint? BuildRestoreRootHint(
            StateGraphHost host,
            MonoBehaviour root)
        {
            if (root == null)
            {
                return null;
            }

            if (root is IOperator)
                return new BindingHint(BindingHintKind.Error, root,
                    "Remove this Operator binding and use its Host recovery switches.",
                    "请移除旧 Operator 恢复绑定，改用 Host 中的两个开关。");
            if (!(root is IHostRestoreBinding))
            {
                return new BindingHint(
                    BindingHintKind.Error,
                    root,
                    root.name + " does not implement IHostRestoreBinding — " +
                        "enabled temporal history will reject this binding",
                    root.name + " 未实现 IHostRestoreBinding——恢复投影" +
                        "启用时间历史时将拒绝此绑定");
            }

            if (!HostComponentBoundary.For(host).Contains(root))
            {
                return new BindingHint(
                    BindingHintKind.Warning,
                    root,
                    root.name + " is outside the Host boundary — the Temporal " +
                        "controller drops it silently at startup. Move it into the " +
                        "Host subtree or pick another component",
                    root.name + " 位于 Host 边界之外——Temporal 控制器启动时会" +
                        "静默丢弃它。请移入 Host 子树或另选组件");
            }

            return null;
        }

        /// <summary>数组内重复引用（返回第二次及以后出现者的索引；Runtime 要求组件唯一）。</summary>
        internal static List<int> FindDuplicateIndices(
            IReadOnlyList<MonoBehaviour> references)
        {
            var duplicateIndices = new List<int>();
            if (references == null)
            {
                return duplicateIndices;
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
                    duplicateIndices.Add(index);
                }
            }

            return duplicateIndices;
        }

        #endregion

        #region Internal Logic

        private static List<Type> GetIntentPayloadTypes(Type type)
        {
            var payloadTypes = new List<Type>();
            Type[] interfaces = type.GetInterfaces();
            for (int index = 0; index < interfaces.Length; index++)
            {
                Type contract = interfaces[index];
                if (contract.IsGenericType &&
                    contract.GetGenericTypeDefinition() ==
                        typeof(IIntentFrameSource<>))
                {
                    payloadTypes.Add(contract.GetGenericArguments()[0]);
                }
            }

            return payloadTypes;
        }

        #endregion
    }
}
