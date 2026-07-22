using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed class CoCoStateGraphEditorWindow : EditorWindow
    {
        private const string StyleGuid = "28e9fb1d474b4b87ac711d1de7aa0dd1";

        [SerializeField] private CoCoStateGraphAsset asset;

        private CoCoStateGraphEditorController controller;
        private CoCoStateGraphEditorCanvas canvas;
        private VisualElement toolbarHost;
        private ScrollView tree;
        private ScrollView details;
        private VisualElement feedbackHost;
        private SerializedObject serializedAsset;
        private CoCoStateDescriptorId addStateDescriptorId;
        private CoCoConditionDescriptorId addConditionDescriptorId;
        private Vector2 contextPosition = new Vector2(80f, 80f);

        [MenuItem("Window/CoCoFlow/State Graph Editor")]
        internal static void OpenWindow()
        {
            CoCoStateGraphAsset selected = Selection.activeObject as CoCoStateGraphAsset;
            Open(selected);
        }

        internal static void Open(CoCoStateGraphAsset target)
        {
            CoCoStateGraphEditorWindow window = GetWindow<CoCoStateGraphEditorWindow>();
            window.titleContent = new GUIContent("State Graph");
            window.minSize = new Vector2(840f, 520f);
            if (target != null)
            {
                window.asset = target;
            }

            window.Show();
            window.Rebuild();
        }

        public void CreateGUI()
        {
            Rebuild();
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            DisposeController();
        }

        private void Rebuild()
        {
            if (rootVisualElement == null)
            {
                return;
            }

            DisposeController();
            rootVisualElement.Clear();
            string stylePath = AssetDatabase.GUIDToAssetPath(StyleGuid);
            StyleSheet styleSheet = string.IsNullOrEmpty(stylePath)
                ? null
                : AssetDatabase.LoadAssetAtPath<StyleSheet>(stylePath);
            if (styleSheet != null)
            {
                rootVisualElement.styleSheets.Add(styleSheet);
            }

            toolbarHost = new VisualElement { name = "state-graph-toolbar-host" };
            rootVisualElement.Add(toolbarHost);

            if (asset == null)
            {
                BuildEmptyState();
                RefreshToolbar();
                return;
            }

            controller = new CoCoStateGraphEditorController(asset);
            serializedAsset = new SerializedObject(asset);
            controller.Changed += OnControllerChanged;
            var body = new TwoPaneSplitView(0, 230f, TwoPaneSplitViewOrientation.Horizontal);
            body.style.flexGrow = 1f;
            rootVisualElement.Add(body);

            tree = new ScrollView { name = "state-graph-tree" };
            tree.style.minWidth = 180f;
            body.Add(tree);

            var workspace = new TwoPaneSplitView(1, 350f, TwoPaneSplitViewOrientation.Horizontal);
            workspace.style.flexGrow = 1f;
            body.Add(workspace);

            canvas = new CoCoStateGraphEditorCanvas(controller);
            canvas.ContextRequested += OnCanvasContextRequested;
            workspace.Add(canvas);

            details = new ScrollView { name = "state-graph-details" };
            details.style.width = 350f;
            details.style.minWidth = 290f;
            workspace.Add(details);

            RefreshToolbar();
            RefreshTree();
            RefreshDetails();
        }

        private void BuildEmptyState()
        {
            var container = new VisualElement { name = "state-graph-empty" };
            container.style.flexGrow = 1f;
            container.style.alignItems = Align.Center;
            container.style.justifyContent = Justify.Center;
            container.Add(new Label("Select a CoCoStateGraphAsset to begin."));
            var field = new ObjectField("State Graph Asset")
            {
                objectType = typeof(CoCoStateGraphAsset),
                allowSceneObjects = false
            };
            field.RegisterValueChangedCallback(evt =>
            {
                asset = evt.newValue as CoCoStateGraphAsset;
                Rebuild();
            });
            container.Add(field);
            rootVisualElement.Add(container);
        }

        private void RefreshToolbar()
        {
            if (toolbarHost == null)
            {
                return;
            }

            toolbarHost.Clear();
            var toolbar = new Toolbar();
            toolbarHost.Add(toolbar);

            var assetField = new ObjectField
            {
                objectType = typeof(CoCoStateGraphAsset),
                allowSceneObjects = false,
                tooltip = "State Graph Asset"
            };
            assetField.style.width = 190f;
            assetField.SetValueWithoutNotify(asset);
            assetField.RegisterValueChangedCallback(evt =>
            {
                asset = evt.newValue as CoCoStateGraphAsset;
                Rebuild();
            });
            toolbar.Add(assetField);

            if (controller == null)
            {
                var emptyCreatePreset = new ToolbarButton(() => CoCoStateGraphPresetWizard.Open())
                {
                    text = "Create Preset"
                };
                emptyCreatePreset.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
                toolbar.Add(emptyCreatePreset);
                return;
            }

            var layerIds = new List<CoCoLayerId>();
            var layerLabels = new List<string>();
            int selectedIndex = 0;
            foreach (CoCoStateGraphLayerRecord layer in asset.Layers)
            {
                if (layer == null || !TryLayerId(layer.LayerId, out CoCoLayerId layerId))
                {
                    continue;
                }

                if (layerId == controller.Session.SelectedLayerId)
                {
                    selectedIndex = layerIds.Count;
                }

                layerIds.Add(layerId);
                layerLabels.Add($"{layer.DisplayName}  [{ShortId(layerId.ToString())}]");
            }

            if (layerLabels.Count > 0)
            {
                var layerPopup = new PopupField<string>(layerLabels, selectedIndex);
                layerPopup.style.width = 190f;
                layerPopup.RegisterValueChangedCallback(evt =>
                {
                    int index = layerLabels.IndexOf(evt.newValue);
                    if (index >= 0)
                    {
                        controller.SelectLayer(layerIds[index]);
                    }
                });
                toolbar.Add(layerPopup);
            }

            var addLayer = new ToolbarButton(() => controller.AddLayer()) { text = "+ Layer" };
            addLayer.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
            toolbar.Add(addLayer);
            toolbar.Add(new ToolbarButton(() => controller.DrillUp())
            {
                text = "Up",
                tooltip = "Move to the parent State canvas"
            });
            var breadcrumb = new Label(controller.BreadcrumbLabel);
            breadcrumb.style.minWidth = 120f;
            breadcrumb.style.unityTextAlign = TextAnchor.MiddleLeft;
            toolbar.Add(breadcrumb);

            var search = new ToolbarSearchField();
            search.name = "state-graph-search";
            search.SetValueWithoutNotify(controller.Session.SearchText);
            search.RegisterValueChangedCallback(evt => controller.SetSearch(evt.newValue));
            toolbar.Add(search);
            var spacer = new ToolbarSpacer();
            spacer.style.flexGrow = 1f;
            toolbar.Add(spacer);
            toolbar.Add(new ToolbarButton(() => controller.Analyze()) { text = "Analyze" });
            var createPreset = new ToolbarButton(() => CoCoStateGraphPresetWizard.Open())
            {
                text = "Create Preset"
            };
            createPreset.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
            toolbar.Add(createPreset);
        }

        private void RefreshDetails()
        {
            if (details == null || controller == null)
            {
                return;
            }

            details.Clear();
            feedbackHost = null;
            serializedAsset?.UpdateIfRequiredOrScript();
            AddHeading("Layer");
            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null)
            {
                details.Add(new HelpBox("Add or select a valid Layer.", HelpBoxMessageType.Info));
                feedbackHost = new VisualElement { name = "state-graph-feedback" };
                details.Add(feedbackHost);
                RefreshFeedback();
                ApplyPlayModeReadOnly();
                return;
            }

            var layerName = new TextField("Name") { value = layer.DisplayName };
            details.Add(layerName);
            details.Add(new Button(() => controller.RenameLayer(layerName.value)) { text = "Rename Layer" });
            var layerOrder = new VisualElement();
            layerOrder.style.flexDirection = FlexDirection.Row;
            layerOrder.Add(new Button(() => controller.MoveSelectedLayer(-1)) { text = "Move Layer Up" });
            layerOrder.Add(new Button(() => controller.MoveSelectedLayer(1)) { text = "Move Layer Down" });
            details.Add(layerOrder);
            details.Add(new Button(() => controller.DuplicateSelectedLayer()) { text = "Duplicate Layer" });
            details.Add(new Button(DeleteLayer) { text = "Delete Layer" });

            AddHeading("Add State");
            IReadOnlyList<CoCoStateDescriptor> stateDescriptors =
                controller.Catalog?.StateDescriptors ?? Array.Empty<CoCoStateDescriptor>();
            StateDescriptorChoice addDescriptor = AddStateDescriptorPopup(
                stateDescriptors,
                addStateDescriptorId,
                persistAsAddDefault: true,
                "add-state-descriptor");
            var stateName = new TextField("Name") { value = "State" };
            details.Add(stateName);
            details.Add(new Button(() =>
            {
                CoCoStateId parent = controller.Session.DrillRootStateId;
                controller.AddState(parent, addDescriptor.Value, stateName.value, NextPosition());
            }) { text = "Add State Here" });
            details.Add(new Button(() => controller.PasteState(
                controller.Session.DrillRootStateId,
                NextPosition())) { text = "Paste Subtree Here" });

            CoCoStateGraphStateRecord selectedState = FindState(
                layer,
                controller.Session.SelectedStateId);
            if (selectedState != null)
            {
                DrawSelectedState(selectedState, stateDescriptors);
            }

            DrawTransitions(layer);
            feedbackHost = new VisualElement { name = "state-graph-feedback" };
            details.Add(feedbackHost);
            RefreshFeedback();

            ApplyPlayModeReadOnly();
        }

        private void RefreshTree()
        {
            if (tree == null || controller == null)
            {
                return;
            }

            tree.Clear();
            var heading = new Label("State Tree");
            heading.AddToClassList("state-graph-heading");
            tree.Add(heading);
            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null)
            {
                tree.Add(new Label("No Layer selected."));
                return;
            }

            IReadOnlyList<CoCoStateGraphStateRecord> searchResults = controller.SearchResults;
            if (!string.IsNullOrWhiteSpace(controller.Session.SearchText))
            {
                var searchHeading = new Label($"Search results ({searchResults.Count})");
                searchHeading.style.unityFontStyleAndWeight = FontStyle.Bold;
                tree.Add(searchHeading);
                foreach (CoCoStateGraphStateRecord result in searchResults)
                {
                    CoCoStateId stateId = ToStateId(result.StateId);
                    tree.Add(new Button(() => controller.NavigateToState(stateId))
                    {
                        text = $"{result.DisplayName}  [{ShortId(stateId.ToString())}]"
                    });
                }
            }

            AddTreeChildren(tree, layer, default, 0);
        }

        private void AddTreeChildren(
            VisualElement parent,
            CoCoStateGraphLayerRecord layer,
            CoCoSerializedId128 parentStateId,
            int depth)
        {
            if (depth > layer.States.Count)
            {
                parent.Add(new HelpBox("State hierarchy contains a cycle.", HelpBoxMessageType.Error));
                return;
            }

            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state == null || state.ParentStateId != parentStateId)
                {
                    continue;
                }

                CoCoStateId stateId = ToStateId(state.StateId);
                bool hasChildren = HasChildren(layer, state.StateId);
                if (!hasChildren)
                {
                    var leaf = new Button(() => controller.NavigateToState(stateId))
                    {
                        text = state.DisplayName
                    };
                    leaf.style.marginLeft = depth * 8f;
                    parent.Add(leaf);
                    continue;
                }

                var foldout = new Foldout
                {
                    text = state.DisplayName,
                    value = !controller.Session.IsCollapsed(stateId)
                };
                foldout.style.marginLeft = depth * 8f;
                foldout.RegisterValueChangedCallback(evt =>
                {
                    controller.Session.SetCollapsed(stateId, !evt.newValue);
                    controller.Session.Save();
                });
                foldout.Add(new Button(() => controller.NavigateToState(stateId))
                {
                    text = "Select / Open Parent Canvas"
                });
                AddTreeChildren(foldout, layer, state.StateId, depth + 1);
                parent.Add(foldout);
            }
        }

        private void DrawSelectedState(
            CoCoStateGraphStateRecord state,
            IReadOnlyList<CoCoStateDescriptor> descriptors)
        {
            AddHeading("Selected State");
            details.Add(new Label(StableStateId(state)));
            var stateName = new TextField("Name") { value = state.DisplayName };
            details.Add(stateName);
            details.Add(new Button(() => controller.RenameSelectedState(stateName.value))
            {
                text = "Rename State"
            });
            var stateOrder = new VisualElement();
            stateOrder.style.flexDirection = FlexDirection.Row;
            stateOrder.Add(new Button(() => controller.MoveSelectedState(-1)) { text = "Move Up" });
            stateOrder.Add(new Button(() => controller.MoveSelectedState(1)) { text = "Move Down" });
            details.Add(stateOrder);

            StateDescriptorChoice selectedDescriptor = AddStateDescriptorPopup(
                descriptors,
                ToStateDescriptorId(state.StateDescriptorId),
                persistAsAddDefault: false,
                "selected-state-descriptor");
            details.Add(new Button(() => controller.SetSelectedStateDescriptor(
                selectedDescriptor.Value)) { text = "Set Descriptor" });
            DrawConfigProperty(
                FindStateConfigProperty(
                    controller.Session.SelectedLayerId,
                    ToStateId(state.StateId)),
                "State Config");

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.Add(new Button(() => controller.SetSelectedStateInitial()) { text = "Set Initial" });
            row.Add(new Button(() => controller.CopySelectedState()) { text = "Copy" });
            details.Add(row);

            CoCoStateId selectedId = ToStateId(state.StateId);
            details.Add(new Button(() => controller.AddState(
                selectedId,
                selectedDescriptor.Value,
                "Child State",
                new Vector2(80f, 80f))) { text = "Add Child State" });

            if (HasChildren(controller.SelectedLayer, state.StateId))
            {
                var openChildren = new Button(() => controller.DrillInto(selectedId))
                {
                    text = "Open Child Canvas"
                };
                openChildren.AddToClassList("state-graph-navigation");
                details.Add(openChildren);
            }

            List<CoCoStateId> siblingIds = SiblingIds(controller.SelectedLayer, state);
            StateIdChoice replacement = AddExplicitStateIdPopup(
                "Initial replacement",
                siblingIds);

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
            details.Add(new Button(() =>
            {
                if (targetParent.Value != currentParent &&
                    RequiresInitialReplacement(controller.SelectedLayer, state) &&
                    !replacement.HasExplicitSelection)
                {
                    ShowNotification(new GUIContent(
                        "Choose an explicit replacement before moving the initial State."));
                    return;
                }

                controller.ReparentSelectedState(
                    targetParent.Value,
                    replacement.Value,
                    new Vector2(80f, 80f));
            }) { text = "Reparent Subtree" });

            details.Add(new Button(() => DeleteState(replacement)) { text = "Delete Subtree" });
        }

        private void DrawTransitions(CoCoStateGraphLayerRecord layer)
        {
            AddHeading("Transitions");
            foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition == null ||
                    !TryTransitionId(transition.TransitionId, out CoCoTransitionId transitionId))
                {
                    continue;
                }

                string label = TransitionLabel(layer, transition);
                details.Add(new Button(() => controller.SelectTransition(transitionId)) { text = label });
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
            StateIdChoice source = AddStateIdPopup("Source State", leafIds, sourceId);
            StateIdChoice target = AddStateIdPopup("Target State", leafIds, targetId);
            var priority = new IntegerField("Priority") { value = selected?.Priority ?? 0 };
            details.Add(priority);
            var windowMode = new EnumField(
                "Window",
                selected?.WindowMode ?? CoCoTransitionWindowMode.Always);
            details.Add(windowMode);
            var start = new DoubleField("Start inclusive")
            {
                value = selected?.WindowStartInclusive ?? 0d
            };
            var end = new DoubleField("End exclusive")
            {
                value = selected?.WindowEndExclusive ?? 1d
            };
            details.Add(start);
            details.Add(end);
            details.Add(new Button(() =>
            {
                CoCoTransitionWindowMode mode = (CoCoTransitionWindowMode)windowMode.value;
                if (!CoCoTransitionWindow.TryCreate(mode, start.value, end.value, out CoCoTransitionWindow window))
                {
                    ShowNotification(new GUIContent("Invalid Transition Window."));
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
            }) { text = selected == null ? "Add Transition" : "Update Transition" });

            if (selected == null)
            {
                return;
            }

            details.Add(new Button(() => controller.DeleteSelectedTransition())
            {
                text = "Delete Transition"
            });

            AddHeading("Conditions");
            for (int index = 0; index < selected.Conditions.Count; index++)
            {
                int capturedIndex = index;
                CoCoStateGraphConditionRecord condition = selected.Conditions[index];
                var conditionRow = new VisualElement();
                conditionRow.style.flexDirection = FlexDirection.Row;
                var label = new Label(controller.ConditionDescriptorLabel(condition));
                label.style.flexGrow = 1f;
                conditionRow.Add(label);
                conditionRow.Add(new Button(() => controller.MoveCondition(
                    capturedIndex,
                    Mathf.Max(0, capturedIndex - 1))) { text = "↑" });
                conditionRow.Add(new Button(() => controller.MoveCondition(
                    capturedIndex,
                    Mathf.Min(selected.Conditions.Count - 1, capturedIndex + 1))) { text = "↓" });
                conditionRow.Add(new Button(() => controller.DeleteCondition(capturedIndex)) { text = "×" });
                details.Add(conditionRow);
                DrawConfigProperty(
                    FindConditionConfigProperty(
                        controller.Session.SelectedLayerId,
                        controller.Session.SelectedTransitionId,
                        capturedIndex),
                    $"Condition {capturedIndex + 1} Config");
            }

            IReadOnlyList<CoCoConditionDescriptor> conditionDescriptors =
                controller.Catalog?.ConditionDescriptors ?? Array.Empty<CoCoConditionDescriptor>();
            CoCoConditionDescriptor conditionDescriptor = AddConditionDescriptorPopup(conditionDescriptors);
            details.Add(new Button(() => controller.AddCondition(
                ResolveConditionDescriptor(conditionDescriptor))) { text = "Add Condition" });
        }

        private void RefreshFeedback()
        {
            if (feedbackHost == null || controller == null)
            {
                return;
            }

            feedbackHost.Clear();
            DrawRequirements(feedbackHost);
            DrawDiagnostics(feedbackHost);
            AddCatalogStatus(feedbackHost);
            if (!string.IsNullOrEmpty(controller.CommandFailure))
            {
                feedbackHost.Add(new HelpBox(controller.CommandFailure, HelpBoxMessageType.Warning));
            }
        }

        private void DrawRequirements(VisualElement parent)
        {
            AddHeading(parent, "Requirements / Host Suggestions");
            foreach (string line in controller.BuildRequirementOverlay())
            {
                var label = new Label(line);
                label.style.whiteSpace = WhiteSpace.Normal;
                parent.Add(label);
            }
        }

        private void DrawDiagnostics(VisualElement parent)
        {
            AddHeading(parent, "Diagnostics");
            if (controller.AnalysisResult == null)
            {
                parent.Add(new Label("Run Analyze to recompute diagnostics."));
                return;
            }

            parent.Add(new Label(controller.AnalysisResult.Succeeded
                ? "Compilation succeeded."
                : "Compilation blocked."));
            foreach (CoCoGraphDiagnostic diagnostic in controller.AnalysisResult.Diagnostics)
            {
                var box = new VisualElement();
                box.Add(new Label(diagnostic.Diagnostic.Message));
                var locate = new Button(() => controller.Locate(diagnostic.Location)) { text = "Locate" };
                locate.AddToClassList("state-graph-navigation");
                box.Add(locate);
                parent.Add(box);
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
                if (!button.ClassListContains("state-graph-navigation"))
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

        private void AddCatalogStatus(VisualElement parent = null)
        {
            if (!string.IsNullOrEmpty(controller?.CatalogStatus))
            {
                (parent ?? details).Add(new HelpBox(controller.CatalogStatus, HelpBoxMessageType.Warning));
            }
        }

        private void DrawConfigProperty(SerializedProperty property, string label)
        {
            if (property == null || serializedAsset == null)
            {
                details.Add(new HelpBox($"{label} is unavailable.", HelpBoxMessageType.Warning));
                return;
            }

            var field = new PropertyField(property.Copy(), label);
            field.name = label == "State Config"
                ? "state-graph-state-config"
                : "state-graph-condition-config";
            field.RegisterCallback<SerializedPropertyChangeEvent>(_ => controller.NotifyConfigChanged());
            field.Bind(serializedAsset);
            details.Add(field);
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
                details.Add(new HelpBox("No State descriptors are available.", HelpBoxMessageType.Warning));
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

            var popup = new PopupField<string>("Descriptor", labels, selectedIndex)
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
            IReadOnlyList<CoCoConditionDescriptor> descriptors)
        {
            if (descriptors.Count == 0)
            {
                details.Add(new HelpBox("No Condition descriptors are available.", HelpBoxMessageType.Info));
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
            var popup = new PopupField<string>("Descriptor", labels, selectedIndex);
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = labels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    addConditionDescriptorId = descriptors[index].DescriptorId;
                }
            });
            details.Add(popup);
            return descriptors[selectedIndex];
        }

        private StateIdChoice AddStateIdPopup(
            string label,
            IReadOnlyList<CoCoStateId> ids,
            CoCoStateId selected)
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
            details.Add(popup);
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

            var labels = new List<string>(ids.Count + 1) { "<Select replacement>" };
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

        private void OnControllerChanged(CoCoStateGraphEditorInvalidation invalidation)
        {
            if ((invalidation & CoCoStateGraphEditorInvalidation.Toolbar) != 0)
            {
                RefreshToolbar();
            }

            if ((invalidation & CoCoStateGraphEditorInvalidation.Tree) != 0)
            {
                RefreshTree();
            }

            if ((invalidation & CoCoStateGraphEditorInvalidation.Details) != 0)
            {
                RefreshDetails();
            }
            else if ((invalidation & CoCoStateGraphEditorInvalidation.Feedback) != 0)
            {
                RefreshFeedback();
            }

            Repaint();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            RefreshToolbar();
            RefreshDetails();
            canvas?.Refresh();
        }

        private void OnCanvasContextRequested(Vector2 position)
        {
            if (!CoCoStateGraphAuthoringOperations.CanEdit(out _))
            {
                return;
            }

            contextPosition = position;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add State Here"), false, () =>
            {
                TryExecuteCanvasAuthoringAction(() => TryAddStateAtCanvasPosition(contextPosition));
            });
            menu.AddItem(new GUIContent("Paste Subtree Here"), false, () =>
                TryExecuteCanvasAuthoringAction(() => controller.PasteState(
                    controller.Session.DrillRootStateId,
                    contextPosition)));
            menu.ShowAsContext();
        }

        internal bool TryAddStateAtCanvasPosition(Vector2 position)
        {
            if (controller == null || !CoCoStateGraphAuthoringOperations.CanEdit(out _))
            {
                return false;
            }

            CoCoStateDescriptor descriptor = ResolveStateDescriptor(addStateDescriptorId);
            return controller.AddState(
                controller.Session.DrillRootStateId,
                descriptor,
                "State",
                position);
        }

        private void TryExecuteCanvasAuthoringAction(Action action)
        {
            if (!TryExecuteCanvasAuthoringAction(action, out string failure))
            {
                ShowNotification(new GUIContent(failure));
            }
        }

        internal static bool TryExecuteCanvasAuthoringAction(Action action, out string failure)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            if (!CoCoStateGraphAuthoringOperations.CanEdit(out failure))
            {
                return false;
            }

            action();
            failure = string.Empty;
            return true;
        }

        private void DeleteLayer()
        {
            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            if (layer == null ||
                !EditorUtility.DisplayDialog(
                    "Delete State Graph Layer",
                    $"Delete '{layer.DisplayName}' with {layer.States.Count} State(s) and " +
                    $"{layer.Transitions.Count} Transition(s)?",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            controller.DeleteLayer();
        }

        private void DeleteState(StateIdChoice replacement)
        {
            CoCoStateGraphLayerRecord layer = controller.SelectedLayer;
            CoCoStateGraphStateRecord selected = FindState(layer, controller.Session.SelectedStateId);
            if (!CoCoStateGraphAuthoringOperations.TryGetDeleteImpact(
                    asset,
                    controller.Session.SelectedLayerId,
                    controller.Session.SelectedStateId,
                    out CoCoStateGraphDeleteImpact impact,
                    out string failure))
            {
                ShowNotification(new GUIContent(failure));
                return;
            }

            bool deletingInitial = selected != null &&
                                   (selected.ParentStateId.IsValid
                                       ? FindState(layer, ToStateId(selected.ParentStateId))?.InitialChildStateId ==
                                         selected.StateId
                                       : layer.InitialStateId == selected.StateId);
            bool hasSurvivingSibling = selected != null && SiblingIds(layer, selected).Count > 0;
            if (deletingInitial && hasSurvivingSibling && !replacement.HasExplicitSelection)
            {
                ShowNotification(new GUIContent(
                    "Choose an explicit replacement before deleting the initial State."));
                return;
            }

            string initialWarning = deletingInitial && !hasSurvivingSibling
                ? "\n\nThis is the last initial State in its scope. Initial will be cleared and the saved " +
                  "draft may remain compiler-invalid until another State is assigned."
                : string.Empty;

            if (EditorUtility.DisplayDialog(
                    "Delete State Subtree",
                    $"Delete {impact.StateCount} State(s) and {impact.TransitionCount} incident Transition(s)?" +
                    initialWarning,
                    "Delete",
                    "Cancel"))
            {
                controller.DeleteSelectedState(replacement.Value);
            }
        }

        private static bool RequiresInitialReplacement(
            CoCoStateGraphLayerRecord layer,
            CoCoStateGraphStateRecord selected)
        {
            if (layer == null || selected == null || SiblingIds(layer, selected).Count == 0)
            {
                return false;
            }

            return selected.ParentStateId.IsValid
                ? FindState(layer, ToStateId(selected.ParentStateId))?.InitialChildStateId ==
                  selected.StateId
                : layer.InitialStateId == selected.StateId;
        }

        private CoCoStateDescriptor ResolveStateDescriptor(CoCoStateDescriptorId descriptorId)
        {
            if (controller?.Catalog != null)
            {
                foreach (CoCoStateDescriptor descriptor in controller.Catalog.StateDescriptors)
                {
                    if (descriptor.DescriptorId == descriptorId)
                    {
                        return descriptor;
                    }
                }
            }

            return null;
        }

        private CoCoConditionDescriptor ResolveConditionDescriptor(CoCoConditionDescriptor fallback)
        {
            if (controller?.Catalog != null)
            {
                foreach (CoCoConditionDescriptor descriptor in controller.Catalog.ConditionDescriptors)
                {
                    if (descriptor.DescriptorId == addConditionDescriptorId)
                    {
                        return descriptor;
                    }
                }
            }

            return fallback;
        }

        private Vector2 NextPosition()
        {
            int index = controller.VisibleStates.Count;
            return new Vector2(40f + (index % 4) * 220f, 60f + (index / 4) * 150f);
        }

        private string StateLabel(CoCoStateId stateId)
        {
            if (!stateId.IsValid)
            {
                return "Layer root";
            }

            CoCoStateGraphStateRecord state = FindState(controller.SelectedLayer, stateId);
            return state == null
                ? stateId.ToString()
                : $"{state.DisplayName}  [{ShortId(stateId.ToString())}]";
        }

        private static string TransitionLabel(
            CoCoStateGraphLayerRecord layer,
            CoCoStateGraphTransitionRecord transition)
        {
            CoCoStateGraphStateRecord source = FindState(layer, ToStateId(transition.SourceStateId));
            CoCoStateGraphStateRecord target = FindState(layer, ToStateId(transition.TargetStateId));
            return $"{source?.DisplayName ?? "?"} → {target?.DisplayName ?? "?"}  " +
                   $"Priority {transition.Priority}";
        }

        private static List<CoCoStateId> SiblingIds(
            CoCoStateGraphLayerRecord layer,
            CoCoStateGraphStateRecord state)
        {
            var result = new List<CoCoStateId>();
            foreach (CoCoStateGraphStateRecord candidate in layer.States)
            {
                if (candidate != null &&
                    candidate.StateId != state.StateId &&
                    candidate.ParentStateId == state.ParentStateId)
                {
                    result.Add(ToStateId(candidate.StateId));
                }
            }

            return result;
        }

        private static CoCoStateGraphStateRecord FindState(
            CoCoStateGraphLayerRecord layer,
            CoCoStateId stateId)
        {
            if (layer == null || !stateId.IsValid)
            {
                return null;
            }

            foreach (CoCoStateGraphStateRecord state in layer.States)
            {
                if (state != null && state.StateId.High == stateId.High && state.StateId.Low == stateId.Low)
                {
                    return state;
                }
            }

            return null;
        }

        private static CoCoStateGraphTransitionRecord FindTransition(
            CoCoStateGraphLayerRecord layer,
            CoCoTransitionId transitionId)
        {
            if (layer == null || !transitionId.IsValid)
            {
                return null;
            }

            foreach (CoCoStateGraphTransitionRecord transition in layer.Transitions)
            {
                if (transition != null &&
                    transition.TransitionId.High == transitionId.High &&
                    transition.TransitionId.Low == transitionId.Low)
                {
                    return transition;
                }
            }

            return null;
        }

        private static bool HasChildren(CoCoStateGraphLayerRecord layer, CoCoSerializedId128 stateId)
        {
            foreach (CoCoStateGraphStateRecord candidate in layer.States)
            {
                if (candidate != null && candidate.ParentStateId == stateId)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddHeading(string text)
        {
            AddHeading(details, text);
        }

        private static void AddHeading(VisualElement parent, string text)
        {
            var label = new Label(text);
            label.AddToClassList("state-graph-heading");
            parent.Add(label);
        }

        private void DisposeController()
        {
            if (canvas != null)
            {
                canvas.ContextRequested -= OnCanvasContextRequested;
                canvas.Dispose();
                canvas = null;
            }

            if (controller != null)
            {
                controller.Changed -= OnControllerChanged;
                controller.Dispose();
                controller = null;
            }

            serializedAsset = null;
        }

        private static CoCoStateId ToStateId(CoCoSerializedId128 id)
        {
            CoCoStateId.TryCreate(id.High, id.Low, out CoCoStateId value);
            return value;
        }

        private static CoCoStateDescriptorId ToStateDescriptorId(CoCoSerializedId128 id)
        {
            CoCoStateDescriptorId.TryCreate(id.High, id.Low, out CoCoStateDescriptorId value);
            return value;
        }

        private static bool TryLayerId(CoCoSerializedId128 id, out CoCoLayerId value) =>
            CoCoLayerId.TryCreate(id.High, id.Low, out value);

        private static bool TryTransitionId(CoCoSerializedId128 id, out CoCoTransitionId value) =>
            CoCoTransitionId.TryCreate(id.High, id.Low, out value);

        private static string StableStateId(CoCoStateGraphStateRecord state) =>
            ToStateId(state.StateId).ToString();

        private static string ShortId(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);

        private sealed class StateIdChoice
        {
            internal CoCoStateId Value;
            internal bool HasExplicitSelection;
        }

        private sealed class StateDescriptorChoice
        {
            internal CoCoStateDescriptor Value;
        }
    }

    internal static class CoCoStateGraphEditorAssetOpenHandler
    {
        [OnOpenAsset]
        private static bool OnOpenAsset(int instanceId, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(instanceId) as CoCoStateGraphAsset;
            if (asset == null)
            {
                return false;
            }

            CoCoStateGraphEditorWindow.Open(asset);
            return true;
        }
    }
}
