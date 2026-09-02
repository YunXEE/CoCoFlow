using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraphHost
{
    /// <summary>
    /// StateGraphHost Inspector（UI Toolkit 重做，D1/D2）。
    /// 六区信息架构：Overview / Bindings / Restore / Capacities / Runtime / Diagnostics。
    /// 契约：全部写操作经 SerializedProperty（Undo/Prefab Override 正确）；
    /// live Runtime（HasLiveRuntime，含 Running/Suspended）期间四配置区写禁用
    /// （Pre7 收口，B1）；单目标检视（不加 CanEditMultipleObjects，N1）；
    /// 验证为 authoring hints（N6），启动权威 Runtime-deferred。
    /// 生命周期：CreateInspectorElement 零订阅；订阅只在 OnEnable/OnDisable 对称。
    /// </summary>
    [CustomEditor(typeof(CoCoStateGraphHost))]
    internal sealed class CoCoStateGraphHostEditor : UnityEditor.Editor
    {
        private const string ModuleUssPath =
            "Packages/com.yunxee.cocoflow/Editor/StateGraphHost/CoCoStateGraphHostEditor.uss";

        private const string DownstreamPropertyName = "downstreamRestoreBinding";

        private static bool _moduleUssMissingReported;

        private VisualElement _root;
        private VisualElement _configRoot;
        private VisualElement _bindingsContent;
        private VisualElement _restorePreviewContent;
        private VisualElement _restoreHintContent;
        private VisualElement _runtimeContent;
        private VisualElement _diagnosticsContent;
        private bool _rebuilding;

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            Undo.undoRedoPerformed += OnUndoRedoPerformed;
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            Undo.undoRedoPerformed -= OnUndoRedoPerformed;
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged;
        }

        public override VisualElement CreateInspectorElement()
        {
            _root = new VisualElement { name = "ccflow-host-inspector" };
            CoCoEditorElements.ApplyTheme(_root);
            ApplyModuleTheme(_root);

            // 外部变化（含 Undo/Prefab 应用）→ 重建动态区；自身重建不触发。
            _root.TrackSerializedObjectValue(
                serializedObject,
                _ =>
                {
                    if (_rebuilding)
                    {
                        return;
                    }

                    serializedObject.Update();
                    RebuildDynamicSections();
                });

            // live 写保护门 + Runtime 区刷新（B1）。
            _root.schedule.Execute(UpdateDynamic).Every(500);

            RebuildAll();
            return _root;
        }

        // ===== 主题 =====

        private static void ApplyModuleTheme(VisualElement root)
        {
            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ModuleUssPath);
            if (styleSheet == null)
            {
                if (!_moduleUssMissingReported)
                {
                    _moduleUssMissingReported = true;
                    Debug.LogError(
                        "[CoCoStateGraphHostEditor] module style sheet missing at " +
                        ModuleUssPath + "; inspector falls back to shared theme only.");
                }

                return;
            }

            if (!root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        // ===== 重建入口 =====

        private void RebuildAll()
        {
            try
            {
                _rebuilding = true;
                serializedObject.Update();
                _root.Clear();
                BuildConfigSections();
                BuildRuntimeSection();
                BuildDiagnosticsSection();
                _root.Bind(serializedObject);
                RebuildDynamicSections();
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private void RebuildDynamicSections()
        {
            RebuildBindings();
            RebuildRestorePreview();
            RebuildDiagnostics();
            UpdateDynamic();
        }

        private void BuildConfigSections()
        {
            _configRoot = new VisualElement { name = "ccflow-host-config" };
            _configRoot.Add(BuildOverviewCard());
            _configRoot.Add(BuildBindingsCard());
            _configRoot.Add(BuildRestoreCard());
            _configRoot.Add(BuildCapacitiesCard());
            _root.Add(_configRoot);
        }

        // ===== Overview =====

        private VisualElement BuildOverviewCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Overview", "总览"));
            card.Add(new PropertyField(
                serializedObject.FindProperty("stateGraphAsset"),
                CoCoEditorLocalization.Text("State Graph Asset", "状态图资产")));
            card.Add(new PropertyField(
                serializedObject.FindProperty("driver"),
                CoCoEditorLocalization.Text("Driver", "驱动")));
            card.Add(new PropertyField(
                serializedObject.FindProperty("autoStart"),
                CoCoEditorLocalization.Text("Auto Start", "自动启动")));
            card.Add(new PropertyField(
                serializedObject.FindProperty("timeScale"),
                CoCoEditorLocalization.Text("Time Scale", "时间倍率")));
            return card;
        }

        // ===== Bindings（三数组 + Actor Context 单引用，B3） =====

        private VisualElement BuildBindingsCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Bindings", "绑定"));
            _bindingsContent = new VisualElement { name = "ccflow-host-bindings" };
            card.Add(_bindingsContent);
            return card;
        }

        private void RebuildBindings()
        {
            if (_bindingsContent == null)
            {
                return;
            }

            _rebuilding = true;
            try
            {
                _bindingsContent.Clear();
                serializedObject.Update();

                BuildBindingList(
                    _bindingsContent,
                    "intentSources",
                    CoCoEditorLocalization.Text("Intent Sources", "Intent 来源"),
                    BindingListKind.IntentSources);
                BuildEventAdaptersSection();
                BuildBindingList(
                    _bindingsContent,
                    "operators",
                    CoCoEditorLocalization.Text("Operators", "Operator"),
                    BindingListKind.Operators);
                BuildActorContextSection();
                _bindingsContent.Bind(serializedObject);
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private enum BindingListKind
        {
            IntentSources = 0,
            Operators = 1
        }

        private void BuildBindingList(
            VisualElement container,
            string propertyPath,
            string title,
            BindingListKind kind)
        {
            container.Add(CreateSubsectionTitle(title));
            SerializedProperty array = serializedObject.FindProperty(propertyPath);

            var assigned = new List<MonoBehaviour>();
            for (int index = 0; index < array.arraySize; index++)
            {
                assigned.Add(
                    array.GetArrayElementAtIndex(index).objectReferenceValue as
                        MonoBehaviour);
            }

            List<MonoBehaviour> duplicates =
                CoCoStateGraphHostBindingRules.FindDuplicateReferences(assigned);
            var host = (CoCoStateGraphHost)target;

            for (int index = 0; index < array.arraySize; index++)
            {
                container.Add(BuildBindingRow(
                    array,
                    index,
                    host,
                    kind,
                    duplicates));
            }

            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.marginTop = 4f;
            var addFromScene = new Button(() =>
                ShowSceneMenu(array, kind))
            {
                text = CoCoEditorLocalization.Text("Add from scene…", "从场景添加…")
            };
            var footerHint = new Label(
                CoCoEditorLocalization.Text(
                    "drop components above or pick from the scene",
                    "拖入组件或从场景选择"))
            {
                name = "ccflow-binding-hint"
            };
            footer.Add(addFromScene);
            footer.Add(footerHint);
            footerHint.AddToClassList("ccflow-muted");
            footerHint.style.marginLeft = 8f;
            footerHint.style.unityTextAlign = TextAnchor.MiddleLeft;
            container.Add(footer);
        }

        private VisualElement BuildBindingRow(
            SerializedProperty array,
            int index,
            CoCoStateGraphHost host,
            BindingListKind kind,
            List<MonoBehaviour> duplicates)
        {
            var row = new VisualElement();
            row.AddToClassList("ccflow-host-binding-row");

            var head = new VisualElement();
            head.AddToClassList("ccflow-host-binding-row__head");

            var field = new ObjectField { allowSceneObjects = true };
            field.AddToClassList("ccflow-host-binding-row__field");
            field.BindProperty(array.GetArrayElementAtIndex(index));
            head.Add(field);

            var remove = new Button(() => RemoveArrayElement(array, index))
            {
                text = "×",
                tooltip = CoCoEditorLocalization.Text("Remove", "移除")
            };
            remove.AddToClassList("ccflow-host-binding-row__remove");
            head.Add(remove);
            row.Add(head);

            MonoBehaviour component =
                array.GetArrayElementAtIndex(index).objectReferenceValue as MonoBehaviour;
            CoCoBindingHint? hint = kind == BindingListKind.IntentSources
                ? CoCoStateGraphHostBindingRules.BuildIntentSourceHint(host, component)
                : CoCoStateGraphHostBindingRules.BuildOperatorHint(host, component);

            string english;
            string chinese;
            if (kind == BindingListKind.IntentSources)
            {
                CoCoStateGraphHostBindingRules.DescribeIntentSource(
                    component, out english, out chinese);
            }
            else
            {
                CoCoStateGraphHostBindingRules.DescribeOperator(
                    component, out english, out chinese);
            }

            var description = new Label(
                CoCoEditorLocalization.Text(english, chinese))
            {
                name = "ccflow-binding-desc"
            };
            description.AddToClassList("ccflow-host-binding-row__desc");
            row.Add(description);

            if (hint.HasValue)
            {
                row.Add(BuildHintRow(hint.Value));
            }

            if (component != null && duplicates.Contains(component))
            {
                row.Add(BuildHintRow(new CoCoBindingHint(
                    CoCoBindingHintKind.Warning,
                    component,
                    component.name + " is referenced more than once — the runtime " +
                        "requires unique binding components",
                    component.name + " 被引用多次——运行时要求绑定组件唯一")));
            }

            return row;
        }

        private void RemoveArrayElement(SerializedProperty array, int index)
        {
            _rebuilding = true;
            try
            {
                serializedObject.Update();
                array.DeleteArrayElementAtIndex(index);
                serializedObject.ApplyModifiedProperties();
            }
            finally
            {
                _rebuilding = false;
            }

            RebuildDynamicSections();
        }

        private void ShowSceneMenu(SerializedProperty array, BindingListKind kind)
        {
            var menu = new GenericMenu();
            var assigned = new List<MonoBehaviour>();
            serializedObject.Update();
            for (int index = 0; index < array.arraySize; index++)
            {
                assigned.Add(
                    array.GetArrayElementAtIndex(index).objectReferenceValue as
                        MonoBehaviour);
            }

            var host = (CoCoStateGraphHost)target;
            var results = new List<MonoBehaviour>();
            if (kind == BindingListKind.IntentSources)
            {
                CoCoStateGraphHostBindingRules.CollectIntentSourceCandidates(
                    host, assigned, results);
            }
            else
            {
                CoCoStateGraphHostBindingRules.CollectOperatorCandidates(
                    host, assigned, results);
            }

            bool any = false;
            foreach (MonoBehaviour component in results)
            {
                if (component == null)
                {
                    continue;
                }

                any = true;
                MonoBehaviour captured = component;
                string english;
                string chinese;
                if (kind == BindingListKind.IntentSources)
                {
                    CoCoStateGraphHostBindingRules.DescribeIntentSource(
                        captured, out english, out chinese);
                }
                else
                {
                    CoCoStateGraphHostBindingRules.DescribeOperator(
                        captured, out english, out chinese);
                }

                menu.AddItem(
                    new GUIContent(
                        CoCoEditorLocalization.Text(english, chinese) +
                        " @ " + captured.name),
                    false,
                    () =>
                    {
                        _rebuilding = true;
                        try
                        {
                            serializedObject.Update();
                            int next = array.arraySize;
                            array.InsertArrayElementAtIndex(next);
                            array.GetArrayElementAtIndex(next).objectReferenceValue =
                                captured;
                            serializedObject.ApplyModifiedProperties();
                        }
                        finally
                        {
                            _rebuilding = false;
                        }

                        RebuildDynamicSections();
                    });
            }

            if (!any)
            {
                menu.AddDisabledItem(new GUIContent(
                    CoCoEditorLocalization.Text(
                        "no matching components inside the Host boundary",
                        "Host 边界内没有匹配组件")));
            }

            menu.ShowAsContext();
        }

        // ----- Event Adapters：有序列表 + 提示，不做槽位验证（B4/D4） -----

        private void BuildEventAdaptersSection()
        {
            _bindingsContent.Add(CreateSubsectionTitle(
                CoCoEditorLocalization.Text("Event Adapters", "Event 适配器")));
            _bindingsContent.Add(new PropertyField(
                serializedObject.FindProperty("eventAdapters"),
                CoCoEditorLocalization.Text("Event Adapters", "Event 适配器")));
            var note = new Label(CoCoEditorLocalization.Text(
                "ordered slot-for-slot against the compiled adapter manifest — " +
                    "the runtime validates each slot at startup",
                "按编译适配器清单逐槽有序对应——运行时在启动时逐槽校验"))
            {
                name = "ccflow-adapters-note"
            };
            note.AddToClassList("ccflow-muted");
            note.style.whiteSpace = WhiteSpace.Normal;
            note.style.marginBottom = 6f;
            _bindingsContent.Add(note);
        }

        // ----- Actor Context：单引用 + 候选 + 浅验证（B3/D3） -----

        private void BuildActorContextSection()
        {
            _bindingsContent.Add(CreateSubsectionTitle(
                CoCoEditorLocalization.Text("Actor Context", "Actor 上下文")));

            SerializedProperty property =
                serializedObject.FindProperty("actorContextBinding");
            MonoBehaviour assigned = property.objectReferenceValue as MonoBehaviour;
            var host = (CoCoStateGraphHost)target;
            if (assigned != null)
            {
                CoCoBindingHint? hint =
                    CoCoStateGraphHostBindingRules.BuildActorContextHint(host, assigned);
                if (hint.HasValue)
                {
                    _bindingsContent.Add(BuildHintRow(hint.Value));
                }
            }

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var field = new PropertyField(
                property,
                CoCoEditorLocalization.Text("Actor Context", "Actor 上下文"))
            {
                style = { flexGrow = 1f }
            };
            row.Add(field);
            var pick = new Button(() => ShowActorContextMenu(property))
            {
                text = CoCoEditorLocalization.Text("Pick…", "选择…"),
                tooltip = CoCoEditorLocalization.Text(
                    "pick an Actor Context binding inside the Host boundary",
                    "选择 Host 边界内的 Actor Context 绑定")
            };
            row.Add(pick);
            _bindingsContent.Add(row);
        }

        private void ShowActorContextMenu(SerializedProperty property)
        {
            serializedObject.Update();
            MonoBehaviour assigned =
                property.objectReferenceValue as MonoBehaviour;
            var host = (CoCoStateGraphHost)target;
            var results = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingRules.CollectActorContextCandidates(
                host,
                assigned,
                results);

            var menu = new GenericMenu();
            if (assigned != null)
            {
                menu.AddItem(
                    new GUIContent(CoCoEditorLocalization.Text("(none)", "（无）")),
                    false,
                    () =>
                    {
                        _rebuilding = true;
                        try
                        {
                            serializedObject.Update();
                            property.objectReferenceValue = null;
                            serializedObject.ApplyModifiedProperties();
                        }
                        finally
                        {
                            _rebuilding = false;
                        }

                        RebuildDynamicSections();
                    });
                menu.AddSeparator(string.Empty);
            }

            bool any = false;
            foreach (MonoBehaviour component in results)
            {
                any = true;
                MonoBehaviour captured = component;
                menu.AddItem(
                    new GUIContent(captured.GetType().Name + " @ " + captured.name),
                    false,
                    () =>
                    {
                        _rebuilding = true;
                        try
                        {
                            serializedObject.Update();
                            property.objectReferenceValue = captured;
                            serializedObject.ApplyModifiedProperties();
                        }
                        finally
                        {
                            _rebuilding = false;
                        }

                        RebuildDynamicSections();
                    });
            }

            if (!any)
            {
                menu.AddDisabledItem(new GUIContent(
                    CoCoEditorLocalization.Text(
                        "no Actor Context bindings inside the Host boundary",
                        "Host 边界内没有 Actor Context 绑定")));
            }

            menu.ShowAsContext();
        }

        // ===== Restore（D5：行为保持 + 原子 Undo，B2） =====

        private VisualElement BuildRestoreCard()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Restore", "恢复"));
            card.Add(new PropertyField(
                serializedObject.FindProperty("contextRestoreBinding"),
                CoCoEditorLocalization.Text("Context Restore Binding", "上下文恢复绑定")));

            var wire = CoCoEditorElements.CreatePrimaryButton(
                CoCoEditorLocalization.Text("Auto-wire chain", "自动连接链"),
                AutoWireRestoreChain);
            wire.style.marginTop = 4f;
            card.Add(wire);

            _restoreHintContent = new VisualElement { name = "ccflow-restore-hints" };
            card.Add(_restoreHintContent);
            _restorePreviewContent = new VisualElement { name = "ccflow-restore-chain" };
            card.Add(_restorePreviewContent);
            return card;
        }

        private void RebuildRestorePreview()
        {
            if (_restorePreviewContent == null || _restoreHintContent == null)
            {
                return;
            }

            _restoreHintContent.Clear();
            _restorePreviewContent.Clear();

            var host = (CoCoStateGraphHost)target;
            MonoBehaviour root = serializedObject.FindProperty("contextRestoreBinding")
                .objectReferenceValue as MonoBehaviour;

            CoCoBindingHint? rootHint =
                CoCoStateGraphHostBindingRules.BuildRestoreRootHint(host, root);
            if (rootHint.HasValue)
            {
                _restoreHintContent.Add(BuildHintRow(rootHint.Value));
            }

            if (root == null)
            {
                var empty = new Label(CoCoEditorLocalization.Text(
                    "no root wired — save/load and temporal restore will not " +
                        "project the world back onto the ledger",
                    "未连接根——存/读档与时间恢复不会把世界投影回账本"));
                empty.AddToClassList("ccflow-muted");
                empty.style.whiteSpace = WhiteSpace.Normal;
                _restorePreviewContent.Add(empty);
                return;
            }

            var nodes = new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
            CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(root, host, nodes);
            for (int index = 0; index < nodes.Count; index++)
            {
                CoCoStateGraphHostBindingRules.CoCoRestoreChainNode node = nodes[index];
                var row = new VisualElement();
                row.AddToClassList("ccflow-host-chain-row");
                var arrow = new Label(node.IsRoot ? "root →" : "      →")
                {
                    name = "ccflow-chain-arrow"
                };
                arrow.AddToClassList("ccflow-host-chain-row__arrow");
                row.Add(arrow);
                var text = new Label(
                    node.IsDestroyed
                        ? CoCoEditorLocalization.Text(
                            "destroyed object",
                            "已失销对象")
                        : node.Component.GetType().Name + " @ " +
                            node.Component.name)
                {
                    name = "ccflow-chain-text"
                };
                text.AddToClassList("ccflow-host-chain-row__text");
                if (!node.ImplementsContract || node.IsRepeat || node.IsDestroyed)
                {
                    text.style.unityFontStyleAndWeight = FontStyle.Bold;
                }

                row.Add(text);
                _restorePreviewContent.Add(row);
            }

            CoCoBindingHint? breakHint =
                CoCoStateGraphHostBindingRules.BuildRestoreChainBreakHint(nodes);
            if (breakHint.HasValue)
            {
                _restoreHintContent.Add(BuildHintRow(breakHint.Value));
            }
        }

    /// <summary>
    /// 自动连接（B2 原子性）：TryBuildRestoreWirePlan 零写入解析全部目标
    /// → 一次性 Record 全部受影响对象 → 统一写入 → 折叠单一 Undo 组；
    /// 校验失败零写入。记录经 CoCoLog（D7）。
    /// </summary>
    private void AutoWireRestoreChain()
    {
        var host = (CoCoStateGraphHost)target;
        var chain = new List<MonoBehaviour>();
        CoCoStateGraphHostBindingRules.CollectRestoreChainCandidates(host, chain);
        if (!CoCoStateGraphHostBindingRules.TryBuildRestoreWirePlan(
                host,
                chain,
                out CoCoStateGraphHostBindingRules.CoCoRestoreWirePlan plan,
                out CoCoBindingHint failure))
        {
            CoCoLog.Warning("[Host Inspector] " + failure.English);
            return;
        }

        serializedObject.Update();
        SerializedProperty rootProperty =
            serializedObject.FindProperty("contextRestoreBinding");

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName(CoCoEditorLocalization.Text(
            "Auto-wire Restore Chain",
            "自动连接 Restore 链"));
        try
        {
            // 1) 先全量 Record（host + 全部待写节点），失败点在写入前。
            Undo.RecordObject(host, "Auto-wire Restore Chain");
            var memberWrites =
                new List<(SerializedObject Serialized, MonoBehaviour Target)>();
            for (int index = 0; index < plan.Upstreams.Count; index++)
            {
                memberWrites.Add((
                    new SerializedObject(plan.Upstreams[index]),
                    plan.Upstreams[index]));
                Undo.RecordObject(
                    plan.Upstreams[index],
                    "Auto-wire Restore Chain");
            }

            if (plan.TailToClear != null &&
                !Contains(plan.Upstreams, plan.TailToClear))
            {
                memberWrites.Add((
                    new SerializedObject(plan.TailToClear),
                    plan.TailToClear));
                Undo.RecordObject(plan.TailToClear, "Auto-wire Restore Chain");
            }

            // 2) 统一写入：root → 全部链接 → 尾节点清空。
            rootProperty.objectReferenceValue = plan.Root;
            serializedObject.ApplyModifiedProperties();

            for (int index = 0; index < plan.Upstreams.Count; index++)
            {
                WriteDownstream(
                    memberWrites[index].Serialized,
                    plan.Downstreams[index]);
            }

            if (plan.TailToClear != null)
            {
                WriteDownstream(
                    FindMemberWrite(memberWrites, plan.TailToClear),
                    null);
            }
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }

        var names = new System.Text.StringBuilder();
        for (int index = 0; index < chain.Count; index++)
        {
            if (index > 0)
            {
                names.Append(" -> ");
            }

            names.Append(chain[index].GetType().Name);
        }

        CoCoLog.Log("[Host Inspector] Restore chain wired: " + names);
        RebuildDynamicSections();
    }

    private static bool Contains(
        IReadOnlyList<MonoBehaviour> list,
        MonoBehaviour value)
    {
        for (int index = 0; index < list.Count; index++)
        {
            if (ReferenceEquals(list[index], value))
            {
                return true;
            }
        }

        return false;
    }

    private static SerializedObject FindMemberWrite(
        List<(SerializedObject Serialized, MonoBehaviour Target)> memberWrites,
        MonoBehaviour target)
    {
        for (int index = 0; index < memberWrites.Count; index++)
        {
            if (ReferenceEquals(memberWrites[index].Target, target))
            {
                return memberWrites[index].Serialized;
            }
        }

        return null;
    }

    private static void WriteDownstream(
        SerializedObject upstream,
        MonoBehaviour value)
    {
        if (upstream == null)
        {
            return;
        }

        SerializedProperty downstream = upstream.FindProperty(
            CoCoStateGraphHostBindingRules.DownstreamPropertyName);
        if (downstream == null)
        {
            return;
        }

        downstream.objectReferenceValue = value;
        upstream.ApplyModifiedProperties();
    }

    // ===== Capacities =====

    private VisualElement BuildCapacitiesCard()
    {
        VisualElement card = CoCoEditorElements.CreateCard(
            CoCoEditorLocalization.Text("Capacities", "容量"));
        card.Add(new PropertyField(
            serializedObject.FindProperty("temporalHistoryCapacity"),
            CoCoEditorLocalization.Text("Temporal History", "时间历史容量")));
        card.Add(new PropertyField(
            serializedObject.FindProperty("contextFrameCapacity"),
            CoCoEditorLocalization.Text("Context Frames", "上下文帧容量")));
        card.Add(new PropertyField(
            serializedObject.FindProperty("eventOutboxCapacity"),
            CoCoEditorLocalization.Text("Event Outbox", "事件发件容量")));
        card.Add(new PropertyField(
            serializedObject.FindProperty("traceCapacity"),
            CoCoEditorLocalization.Text("Trace", "Trace 容量")));
        card.Add(new PropertyField(
            serializedObject.FindProperty("eventLaneCapacity"),
            CoCoEditorLocalization.Text("Event Lanes", "事件通道容量")));
        card.Add(new PropertyField(
            serializedObject.FindProperty("eventSourceCapacity"),
            CoCoEditorLocalization.Text("Event Sources", "事件源容量")));
        card.Add(new PropertyField(
            serializedObject.FindProperty("eventDedupCapacity"),
            CoCoEditorLocalization.Text("Event Dedup", "事件去重容量")));

        var note = new Label(CoCoEditorLocalization.Text(
            "Trace capacity defaults to 0 and applies when the runtime starts; " +
            "it cannot change while the runtime is live",
            "Trace 容量默认 0，在运行时启动时生效；运行期间不可变更"))
        {
            name = "ccflow-capacity-note"
        };
        note.AddToClassList("ccflow-muted");
        note.style.whiteSpace = WhiteSpace.Normal;
        card.Add(note);
        return card;
}

    // ===== Runtime 状态（只读） =====

    private void BuildRuntimeSection()
    {
        VisualElement card = CoCoEditorElements.CreateCard(
            CoCoEditorLocalization.Text("Runtime Status", "运行时状态"));
        _runtimeContent = new VisualElement { name = "ccflow-host-runtime" };
        card.Add(_runtimeContent);

        var open = new Button(OpenDebugger)
        {
            text = CoCoEditorLocalization.Text("Open Debugger", "打开调试器")
        };
    open.style.marginTop = 4f;
    card.Add(open);
    _root.Add(card);
}

    private void UpdateDynamic()
    {
        var host = (CoCoStateGraphHost)target;
        bool live = host != null && host.HasLiveRuntime;
        if (_configRoot != null)
        {
            _configRoot.SetEnabled(!live);
        }

    UpdateRuntimeCard(host);
}

    private void UpdateRuntimeCard(CoCoStateGraphHost host)
    {
        if (_runtimeContent == null)
        {
            return;
        }

    _runtimeContent.Clear();

    var editHint = new Label(CoCoEditorLocalization.Text(
    "runtime status appears in Play Mode with a live Host",
    "运行时状态在 Play 模式且 Host 运行时显示"));
    editHint.AddToClassList("ccflow-muted");
    editHint.style.whiteSpace = WhiteSpace.Normal;

    if (host == null)
    {
        _runtimeContent.Add(editHint);
        return;
    }

    _runtimeContent.Add(CreateKeyValueRow(
    CoCoEditorLocalization.Text("Lifecycle", "生命周期"),
    host.Lifecycle.ToString(),
    out VisualElement badgeRow));
    CoCoEditorElements.SetBadgeKind(
    badgeRow,
    LifecycleToBadgeKind(host.Lifecycle, host.Fault.IsFaulted));

    if (host.Fault.IsFaulted)
    {
        _runtimeContent.Add(CreateKeyValueRow(
        "Fault",
        host.Fault.Diagnostic.Domain + "/" +
        host.Fault.Diagnostic.Code + ": " +
        host.Fault.Diagnostic.Message,
        out _));
    }

    if (host.RequiresWorldCorrection)
    {
        _runtimeContent.Add(CreateKeyValueRow(
        CoCoEditorLocalization.Text("World Correction", "世界修正"),
        CoCoEditorLocalization.Text("required", "需要"),
        out _));
    }

    if (host.GraphInstanceId.IsValid)
    {
        _runtimeContent.Add(CreateKeyValueRow(
        CoCoEditorLocalization.Text("Graph Instance", "图实例"),
        host.GraphInstanceId.ToString(),
        out _));
    }

    if (host.LastDiagnostic.IsError)
    {
        _runtimeContent.Add(CreateKeyValueRow(
        CoCoEditorLocalization.Text("Last Diagnostic", "最后诊断"),
        host.LastDiagnostic.Domain + "/" + host.LastDiagnostic.Code +
        ": " + host.LastDiagnostic.Message,
        out _));
    }

    if (!Application.isPlaying)
    {
        _runtimeContent.Add(editHint);
    }
}

    private static CoCoEditorBadgeKind LifecycleToBadgeKind(
    CoCoRuntimeLifecycleState lifecycle,
    bool faulted)
    {
        if (faulted)
        {
            return CoCoEditorBadgeKind.Error;
        }

    switch (lifecycle)
    {
        case CoCoRuntimeLifecycleState.Running:
        return CoCoEditorBadgeKind.Success;
        case CoCoRuntimeLifecycleState.Suspended:
        return CoCoEditorBadgeKind.Warning;
        default:
        return CoCoEditorBadgeKind.Neutral;
    }
}

    private void OpenDebugger()
    {
        CoCoStateGraphDebuggerWindow.Open((CoCoStateGraphHost)target);
    }

        // ===== Diagnostics（authoring hints 汇集） =====

        private void BuildDiagnosticsSection()
        {
            VisualElement card = CoCoEditorElements.CreateCard(
                CoCoEditorLocalization.Text("Diagnostics", "诊断"));
            _diagnosticsContent = new VisualElement { name = "ccflow-host-diagnostics" };
            card.Add(_diagnosticsContent);
            _root.Add(card);
        }

        private void RebuildDiagnostics()
        {
            if (_diagnosticsContent == null)
            {
                return;
            }

            _diagnosticsContent.Clear();
            var host = (CoCoStateGraphHost)target;
            if (host == null)
            {
                return;
            }

            List<CoCoBindingHint> hints = CollectDiagnostics(host);
            if (hints.Count == 0)
            {
                var ok = new Label(CoCoEditorLocalization.Text(
                    "no assembly hints — remaining startup authority belongs to " +
                        "the runtime",
                    "无装配期提示——其余启动权威归运行时"));
                ok.AddToClassList("ccflow-muted");
                ok.style.whiteSpace = WhiteSpace.Normal;
                _diagnosticsContent.Add(ok);
                return;
            }

            for (int index = 0; index < hints.Count; index++)
            {
                _diagnosticsContent.Add(BuildHintRow(hints[index]));
            }
        }

        private List<CoCoBindingHint> CollectDiagnostics(CoCoStateGraphHost host)
        {
            var hints = new List<CoCoBindingHint>();

            if (host.StateGraphAsset == null)
            {
                hints.Add(new CoCoBindingHint(
                    CoCoBindingHintKind.Error,
                    null,
                    "no StateGraph asset assigned — the Host cannot start",
                    "未指定 StateGraph 资产——Host 无法启动"));
            }

            AppendArrayHints(
                hints, host, "intentSources",
                CoCoStateGraphHostBindingRules.BuildIntentSourceHint);
            AppendArrayHints(
                hints, host, "operators",
                CoCoStateGraphHostBindingRules.BuildOperatorHint);

            MonoBehaviour actor = serializedObject.FindProperty("actorContextBinding")
                .objectReferenceValue as MonoBehaviour;
            CoCoBindingHint? actorHint =
                CoCoStateGraphHostBindingRules.BuildActorContextHint(host, actor);
            if (actorHint.HasValue)
            {
                hints.Add(actorHint.Value);
            }

            MonoBehaviour restoreRoot =
                serializedObject.FindProperty("contextRestoreBinding")
                    .objectReferenceValue as MonoBehaviour;
            CoCoBindingHint? restoreHint =
                CoCoStateGraphHostBindingRules.BuildRestoreRootHint(host, restoreRoot);
            if (restoreHint.HasValue)
            {
                hints.Add(restoreHint.Value);
            }

            if (restoreRoot != null)
            {
                var nodes =
                    new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
                CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(
                    restoreRoot, host, nodes);
                CoCoBindingHint? breakHint =
                    CoCoStateGraphHostBindingRules.BuildRestoreChainBreakHint(nodes);
                if (breakHint.HasValue)
                {
                    hints.Add(breakHint.Value);
                }
            }

            return hints;
        }

        private void AppendArrayHints(
            List<CoCoBindingHint> hints,
            CoCoStateGraphHost host,
            string propertyPath,
            Func<CoCoStateGraphHost, MonoBehaviour, CoCoBindingHint?> buildHint)
        {
            SerializedProperty array = serializedObject.FindProperty(propertyPath);
            var assigned = new List<MonoBehaviour>();
            for (int index = 0; index < array.arraySize; index++)
            {
                assigned.Add(
                    array.GetArrayElementAtIndex(index).objectReferenceValue as
                        MonoBehaviour);
            }

            List<MonoBehaviour> duplicates =
                CoCoStateGraphHostBindingRules.FindDuplicateReferences(assigned);
            for (int index = 0; index < assigned.Count; index++)
            {
                CoCoBindingHint? hint = buildHint(host, assigned[index]);
                if (hint.HasValue)
                {
                    hints.Add(hint.Value);
                }

                MonoBehaviour component = assigned[index];
                if (component != null && duplicates.Contains(component))
                {
                    hints.Add(new CoCoBindingHint(
                        CoCoBindingHintKind.Warning,
                        component,
                        component.name + " is referenced more than once in " +
                            propertyPath + " — the runtime requires unique binding " +
                            "components",
                        component.name + " 在 " + propertyPath +
                            " 中被引用多次——运行时要求绑定组件唯一"));
                }
            }
        }

        // ===== 小构件 =====

        private static Label CreateSubsectionTitle(string title)
        {
            var label = new Label(title);
            label.AddToClassList("ccflow-host-subsection");
            return label;
        }

        private static VisualElement BuildHintRow(CoCoBindingHint hint)
        {
            return CoCoEditorElements.CreateDiagnosticRow(
                hint.LocalizedText,
                HintToBadgeKind(hint.Kind),
                hint.Target == null
                    ? null
                    : () => EditorGUIUtility.PingObject(hint.Target));
        }

        private static CoCoEditorBadgeKind HintToBadgeKind(CoCoBindingHintKind kind)
        {
            switch (kind)
            {
                case CoCoBindingHintKind.Error:
                    return CoCoEditorBadgeKind.Error;
                case CoCoBindingHintKind.Warning:
                    return CoCoEditorBadgeKind.Warning;
                default:
                    return CoCoEditorBadgeKind.Info;
            }
        }

        private VisualElement CreateKeyValueRow(
            string key,
            string value,
            out VisualElement badgeRow)
        {
            var row = new VisualElement();
            row.AddToClassList("ccflow-host-kv-row");
            var keyLabel = new Label(key);
            keyLabel.AddToClassList("ccflow-host-kv-row__key");
            row.Add(keyLabel);

            badgeRow = CoCoEditorElements.CreateBadge(
                value, CoCoEditorBadgeKind.Neutral);
            badgeRow.Q<Label>("ccflow-badge-text").style.unityTextAlign =
                TextAnchor.MiddleLeft;
            badgeRow.style.alignSelf = Align.FlexStart;
            badgeRow.style.flexShrink = 1f;
            badgeRow.style.whiteSpace = WhiteSpace.Normal;
            row.Add(badgeRow);
            return row;
        }

        // ===== 生命周期回调 =====

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (_root == null)
            {
                return;
            }

            serializedObject.Update();
            RebuildAll();
        }

        private void OnUndoRedoPerformed()
        {
            if (_root == null)
            {
                return;
            }

            serializedObject.Update();
            RebuildDynamicSections();
        }

        private void OnLanguageChanged()
        {
            if (_root == null)
            {
                return;
            }

            RebuildAll();
        }
    }
}
