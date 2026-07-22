using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed class CoCoStateGraphPresetWizard : EditorWindow
    {
        private enum PresetKind
        {
            Simple = 0,
            Combo = 1
        }

        private PresetKind presetKind;
        private CoCoGraphDescriptorCatalog catalog;
        private CoCoStateDescriptorId simpleSourceDescriptorId;
        private CoCoStateDescriptorId simpleTargetDescriptorId;
        private CoCoStateDescriptorId comboStepDescriptorId;
        private CoCoStateDescriptorId comboExitDescriptorId;
        private double comboWindowStart = 0.9d;
        private double comboWindowEnd = 1d;
        private VisualElement form;
        private HelpBox status;

        internal static void Open()
        {
            CoCoStateGraphPresetWizard window = GetWindow<CoCoStateGraphPresetWizard>(utility: true);
            window.titleContent = new GUIContent("State Graph Preset");
            window.minSize = new Vector2(430f, 390f);
            window.Show();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.paddingLeft = 12f;
            rootVisualElement.style.paddingRight = 12f;
            rootVisualElement.style.paddingTop = 10f;
            rootVisualElement.style.paddingBottom = 10f;

            var heading = new Label("Create State Graph Preset");
            heading.style.fontSize = 15f;
            heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            rootVisualElement.Add(heading);
            rootVisualElement.Add(new Label(
                "Presets create ordinary CoCoStateGraphAsset files using descriptors from the registered catalog."));

            var kind = new EnumField("Preset", presetKind);
            kind.RegisterValueChangedCallback(evt =>
            {
                presetKind = (PresetKind)evt.newValue;
                RebuildForm();
            });
            rootVisualElement.Add(kind);

            form = new VisualElement();
            rootVisualElement.Add(form);
            status = new HelpBox(string.Empty, HelpBoxMessageType.None);
            status.style.display = DisplayStyle.None;
            rootVisualElement.Add(status);
            var create = new Button(CreatePreset) { text = "Create Asset…" };
            create.SetEnabled(CoCoStateGraphAuthoringOperations.CanEdit(out _));
            rootVisualElement.Add(create);
            ReloadCatalog();
            RebuildForm();
        }

        private void ReloadCatalog()
        {
            catalog = null;
            Func<CoCoGraphDescriptorCatalog> provider = CoCoStateGraphEditorCatalogProvider.Provider;
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
                    CoCoStateGraphAuthoringDependencyClosureValidator.Validate(catalog);
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
                form.Add(new Label("A catalog is required before a preset can be parameterized."));
                return;
            }

            if (presetKind == PresetKind.Simple)
            {
                IReadOnlyList<CoCoStateDescriptor> descriptors = catalog.StateDescriptors;
                AddStateDescriptorPopup(
                    "Start descriptor",
                    descriptors,
                    simpleSourceDescriptorId,
                    value => simpleSourceDescriptorId = value);
                AddStateDescriptorPopup(
                    "End descriptor",
                    descriptors,
                    simpleTargetDescriptorId,
                    value => simpleTargetDescriptorId = value);
                form.Add(new HelpBox(
                    "Creates one Layer with Start and End root leaf States and one Start → End Always Transition.",
                    HelpBoxMessageType.Info));
                return;
            }

            var progressDescriptors = new List<CoCoStateDescriptor>();
            foreach (CoCoStateDescriptor descriptor in catalog.StateDescriptors)
            {
                if (descriptor.ProvidesActionProgress)
                {
                    progressDescriptors.Add(descriptor);
                }
            }

            AddStateDescriptorPopup(
                "Step1–4 descriptor",
                progressDescriptors,
                comboStepDescriptorId,
                value => comboStepDescriptorId = value);
            AddStateDescriptorPopup(
                "Exit descriptor",
                catalog.StateDescriptors,
                comboExitDescriptorId,
                value => comboExitDescriptorId = value);
            var start = new DoubleField("Window start inclusive") { value = comboWindowStart };
            start.RegisterValueChangedCallback(evt => comboWindowStart = evt.newValue);
            form.Add(start);
            var end = new DoubleField("Window end exclusive") { value = comboWindowEnd };
            end.RegisterValueChangedCallback(evt => comboWindowEnd = evt.newValue);
            form.Add(end);
            form.Add(new HelpBox(
                "Creates Step1 → Step2 → Step3 → Step4 → Exit using four ActionProgress Transitions.",
                HelpBoxMessageType.Info));
        }

        private void AddStateDescriptorPopup(
            string label,
            IReadOnlyList<CoCoStateDescriptor> descriptors,
            CoCoStateDescriptorId selectedId,
            Action<CoCoStateDescriptorId> selected)
        {
            if (descriptors.Count == 0)
            {
                form.Add(new HelpBox($"No descriptors are eligible for '{label}'.", HelpBoxMessageType.Error));
                selected(default);
                return;
            }

            int selectedIndex = 0;
            var labels = new List<string>(descriptors.Count);
            for (int index = 0; index < descriptors.Count; index++)
            {
                CoCoStateDescriptor descriptor = descriptors[index];
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

            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
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
                CoCoStateGraphEditorWindow.Open(asset);
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
                        out CoCoStateDescriptor sourceDescriptor) ||
                    !catalog.TryGetStateDescriptor(
                        simpleTargetDescriptorId,
                        out CoCoStateDescriptor targetDescriptor))
                {
                    failure = "Select both Simple State descriptors.";
                    return false;
                }

                if (!CoCoStateGraphEditorController.TryCreateStateConfig(
                        sourceDescriptor,
                        out CoCoStateConfig sourceConfig,
                        out failure) ||
                    !CoCoStateGraphEditorController.TryCreateStateConfig(
                        targetDescriptor,
                        out CoCoStateConfig targetConfig,
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
                    out CoCoStateDescriptor stepDescriptor) ||
                !stepDescriptor.ProvidesActionProgress ||
                !catalog.TryGetStateDescriptor(
                    comboExitDescriptorId,
                    out CoCoStateDescriptor exitDescriptor))
            {
                failure = "Combo requires an ActionProgress Step descriptor and an Exit descriptor.";
                return false;
            }

            if (!CoCoTransitionWindow.TryCreate(
                    CoCoTransitionWindowMode.ActionProgress,
                    comboWindowStart,
                    comboWindowEnd,
                    out CoCoTransitionWindow window))
            {
                failure = "Combo requires a valid ActionProgress [start, end) Window within [0, 1].";
                return false;
            }

            if (!CoCoStateGraphEditorController.TryCreateStateConfig(
                    stepDescriptor,
                    out CoCoStateConfig stepConfig,
                    out failure) ||
                !CoCoStateGraphEditorController.TryCreateStateConfig(
                    exitDescriptor,
                    out CoCoStateConfig exitConfig,
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
            var transient = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
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

                CoCoStateGraphAssetCompileResult result =
                    new CoCoStateGraphAssetCompiler().Compile(transient, catalog);
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
            CoCoStateGraphAsset asset,
            CoCoGraphDescriptorCatalog catalog,
            CoCoStateDescriptorId sourceDescriptorId,
            CoCoStateDescriptorId targetDescriptorId,
            out string failure)
        {
            failure = string.Empty;
            if (asset == null || catalog == null)
            {
                failure = "Simple preset requires one Asset and frozen descriptor Catalog.";
                return false;
            }

            if (!catalog.TryGetStateDescriptor(sourceDescriptorId, out CoCoStateDescriptor source) ||
                !catalog.TryGetStateDescriptor(targetDescriptorId, out CoCoStateDescriptor target) ||
                !CoCoStateGraphEditorController.TryCreateStateConfig(
                    source,
                    out CoCoStateConfig sourceConfig,
                    out failure) ||
                !CoCoStateGraphEditorController.TryCreateStateConfig(
                    target,
                    out CoCoStateConfig targetConfig,
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
            CoCoStateGraphAsset asset,
            CoCoGraphDescriptorCatalog catalog,
            CoCoStateDescriptorId stepDescriptorId,
            CoCoStateDescriptorId exitDescriptorId,
            CoCoTransitionWindow window,
            out string failure)
        {
            failure = string.Empty;
            if (asset == null || catalog == null)
            {
                failure = "Combo preset requires one Asset and frozen descriptor Catalog.";
                return false;
            }

            if (!window.IsValid ||
                window.Mode != CoCoTransitionWindowMode.ActionProgress ||
                !catalog.TryGetStateDescriptor(stepDescriptorId, out CoCoStateDescriptor step) ||
                !step.ProvidesActionProgress ||
                !catalog.TryGetStateDescriptor(exitDescriptorId, out CoCoStateDescriptor exit) ||
                !CoCoStateGraphEditorController.TryCreateStateConfig(
                    step,
                    out CoCoStateConfig stepConfig,
                    out failure) ||
                !CoCoStateGraphEditorController.TryCreateStateConfig(
                    exit,
                    out CoCoStateConfig exitConfig,
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
            CoCoStateGraphAsset asset,
            PresetParameters parameters,
            out string failure)
        {
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Simple");
            if (!CoCoStateGraphAuthoringOperations.TryAddState(
                    asset,
                    layerId,
                    default,
                    parameters.PrimaryDescriptor.DescriptorId,
                    parameters.PrimaryConfig,
                    "Start",
                    new Vector2(80f, 120f),
                    out CoCoStateId start,
                    out failure) ||
                !CoCoStateGraphAuthoringOperations.TryAddState(
                    asset,
                    layerId,
                    default,
                    parameters.ExitDescriptor.DescriptorId,
                    parameters.ExitConfig,
                    "End",
                    new Vector2(360f, 120f),
                    out CoCoStateId end,
                    out failure))
            {
                return false;
            }

            return CoCoStateGraphAuthoringOperations.TryAddTransition(
                asset,
                layerId,
                start,
                end,
                0,
                CoCoTransitionWindow.Always,
                catalog: null,
                out _,
                out failure);
        }

        private static bool PopulateCombo(
            CoCoStateGraphAsset asset,
            PresetParameters parameters,
            CoCoGraphDescriptorCatalog catalog,
            out string failure)
        {
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Combo");
            var stateIds = new CoCoStateId[5];
            for (int index = 0; index < stateIds.Length; index++)
            {
                bool isExit = index == stateIds.Length - 1;
                CoCoStateDescriptor descriptor = isExit
                    ? parameters.ExitDescriptor
                    : parameters.PrimaryDescriptor;
                if (!CoCoStateGraphEditorController.TryCreateStateConfig(
                        descriptor,
                        out CoCoStateConfig config,
                        out failure) ||
                    !CoCoStateGraphAuthoringOperations.TryAddState(
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
                if (!CoCoStateGraphAuthoringOperations.TryAddTransition(
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
                CoCoStateDescriptor primaryDescriptor,
                CoCoStateDescriptor exitDescriptor,
                CoCoStateConfig primaryConfig,
                CoCoStateConfig exitConfig,
                CoCoTransitionWindow window)
            {
                PrimaryDescriptor = primaryDescriptor;
                ExitDescriptor = exitDescriptor;
                PrimaryConfig = primaryConfig;
                ExitConfig = exitConfig;
                Window = window;
            }

            internal CoCoStateDescriptor PrimaryDescriptor { get; }
            internal CoCoStateDescriptor ExitDescriptor { get; }
            internal CoCoStateConfig PrimaryConfig { get; }
            internal CoCoStateConfig ExitConfig { get; }
            internal CoCoTransitionWindow Window { get; }
        }
    }
}
