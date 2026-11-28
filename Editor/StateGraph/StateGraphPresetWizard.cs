using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Core;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    [MovedFrom(true, sourceClassName: "CoCoStateGraphPresetWizard")]
    internal sealed class StateGraphPresetWizard : EditorWindow
    {
        private enum PresetKind
        {
            Simple = 0,
            Combo = 1
        }

        private PresetKind presetKind;
        private GraphDescriptorCatalog catalog;
        private StateDescriptorId simpleSourceDescriptorId;
        private StateDescriptorId simpleTargetDescriptorId;
        private StateDescriptorId comboStepDescriptorId;
        private StateDescriptorId comboExitDescriptorId;
        private double comboWindowStart = 0.9d;
        private double comboWindowEnd = 1d;
        private VisualElement form;
        private HelpBox status;

        internal static void Open()
        {
            StateGraphPresetWizard window = GetWindow<StateGraphPresetWizard>(utility: true);
            window.titleContent = new GUIContent("State Graph Preset");
            window.minSize = new Vector2(430f, 390f);
            window.Show();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            CoCoEditorElements.ApplyTheme(rootVisualElement);

            var heading = CoCoEditorElements.CreateHeading(CoCoEditorLocalization.Text(
                "Create State Graph Preset",
                "创建 StateGraph 预设"));
            rootVisualElement.Add(heading);
            var intro = new Label(CoCoEditorLocalization.Text(
                "Presets create ordinary StateGraphAsset files using descriptors from the registered catalog.",
                "预设使用注册目录中的描述符创建普通 StateGraphAsset 文件。"));
            intro.AddToClassList("sg-muted");
            rootVisualElement.Add(intro);

            var card = CoCoEditorElements.CreateCard(CoCoEditorLocalization.Text("Parameters", "参数"));
            var kind = new EnumField(CoCoEditorLocalization.Text("Preset", "预设"), presetKind);
            kind.RegisterValueChangedCallback(evt =>
            {
                presetKind = (PresetKind)evt.newValue;
                RebuildForm();
            });
            card.Add(kind);

            form = new VisualElement();
            card.Add(form);
            rootVisualElement.Add(card);

            status = new HelpBox(string.Empty, HelpBoxMessageType.None);
            status.style.display = DisplayStyle.None;
            rootVisualElement.Add(status);
            var create = CoCoEditorElements.CreatePrimaryButton("Create Asset…", CreatePreset);
            create.SetEnabled(StateGraphAuthoringOperations.CanEdit(out _));
            rootVisualElement.Add(create);
            ReloadCatalog();
            RebuildForm();
        }

        private void ReloadCatalog()
        {
            catalog = null;
            Func<GraphDescriptorCatalog> provider = StateGraphEditorCatalogProvider.Provider;
            if (provider == null)
            {
                SetStatus(
                    "No descriptor catalog provider is registered. Project Editor setup must inject a frozen catalog.",
                    HelpBoxMessageType.Warning);
                return;
            }

            try
            {
                catalog = provider();
                if (catalog == null)
                {
                    SetStatus("The registered descriptor catalog provider returned null.", HelpBoxMessageType.Warning);
                    return;
                }

                CoCoDiagnostic[] diagnostics =
                    StateGraphAuthoringDependencyClosureValidator.Validate(catalog);
                if (diagnostics.Length > 0)
                {
                    catalog = null;
                    SetStatus(diagnostics[0].Message, HelpBoxMessageType.Error);
                }
            }
            catch (Exception exception)
            {
                SetStatus($"Descriptor catalog provider failed: {exception.Message}", HelpBoxMessageType.Error);
            }
        }

        private void RebuildForm()
        {
            if (form == null)
            {
                return;
            }

            form.Clear();
            if (catalog == null)
            {
                form.Add(new Label(CoCoEditorLocalization.Text(
                    "A catalog is required before a preset can be parameterized.",
                    "需要目录才能配置预设参数。")));
                return;
            }

            if (presetKind == PresetKind.Simple)
            {
                IReadOnlyList<StateDescriptor> descriptors = catalog.StateDescriptors;
                AddStateDescriptorPopup(
                    CoCoEditorLocalization.Text("Start descriptor", "起始描述符"),
                    descriptors,
                    simpleSourceDescriptorId,
                    value => simpleSourceDescriptorId = value);
                AddStateDescriptorPopup(
                    CoCoEditorLocalization.Text("End descriptor", "结束描述符"),
                    descriptors,
                    simpleTargetDescriptorId,
                    value => simpleTargetDescriptorId = value);
                form.Add(new HelpBox(
                    CoCoEditorLocalization.Text(
                        "Creates one Layer with Start and End root leaf States and one Start → End Always Transition.",
                        "创建一个 Layer，含 Start/End 两个根叶子 State 和一条 Start → End Always Transition。"),
                    HelpBoxMessageType.Info));
                return;
            }

            var progressDescriptors = new List<StateDescriptor>();
            foreach (StateDescriptor descriptor in catalog.StateDescriptors)
            {
                if (descriptor.ProvidesActionProgress)
                {
                    progressDescriptors.Add(descriptor);
                }
            }

            AddStateDescriptorPopup(
                CoCoEditorLocalization.Text("Step1–4 descriptor", "Step1–4 描述符"),
                progressDescriptors,
                comboStepDescriptorId,
                value => comboStepDescriptorId = value);
            AddStateDescriptorPopup(
                CoCoEditorLocalization.Text("Exit descriptor", "退出描述符"),
                catalog.StateDescriptors,
                comboExitDescriptorId,
                value => comboExitDescriptorId = value);
            var start = new DoubleField(CoCoEditorLocalization.Text("Window start inclusive", "窗口起始（含）"))
            {
                value = comboWindowStart
            };
            start.RegisterValueChangedCallback(evt => comboWindowStart = evt.newValue);
            form.Add(start);
            var end = new DoubleField(CoCoEditorLocalization.Text("Window end exclusive", "窗口结束（不含）"))
            {
                value = comboWindowEnd
            };
            end.RegisterValueChangedCallback(evt => comboWindowEnd = evt.newValue);
            form.Add(end);
            form.Add(new HelpBox(
                CoCoEditorLocalization.Text(
                    "Creates Step1 → Step2 → Step3 → Step4 → Exit using four ActionProgress Transitions.",
                    "用四条 ActionProgress Transition 创建 Step1 → Step2 → Step3 → Step4 → Exit。"),
                HelpBoxMessageType.Info));
        }

        private void AddStateDescriptorPopup(
            string label,
            IReadOnlyList<StateDescriptor> descriptors,
            StateDescriptorId selectedId,
            Action<StateDescriptorId> selected)
        {
            if (descriptors.Count == 0)
            {
                form.Add(new HelpBox(
                    CoCoEditorLocalization.Text(
                        $"No descriptors are eligible for '{label}'.",
                        $"「{label}」没有可用描述符。"),
                    HelpBoxMessageType.Error));
                selected(default);
                return;
            }

            int selectedIndex = 0;
            var labels = new List<string>(descriptors.Count);
            for (int index = 0; index < descriptors.Count; index++)
            {
                StateDescriptor descriptor = descriptors[index];
                labels.Add($"{descriptor.LogicType.Name}  [{ShortId(descriptor.DescriptorId.ToString())}]");
                if (descriptor.DescriptorId == selectedId)
                {
                    selectedIndex = index;
                }
            }

            selected(descriptors[selectedIndex].DescriptorId);
            var popup = new PopupField<string>(label, labels, selectedIndex);
            popup.RegisterValueChangedCallback(evt =>
            {
                int index = labels.IndexOf(evt.newValue);
                if (index >= 0)
                {
                    selected(descriptors[index].DescriptorId);
                }
            });
            form.Add(popup);
        }

        private void CreatePreset()
        {
            if (catalog == null)
            {
                SetStatus("A frozen descriptor catalog is required.", HelpBoxMessageType.Error);
                return;
            }

            if (!TryPrepare(out PresetParameters parameters, out string failure))
            {
                SetStatus(failure, HelpBoxMessageType.Error);
                return;
            }

            if (!TryPreflight(parameters, out failure))
            {
                SetStatus(failure, HelpBoxMessageType.Error);
                return;
            }

            string defaultName = presetKind == PresetKind.Simple
                ? "SimpleStateGraph"
                : "ComboStateGraph";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create State Graph Preset",
                defaultName,
                "asset",
                "Choose a location for the ordinary StateGraph Asset.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (AssetDatabase.LoadMainAssetAtPath(path) != null)
            {
                SetStatus("The selected asset path is already occupied.", HelpBoxMessageType.Error);
                return;
            }

            StateGraphAsset asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            string undoName = $"Create {presetKind} State Graph Preset";
            Undo.SetCurrentGroupName(undoName);
            try
            {
                AssetDatabase.CreateAsset(asset, path);
                Undo.RegisterCreatedObjectUndo(asset, undoName);
                string guid = AssetDatabase.AssetPathToGUID(path);
                asset.EnsureAssetIdentity(guid);
                bool populated = presetKind == PresetKind.Simple
                    ? PopulateSimple(asset, parameters, out failure)
                    : PopulateCombo(asset, parameters, catalog, out failure);
                if (!populated)
                {
                    throw new InvalidOperationException(failure);
                }

                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
                Undo.CollapseUndoOperations(undoGroup);
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
                StateGraphEditorWindow.Open(asset);
                Close();
            }
            catch (Exception exception)
            {
                if (!string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
                {
                    AssetDatabase.DeleteAsset(path);
                }
                else
                {
                    DestroyImmediate(asset);
                }

                SetStatus($"Preset creation failed without leaving an Asset: {exception.Message}", HelpBoxMessageType.Error);
            }
        }

        private bool TryPrepare(out PresetParameters parameters, out string failure)
        {
            parameters = default;
            if (presetKind == PresetKind.Simple)
            {
                if (!catalog.TryGetStateDescriptor(
                        simpleSourceDescriptorId,
                        out StateDescriptor sourceDescriptor) ||
                    !catalog.TryGetStateDescriptor(
                        simpleTargetDescriptorId,
                        out StateDescriptor targetDescriptor))
                {
                    failure = "Select both Simple State descriptors.";
                    return false;
                }

                if (!StateGraphEditorController.TryCreateStateConfig(
                        sourceDescriptor,
                        out StateConfig sourceConfig,
                        out failure) ||
                    !StateGraphEditorController.TryCreateStateConfig(
                        targetDescriptor,
                        out StateConfig targetConfig,
                        out failure))
                {
                    return false;
                }

                parameters = new PresetParameters(
                    sourceDescriptor,
                    targetDescriptor,
                    sourceConfig,
                    targetConfig,
                    default);
                failure = string.Empty;
                return true;
            }

            if (!catalog.TryGetStateDescriptor(
                    comboStepDescriptorId,
                    out StateDescriptor stepDescriptor) ||
                !stepDescriptor.ProvidesActionProgress ||
                !catalog.TryGetStateDescriptor(
                    comboExitDescriptorId,
                    out StateDescriptor exitDescriptor))
            {
                failure = "Combo requires an ActionProgress Step descriptor and an Exit descriptor.";
                return false;
            }

            if (!TransitionWindow.TryCreate(
                    TransitionWindowMode.ActionProgress,
                    comboWindowStart,
                    comboWindowEnd,
                    out TransitionWindow window))
            {
                failure = "Combo requires a valid ActionProgress [start, end) Window within [0, 1].";
                return false;
            }

            if (!StateGraphEditorController.TryCreateStateConfig(
                    stepDescriptor,
                    out StateConfig stepConfig,
                    out failure) ||
                !StateGraphEditorController.TryCreateStateConfig(
                    exitDescriptor,
                    out StateConfig exitConfig,
                    out failure))
            {
                return false;
            }

            parameters = new PresetParameters(
                stepDescriptor,
                exitDescriptor,
                stepConfig,
                exitConfig,
                window);
            failure = string.Empty;
            return true;
        }

        private bool TryPreflight(PresetParameters parameters, out string failure)
        {
            var transient = ScriptableObject.CreateInstance<StateGraphAsset>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            try
            {
                transient.EnsureAssetIdentity("preset-preflight");
                bool populated = presetKind == PresetKind.Simple
                    ? PopulateSimple(transient, parameters, out failure)
                    : PopulateCombo(transient, parameters, catalog, out failure);
                if (!populated)
                {
                    return false;
                }

                GraphAssetCompileResult result =
                    new GraphAssetCompiler().Compile(transient, catalog);
                if (result.Succeeded)
                {
                    failure = string.Empty;
                    return true;
                }

                failure = result.Diagnostics.Count > 0
                    ? $"Preset preflight failed: {result.Diagnostics[0].Diagnostic.Message}"
                    : "Preset preflight failed without a diagnostic.";
                return false;
            }
            catch (Exception exception)
            {
                failure = $"Preset preflight failed: {exception.Message}";
                return false;
            }
            finally
            {
                Undo.RevertAllDownToGroup(undoGroup);
                DestroyImmediate(transient);
            }
        }

        internal static bool TryPopulateSimple(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog,
            StateDescriptorId sourceDescriptorId,
            StateDescriptorId targetDescriptorId,
            out string failure)
        {
            failure = string.Empty;
            if (asset == null || catalog == null)
            {
                failure = "Simple preset requires one Asset and frozen descriptor Catalog.";
                return false;
            }

            if (!catalog.TryGetStateDescriptor(sourceDescriptorId, out StateDescriptor source) ||
                !catalog.TryGetStateDescriptor(targetDescriptorId, out StateDescriptor target) ||
                !StateGraphEditorController.TryCreateStateConfig(
                    source,
                    out StateConfig sourceConfig,
                    out failure) ||
                !StateGraphEditorController.TryCreateStateConfig(
                    target,
                    out StateConfig targetConfig,
                    out failure))
            {
                if (string.IsNullOrEmpty(failure))
                {
                    failure = "Simple preset descriptors are invalid.";
                }

                return false;
            }

            return PopulateSimple(
                asset,
                new PresetParameters(source, target, sourceConfig, targetConfig, default),
                out failure);
        }

        internal static bool TryPopulateCombo(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog,
            StateDescriptorId stepDescriptorId,
            StateDescriptorId exitDescriptorId,
            TransitionWindow window,
            out string failure)
        {
            failure = string.Empty;
            if (asset == null || catalog == null)
            {
                failure = "Combo preset requires one Asset and frozen descriptor Catalog.";
                return false;
            }

            if (!window.IsValid ||
                window.Mode != TransitionWindowMode.ActionProgress ||
                !catalog.TryGetStateDescriptor(stepDescriptorId, out StateDescriptor step) ||
                !step.ProvidesActionProgress ||
                !catalog.TryGetStateDescriptor(exitDescriptorId, out StateDescriptor exit) ||
                !StateGraphEditorController.TryCreateStateConfig(
                    step,
                    out StateConfig stepConfig,
                    out failure) ||
                !StateGraphEditorController.TryCreateStateConfig(
                    exit,
                    out StateConfig exitConfig,
                    out failure))
            {
                if (string.IsNullOrEmpty(failure))
                {
                    failure = "Combo preset descriptors or Window are invalid.";
                }

                return false;
            }

            return PopulateCombo(
                asset,
                new PresetParameters(step, exit, stepConfig, exitConfig, window),
                catalog,
                out failure);
        }

        private static bool PopulateSimple(
            StateGraphAsset asset,
            PresetParameters parameters,
            out string failure)
        {
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Simple");
            if (!StateGraphAuthoringOperations.TryAddState(
                    asset,
                    layerId,
                    default,
                    parameters.PrimaryDescriptor.DescriptorId,
                    parameters.PrimaryConfig,
                    "Start",
                    new Vector2(80f, 120f),
                    out StateId start,
                    out failure) ||
                !StateGraphAuthoringOperations.TryAddState(
                    asset,
                    layerId,
                    default,
                    parameters.ExitDescriptor.DescriptorId,
                    parameters.ExitConfig,
                    "End",
                    new Vector2(360f, 120f),
                    out StateId end,
                    out failure))
            {
                return false;
            }

            return StateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                start,
                end,
                0,
                TransitionWindow.Always,
                catalog: null,
                out _,
                out failure);
        }

        private static bool PopulateCombo(
            StateGraphAsset asset,
            PresetParameters parameters,
            GraphDescriptorCatalog catalog,
            out string failure)
        {
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Combo");
            var stateIds = new StateId[5];
            for (int index = 0; index < stateIds.Length; index++)
            {
                bool isExit = index == stateIds.Length - 1;
                StateDescriptor descriptor = isExit
                    ? parameters.ExitDescriptor
                    : parameters.PrimaryDescriptor;
                if (!StateGraphEditorController.TryCreateStateConfig(
                        descriptor,
                        out StateConfig config,
                        out failure) ||
                    !StateGraphAuthoringOperations.TryAddState(
                        asset,
                        layerId,
                        default,
                        descriptor.DescriptorId,
                        config,
                        isExit ? "Exit" : $"Step{index + 1}",
                        new Vector2(60f + index * 230f, 120f),
                        out stateIds[index],
                        out failure))
                {
                    return false;
                }
            }

            for (int index = 0; index < stateIds.Length - 1; index++)
            {
                if (!StateGraphAuthoringOperations.TryAddTransition(
                        asset,
                        layerId,
                        stateIds[index],
                        stateIds[index + 1],
                        0,
                        parameters.Window,
                        catalog,
                        out _,
                        out failure))
                {
                    return false;
                }
            }

            failure = string.Empty;
            return true;
        }

        private void SetStatus(string message, HelpBoxMessageType type)
        {
            if (status == null)
            {
                return;
            }

            status.text = message ?? string.Empty;
            status.messageType = type;
            status.style.display = string.IsNullOrEmpty(message)
                ? DisplayStyle.None
                : DisplayStyle.Flex;
        }

        private static string ShortId(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);

        private readonly struct PresetParameters
        {
            internal PresetParameters(
                StateDescriptor primaryDescriptor,
                StateDescriptor exitDescriptor,
                StateConfig primaryConfig,
                StateConfig exitConfig,
                TransitionWindow window)
            {
                PrimaryDescriptor = primaryDescriptor;
                ExitDescriptor = exitDescriptor;
                PrimaryConfig = primaryConfig;
                ExitConfig = exitConfig;
                Window = window;
            }

            internal StateDescriptor PrimaryDescriptor { get; }
            internal StateDescriptor ExitDescriptor { get; }
            internal StateConfig PrimaryConfig { get; }
            internal StateConfig ExitConfig { get; }
            internal TransitionWindow Window { get; }
        }
    }
}
