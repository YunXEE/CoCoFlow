using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>
    /// StateGraph 主编辑器窗口的右栏详情面板（P03 重做，partial）。
    /// 按上下文分区卡片化：Layer / Add State / Selected State / Transitions /
    /// Requirements / Diagnostics(反馈区)。
    /// </summary>
    internal sealed partial class CoCoStateGraphEditorWindow
    {
        private void RefreshDetails()
        {
            if (details == null || controller == null)
            {
                return;
            }

            details.Clear();
            feedbackHost = null;
            serializedAsset?.UpdateIfRequiredOrScript();

            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null)
            {
                var empty = CoCoEditorElements.CreateEmptyState(
                    L("No Layer", "无 Layer"),
                    L("This State Graph has no Layer to edit yet.", "此 StateGraph 还没有可编辑的 Layer。"),
                    L("Use “+ Layer” in the toolbar to create the first Layer.",
                        "使用工具栏的「+ 层」创建第一个 Layer。"));
                details.Add(empty);
                feedbackHost = CoCoEditorElements.CreateFeedbackHost();
                feedbackHost.name = "state-graph-feedback";
                details.Add(feedbackHost);
                RefreshFeedback();
                ApplyPlayModeReadOnly();
                return;
            }

            DrawLayerCard(layer);
            DrawAddStateCard();
            DrawSelectedStateCard();
            DrawTransitionsCard(layer);
            DrawRequirementsCard();

            feedbackHost = CoCoEditorElements.CreateFeedbackHost();
            feedbackHost.name = "state-graph-feedback";
            details.Add(feedbackHost);
            RefreshFeedback();

            ApplyPlayModeReadOnly();
        }

        private void DrawLayerCard(CoCoStateGraphLayerRecord layer)
        {
            var card = CoCoEditorElements.CreateCard(L("Layer", "Layer"));
            card.AddToClassList("sg-card");
            texts.Register(card[0], "Layer", "Layer");

            var idLabel = new Label(TryLayerId(layer.LayerId, out CoCoLayerId layerId)
                ? layerId.ToString()
                : layer.LayerId.ToString());
            idLabel.AddToClassList("sg-id-label");
            card.Add(idLabel);

            var layerName = new TextField(L("Name")) { value = layer.DisplayName };
            card.Add(layerName);

            var rename = new Button(() => controller.RenameLayer(layerName.value))
            {
                text = L("Rename Layer", "重命名 Layer")
            };
            texts.Register(rename, "Rename Layer", "重命名 Layer");
            card.Add(rename);

            var order = new VisualElement();
            order.style.flexDirection = FlexDirection.Row;
            var moveUp = new Button(() => controller.MoveSelectedLayer(-1)) { text = L("Move Layer Up", "上移 Layer") };
            texts.Register(moveUp, "Move Layer Up", "上移 Layer");
            var moveDown = new Button(() => controller.MoveSelectedLayer(1)) { text = L("Move Layer Down", "下移 Layer") };
            texts.Register(moveDown, "Move Layer Down", "下移 Layer");
            order.Add(moveUp);
            order.Add(moveDown);
            card.Add(order);

            var duplicate = new Button(() => controller.DuplicateSelectedLayer())
            {
                text = L("Duplicate Layer", "复制 Layer")
            };
            texts.Register(duplicate, "Duplicate Layer", "复制 Layer");
            card.Add(duplicate);

            var delete = CoCoEditorElements.CreateDangerButton(L("Delete Layer", "删除 Layer"), DeleteLayer);
            texts.Register(delete, "Delete Layer", "删除 Layer");
            card.Add(delete);

            details.Add(card);
        }

        private void DrawAddStateCard()
        {
            var card = CoCoEditorElements.CreateCard(L("Add State", "添加 State"));
            card.AddToClassList("sg-card");
            texts.Register(card[0], "Add State", "添加 State");

            IReadOnlyList<CoCoStateDescriptor> stateDescriptors =
                controller.Catalog?.StateDescriptors ?? Array.Empty<CoCoStateDescriptor>();
            StateDescriptorChoice addDescriptor = AddStateDescriptorPopup(
                stateDescriptors,
                addStateDescriptorId,
                persistAsAddDefault: true,
                "add-state-descriptor");
            var stateName = new TextField(L("Name")) { value = L("State", "State") };
            card.Add(stateName);

            // Create-new-logic lane: graph-driven authoring. Entering a new
            // state name and clicking Create generates the attributed state
            // script; after compilation and standard-catalog rescan, this
            // panel reopens with the fresh descriptor preselected.
            var createNewScriptName = new TextField(L("New Logic Name", "新逻辑名称"));
            texts.Register(createNewScriptName, "New Logic Name", "新逻辑名称");
            card.Add(createNewScriptName);
            var createStatus = new Label(string.Empty);
            createStatus.style.whiteSpace = WhiteSpace.Normal;
            card.Add(createStatus);
            var createScript = new Button(() =>
            {
                string error = CoCoStateScriptWizard.TryCreate(createNewScriptName.value);
                if (error != null)
                {
                    createStatus.text = error;
                    return;
                }

                createStatus.text =
                    createNewScriptName.value.Trim() +
                    "Logic.cs " + L("generated. Waiting for script compilation...", "已生成。等待脚本编译……");
                pendingCreateSelectName = createNewScriptName.value.Trim() + "Logic";
                awaitingCompilation = true;
            })
            {
                text = L("Create New Logic Script", "创建新逻辑脚本")
            };
            texts.Register(createScript, "Create New Logic Script", "创建新逻辑脚本");
            card.Add(createScript);

            var addHere = new Button(() =>
            {
                CoCoStateId parent = controller.Session.DrillRootStateId;
                controller.AddState(parent, addDescriptor.Value, stateName.value, NextPosition());
            })
            {
                text = "Add State Here"
            };
            card.Add(addHere);

            var pasteHere = new Button(() => controller.PasteState(
                controller.Session.DrillRootStateId,
                NextPosition()))
            {
                text = L("Paste Subtree Here", "在此粘贴子树")
            };
            texts.Register(pasteHere, "Paste Subtree Here", "在此粘贴子树");
            card.Add(pasteHere);

            details.Add(card);
        }

        private void DrawSelectedStateCard()
        {
            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            CoCoStateGraphStateRecord state = FindState(layer, controller.Session.SelectedStateId);
            if (state == null)
            {
                return;
            }

            IReadOnlyList<CoCoStateDescriptor> stateDescriptors =
                controller.Catalog?.StateDescriptors ?? Array.Empty<CoCoStateDescriptor>();

            var card = CoCoEditorElements.CreateCard(L("Selected State", "选中的 State"));
            card.AddToClassList("sg-card");
            texts.Register(card[0], "Selected State", "选中的 State");

            var idLabel = new Label(StableStateId(state));
            idLabel.AddToClassList("sg-id-label");
            card.Add(idLabel);

            var stateName = new TextField(L("Name")) { value = state.DisplayName };
            card.Add(stateName);
            var rename = new Button(() => controller.RenameSelectedState(stateName.value))
            {
                text = L("Rename State", "重命名 State")
            };
            texts.Register(rename, "Rename State", "重命名 State");
            card.Add(rename);

            var order = new VisualElement();
            order.style.flexDirection = FlexDirection.Row;
            var moveUp = new Button(() => controller.MoveSelectedState(-1)) { text = L("Move Up", "上移") };
            texts.Register(moveUp, "Move Up", "上移");
            var moveDown = new Button(() => controller.MoveSelectedState(1)) { text = L("Move Down", "下移") };
            texts.Register(moveDown, "Move Down", "下移");
            order.Add(moveUp);
            order.Add(moveDown);
            card.Add(order);

            StateDescriptorChoice selectedDescriptor = AddStateDescriptorPopup(
                stateDescriptors,
                ToStateDescriptorId(state.StateDescriptorId),
                persistAsAddDefault: false,
                "selected-state-descriptor");
            var setDescriptor = new Button(() => controller.SetSelectedStateDescriptor(
                    selectedDescriptor.Value))
            {
                text = "Set Descriptor"
            };
            card.Add(setDescriptor);

            DrawConfigProperty(
                card,
                FindStateConfigProperty(
                    controller.Session.SelectedLayerId,
                    ToStateId(state.StateId)),
                "State Config",
                "state-graph-state-config");

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var setInitial = new Button(() => controller.SetSelectedStateInitial())
            {
                text = L("Set Initial", "设为 Initial")
            };
            texts.Register(setInitial, "Set Initial", "设为 Initial");
            var copy = new Button(() => controller.CopySelectedState()) { text = L("Copy", "复制") };
            texts.Register(copy, "Copy", "复制");
            row.Add(setInitial);
            row.Add(copy);
            card.Add(row);

            CoCoStateId selectedId = ToStateId(state.StateId);
            var addChild = new Button(() => controller.AddState(
                    selectedId,
                    selectedDescriptor.Value,
                    "Child State",
                    new Vector2(80f, 80f)))
            {
                text = "Add Child State"
            };
            card.Add(addChild);

            if (HasChildren(controller.SelectedLayer, state.StateId))
            {
                var openChildren = new Button(() => controller.DrillInto(selectedId))
                {
                    text = L("Open Child Canvas", "打开子画布"),
                    tooltip = L("Double-click the card on the canvas also drills in.",
                        "双击画布卡片同样可下钻。")
                };
                texts.Register(openChildren, "Open Child Canvas", "打开子画布");
                openChildren.AddToClassList("sg-navigation");
                card.Add(openChildren);
            }

            List<CoCoStateId> siblingIds = SiblingIds(controller.SelectedLayer, state);
            StateIdChoice replacement = AddExplicitStateIdPopup("Initial replacement", siblingIds);

            var parentChoices = new List<CoCoStateId> { default };
            foreach (CoCoStateGraphStateRecord candidate in controller.SelectedLayer.States)
            {
                if (candidate != null && candidate.StateId != state.StateId)
                {
                    parentChoices.Add(ToStateId(candidate.StateId));
                }
            }

            CoCoStateId currentParent = ToStateId(state.ParentStateId);
            StateIdChoice targetParent = AddStateIdPopup("Reparent under", parentChoices, currentParent);
            var reparent = new Button(() =>
            {
                if (targetParent.Value != currentParent &&
                    RequiresInitialReplacement(controller.SelectedLayer, state) &&
                    !replacement.HasExplicitSelection)
                {
                    ShowNotification(new GUIContent(
                        L("Choose an explicit replacement before moving the initial State.",
                            "移动初始 State 前请选择显式替换。")));
                    return;
                }

                controller.ReparentSelectedState(
                    targetParent.Value,
                    replacement.Value,
                    new Vector2(80f, 80f));
            })
            {
                text = L("Reparent Subtree", "移动子树")
            };
            texts.Register(reparent, "Reparent Subtree", "移动子树");
            card.Add(reparent);

            var delete = CoCoEditorElements.CreateDangerButton(
                L("Delete Subtree", "删除子树"),
                () => DeleteState(replacement));
            texts.Register(delete, "Delete Subtree", "删除子树");
            card.Add(delete);

            details.Add(card);
        }

        private void DrawTransitionsCard(CoCoStateGraphLayerRecord layer)
        {
            var card = CoCoEditorElements.CreateCard(L("Transitions", "Transition"));
            card.AddToClassList("sg-card");
            texts.Register(card[0], "Transitions", "Transition");

            foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition == null ||
                    !TryTransitionId(transition.TransitionId, out CoCoTransitionId transitionId))
                {
                    continue;
                }

                bool selectedTransition = transitionId == controller.Session.SelectedTransitionId;
                string label = TransitionLabel(layer, transition);
                var row = new Button(() => controller.SelectTransition(transitionId))
                {
                    text = label
                };
                row.AddToClassList("sg-transition-row");
                if (selectedTransition)
                {
                    row.AddToClassList("sg-transition-row--selected");
                }

                card.Add(row);
            }

            var leafIds = new List<CoCoStateId>();
            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state != null && !HasChildren(layer, state.StateId))
                {
                    leafIds.Add(ToStateId(state.StateId));
                }
            }

            CoCoStateGraphTransitionRecord selected = FindTransition(
                layer,
                controller.Session.SelectedTransitionId);
            CoCoStateId sourceId = selected == null
                ? leafIds.Count > 0 ? leafIds[0] : default
                : ToStateId(selected.SourceStateId);
            CoCoStateId targetId = selected == null
                ? leafIds.Count > 1 ? leafIds[1] : sourceId
                : ToStateId(selected.TargetStateId);
            StateIdChoice source = AddStateIdPopup("Source State", leafIds, sourceId, card);
            StateIdChoice target = AddStateIdPopup("Target State", leafIds, targetId, card);
            var priority = new IntegerField(L("Priority", "优先级")) { value = selected?.Priority ?? 0 };
            texts.Register(priority, "Priority", "优先级");
            card.Add(priority);
            var windowMode = new EnumField(
                L("Window", "窗口"),
                selected?.WindowMode ?? CoCoTransitionWindowMode.Always);
            texts.Register(windowMode, "Window", "窗口");
            card.Add(windowMode);
            var start = new DoubleField(L("Start inclusive", "起始（含）"))
            {
                value = selected?.WindowStartInclusive ?? 0d
            };
            texts.Register(start, "Start inclusive", "起始（含）");
            card.Add(start);
            var end = new DoubleField(L("End exclusive", "结束（不含）"))
            {
                value = selected?.WindowEndExclusive ?? 1d
            };
            texts.Register(end, "End exclusive", "结束（不含）");
            card.Add(end);

            var apply = new Button(() =>
            {
                CoCoTransitionWindowMode mode = (CoCoTransitionWindowMode)windowMode.value;
                if (!CoCoTransitionWindow.TryCreate(mode, start.value, end.value, out CoCoTransitionWindow window))
                {
                    ShowNotification(new GUIContent(L("Invalid Transition Window.", "无效的 Transition 窗口。")));
                    return;
                }

                if (selected == null)
                {
                    controller.AddTransition(source.Value, target.Value, priority.value, window);
                }
                else
                {
                    controller.UpdateSelectedTransition(source.Value, target.Value, priority.value, window);
                }
            })
            {
                text = selected == null ? L("Add Transition", "添加 Transition") : L("Update Transition", "更新 Transition")
            };
            if (selected == null)
            {
                texts.Register(apply, "Add Transition", "添加 Transition");
            }
            else
            {
                texts.Register(apply, "Update Transition", "更新 Transition");
            }

            card.Add(apply);

            if (selected == null)
            {
                details.Add(card);
                return;
            }

            var deleteTransition = new Button(() => controller.DeleteSelectedTransition())
            {
                text = L("Delete Transition", "删除 Transition")
            };
            texts.Register(deleteTransition, "Delete Transition", "删除 Transition");
            card.Add(deleteTransition);

            var conditionsHeading = CoCoEditorElements.CreateHeading(L("Conditions", "条件"));
            texts.Register(conditionsHeading, "Conditions", "条件");
            card.Add(conditionsHeading);
            for (int index = 0; index < selected.Conditions.Count; index++)
            {
                int capturedIndex = index;
                CoCoStateGraphConditionRecord condition = selected.Conditions[index];
                var conditionRow = new VisualElement();
                conditionRow.AddToClassList("sg-condition-row");
                var label = new Label(controller.ConditionDescriptorLabel(condition));
                label.AddToClassList("sg-condition-row__label");
                conditionRow.Add(label);
                conditionRow.Add(new Button(() => controller.MoveCondition(
                    capturedIndex,
                    Mathf.Max(0, capturedIndex - 1))) { text = "↑" });
                conditionRow.Add(new Button(() => controller.MoveCondition(
                    capturedIndex,
                    Mathf.Min(selected.Conditions.Count - 1, capturedIndex + 1))) { text = "↓" });
                conditionRow.Add(new Button(() => controller.DeleteCondition(capturedIndex)) { text = "×" });
                card.Add(conditionRow);
                DrawConfigProperty(
                    card,
                    FindConditionConfigProperty(
                        controller.Session.SelectedLayerId,
                        controller.Session.SelectedTransitionId,
                        capturedIndex),
                    L($"Condition {capturedIndex + 1} Config", $"条件 {capturedIndex + 1} 配置"),
                    "state-graph-condition-config");
            }

            IReadOnlyList<CoCoConditionDescriptor> conditionDescriptors =
                controller.Catalog?.ConditionDescriptors ?? Array.Empty<CoCoConditionDescriptor>();
            CoCoConditionDescriptor conditionDescriptor = AddConditionDescriptorPopup(conditionDescriptors, card);
            var addCondition = new Button(() => controller.AddCondition(
                    ResolveConditionDescriptor(conditionDescriptor)))
            {
                text = L("Add Condition", "添加条件")
            };
            texts.Register(addCondition, "Add Condition", "添加条件");
            card.Add(addCondition);

            details.Add(card);
        }

        private void DrawRequirementsCard()
        {
            var card = CoCoEditorElements.CreateCard(L("Requirements / Host Suggestions", "需求 / Host 建议"));
            card.AddToClassList("sg-card");
            texts.Register(card[0], "Requirements / Host Suggestions", "需求 / Host 建议");
            foreach (string line in controller.BuildRequirementOverlay())
            {
                var label = new Label(line);
                label.style.whiteSpace = WhiteSpace.Normal;
                card.Add(label);
            }

            details.Add(card);
        }

        private void RefreshFeedback()
        {
            if (feedbackHost == null || controller == null)
            {
                return;
            }

            feedbackHost.Clear();
            DrawDiagnostics(feedbackHost);
            if (!string.IsNullOrEmpty(controller.CatalogStatus))
            {
                feedbackHost.Add(new HelpBox(controller.CatalogStatus, HelpBoxMessageType.Warning));
            }

            if (!string.IsNullOrEmpty(controller.CommandFailure))
            {
                feedbackHost.Add(new HelpBox(controller.CommandFailure, HelpBoxMessageType.Warning));
            }
        }

        private void DrawDiagnostics(VisualElement parent)
        {
            var heading = CoCoEditorElements.CreateHeading(L("Diagnostics", "诊断"));
            texts.Register(heading, "Diagnostics", "诊断");
            parent.Add(heading);
            if (controller.AnalysisResult == null)
            {
                var hint = new Label(L("Run Analyze to recompute diagnostics.", "运行 Analyze 重新计算诊断。"));
                hint.AddToClassList("sg-muted");
                texts.Register(hint, "Run Analyze to recompute diagnostics.", "运行 Analyze 重新计算诊断。");
                parent.Add(hint);
                return;
            }

            // 精确文案锚点：英文态必须是 "Compilation succeeded."（交互测试断言）。
            var status = new Label(controller.AnalysisResult.Succeeded
                ? L("Compilation succeeded.", "编译成功。")
                : L("Compilation blocked.", "编译阻断。"));
            parent.Add(status);

            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;
            foreach (CoCoGraphDiagnostic diagnostic in controller.AnalysisResult.Diagnostics)
            {
                switch (diagnostic.Diagnostic.Severity)
                {
                    case CoCoDiagnosticSeverity.Error:
                        errorCount++;
                        break;
                    case CoCoDiagnosticSeverity.Warning:
                        warningCount++;
                        break;
                    case CoCoDiagnosticSeverity.Information:
                        infoCount++;
                        break;
                }
            }

            if (controller.AnalysisResult.Diagnostics.Count > 0)
            {
                var counts = new VisualElement();
                counts.style.flexDirection = FlexDirection.Row;
                if (errorCount > 0)
                {
                    counts.Add(CoCoEditorElements.CreateBadge(
                        L($"Error {errorCount}", $"错误 {errorCount}"), CoCoEditorBadgeKind.Error));
                }

                if (warningCount > 0)
                {
                    counts.Add(CoCoEditorElements.CreateBadge(
                        L($"Warn {warningCount}", $"警告 {warningCount}"), CoCoEditorBadgeKind.Warning));
                }

                if (infoCount > 0)
                {
                    counts.Add(CoCoEditorElements.CreateBadge(
                        L($"Info {infoCount}", $"信息 {infoCount}"), CoCoEditorBadgeKind.Info));
                }

                parent.Add(counts);
            }

            foreach (CoCoGraphDiagnostic diagnostic in controller.AnalysisResult.Diagnostics)
            {
                CoCoGraphDiagnostic captured = diagnostic;
                parent.Add(CoCoEditorElements.CreateDiagnosticRow(
                    captured.Diagnostic.Message,
                    SeverityToBadgeKind(captured.Diagnostic.Severity),
                    () => controller.Locate(captured.Location)));
            }
        }

        /// <summary>severity→kind 映射（P02 §2.4 委托 P03 实现的唯一映射表）。</summary>
        internal static CoCoEditorBadgeKind SeverityToBadgeKind(CoCoDiagnosticSeverity severity)
        {
            switch (severity)
            {
                case CoCoDiagnosticSeverity.Error:
                    return CoCoEditorBadgeKind.Error;
                case CoCoDiagnosticSeverity.Warning:
                    return CoCoEditorBadgeKind.Warning;
                case CoCoDiagnosticSeverity.Information:
                    return CoCoEditorBadgeKind.Info;
                default:
                    return CoCoEditorBadgeKind.Neutral;
            }
        }

        private void ApplyPlayModeReadOnly()
        {
            if (CoCoStateGraphAuthoringOperations.CanEdit(out string failure))
            {
                return;
            }

            details.Insert(0, new HelpBox(failure, HelpBoxMessageType.Info));
            details.Query<Button>().ForEach(button =>
            {
                if (!button.ClassListContains("sg-navigation"))
                {
                    button.SetEnabled(false);
                }
            });
            details.Query<TextField>().ForEach(field => field.SetEnabled(false));
            details.Query<IntegerField>().ForEach(field => field.SetEnabled(false));
            details.Query<DoubleField>().ForEach(field => field.SetEnabled(false));
            details.Query<EnumField>().ForEach(field => field.SetEnabled(false));
            details.Query<PopupField<string>>().ForEach(field => field.SetEnabled(false));
            details.Query<PropertyField>().ForEach(field => field.SetEnabled(false));
        }

        private void DrawConfigProperty(
            VisualElement parent,
            SerializedProperty property,
            string label,
            string fieldName)
        {
            if (property == null || serializedAsset == null)
            {
                parent.Add(new HelpBox($"{label} " + L("is unavailable.", "不可用。"), HelpBoxMessageType.Warning));
                return;
            }

            var field = new PropertyField(property.Copy(), label);
            field.name = fieldName;
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => controller.NotifyConfigChanged());
            field.Bind(serializedAsset);
            parent.Add(field);
        }

        private SerializedProperty FindStateConfigProperty(
            CoCoLayerId layerId,
            CoCoStateId stateId)
        {
            SerializedProperty layer = FindLayerProperty(layerId);
            SerializedProperty states = layer?.FindPropertyRelative("states");
            for (int index = 0; states != null && index < states.arraySize; index++)
            {
                SerializedProperty state = states.GetArrayElementAtIndex(index);
                if (SerializedIdMatches(state.FindPropertyRelative("stateId"), stateId.High, stateId.Low))
                {
                    return state.FindPropertyRelative("config");
                }
            }

            return null;
        }

        private SerializedProperty FindConditionConfigProperty(
            CoCoLayerId layerId,
            CoCoTransitionId transitionId,
            int conditionIndex)
        {
            SerializedProperty layer = FindLayerProperty(layerId);
            SerializedProperty transitions = layer?.FindPropertyRelative("transitions");
            for (int index = 0; transitions != null && index < transitions.arraySize; index++)
            {
                SerializedProperty transition = transitions.GetArrayElementAtIndex(index);
                if (!SerializedIdMatches(
                        transition.FindPropertyRelative("transitionId"),
                        transitionId.High,
                        transitionId.Low))
                {
                    continue;
                }

                SerializedProperty conditions = transition.FindPropertyRelative("conditions");
                return conditions != null &&
                       conditionIndex >= 0 &&
                       conditionIndex < conditions.arraySize
                    ? conditions.GetArrayElementAtIndex(conditionIndex).FindPropertyRelative("config")
                    : null;
            }

            return null;
        }

        private SerializedProperty FindLayerProperty(CoCoLayerId layerId)
        {
            SerializedProperty layers = serializedAsset?.FindProperty("layers");
            for (int index = 0; layers != null && index < layers.arraySize; index++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(index);
                if (SerializedIdMatches(layer.FindPropertyRelative("layerId"), layerId.High, layerId.Low))
                {
                    return layer;
                }
            }

            return null;
        }

        private static bool SerializedIdMatches(
            SerializedProperty serializedId,
            ulong expectedHigh,
            ulong expectedLow)
        {
            SerializedProperty high = serializedId?.FindPropertyRelative("high");
            SerializedProperty low = serializedId?.FindPropertyRelative("low");
            return high != null && low != null &&
                   high.ulongValue == expectedHigh &&
                   low.ulongValue == expectedLow;
        }

        private StateDescriptorChoice AddStateDescriptorPopup(
            IReadOnlyList<CoCoStateDescriptor> descriptors,
            CoCoStateDescriptorId preferred,
            bool persistAsAddDefault,
            string popupName)
        {
            var choice = new StateDescriptorChoice();
            if (descriptors.Count == 0)
            {
                details.Add(new HelpBox(
                    L("No State descriptors are available.", "没有可用的 State 描述符。"),
                    HelpBoxMessageType.Warning));
                return choice;
            }

            int selectedIndex = 0;
            var labels = new List<string>(descriptors.Count);
            for (int index = 0; index < descriptors.Count; index++)
            {
                CoCoStateDescriptor descriptor = descriptors[index];
                labels.Add($"{descriptor.LogicType.Name}  [{ShortId(descriptor.DescriptorId.ToString())}]");
                if ((preferred.IsValid && descriptor.DescriptorId == preferred) ||
                    (!preferred.IsValid && descriptor.DescriptorId == addStateDescriptorId))
                {
                    selectedIndex = index;
                }
            }

            choice.Value = descriptors[selectedIndex];
            if (persistAsAddDefault)
            {
                addStateDescriptorId = choice.Value.DescriptorId;
            }

            var popup = new PopupField<string>(L("Descriptor", "描述符"), labels, selectedIndex)
            {
                name = popupName
            };
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = labels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    choice.Value = descriptors[index];
                    if (persistAsAddDefault)
                    {
                        addStateDescriptorId = choice.Value.DescriptorId;
                    }
                }
            });
            details.Add(popup);
            return choice;
        }

        private CoCoConditionDescriptor AddConditionDescriptorPopup(
            IReadOnlyList<CoCoConditionDescriptor> descriptors,
            VisualElement parent)
        {
            if (descriptors.Count == 0)
            {
                parent.Add(new HelpBox(
                    L("No Condition descriptors are available.", "没有可用的 Condition 描述符。"),
                    HelpBoxMessageType.Info));
                return null;
            }

            int selectedIndex = 0;
            var labels = new List<string>(descriptors.Count);
            for (int index = 0; index < descriptors.Count; index++)
            {
                CoCoConditionDescriptor descriptor = descriptors[index];
                labels.Add($"{descriptor.ConditionType.Name}  [{ShortId(descriptor.DescriptorId.ToString())}]");
                if (descriptor.DescriptorId == addConditionDescriptorId)
                {
                    selectedIndex = index;
                }
            }

            addConditionDescriptorId = descriptors[selectedIndex].DescriptorId;
            var popup = new PopupField<string>(L("Descriptor", "描述符"), labels, selectedIndex);
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = labels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    addConditionDescriptorId = descriptors[index].DescriptorId;
                }
            });
            parent.Add(popup);
            return descriptors[selectedIndex];
        }

        private StateIdChoice AddStateIdPopup(
            string label,
            IReadOnlyList<CoCoStateId> ids,
            CoCoStateId selected)
        {
            return AddStateIdPopup(label, ids, selected, details);
        }

        private StateIdChoice AddStateIdPopup(
            string label,
            IReadOnlyList<CoCoStateId> ids,
            CoCoStateId selected,
            VisualElement parent)
        {
            var choice = new StateIdChoice();
            if (ids.Count == 0)
            {
                return choice;
            }

            int selectedIndex = 0;
            var labels = new List<string>(ids.Count);
            for (int index = 0; index < ids.Count; index++)
            {
                labels.Add(StateLabel(ids[index]));
                if (ids[index] == selected)
                {
                    selectedIndex = index;
                }
            }

            choice.Value = ids[selectedIndex];
            var popup = new PopupField<string>(label, labels, selectedIndex);
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = labels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    choice.Value = ids[index];
                }
            });
            parent.Add(popup);
            return choice;
        }

        private StateIdChoice AddExplicitStateIdPopup(
            string label,
            IReadOnlyList<CoCoStateId> ids)
        {
            var choice = new StateIdChoice();
            if (ids.Count == 0)
            {
                return choice;
            }

            var labels = new List<string>(ids.Count + 1)
            {
                "<" + L("Select replacement", "选择替换") + ">"
            };
            for (int index = 0; index < ids.Count; index++)
            {
                labels.Add(StateLabel(ids[index]));
            }

            var popup = new PopupField<string>(label, labels, 0);
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = labels.IndexOf(evt.newValue) - 1;
                choice.HasExplicitSelection = index >= 0;
                choice.Value = index >= 0 ? ids[index] : default;
            });

            details.Add(popup);
            return choice;
        }
    }
}
