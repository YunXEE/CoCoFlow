using System;
using System.Collections.Generic;
using System.Linq;
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
        private Label _stateDraftStatus;
        private Button _stateSaveButton;
        private Button _stateDiscardButton;
        private VisualElement _stateConfigHost;
        private Foldout _stateStructureActions;

        private void RefreshDetails()
        {
            if (details == null || controller == null)
            {
                return;
            }

            details.Unbind();
            details.Clear();
            _stateDraftStatus = null;
            _stateConfigHost = null;
            _stateStructureActions = null;
            UseStateEditingAsset();
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
                RefreshFeedback();
                ApplyPlayModeReadOnly();
                return;
            }

            bool hasSelectedTransition = FindTransition(layer, controller.Session.SelectedTransitionId) != null;
            bool hasSelectedState = FindState(layer, controller.Session.SelectedStateId) != null;
            if (hasSelectedState || hasSelectedTransition)
            {
                var selected = new VisualElement { name = "state-graph-selected-details" };
                if (hasSelectedTransition)
                    CollectDetailsSection(selected, () => DrawTransitionsCard(layer));
                else
                    CollectDetailsSection(selected, DrawSelectedStateCard);
                details.Add(selected);
            }

            RefreshFeedback();

            if (!hasSelectedState && !hasSelectedTransition)
            {
                var management = new VisualElement { name = "state-graph-layer-management" };
                CollectDetailsSection(management, () => DrawLayerCard(layer));
                CollectDetailsSection(management, DrawAddStateCard);
                CollectDetailsSection(management, DrawAddLogicCard);
                details.Add(management);
            }

            if (!hasSelectedTransition)
            {
                var transitions = new VisualElement { name = "state-graph-transitions" };
                CollectDetailsSection(transitions, () => DrawTransitionsCard(layer));
                details.Add(transitions);
            }

            ApplyPlayModeReadOnly();
        }

        private Foldout CreateDetailsFoldout(string name, string english, string chinese)
        {
            var foldout = new Foldout { name = name, text = L(english, chinese), value = false };
            foldout.AddToClassList("sg-details-foldout");
            texts.Register(foldout.Q<Toggle>().Q<Label>(), english, chinese);
            return foldout;
        }

        private void CollectDetailsSection(VisualElement parent, Action draw)
        {
            int first = details.childCount;
            draw();
            while (details.childCount > first)
            {
                parent.Add(details[first]);
            }
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

            var layerName = new TextField(L("Name", "名称")) { value = layer.DisplayName };
            card.Add(layerName);

            var rename = new Button(() => controller.RenameLayer(layerName.value))
            {
                text = L("Rename Layer", "重命名 Layer")
            };
            texts.Register(rename, "Rename Layer", "重命名 Layer");
            card.Add(rename);

            var order = new VisualElement();
            order.style.flexDirection = FlexDirection.Row;
            var moveUp = new Button(() => controller.MoveSelectedLayer(-1)) { text = L("Move Layer Up", "Layer 上移") };
            texts.Register(moveUp, "Move Layer Up", "Layer 上移");
            var moveDown = new Button(() => controller.MoveSelectedLayer(1)) { text = L("Move Layer Down", "Layer 下移") };
            texts.Register(moveDown, "Move Layer Down", "Layer 下移");
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
            card.name = "state-graph-add-state";
            card.AddToClassList("sg-card");
            texts.Register(card[0], "Add State", "添加 State");

            IReadOnlyList<CoCoStateDescriptor> stateDescriptors =
                controller.Catalog?.StateDescriptors ?? Array.Empty<CoCoStateDescriptor>();
            StateDescriptorChoice addDescriptor = AddStateDescriptorPopup(
                stateDescriptors,
                addStateDescriptorId,
                persistAsAddDefault: true,
                "add-state-descriptor", card);
            var stateName = new TextField(L("Name", "名称")) { value = L("State", "State") };
            card.Add(stateName);

            var addHere = new Button(() =>
            {
                controller.AddState(default, addDescriptor.Value, stateName.value, NextPosition());
            })
            {
                text = "Add State Here"
            };
            card.Add(addHere);

            var pasteHere = new Button(() => controller.PasteState(
                default,
                NextPosition()))
            {
                text = L("Paste Subtree Here", "粘贴子树到此处")
            };
            texts.Register(pasteHere, "Paste Subtree Here", "粘贴子树到此处");
            card.Add(pasteHere);

            details.Add(card);
        }

        private void DrawAddLogicCard()
        {
            var card = CoCoEditorElements.CreateCard(L("Add Logic", "添加 Logic"));
            card.name = "state-graph-add-logic";
            card.AddToClassList("sg-card");
            texts.Register(card[0], "Add Logic", "添加 Logic");

            var createNewScriptName = new TextField(L("New Logic Name", "新逻辑名称"))
            {
                name = "state-graph-new-logic-name"
            };
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
                    "Logic.cs " + L("generated. Waiting for script compilation...", "已生成，等待脚本编译……");
                pendingCreateSelectName = createNewScriptName.value.Trim() + "Logic";
                awaitingCompilation = true;
            })
            {
                name = "state-graph-create-logic",
                text = L("Create New Logic Script", "生成新逻辑脚本")
            };
            texts.Register(createScript, "Create New Logic Script", "生成新逻辑脚本");
            card.Add(createScript);
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

            CoCoStateGraphStateRecord draft = controller.BeginStateDraft();
            UseStateEditingAsset();
            var stateName = new TextField(L("Name", "名称"))
            {
                name = "state-graph-state-name", value = draft.DisplayName
            };
            stateName.RegisterValueChangedCallback(evt => controller.SetStateDraftName(evt.newValue));
            card.Add(stateName);

            StateDescriptorChoice selectedDescriptor = AddStateDescriptorPopup(
                stateDescriptors,
                ToStateDescriptorId(draft.StateDescriptorId),
                persistAsAddDefault: false,
                "selected-state-descriptor", card,
                descriptor =>
                {
                    if (controller.SetStateDraftDescriptor(descriptor))
                        RefreshStateConfig();
                });
            var hint = new Label(L("Changing Logic resets its draft StateConfig. Save to apply all changes.",
                "更换 Logic 会重置草稿配置；名称、Logic 和 StateConfig 一起保存。"));
            hint.AddToClassList("sg-muted");
            card.Add(hint);

            var saveRow = new VisualElement();
            saveRow.AddToClassList("sg-state-save-row");
            _stateDraftStatus = new Label { name = "state-graph-state-save-status" };
            saveRow.Add(_stateDraftStatus);
            _stateSaveButton = new Button(SaveChanges)
            {
                name = "state-graph-save-state", text = L("Save", "保存")
            };
            texts.Register(_stateSaveButton, "Save", "保存");
            _stateSaveButton.AddToClassList("ccflow-button--primary");
            saveRow.Add(_stateSaveButton);
            _stateDiscardButton = new Button(DiscardChanges)
            {
                name = "state-graph-discard-state", text = L("Discard", "放弃修改")
            };
            texts.Register(_stateDiscardButton, "Discard", "放弃修改");
            saveRow.Add(_stateDiscardButton);
            card.Add(saveRow);

            _stateConfigHost = new VisualElement { name = "state-graph-state-config-host" };
            card.Add(_stateConfigHost);
            RefreshStateConfig();

            _stateStructureActions = CreateDetailsFoldout("state-graph-state-structure", "Structure actions", "结构操作");
            card.Add(_stateStructureActions);
            var actions = _stateStructureActions;
            var idLabel = new Label("State ID: " + StableStateId(state));
            idLabel.AddToClassList("sg-id-label");
            actions.Add(idLabel);
            var structureHint = new Label(L("These actions apply immediately. Save or discard your draft first.",
                "以下操作立即生效；请先保存或放弃当前草稿。"));
            structureHint.AddToClassList("sg-muted");
            actions.Add(structureHint);
            var order = new VisualElement();
            order.AddToClassList("sg-card-row");
            var moveUp = new Button(() => controller.MoveSelectedState(-1)) { text = L("Earlier in State order", "State 顺序提前") };
            texts.Register(moveUp, "Earlier in State order", "State 顺序提前");
            var moveDown = new Button(() => controller.MoveSelectedState(1)) { text = L("Later in State order", "State 顺序延后") };
            texts.Register(moveDown, "Later in State order", "State 顺序延后");
            order.Add(moveUp);
            order.Add(moveDown);
            actions.Add(order);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            var setInitial = new Button(() => controller.SetSelectedStateInitial())
            {
                text = L("Use as default entry", "设为默认进入状态")
            };
            texts.Register(setInitial, "Use as default entry", "设为默认进入状态");
            var copy = new Button(() => controller.CopySelectedState()) { text = L("Copy State and children", "复制状态及子状态") };
            texts.Register(copy, "Copy State and children", "复制状态及子状态");
            row.Add(setInitial);
            row.Add(copy);
            actions.Add(row);

            CoCoStateId selectedId = ToStateId(state.StateId);
            var addChild = new Button(() => controller.AddState(
                    selectedId,
                    selectedDescriptor.Value,
                    "Child State",
                    new Vector2(80f, 80f)))
            {
                text = L("Create child State", "新建子状态")
            };
            texts.Register(addChild, "Create child State", "新建子状态");
            actions.Add(addChild);

            List<CoCoStateId> siblingIds = SiblingIds(controller.SelectedLayer, state);
            StateIdChoice replacement = AddExplicitStateIdPopup(L("Replacement default entry", "替代的默认进入状态"), siblingIds, actions);

            var parentChoices = new List<CoCoStateId> { default };
            foreach (CoCoStateGraphStateRecord candidate in controller.SelectedLayer.States)
            {
                if (candidate != null && candidate.StateId != state.StateId)
                {
                    parentChoices.Add(ToStateId(candidate.StateId));
                }
            }

            CoCoStateId currentParent = ToStateId(state.ParentStateId);
            StateIdChoice targetParent = AddStateIdPopup(L("New parent", "新的父状态"), parentChoices, currentParent, actions);
            var reparent = new Button(() =>
            {
                if (targetParent.Value != currentParent &&
                    RequiresInitialReplacement(controller.SelectedLayer, state) &&
                    !replacement.HasExplicitSelection)
                {
                    ShowNotification(new GUIContent(
                        L("Choose an explicit replacement before moving the initial State.",
                            "移动初始 State 前请先选择替换目标。")));
                    return;
                }

                controller.ReparentSelectedState(
                    targetParent.Value,
                    replacement.Value,
                    new Vector2(80f, 80f));
            })
            {
                text = L("Change parent of State and children", "更改状态及子状态的父级")
            };
            texts.Register(reparent, "Change parent of State and children", "更改状态及子状态的父级");
            actions.Add(reparent);

            var delete = CoCoEditorElements.CreateDangerButton(
                L("Delete State and children", "删除状态及子状态"),
                () => DeleteState(replacement));
            texts.Register(delete, "Delete State and children", "删除状态及子状态");
            actions.Add(delete);

            details.Add(card);
            RefreshStateDraftStatus();
        }

        private void UseStateEditingAsset()
        {
            if (serializedAsset != null && serializedAsset.targetObject == controller.StateEditingAsset) return;
            serializedAsset?.Dispose();
            serializedAsset = new SerializedObject(controller.StateEditingAsset);
        }

        private void RefreshStateConfig()
        {
            if (_stateConfigHost == null) return;
            _stateConfigHost.Unbind();
            _stateConfigHost.Clear();
            CoCoStateGraphStateRecord draft = controller.BeginStateDraft();
            if (draft == null) return;
            UseStateEditingAsset();
            serializedAsset.Update();
            if (DrawFieldConfig(_stateConfigHost, draft)) return;
            var title = new Label("StateConfig");
            title.AddToClassList("sg-state-config-title");
            _stateConfigHost.Add(title);
            SerializedProperty property = FindStateConfigProperty(controller.Session.SelectedLayerId,
                ToStateId(draft.StateId));
            if (property == null) return;
            // Draw the Config's fields directly: no root foldout or delayed
            // attempt to reopen Unity's generated managed-reference foldout.
            var field = new VisualElement { name = "state-graph-state-config" };
            SerializedProperty child = property.Copy();
            SerializedProperty end = property.GetEndProperty();
            if (child.NextVisible(true))
            {
                do
                {
                    if (SerializedProperty.EqualContents(child, end)) break;
                    child.isExpanded = true;
                    field.Add(new PropertyField(child.Copy()));
                } while (child.NextVisible(false));
            }

            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => controller.NotifyConfigChanged());
            field.Bind(serializedAsset);
            _stateConfigHost.Add(field);
        }

        private void RefreshStateDraftStatus()
        {
            bool pending = controller != null && controller.HasStateDraftChanges;
            hasUnsavedChanges = pending;
            saveChangesMessage = L("Save the State name, Logic and StateConfig?", "是否保存 State 的名称、Logic 和 StateConfig？");
            if (_stateDraftStatus == null) return;
            _stateDraftStatus.text = pending ? L("Unsaved changes", "未保存") : L("Saved", "已保存");
            _stateDraftStatus.EnableInClassList("sg-state-draft-pending", pending);
            bool editable = CoCoStateGraphAuthoringOperations.CanEdit(out _);
            _stateSaveButton?.SetEnabled(pending && editable);
            _stateDiscardButton?.SetEnabled(pending && editable);
            _stateStructureActions?.SetEnabled(!pending && editable);
        }

        private void DrawTransitionsCard(CoCoStateGraphLayerRecord layer)
        {
            CoCoStateGraphTransitionRecord selected = FindTransition(layer, controller.Session.SelectedTransitionId);
            CoCoStateId sourceScope = selected != null
                ? ToStateId(selected.SourceStateId) : controller.Session.SelectedStateId;
            CoCoStateGraphStateRecord sourceState = FindState(layer, sourceScope);
            bool showAll = sourceState == null;
            string title = showAll ? "All Transitions" : "Transitions";
            string chineseTitle = showAll ? "全部 Transition" : "Transition";
            var card = CoCoEditorElements.CreateCard(L(title, chineseTitle));
            card.AddToClassList("sg-card");
            texts.Register(card[0], title, chineseTitle);

            var transitionList = new VisualElement { name = "state-graph-transition-list" };
            var outgoing = new ScrollView
            {
                name = "state-graph-outgoing-transitions",
                verticalScrollerVisibility = ScrollerVisibility.Auto
            };
            IEnumerable<CoCoStateGraphTransitionRecord> transitions = layer.Transitions.Where(edge => edge != null);
            if (showAll)
            {
                card.name = "state-graph-all-transitions";
                outgoing.AddToClassList("sg-transition-list--all");
                transitions = transitions.GroupBy(edge => edge.SourceStateId)
                    .OrderByDescending(group => group.Count()).SelectMany(group => group);
            }
            int outgoingCount = 0;
            foreach (CoCoStateGraphTransitionRecord transition in transitions)
            {
                if ((!showAll && ToStateId(transition.SourceStateId) != sourceScope) ||
                    !TryTransitionId(transition.TransitionId, out CoCoTransitionId transitionId))
                {
                    continue;
                }

                bool selectedTransition = transitionId == controller.Session.SelectedTransitionId;
                var row = new Button(() => controller.SelectTransition(transitionId))
                {
                    text = TransitionLabel(layer, transition)
                };
                row.AddToClassList("sg-transition-row");
                if (selectedTransition)
                {
                    row.AddToClassList("sg-transition-row--selected");
                }

                outgoing.Add(row);
                outgoingCount++;
            }
            transitionList.Add(new Label(showAll
                ? L($"All States · {outgoingCount} Transitions", $"全部 State · {outgoingCount} 条 Transition")
                : L($"From {sourceState.DisplayName} · {outgoingCount} outgoing",
                    $"来自 {sourceState.DisplayName} · {outgoingCount} 条出边")));
            transitionList.Add(outgoing);
            if (outgoingCount == 0)
                outgoing.Add(new Label(showAll ? L("No Transitions in this Layer.", "此 Layer 暂无 Transition。")
                    : L("No outgoing Transitions.", "暂无出边。")));

            if (showAll)
            {
                card.Add(transitionList);
                details.Add(card);
                UpdateAllTransitionsHeight();
                return;
            }

            var leafIds = new List<CoCoStateId>();
            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state != null && !HasChildren(layer, state.StateId))
                {
                    leafIds.Add(ToStateId(state.StateId));
                }
            }

            if (selected == null && !leafIds.Contains(sourceScope))
            {
                card.Add(new HelpBox(L("Only leaf States can have outgoing Transitions.",
                    "只有叶子 State 可以创建出边。"), HelpBoxMessageType.Info));
                card.Add(transitionList);
                details.Add(card);
                return;
            }
            CoCoStateId sourceId = sourceScope;
            CoCoStateId targetId = selected == null
                ? leafIds.Find(id => id != sourceId)
                : ToStateId(selected.TargetStateId);
            if (!targetId.IsValid) targetId = sourceId;
            StateIdChoice source = AddStateIdPopup("Source State",
                selected == null ? new List<CoCoStateId> { sourceId } : leafIds, sourceId, card);
            if (selected == null) card.Q<PopupField<string>>().SetEnabled(false);
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
                card.Add(transitionList);
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
            card.Add(transitionList);

            details.Add(card);
        }

        private void UpdateAllTransitionsHeight()
        {
            ScrollView list = details?.Q<ScrollView>("state-graph-outgoing-transitions");
            float height = details?.contentViewport.layout.height ?? 0f;
            if (list?.ClassListContains("sg-transition-list--all") == true && height > 0f)
            {
                details.Q<VisualElement>("state-graph-all-transitions").style.maxHeight = height * 0.5f;
                list.style.maxHeight = height * 0.5f;
            }
        }

        private void DrawRequirements()
        {
            var requirements = new VisualElement { name = "state-graph-requirements" };
            var heading = CoCoEditorElements.CreateHeading(L("Requirements / Host Suggestions", "需求 / Host 建议"));
            texts.Register(heading, "Requirements / Host Suggestions", "需求 / Host 建议");
            requirements.Add(heading);
            CoCoStateGraphRequirementPresenter.FillCard(
                requirements,
                controller.BuildRequirementSections(),
                controller.AnalysisResult?.Succeeded == true);
            feedbackHost.Add(requirements);
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

            DrawRequirements();
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
            string popupName,
            VisualElement parent,
            Action<CoCoStateDescriptor> onChanged = null)
        {
            var choice = new StateDescriptorChoice();
            if (descriptors.Count == 0)
            {
                parent.Add(new HelpBox(
                    L("No State descriptors are available.", "没有可用的 State 描述符。"),
                    HelpBoxMessageType.Warning));
                return choice;
            }

            bool unresolved = !persistAsAddDefault && preferred.IsValid &&
                !descriptors.Any(descriptor => descriptor.DescriptorId == preferred);
            int choiceOffset = unresolved ? 1 : 0;
            int selectedIndex = 0;
            var labels = new List<string>(descriptors.Count + choiceOffset);
            if (unresolved)
                labels.Add(L($"Unresolved Logic [{ShortId(preferred.ToString())}]",
                    $"未找到 Logic [{ShortId(preferred.ToString())}]"));
            for (int index = 0; index < descriptors.Count; index++)
            {
                CoCoStateDescriptor descriptor = descriptors[index];
                labels.Add($"{descriptor.LogicType.Name}  [{ShortId(descriptor.DescriptorId.ToString())}]");
                if ((preferred.IsValid && descriptor.DescriptorId == preferred) ||
                    (!preferred.IsValid && descriptor.DescriptorId == addStateDescriptorId))
                {
                    selectedIndex = index + choiceOffset;
                }
            }

            choice.Value = unresolved ? null : descriptors[selectedIndex];
            if (persistAsAddDefault)
            {
                addStateDescriptorId = choice.Value.DescriptorId;
            }

            var popup = new PopupField<string>("Logic", labels, selectedIndex)
            {
                name = popupName
            };
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = labels.IndexOf(evt.newValue) - choiceOffset;
                if (index < 0)
                {
                    popup.SetValueWithoutNotify(evt.previousValue);
                    return;
                }
                if (index >= 0)
                {
                    choice.Value = descriptors[index];
                    if (persistAsAddDefault)
                    {
                        addStateDescriptorId = choice.Value.DescriptorId;
                    }
                    onChanged?.Invoke(choice.Value);
                }
            });
            parent.Add(popup);
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
            IReadOnlyList<CoCoStateId> ids,
            VisualElement parent)
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

            parent.Add(popup);
            return choice;
        }
    }
}
