#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace CoCoFlow.Editor.Core
{
    public sealed class CoCoFlowSetupAssistant : EditorWindow
    {
        private const string ManifestPath = "Packages/manifest.json";
        private const string UniTaskPackageName = "com.cysharp.unitask";
        private const string RecommendedUniTaskGitUrl = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11";
        private const string AddressablesPackageName = "com.unity.addressables";
        private const string RecommendedAddressablesVersion =
            AddressablesVersionPolicy.MinimumVersion;
        private const string RecommendedAddressablesPackage =
            AddressablesPackageName + "@" + RecommendedAddressablesVersion;
        private const string NewtonsoftPackageName = "com.unity.nuget.newtonsoft-json";
        private const string NewtonsoftMinimumVersion = "3.2.2";
        private const string CinemachineAssemblyName = "Unity.Cinemachine";
        private const string OpenUpmRegistryName = "package.openupm.com";
        private const string OpenUpmRegistryUrl = "https://package.openupm.com";
        private const string UniTaskScope = "com.cysharp.unitask";
        private const string UniTaskDefine = "COCOFLOW_UNITASK_SUPPORT";
        private const string DotweenDefine = "COCOFLOW_DOTWEEN_SUPPORT";
        private const string UniTaskDotweenDefine = "UNITASK_DOTWEEN_SUPPORT";
        private const string UniTaskSupportedRange = CoCoUniTaskVersionPolicy.SupportedRange;

        private static readonly ModuleDefinition[] Modules =
        {
            new ModuleDefinition(
                "Core",
                new string[0],
                new string[0],
                "Always compiled."),
            new ModuleDefinition(
                "Input",
                new string[0],
                new[] { "Unity.InputSystem" },
                "Input System runtime module."),
            new ModuleDefinition(
                "Input Prompt (UI)",
                new[] { UniTaskDefine, DotweenDefine, UniTaskDotweenDefine },
                new[]
                {
                    "Unity.InputSystem",
                    "CoCoFlow.Runtime.Modules.Input",
                    "CoCoFlow.Runtime.Modules.Input.UI",
                    "CoCoFlow.Runtime.Modules.Localization.UI",
                    "CoCoFlow.Runtime.Modules.UI"
                },
                "Optional UI V2 binding display, device glyph, and localized prompt presentation."),
            new ModuleDefinition(
                "Localization",
                new string[0],
                new[]
                {
                    "Unity.Localization",
                    "CoCoFlow.Runtime.Modules.Localization"
                },
                "Official Unity Localization core integration and diagnostics."),
            new ModuleDefinition(
                "Localization (UI)",
                new[] { UniTaskDefine, DotweenDefine, UniTaskDotweenDefine },
                new[]
                {
                    "Unity.Localization",
                    "Unity.TextMeshPro",
                    "CoCoFlow.Runtime.Modules.Localization.UI",
                    "CoCoFlow.Runtime.Modules.UI"
                },
                "Optional UI V2 localized text Widget."),
            new ModuleDefinition(
                "Camera",
                new string[0],
                new[] { CinemachineAssemblyName },
                "Cinemachine runtime module."),
            new ModuleDefinition(
                "Content (Direct)",
                new[] { UniTaskDefine },
                new[] { "UniTask" },
                "Direct Asset, Prefab Source, and additive Scene ownership."),
            new ModuleDefinition(
                "Content (Addressables)",
                new[] { UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "Unity.Addressables",
                    "CoCoFlow.Runtime.Content.Addressables"
                },
                "Optional Addressables backend; enabled by assembly version detection."),
            new ModuleDefinition(
                "Pooling",
                new[] { UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Content",
                    "CoCoFlow.Runtime.Pooling"
                },
                "Content-backed GameObject pooling using Unity's private pool implementation."),
            new ModuleDefinition(
                "Pooling (Temporal)",
                new[] { UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Pooling",
                    "CoCoFlow.Runtime.Pooling.Temporal",
                    "CoCoFlow.Runtime.StateGraphHost"
                },
                "Optional Host-scoped temporal retention sidecar; not world rollback."),
            new ModuleDefinition(
                "Map (Region Fidelity)",
                new[] { UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Content",
                    "CoCoFlow.Runtime.Modules.Map"
                },
                "Compiled Region Profiles, scoped Demand Leases, per-Chunk fidelity, and transactional participants."),
            new ModuleDefinition(
                "Map (Pooling)",
                new[] { UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Modules.Map",
                    "CoCoFlow.Runtime.Pooling",
                    "CoCoFlow.Runtime.Modules.Map.Pooling"
                },
                "Optional Region participant adapter with committed-node PoolScope ownership."),
            new ModuleDefinition(
                "Map (Temporal)",
                new[] { UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Modules.Map",
                    "CoCoFlow.Runtime.StateGraphHost",
                    "CoCoFlow.Runtime.Modules.Map.Temporal"
                },
                "Map-first availability and retention decorator; it does not replay Map state."),
            new ModuleDefinition(
                "Animation",
                new string[0],
                new[]
                {
                    "CoCoFlow.Runtime.Animation.Contracts",
                    "CoCoFlow.Runtime.Modules.Animation"
                },
                "Animator Controller parameter/trigger and manual Playable Operators."),
            new ModuleDefinition(
                "Animation (DOTween)",
                new[] { DotweenDefine },
                new[]
                {
                    "DOTween",
                    "CoCoFlow.Runtime.Modules.Animation",
                    "CoCoFlow.Runtime.Modules.Animation.DOTween"
                },
                "Optional Operator-owned manual modulation; never advances global tweens."),
            new ModuleDefinition(
                "Animation (UniTask)",
                new[] { UniTaskDefine },
                new[]
                {
                    "UniTask",
                    "CoCoFlow.Runtime.Modules.Animation",
                    "CoCoFlow.Runtime.Modules.Animation.UniTask"
                },
                "Optional playback-token waiter; cancellation does not stop playback."),
            new ModuleDefinition(
                "UI",
                new[] { UniTaskDefine, DotweenDefine, UniTaskDotweenDefine },
                new[] { "UniTask", "DOTween.Modules", "UniTask.DOTween", "Unity.TextMeshPro", "CoCoFlow.Runtime.Content" },
                "DOTween animated UI module.")
        };

        private readonly List<string> _log = new List<string>();
        private DependencyStatus _status;
        private Vector2 _scrollPosition;
        private AddRequest _uniTaskRequest;
        private AddRequest _addressablesRequest;
        private bool _isBusy;

        [MenuItem("CoCoFlow/Setup/Setup Assistant")]
        public static void Open()
        {
            var window = GetWindow<CoCoFlowSetupAssistant>("CoCoFlow Setup");
            window.minSize = new Vector2(620f, 560f);
            window.RefreshStatus();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshStatus();
        }

        private void OnDisable()
        {
            EditorApplication.update -= TickPackageRequest;
            EditorApplication.update -= TickAddressablesPackageRequest;
        }

        private void OnGUI()
        {
            if (_status == null)
                RefreshStatus();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawHeader();
            DrawDependencies();
            DrawDefines();
            DrawModules();
            DrawActions();
            DrawLog();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("CoCoFlow Setup Assistant", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Configure project dependencies and enable CoCoFlow support defines.",
                MessageType.Info);
        }

        private void DrawDependencies()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawStatusLine("UniTask", _status.UniTaskMessage, _status.UniTaskState);
                DrawStatusLine(
                    "Addressables (Optional)",
                    _status.AddressablesMessage,
                    _status.AddressablesState);
                DrawStatusLine("Newtonsoft", _status.NewtonsoftMessage, _status.NewtonsoftState);
                DrawStatusLine("Cinemachine", _status.CinemachineInstalled ? "Detected from package dependency." : "Missing. It should resolve from CoCoFlow package dependencies.", _status.CinemachineInstalled ? MessageType.Info : MessageType.Warning);
                DrawStatusLine("DOTween", _status.DotweenMessage, _status.DotweenModulesInstalled ? MessageType.Info : MessageType.Warning);

                if (_status.HasUniTaskOpenUpmScope)
                    DrawStatusLine("OpenUPM", "UniTask scope is still present and will be removed by Apply Recommended Dependencies.", MessageType.Warning);
            }
        }

        private void DrawDefines()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Support Defines", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawDefineLine(UniTaskDefine);
                DrawDefineLine(DotweenDefine);
                DrawDefineLine(UniTaskDotweenDefine);
            }
        }

        private void DrawModules()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Modules", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                foreach (var module in Modules)
                {
                    var missingAssemblies = module.RequiredAssemblies.Where(assembly => !_status.AssemblyAvailable(assembly)).ToArray();
                    var missingDefines = module.RequiredSupportDefines.Where(define => !_status.DefinePresentOnAllTargets(define)).ToArray();
                    var state = missingAssemblies.Length == 0 && missingDefines.Length == 0 ? MessageType.Info : MessageType.Warning;
                    var message = BuildModuleMessage(module, missingAssemblies, missingDefines);
                    DrawStatusLine(module.DisplayName, message, state);
                }
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_isBusy))
                {
                    if (GUILayout.Button("Apply Recommended Dependencies", GUILayout.Height(30f)))
                        ApplyRecommendedDependencies();

                    if (GUILayout.Button("Refresh Status", GUILayout.Height(30f)))
                        RefreshStatus();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(
                           _isBusy || !_status.AddressablesInstallRecommended))
                {
                    if (GUILayout.Button(
                            "Install Supported Addressables " +
                            RecommendedAddressablesVersion,
                            GUILayout.Height(26f)))
                        InstallOptionalAddressables();
                }

                EditorGUILayout.LabelField(
                    "Adds the optional project dependency only; no global support define is written.",
                    EditorStyles.wordWrappedMiniLabel,
                    GUILayout.MinHeight(26f));
            }
        }

        private void DrawLog()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Log", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                if (_log.Count == 0)
                {
                    EditorGUILayout.LabelField("No actions yet.");
                    return;
                }

                foreach (var line in _log)
                    EditorGUILayout.LabelField(line, EditorStyles.wordWrappedLabel);
            }
        }

        private static void DrawStatusLine(string label, string message, MessageType state)
        {
            var prefix = state == MessageType.Info ? "OK" : state == MessageType.Warning ? "WARN" : "ERROR";
            EditorGUILayout.LabelField(label, prefix + " - " + message, EditorStyles.wordWrappedLabel);
        }

        private void DrawDefineLine(string define)
        {
            if (_status.MissingDefineTargets.TryGetValue(define, out var missing) && missing.Count > 0)
                DrawStatusLine(define, BuildDefineMessage(missing), MessageType.Warning);
            else
                DrawStatusLine(define, "Enabled on all checked targets.", MessageType.Info);
        }

        private string BuildModuleMessage(ModuleDefinition module, string[] missingAssemblies, string[] missingDefines)
        {
            if (missingAssemblies.Length == 0 && missingDefines.Length == 0)
                return "Enabled. " + module.Description;

            var parts = new List<string>();
            if (missingAssemblies.Length > 0)
                parts.Add("Missing assemblies: " + string.Join(", ", missingAssemblies));

            if (missingDefines.Length > 0)
                parts.Add("Defines: " + string.Join("; ", missingDefines.Select(BuildDefineSummary).ToArray()));

            return "Disabled or partial. " + string.Join(" | ", parts.ToArray());
        }

        private string BuildDefineMessage(List<string> missingTargets)
        {
            if (_status.CheckedTargetCount <= 0)
                return "Missing on checked targets.";

            if (missingTargets.Count >= _status.CheckedTargetCount)
                return "Disabled on checked targets.";

            var enabledCount = _status.CheckedTargetCount - missingTargets.Count;
            return "Partial: enabled on " + enabledCount + "/" + _status.CheckedTargetCount + " targets; missing " + FormatTargetList(missingTargets) + ".";
        }

        private string BuildDefineSummary(string define)
        {
            if (!_status.MissingDefineTargets.TryGetValue(define, out var missing) || missing.Count == 0)
                return define + " enabled";

            if (_status.CheckedTargetCount > 0 && missing.Count >= _status.CheckedTargetCount)
                return define + " off";

            return define + " partial";
        }

        private static string FormatTargetList(List<string> targets)
        {
            const int maxVisibleTargets = 4;
            if (targets.Count <= maxVisibleTargets)
                return string.Join(", ", targets.ToArray());

            return string.Join(", ", targets.Take(maxVisibleTargets).ToArray()) + " +" + (targets.Count - maxVisibleTargets);
        }

        private void ApplyRecommendedDependencies()
        {
            _log.Clear();

            try
            {
                ConfigureProjectManifest();
            }
            catch (Exception ex)
            {
                AddLog("ERROR: Failed to update Packages/manifest.json. " + ex.Message);
                Debug.LogError("[CoCoFlow Setup] Failed to update manifest:\n" + ex);
                RefreshStatus();
                return;
            }

            try
            {
                _uniTaskRequest = Client.Add(RecommendedUniTaskGitUrl);
                _isBusy = true;
                AddLog("Requested UniTask Git dependency: " + RecommendedUniTaskGitUrl);
                EditorApplication.update -= TickPackageRequest;
                EditorApplication.update += TickPackageRequest;
            }
            catch (Exception ex)
            {
                AddLog("ERROR: Failed to start UniTask install. " + ex.Message);
                Debug.LogError("[CoCoFlow Setup] Failed to start UniTask install:\n" + ex);
                ApplyAvailableSupportDefines(false);
                RefreshStatus();
            }
        }

        private void TickPackageRequest()
        {
            if (_uniTaskRequest == null || !_uniTaskRequest.IsCompleted)
                return;

            EditorApplication.update -= TickPackageRequest;
            _isBusy = false;

            if (_uniTaskRequest.Status == StatusCode.Failure)
            {
                var message = _uniTaskRequest.Error != null ? _uniTaskRequest.Error.message : "Unknown Package Manager error.";
                AddLog("ERROR: UniTask install failed. " + message);
                Debug.LogError("[CoCoFlow Setup] UniTask install failed: " + message);
                ApplyAvailableSupportDefines(false);
                RefreshStatus();
                return;
            }

            AddLog("UniTask Git dependency installed.");
            ApplyAvailableSupportDefines(true);
            AssetDatabase.Refresh();
            RefreshStatus();
        }

        private void InstallOptionalAddressables()
        {
            _log.Clear();

            try
            {
                _addressablesRequest = Client.Add(RecommendedAddressablesPackage);
                _isBusy = true;
                AddLog("Requested optional Addressables dependency: " + RecommendedAddressablesPackage);
                EditorApplication.update -= TickAddressablesPackageRequest;
                EditorApplication.update += TickAddressablesPackageRequest;
            }
            catch (Exception ex)
            {
                _isBusy = false;
                AddLog("ERROR: Failed to start optional Addressables install. " + ex.Message);
                Debug.LogError("[CoCoFlow Setup] Failed to start optional Addressables install:\n" + ex);
                RefreshStatus();
            }
        }

        private void TickAddressablesPackageRequest()
        {
            if (_addressablesRequest == null || !_addressablesRequest.IsCompleted)
                return;

            EditorApplication.update -= TickAddressablesPackageRequest;
            _isBusy = false;

            if (_addressablesRequest.Status == StatusCode.Failure)
            {
                var message = _addressablesRequest.Error != null
                    ? _addressablesRequest.Error.message
                    : "Unknown Package Manager error.";
                AddLog("ERROR: Optional Addressables install failed. " + message);
                Debug.LogError("[CoCoFlow Setup] Optional Addressables install failed: " + message);
                RefreshStatus();
                return;
            }

            AddLog("Optional Addressables dependency installed.");
            AssetDatabase.Refresh();
            RefreshStatus();
        }

        private void ConfigureProjectManifest()
        {
            var manifest = LoadManifest();
            var root = manifest.Root;

            var dependencies = GetOrCreateObject(root, "dependencies", manifest);
            if (!dependencies.TryGetString(NewtonsoftPackageName, out var existingNewtonsoft))
            {
                dependencies.Set(NewtonsoftPackageName, new JsonString(NewtonsoftMinimumVersion));
                manifest.Changed = true;
                AddLog("Added Newtonsoft dependency " + NewtonsoftMinimumVersion + ".");
            }
            else if (IsSemanticVersionLower(existingNewtonsoft, NewtonsoftMinimumVersion))
            {
                dependencies.Set(NewtonsoftPackageName, new JsonString(NewtonsoftMinimumVersion));
                manifest.Changed = true;
                AddLog("Updated Newtonsoft from " + existingNewtonsoft + " to " + NewtonsoftMinimumVersion + ".");
            }
            else
            {
                AddLog("Newtonsoft dependency already satisfies " + NewtonsoftMinimumVersion + " (" + existingNewtonsoft + ").");
            }

            RemoveUniTaskOpenUpmScope(root, manifest);

            if (manifest.Changed)
            {
                string nextManifest =
                    manifest.Root.ToJson(0) + Environment.NewLine;
                if (!CoCoAtomicFileTransaction.TryReplaceUtf8(
                        ManifestPath,
                        nextManifest,
                        IsValidManifestJson,
                        out string backupPath,
                        out string error))
                {
                    throw new IOException(
                        "Atomic manifest replacement failed: " + error);
                }

                AddLog(
                    "Updated Packages/manifest.json atomically. Backup: " +
                    backupPath + ".");
            }
            else
            {
                AddLog("Packages/manifest.json already has recommended non-UniTask entries.");
            }
        }

        private void RemoveUniTaskOpenUpmScope(JsonObject root, ManifestDocument manifest)
        {
            if (!root.TryGetArray("scopedRegistries", out var registries))
                return;

            for (var registryIndex = registries.Items.Count - 1; registryIndex >= 0; registryIndex--)
            {
                if (!(registries.Items[registryIndex] is JsonObject registry))
                    continue;

                var isOpenUpm = registry.TryGetString("name", out var name) && name == OpenUpmRegistryName;
                isOpenUpm = isOpenUpm || (registry.TryGetString("url", out var url) && url == OpenUpmRegistryUrl);
                if (!isOpenUpm || !registry.TryGetArray("scopes", out var scopes))
                    continue;

                for (var scopeIndex = scopes.Items.Count - 1; scopeIndex >= 0; scopeIndex--)
                {
                    if (scopes.Items[scopeIndex] is JsonString scope && scope.Value == UniTaskScope)
                    {
                        scopes.Items.RemoveAt(scopeIndex);
                        manifest.Changed = true;
                        AddLog("Removed UniTask scope from OpenUPM registry.");
                    }
                }

                if (scopes.Items.Count == 0)
                {
                    registries.Items.RemoveAt(registryIndex);
                    manifest.Changed = true;
                    AddLog("Removed empty OpenUPM registry entry.");
                }
            }
        }

        private void ApplyAvailableSupportDefines(bool uniTaskInstallSucceeded)
        {
            // D-02 define 权威状态机：UPM 注册 ⇒ resolved dependency 为唯一权威；
            // 仅「无 UPM 包但程序集存在」（unitypackage）才允许手动 define；
            // 两者皆无 ⇒ 模块缺失。
            string unitaskDependency = ReadManifestUniTaskDependency();
            bool assemblyAvailable = uniTaskInstallSucceeded ||
                                     IsAssemblyInstalled("UniTask") ||
                                     IsTypeAvailable("Cysharp.Threading.Tasks.UniTask, UniTask");
            var form = ClassifyUniTaskForm(!string.IsNullOrEmpty(unitaskDependency), assemblyAvailable);
            var compatibility = CoCoUniTaskVersionPolicy.Evaluate(unitaskDependency);
            bool uniTaskUsable;

            if (form == CoCoUniTaskInstallForm.UpmRegistered)
            {
                // UPM 形态：无论版本是否兼容，都必须移除遗留全局 define，
                // 防止其旁路 versionDefines 的版本边界；失败 = 显式 partial/error。
                RemoveDefinesFromAllValidTargets(UniTaskDefine);

                if (compatibility == CoCoUniTaskVersionCompatibility.BelowMinimum ||
                    compatibility == CoCoUniTaskVersionCompatibility.AtOrAboveMaximum)
                {
                    AddLog("ERROR: UniTask UPM version is outside " + UniTaskSupportedRange +
                          " (" + unitaskDependency + "). UniTask-linked assemblies stay disabled;" +
                          " assembly-only fallback is not allowed.");
                    uniTaskUsable = false;
                }
                else
                {
                    AddLog("UniTask support define is managed automatically by asmdef versionDefines " +
                           UniTaskSupportedRange + ".");
                    uniTaskUsable = true;
                }
            }
            else
            {
                uniTaskUsable = form == CoCoUniTaskInstallForm.AssemblyOnly;
                if (uniTaskUsable)
                    AddLog("UniTask detected as assembly-only (unitypackage). Manual support define is required and allowed.");
            }

            string[] defines = SelectAvailableSupportDefines(
                uniTaskUsable,
                IsDotweenInstalled(),
                IsDotweenModuleInstalled(),
                IsAssemblyInstalled("UniTask.DOTween"));

            if (form == CoCoUniTaskInstallForm.UpmRegistered)
            {
                // UniTask define 已由 versionDefines 管理，不进入手动集合。
                defines = defines.Where(define => define != UniTaskDefine).ToArray();
            }

            if (defines.Length == 0)
            {
                AddLog("No support defines were added because dependencies are not available yet.");
                return;
            }

            AddDefinesToAllValidTargets(defines);
        }

        internal static CoCoUniTaskInstallForm ClassifyUniTaskForm(
            bool manifestHasUniTaskDependency,
            bool uniTaskAssemblyAvailable)
        {
            if (manifestHasUniTaskDependency)
                return CoCoUniTaskInstallForm.UpmRegistered;

            return uniTaskAssemblyAvailable
                ? CoCoUniTaskInstallForm.AssemblyOnly
                : CoCoUniTaskInstallForm.None;
        }

        private string ReadManifestUniTaskDependency()
        {
            try
            {
                var root = LoadManifest().Root;
                if (root.TryGetObject("dependencies", out var dependencies) &&
                    dependencies.TryGetString(UniTaskPackageName, out var dependency))
                    return dependency;
            }
            catch
            {
                // manifest 缺失/损坏时按无 UPM 注册处理，交由程序集探测。
            }

            return null;
        }

        internal static string[] SelectAvailableSupportDefines(
            bool uniTaskAvailable,
            bool dotweenAvailable,
            bool dotweenModulesAvailable,
            bool uniTaskDotweenAvailable)
        {
            var defines = new List<string>();
            if (uniTaskAvailable)
            {
                defines.Add(UniTaskDefine);
            }

            if (dotweenAvailable)
            {
                defines.Add(DotweenDefine);
            }

            if (uniTaskAvailable &&
                dotweenAvailable &&
                dotweenModulesAvailable &&
                uniTaskDotweenAvailable)
            {
                defines.Add(UniTaskDotweenDefine);
            }

            return defines.ToArray();
        }

        private void AddDefinesToAllValidTargets(params string[] definesToAdd)
        {
            var changedTargets = new List<string>();
            var skippedTargets = new List<string>();

            foreach (BuildTargetGroup group in GetCheckedBuildTargetGroups())
            {
                try
                {
                    var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
                    var current = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                    var updated = AddDefines(current, definesToAdd);

                    if (updated == current)
                        continue;

                    PlayerSettings.SetScriptingDefineSymbols(namedTarget, updated);
                    changedTargets.Add(group.ToString());
                }
                catch (Exception ex)
                {
                    skippedTargets.Add(group + " (" + ex.GetType().Name + ")");
                }
            }

            if (changedTargets.Count > 0)
                AddLog("Added support defines to " + changedTargets.Count + " target group(s): " + FormatTargetList(changedTargets) + ".");
            else
                AddLog("Support defines were already configured for checked build targets.");

            if (skippedTargets.Count > 0)
                AddLog("Skipped " + skippedTargets.Count + " unsupported target group(s): " + FormatTargetList(skippedTargets) + ".");
        }

        private void RemoveDefinesFromAllValidTargets(params string[] definesToRemove)
        {
            if (definesToRemove == null || definesToRemove.Length == 0)
                return;

            var changedTargets = new List<string>();
            var skippedTargets = new List<string>();

            var extraTargets = new List<NamedBuildTarget>();
            try { extraTargets.Add(NamedBuildTarget.Server); } catch { }

            foreach (var named in extraTargets)
            {
                try
                {
                    var current = PlayerSettings.GetScriptingDefineSymbols(named);
                    var defines = SplitDefines(current);
                    if (!definesToRemove.Any(define => defines.Contains(define)))
                        continue;
                    var updated = string.Join(";", defines.Where(define => !definesToRemove.Contains(define)).ToArray());
                    PlayerSettings.SetScriptingDefineSymbols(named, updated);
                    changedTargets.Add(named.TargetName);
                }
                catch (Exception ex)
                {
                    skippedTargets.Add(named.TargetName + " (" + ex.GetType().Name + ")");
                }
            }

            foreach (BuildTargetGroup group in GetCheckedBuildTargetGroups())
            {
                try
                {
                    var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
                    var current = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                    var defines = SplitDefines(current);

                    if (!definesToRemove.Any(define => defines.Contains(define)))
                        continue;

                    var updated = string.Join(";", defines.Where(define => !definesToRemove.Contains(define)).ToArray());
                    PlayerSettings.SetScriptingDefineSymbols(namedTarget, updated);
                    changedTargets.Add(group.ToString());
                }
                catch (Exception ex)
                {
                    skippedTargets.Add(group + " (" + ex.GetType().Name + ")");
                }
            }

            if (skippedTargets.Count > 0)
            {
                // 清理失败 = 显式 partial/error，不得显示成功（D-02 状态机红线）。
                AddLog("ERROR: Legacy define cleanup incomplete - failed on " + skippedTargets.Count +
                      " target group(s): " + FormatTargetList(skippedTargets) +
                      ". Stale manual defines remain and must be resolved manually.");
                Debug.LogError("[CoCoFlow Setup] Legacy define cleanup failed on: " + FormatTargetList(skippedTargets));
            }
            else if (changedTargets.Count > 0)
            {
                AddLog("Removed stale manual define(s) from " + changedTargets.Count +
                      " target group(s): " + FormatTargetList(changedTargets) +
                      "; versionDefines is now the single authority.");
            }
        }

        private void RefreshStatus()
        {
            _status = BuildStatus();
            Repaint();
        }

        private DependencyStatus BuildStatus()
        {
            var status = new DependencyStatus();

            try
            {
                var manifest = LoadManifest();
                var root = manifest.Root;
                if (root.TryGetObject("dependencies", out var dependencies))
                {
                    if (dependencies.TryGetString(UniTaskPackageName, out var unitaskDependency))
                        status.UniTaskDependency = unitaskDependency;

                    if (dependencies.TryGetString(AddressablesPackageName, out var addressablesDependency))
                        status.AddressablesDependency = addressablesDependency;

                    if (dependencies.TryGetString(NewtonsoftPackageName, out var newtonsoftDependency))
                        status.NewtonsoftDependency = newtonsoftDependency;
                }

                status.HasUniTaskOpenUpmScope = HasUniTaskOpenUpmScope(root);
            }
            catch (Exception ex)
            {
                status.ManifestError = ex.Message;
            }

            status.UniTaskInstalled = IsAssemblyInstalled("UniTask") || IsTypeAvailable("Cysharp.Threading.Tasks.UniTask, UniTask");
            var unitaskForm = ClassifyUniTaskForm(!string.IsNullOrEmpty(status.UniTaskDependency), status.UniTaskInstalled);
            var unitaskCompatibility = CoCoUniTaskVersionPolicy.Evaluate(status.UniTaskDependency);
            status.UniTaskDefineAutomatic = unitaskForm == CoCoUniTaskInstallForm.UpmRegistered &&
                                            unitaskCompatibility != CoCoUniTaskVersionCompatibility.BelowMinimum &&
                                            unitaskCompatibility != CoCoUniTaskVersionCompatibility.AtOrAboveMaximum;
            status.UniTaskVersionBlocked = unitaskForm == CoCoUniTaskInstallForm.UpmRegistered &&
                                           (unitaskCompatibility == CoCoUniTaskVersionCompatibility.BelowMinimum ||
                                            unitaskCompatibility == CoCoUniTaskVersionCompatibility.AtOrAboveMaximum);
            status.AddressablesInstalled = IsAssemblyInstalled("Unity.Addressables") ||
                                           IsTypeAvailable(
                                               "UnityEngine.AddressableAssets.Addressables, Unity.Addressables");
            status.CinemachineInstalled = IsAssemblyInstalled(CinemachineAssemblyName) || IsTypeAvailable("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
            status.DotweenInstalled = IsDotweenInstalled();
            status.DotweenModulesInstalled = IsDotweenModuleInstalled();
            var checkedTargets = GetCheckedBuildTargetGroups();
            status.CheckedTargetCount = checkedTargets.Count;
            // UPM 形态下 UniTask define 由 versionDefines 自动管理，
            // 不再要求手动出现在 ScriptingDefineSymbols（否则误报 missing）。
            var manualDefines = unitaskForm == CoCoUniTaskInstallForm.UpmRegistered
                ? new[] { DotweenDefine, UniTaskDotweenDefine }
                : new[] { UniTaskDefine, DotweenDefine, UniTaskDotweenDefine };
            status.MissingDefineTargets = GetMissingDefineTargets(manualDefines, checkedTargets);
            if (unitaskForm == CoCoUniTaskInstallForm.UpmRegistered && status.UniTaskDefineAutomatic && status.UniTaskInstalled)
            {
                // versionDefines is the authority here; record the define as
                // satisfied so module status does not show it as partial.
                // UniTaskInstalled is required: an unresolved dependency cannot
                // emit versionDefines yet (fresh open / failed git fetch).
                status.MissingDefineTargets[UniTaskDefine] = new List<string>();
            }
            else if (status.UniTaskVersionBlocked)
            {
                // Blocked UPM version: linked assemblies are disabled; the define
                // must not be reported as enabled on any target.
                status.MissingDefineTargets[UniTaskDefine] = checkedTargets.Select(t => t.ToString()).ToList();
            }

            status.AssemblyStates["UniTask"] = status.UniTaskInstalled;
            status.AssemblyStates["UniTask.DOTween"] = IsAssemblyInstalled("UniTask.DOTween");
            status.AssemblyStates[CinemachineAssemblyName] = status.CinemachineInstalled;
            status.AssemblyStates["Unity.Addressables"] = IsAssemblyInstalled("Unity.Addressables");
            status.AssemblyStates["CoCoFlow.Runtime.Content"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Content");
            status.AssemblyStates["CoCoFlow.Runtime.Content.Addressables"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Content.Addressables");
            status.AssemblyStates["CoCoFlow.Runtime.Pooling"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Pooling");
            status.AssemblyStates["CoCoFlow.Runtime.Pooling.Temporal"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Pooling.Temporal");
            status.AssemblyStates["CoCoFlow.Runtime.StateGraphHost"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.StateGraphHost");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Map"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Map");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Map.Pooling"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Map.Pooling");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Map.Temporal"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Map.Temporal");
            status.AssemblyStates["CoCoFlow.Runtime.Animation.Contracts"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Animation.Contracts");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Animation"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Animation");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Animation.DOTween"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Animation.DOTween");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Animation.UniTask"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Animation.UniTask");
            status.AssemblyStates["Unity.InputSystem"] = IsAssemblyInstalled("Unity.InputSystem");
            status.AssemblyStates["Unity.Localization"] =
                IsAssemblyInstalled("Unity.Localization");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Input.UI"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Input.UI");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Localization"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Localization");
            status.AssemblyStates["CoCoFlow.Runtime.Modules.Localization.UI"] =
                IsAssemblyInstalled("CoCoFlow.Runtime.Modules.Localization.UI");
            status.AssemblyStates["Unity.Mathematics"] = IsAssemblyInstalled("Unity.Mathematics");
            status.AssemblyStates["Unity.TextMeshPro"] = IsAssemblyInstalled("Unity.TextMeshPro");
            status.AssemblyStates["DOTween"] = status.DotweenInstalled;
            status.AssemblyStates["DOTween.Modules"] = status.DotweenModulesInstalled;

            status.UpdateMessages();
            return status;
        }

        private static bool HasUniTaskOpenUpmScope(JsonObject root)
        {
            if (!root.TryGetArray("scopedRegistries", out var registries))
                return false;

            foreach (var item in registries.Items)
            {
                if (!(item is JsonObject registry))
                    continue;

                var isOpenUpm = registry.TryGetString("name", out var name) && name == OpenUpmRegistryName;
                isOpenUpm = isOpenUpm || (registry.TryGetString("url", out var url) && url == OpenUpmRegistryUrl);
                if (!isOpenUpm || !registry.TryGetArray("scopes", out var scopes))
                    continue;

                if (scopes.Items.OfType<JsonString>().Any(scope => scope.Value == UniTaskScope))
                    return true;
            }

            return false;
        }

        private static bool IsDotweenInstalled()
        {
            return IsAssemblyInstalled("DOTween") ||
                   IsTypeAvailable("DG.Tweening.Tween, DOTween");
        }

        private static bool IsDotweenModuleInstalled()
        {
            return IsAssemblyInstalled("DOTween.Modules");
        }

        private static bool IsAssemblyInstalled(string assemblyName)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name == assemblyName)
                    return true;
            }

            return false;
        }

        private static bool IsTypeAvailable(string assemblyQualifiedTypeName)
        {
            return Type.GetType(assemblyQualifiedTypeName, false) != null;
        }

        private static Dictionary<string, List<string>> GetMissingDefineTargets(string[] requiredDefines, List<BuildTargetGroup> checkedTargets)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var define in requiredDefines)
                result[define] = new List<string>();

            foreach (var group in checkedTargets)
            {
                try
                {
                    var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
                    var current = SplitDefines(PlayerSettings.GetScriptingDefineSymbols(namedTarget));
                    foreach (var define in requiredDefines)
                    {
                        if (!current.Contains(define))
                            result[define].Add(group.ToString());
                    }
                }
                catch
                {
                    // Some enum values are unavailable when the platform module is not installed.
                }
            }

            return result;
        }

        private static List<BuildTargetGroup> GetCheckedBuildTargetGroups()
        {
            var result = new List<BuildTargetGroup>();

            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                    continue;

                try
                {
                    var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
                    PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                    result.Add(group);
                }
                catch
                {
                    // Some enum values are unavailable when the platform module is not installed.
                }
            }

            return result;
        }

        private static string AddDefines(string current, IEnumerable<string> definesToAdd)
        {
            var defines = SplitDefines(current);
            var changed = false;

            foreach (var define in definesToAdd)
            {
                if (defines.Contains(define))
                    continue;

                defines.Add(define);
                changed = true;
            }

            return changed ? string.Join(";", defines.ToArray()) : current;
        }

        private static List<string> SplitDefines(string defines)
        {
            return defines
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(define => define.Trim())
                .Where(define => !string.IsNullOrEmpty(define))
                .Distinct()
                .ToList();
        }

        private static ManifestDocument LoadManifest()
        {
            if (!File.Exists(ManifestPath))
                throw new FileNotFoundException("Could not find " + ManifestPath + ".");

            var text = File.ReadAllText(ManifestPath);
            var root = new JsonParser(text).Parse();
            if (!(root is JsonObject rootObject))
                throw new InvalidDataException("Project manifest root must be a JSON object.");

            return new ManifestDocument(rootObject);
        }

        private static bool IsValidManifestJson(string text)
        {
            try
            {
                return new JsonParser(text).Parse() is JsonObject;
            }
            catch
            {
                return false;
            }
        }

        private static JsonObject GetOrCreateObject(JsonObject parent, string key, ManifestDocument manifest)
        {
            if (parent.TryGetObject(key, out var obj))
                return obj;

            obj = new JsonObject();
            parent.Set(key, obj);
            manifest.Changed = true;
            return obj;
        }

        private static bool IsSemanticVersionLower(string current, string minimum)
        {
            if (!TryParseSemanticVersion(current, out var currentParts) ||
                !TryParseSemanticVersion(minimum, out var minimumParts))
                return false;

            for (var i = 0; i < 3; i++)
            {
                if (currentParts[i] < minimumParts[i])
                    return true;

                if (currentParts[i] > minimumParts[i])
                    return false;
            }

            return false;
        }

        private static bool TryParseSemanticVersion(string version, out int[] parts)
        {
            parts = new[] { 0, 0, 0 };
            if (string.IsNullOrEmpty(version))
                return false;

            var core = version.Split(new[] { '-' }, 2)[0];
            var split = core.Split('.');
            for (var i = 0; i < parts.Length && i < split.Length; i++)
            {
                if (!int.TryParse(split[i], out parts[i]))
                    return false;
            }

            return split.Length > 0;
        }

        private void AddLog(string message)
        {
            _log.Add(message);
            Debug.Log("[CoCoFlow Setup] " + message);
            Repaint();
        }

        private sealed class ModuleDefinition
        {
            public ModuleDefinition(
                string displayName,
                string[] requiredSupportDefines,
                string[] requiredAssemblies,
                string description)
            {
                DisplayName = displayName;
                RequiredSupportDefines = requiredSupportDefines;
                RequiredAssemblies = requiredAssemblies;
                Description = description;
            }

            public string DisplayName { get; }
            public string[] RequiredSupportDefines { get; }
            public string[] RequiredAssemblies { get; }
            public string Description { get; }
        }

        private sealed class DependencyStatus
        {
            public string ManifestError { get; set; }
            public string UniTaskDependency { get; set; }
            public string AddressablesDependency { get; set; }
            public string NewtonsoftDependency { get; set; }
            public bool HasUniTaskOpenUpmScope { get; set; }
            public bool UniTaskInstalled { get; set; }
            public bool UniTaskDefineAutomatic { get; set; }
            public bool UniTaskVersionBlocked { get; set; }
            public bool AddressablesInstalled { get; set; }
            public bool CinemachineInstalled { get; set; }
            public bool DotweenInstalled { get; set; }
            public bool DotweenModulesInstalled { get; set; }
            public string UniTaskMessage { get; private set; }
            public string AddressablesMessage { get; private set; }
            public string NewtonsoftMessage { get; private set; }
            public string DotweenMessage { get; private set; }
            public MessageType UniTaskState { get; private set; }
            public MessageType AddressablesState { get; private set; }
            public MessageType NewtonsoftState { get; private set; }
            public int CheckedTargetCount { get; set; }
            public Dictionary<string, List<string>> MissingDefineTargets { get; set; } = new Dictionary<string, List<string>>();
            public Dictionary<string, bool> AssemblyStates { get; } = new Dictionary<string, bool>();

            public bool AddressablesInstallRecommended =>
                string.IsNullOrEmpty(AddressablesDependency) ||
                AddressablesVersionPolicy.Evaluate(AddressablesDependency) !=
                AddressablesVersionCompatibility.Supported ||
                !AddressablesInstalled;

            public bool DefinePresentOnAllTargets(string define)
            {
                return MissingDefineTargets.TryGetValue(define, out var missing) && missing.Count == 0;
            }

            public bool AssemblyAvailable(string assembly)
            {
                return AssemblyStates.TryGetValue(assembly, out var available) && available;
            }

            public void UpdateMessages()
            {
                if (!string.IsNullOrEmpty(ManifestError))
                {
                    UniTaskMessage = "Manifest error: " + ManifestError;
                    AddressablesMessage = "Manifest error: " + ManifestError;
                    NewtonsoftMessage = "Manifest error: " + ManifestError;
                    DotweenMessage = "Manifest error: " + ManifestError;
                    UniTaskState = MessageType.Error;
                    AddressablesState = MessageType.Error;
                    NewtonsoftState = MessageType.Error;
                    return;
                }

                if (string.IsNullOrEmpty(UniTaskDependency))
                {
                    UniTaskMessage = UniTaskInstalled ? "Assembly detected, but project manifest dependency is missing." : "Missing. Apply will add the recommended Git URL.";
                    UniTaskState = MessageType.Warning;
                }
                else if (UniTaskDependency == RecommendedUniTaskGitUrl)
                {
                    UniTaskMessage = UniTaskInstalled ? "Installed from recommended Git URL." : "Recommended Git URL is configured; package may still be resolving.";
                    UniTaskState = UniTaskInstalled ? MessageType.Info : MessageType.Warning;
                }
                else
                {
                    UniTaskMessage = "Installed from non-recommended source: " + UniTaskDependency;
                    UniTaskState = MessageType.Warning;
                }

                if (UniTaskVersionBlocked)
                {
                    UniTaskMessage = "Installed version is outside " + CoCoUniTaskVersionPolicy.SupportedRange +
                                    "; UniTask-linked assemblies are disabled (no assembly-only fallback).";
                    UniTaskState = MessageType.Error;
                }
                else if (UniTaskDefineAutomatic)
                {
                    UniTaskMessage = "Installed (UPM). Support define is managed automatically by asmdef versionDefines " +
                                    CoCoUniTaskVersionPolicy.SupportedRange + ".";
                    UniTaskState = MessageType.Info;
                }

                if (string.IsNullOrEmpty(AddressablesDependency))
                {
                    AddressablesMessage = AddressablesInstalled
                        ? "Assembly detected without a direct project manifest dependency."
                        : "Not installed. Direct Content remains available; install only when the Addressables backend is needed.";
                    AddressablesState = AddressablesInstalled
                        ? MessageType.Warning
                        : MessageType.Info;
                }
                else
                {
                    AddressablesVersionCompatibility compatibility =
                        AddressablesVersionPolicy.Evaluate(AddressablesDependency);
                    switch (compatibility)
                    {
                        case AddressablesVersionCompatibility.BelowMinimum:
                            AddressablesMessage = "Version " + AddressablesDependency +
                                                  " is below the supported range " +
                                                  AddressablesVersionPolicy.SupportedRange + ".";
                            AddressablesState = MessageType.Warning;
                            break;
                        case AddressablesVersionCompatibility.AtOrAboveMaximum:
                            AddressablesMessage = "Version " + AddressablesDependency +
                                                  " is outside the supported range " +
                                                  AddressablesVersionPolicy.SupportedRange + ".";
                            AddressablesState = MessageType.Warning;
                            break;
                        case AddressablesVersionCompatibility.Supported:
                            AddressablesMessage = AddressablesInstalled
                                ? "Installed at " + AddressablesDependency +
                                  " within supported range " +
                                  AddressablesVersionPolicy.SupportedRange + "."
                                : "Dependency " + AddressablesDependency +
                                  " is configured within supported range " +
                                  AddressablesVersionPolicy.SupportedRange +
                                  "; the package may still be resolving.";
                            AddressablesState = AddressablesInstalled
                                ? MessageType.Info
                                : MessageType.Warning;
                            break;
                        default:
                            AddressablesMessage = "Could not verify dependency '" +
                                                  AddressablesDependency +
                                                  "' against supported range " +
                                                  AddressablesVersionPolicy.SupportedRange + ".";
                            AddressablesState = MessageType.Warning;
                            break;
                    }
                }

                if (string.IsNullOrEmpty(NewtonsoftDependency))
                {
                    NewtonsoftMessage = "Missing. Apply will add " + NewtonsoftMinimumVersion + ".";
                    NewtonsoftState = MessageType.Warning;
                }
                else if (IsSemanticVersionLower(NewtonsoftDependency, NewtonsoftMinimumVersion))
                {
                    NewtonsoftMessage = "Version " + NewtonsoftDependency + " is below " + NewtonsoftMinimumVersion + ".";
                    NewtonsoftState = MessageType.Warning;
                }
                else
                {
                    NewtonsoftMessage = "Version " + NewtonsoftDependency + " satisfies " + NewtonsoftMinimumVersion + ".";
                    NewtonsoftState = MessageType.Info;
                }

                if (DotweenModulesInstalled)
                {
                    DotweenMessage = "Detected with DOTween.Modules.";
                }
                else if (DotweenInstalled)
                {
                    DotweenMessage = "DOTween detected, but DOTween.Modules is missing.";
                }
                else
                {
                    DotweenMessage = "Missing. Install DOTween manually.";
                }
            }
        }

        private sealed class ManifestDocument
        {
            public ManifestDocument(JsonObject root)
            {
                Root = root;
            }

            public JsonObject Root { get; }
            public bool Changed { get; set; }
        }

        private abstract class JsonValue
        {
            public abstract string ToJson(int indent);

            protected static string Indent(int count)
            {
                return new string(' ', count);
            }

            protected static string Quote(string value)
            {
                var builder = new StringBuilder(value.Length + 2);
                builder.Append('"');
                foreach (var c in value)
                {
                    switch (c)
                    {
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            builder.Append(c);
                            break;
                    }
                }
                builder.Append('"');
                return builder.ToString();
            }
        }

        private sealed class JsonObject : JsonValue
        {
            private readonly List<string> _keys = new List<string>();
            private readonly Dictionary<string, JsonValue> _values = new Dictionary<string, JsonValue>();

            public void Set(string key, JsonValue value)
            {
                if (!_values.ContainsKey(key))
                    _keys.Add(key);

                _values[key] = value;
            }

            public bool TryGetString(string key, out string value)
            {
                if (_values.TryGetValue(key, out var jsonValue) && jsonValue is JsonString jsonString)
                {
                    value = jsonString.Value;
                    return true;
                }

                value = null;
                return false;
            }

            public bool TryGetArray(string key, out JsonArray value)
            {
                if (_values.TryGetValue(key, out var jsonValue) && jsonValue is JsonArray jsonArray)
                {
                    value = jsonArray;
                    return true;
                }

                value = null;
                return false;
            }

            public bool TryGetObject(string key, out JsonObject value)
            {
                if (_values.TryGetValue(key, out var jsonValue) && jsonValue is JsonObject jsonObject)
                {
                    value = jsonObject;
                    return true;
                }

                value = null;
                return false;
            }

            public override string ToJson(int indent)
            {
                if (_keys.Count == 0)
                    return "{}";

                var builder = new StringBuilder();
                builder.AppendLine("{");
                for (var i = 0; i < _keys.Count; i++)
                {
                    var key = _keys[i];
                    builder.Append(Indent(indent + 2));
                    builder.Append(Quote(key));
                    builder.Append(": ");
                    builder.Append(_values[key].ToJson(indent + 2));
                    if (i < _keys.Count - 1)
                        builder.Append(',');
                    builder.AppendLine();
                }
                builder.Append(Indent(indent));
                builder.Append('}');
                return builder.ToString();
            }
        }

        private sealed class JsonArray : JsonValue
        {
            public readonly List<JsonValue> Items = new List<JsonValue>();

            public override string ToJson(int indent)
            {
                if (Items.Count == 0)
                    return "[]";

                var builder = new StringBuilder();
                builder.AppendLine("[");
                for (var i = 0; i < Items.Count; i++)
                {
                    builder.Append(Indent(indent + 2));
                    builder.Append(Items[i].ToJson(indent + 2));
                    if (i < Items.Count - 1)
                        builder.Append(',');
                    builder.AppendLine();
                }
                builder.Append(Indent(indent));
                builder.Append(']');
                return builder.ToString();
            }
        }

        private sealed class JsonString : JsonValue
        {
            public JsonString(string value)
            {
                Value = value;
            }

            public string Value { get; }

            public override string ToJson(int indent)
            {
                return Quote(Value);
            }
        }

        private sealed class JsonRaw : JsonValue
        {
            public JsonRaw(string value)
            {
                Value = value;
            }

            private string Value { get; }

            public override string ToJson(int indent)
            {
                return Value;
            }
        }

        private sealed class JsonParser
        {
            private readonly string _text;
            private int _index;

            public JsonParser(string text)
            {
                _text = text;
            }

            public JsonValue Parse()
            {
                SkipWhitespace();
                var value = ParseValue();
                SkipWhitespace();
                if (_index != _text.Length)
                    throw Error("Unexpected trailing characters.");

                return value;
            }

            private JsonValue ParseValue()
            {
                SkipWhitespace();
                if (_index >= _text.Length)
                    throw Error("Unexpected end of JSON.");

                var c = _text[_index];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return new JsonString(ParseString());
                if (c == '-' || char.IsDigit(c)) return new JsonRaw(ParseNumber());
                if (MatchLiteral("true")) return new JsonRaw("true");
                if (MatchLiteral("false")) return new JsonRaw("false");
                if (MatchLiteral("null")) return new JsonRaw("null");

                throw Error("Unexpected JSON token '" + c + "'.");
            }

            private JsonObject ParseObject()
            {
                Expect('{');
                var obj = new JsonObject();
                SkipWhitespace();
                if (TryConsume('}'))
                    return obj;

                while (true)
                {
                    SkipWhitespace();
                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    var value = ParseValue();
                    obj.Set(key, value);
                    SkipWhitespace();

                    if (TryConsume('}'))
                        return obj;

                    Expect(',');
                }
            }

            private JsonArray ParseArray()
            {
                Expect('[');
                var array = new JsonArray();
                SkipWhitespace();
                if (TryConsume(']'))
                    return array;

                while (true)
                {
                    array.Items.Add(ParseValue());
                    SkipWhitespace();

                    if (TryConsume(']'))
                        return array;

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();

                while (_index < _text.Length)
                {
                    var c = _text[_index++];
                    if (c == '"')
                        return builder.ToString();

                    if (c != '\\')
                    {
                        builder.Append(c);
                        continue;
                    }

                    if (_index >= _text.Length)
                        throw Error("Unexpected end of string escape.");

                    var escaped = _text[_index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            builder.Append(ParseUnicodeEscape());
                            break;
                        default:
                            throw Error("Invalid string escape '\\" + escaped + "'.");
                    }
                }

                throw Error("Unterminated string.");
            }

            private char ParseUnicodeEscape()
            {
                if (_index + 4 > _text.Length)
                    throw Error("Incomplete unicode escape.");

                var hex = _text.Substring(_index, 4);
                _index += 4;
                return (char)Convert.ToInt32(hex, 16);
            }

            private string ParseNumber()
            {
                var start = _index;
                if (_text[_index] == '-')
                    _index++;

                while (_index < _text.Length && char.IsDigit(_text[_index]))
                    _index++;

                if (_index < _text.Length && _text[_index] == '.')
                {
                    _index++;
                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                        _index++;
                }

                if (_index < _text.Length && (_text[_index] == 'e' || _text[_index] == 'E'))
                {
                    _index++;
                    if (_index < _text.Length && (_text[_index] == '+' || _text[_index] == '-'))
                        _index++;

                    while (_index < _text.Length && char.IsDigit(_text[_index]))
                        _index++;
                }

                return _text.Substring(start, _index - start);
            }

            private bool MatchLiteral(string literal)
            {
                if (_index + literal.Length > _text.Length)
                    return false;

                if (string.Compare(_text, _index, literal, 0, literal.Length, StringComparison.Ordinal) != 0)
                    return false;

                _index += literal.Length;
                return true;
            }

            private void SkipWhitespace()
            {
                while (_index < _text.Length && char.IsWhiteSpace(_text[_index]))
                    _index++;
            }

            private void Expect(char expected)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != expected)
                    throw Error("Expected '" + expected + "'.");
                _index++;
            }

            private bool TryConsume(char expected)
            {
                SkipWhitespace();
                if (_index >= _text.Length || _text[_index] != expected)
                    return false;

                _index++;
                return true;
            }

            private Exception Error(string message)
            {
                return new InvalidDataException(message + " At character " + _index + ".");
            }
        }
    }

    internal enum AddressablesVersionCompatibility
    {
        Unknown = 0,
        BelowMinimum = 1,
        Supported = 2,
        AtOrAboveMaximum = 3
    }

    internal enum CoCoUniTaskInstallForm
    {
        None = 0,
        UpmRegistered = 1,
        AssemblyOnly = 2
    }

    internal enum CoCoUniTaskVersionCompatibility
    {
        Unknown = 0,
        BelowMinimum = 1,
        Supported = 2,
        AtOrAboveMaximum = 3
    }

    internal static class CoCoUniTaskVersionPolicy
    {
        internal const string MinimumVersion = "2.5.11";
        internal const string MaximumExclusiveVersion = "3.0.0";
        internal const string SupportedRange = "[2.5.11,3.0.0)";

        internal static CoCoUniTaskVersionCompatibility Evaluate(string dependency)
        {
            string version = ExtractVersion(dependency);
            if (version == null)
                return CoCoUniTaskVersionCompatibility.Unknown;

            if (Compare(version, MinimumVersion) < 0)
                return CoCoUniTaskVersionCompatibility.BelowMinimum;

            return Compare(version, MaximumExclusiveVersion) >= 0
                ? CoCoUniTaskVersionCompatibility.AtOrAboveMaximum
                : CoCoUniTaskVersionCompatibility.Supported;
        }

        // 接受 "2.5.11" 或 git URL 尾缀 "...#2.5.11"；其余（file: 路径等）返回 null → Unknown，
        // 交由 Unity versionDefines 机制自行评估 resolved 版本。
        internal static string ExtractVersion(string dependency)
        {
            if (string.IsNullOrEmpty(dependency))
                return null;

            var token = dependency.Trim();
            var hashIndex = token.LastIndexOf('#');
            if (hashIndex >= 0)
                token = token.Substring(hashIndex + 1);

            var parts = token.Split('.');
            if (parts.Length != 3)
                return null;

            foreach (var part in parts)
            {
                if (!int.TryParse(part, out _))
                    return null;
            }

            return token;
        }

        private static int Compare(string left, string right)
        {
            var leftParts = left.Split('.');
            var rightParts = right.Split('.');

            for (var index = 0; index < 3; index++)
            {
                var comparison = int.Parse(leftParts[index]).CompareTo(int.Parse(rightParts[index]));
                if (comparison != 0)
                    return comparison;
            }

            return 0;
        }
    }

    internal static class AddressablesVersionPolicy
    {
        internal const string MinimumVersion = "2.9.1";
        internal const string MaximumExclusiveVersion = "3.0.0";
        internal const string SupportedRange = "[2.9.1,3.0.0)";

        private static readonly SemanticVersion Minimum =
            SemanticVersion.ParseRequired(MinimumVersion);
        private static readonly SemanticVersion MaximumExclusive =
            SemanticVersion.ParseRequired(MaximumExclusiveVersion);

        internal static AddressablesVersionCompatibility Evaluate(string version)
        {
            if (!SemanticVersion.TryParse(version, out SemanticVersion parsed))
            {
                return AddressablesVersionCompatibility.Unknown;
            }

            if (parsed.CompareTo(Minimum) < 0)
            {
                return AddressablesVersionCompatibility.BelowMinimum;
            }

            return parsed.CompareTo(MaximumExclusive) >= 0
                ? AddressablesVersionCompatibility.AtOrAboveMaximum
                : AddressablesVersionCompatibility.Supported;
        }

        private readonly struct SemanticVersion : IComparable<SemanticVersion>
        {
            private SemanticVersion(
                int major,
                int minor,
                int patch,
                string[] prereleaseIdentifiers)
            {
                Major = major;
                Minor = minor;
                Patch = patch;
                PrereleaseIdentifiers = prereleaseIdentifiers;
            }

            private int Major { get; }
            private int Minor { get; }
            private int Patch { get; }
            private string[] PrereleaseIdentifiers { get; }

            public int CompareTo(SemanticVersion other)
            {
                int comparison = Major.CompareTo(other.Major);
                if (comparison != 0) return comparison;

                comparison = Minor.CompareTo(other.Minor);
                if (comparison != 0) return comparison;

                comparison = Patch.CompareTo(other.Patch);
                if (comparison != 0) return comparison;

                bool hasPrerelease = PrereleaseIdentifiers.Length != 0;
                bool otherHasPrerelease = other.PrereleaseIdentifiers.Length != 0;
                if (!hasPrerelease || !otherHasPrerelease)
                {
                    if (hasPrerelease == otherHasPrerelease) return 0;
                    return hasPrerelease ? -1 : 1;
                }

                int count = Math.Min(
                    PrereleaseIdentifiers.Length,
                    other.PrereleaseIdentifiers.Length);
                for (int index = 0; index < count; index++)
                {
                    comparison = CompareIdentifier(
                        PrereleaseIdentifiers[index],
                        other.PrereleaseIdentifiers[index]);
                    if (comparison != 0) return comparison;
                }

                return PrereleaseIdentifiers.Length.CompareTo(
                    other.PrereleaseIdentifiers.Length);
            }

            internal static SemanticVersion ParseRequired(string version)
            {
                if (TryParse(version, out SemanticVersion parsed))
                {
                    return parsed;
                }

                throw new InvalidOperationException(
                    "The frozen Addressables version boundary is invalid: " + version);
            }

            internal static bool TryParse(
                string version,
                out SemanticVersion parsed)
            {
                parsed = default;
                if (string.IsNullOrWhiteSpace(version)) return false;

                string value = version.Trim();
                string buildMetadata = string.Empty;
                int buildIndex = value.IndexOf('+');
                if (buildIndex >= 0)
                {
                    buildMetadata = value.Substring(buildIndex + 1);
                    value = value.Substring(0, buildIndex);
                    if (!HasValidIdentifiers(buildMetadata))
                    {
                        return false;
                    }
                }

                string prerelease = string.Empty;
                int prereleaseIndex = value.IndexOf('-');
                if (prereleaseIndex >= 0)
                {
                    prerelease = value.Substring(prereleaseIndex + 1);
                    value = value.Substring(0, prereleaseIndex);
                    if (string.IsNullOrEmpty(prerelease)) return false;
                }

                string[] core = value.Split('.');
                if (core.Length != 3) return false;

                var parts = new int[3];
                for (int index = 0; index < core.Length; index++)
                {
                    if (!TryParseCorePart(core[index], out parts[index]))
                    {
                        return false;
                    }
                }

                string[] identifiers = string.IsNullOrEmpty(prerelease)
                    ? Array.Empty<string>()
                    : prerelease.Split('.');
                foreach (string identifier in identifiers)
                {
                    if (!IsValidIdentifier(identifier)) return false;
                    if (IsNumeric(identifier) &&
                        identifier.Length > 1 &&
                        identifier[0] == '0')
                    {
                        return false;
                    }
                }

                parsed = new SemanticVersion(
                    parts[0],
                    parts[1],
                    parts[2],
                    identifiers);
                return true;
            }

            private static bool TryParseCorePart(string value, out int part)
            {
                part = 0;
                if (string.IsNullOrEmpty(value)) return false;
                if (value.Length > 1 && value[0] == '0') return false;
                foreach (char character in value)
                {
                    if (character < '0' || character > '9') return false;
                }

                return int.TryParse(value, out part) && part >= 0;
            }

            private static bool IsValidIdentifier(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                foreach (char character in value)
                {
                    bool asciiDigit = character >= '0' && character <= '9';
                    bool asciiUpper = character >= 'A' && character <= 'Z';
                    bool asciiLower = character >= 'a' && character <= 'z';
                    if (!asciiDigit &&
                        !asciiUpper &&
                        !asciiLower &&
                        character != '-')
                    {
                        return false;
                    }
                }

                return true;
            }

            private static bool HasValidIdentifiers(string value)
            {
                if (string.IsNullOrEmpty(value)) return false;
                foreach (string identifier in value.Split('.'))
                {
                    if (!IsValidIdentifier(identifier)) return false;
                }

                return true;
            }

            private static bool IsNumeric(string value)
            {
                foreach (char character in value)
                {
                    if (character < '0' || character > '9') return false;
                }

                return value.Length != 0;
            }

            private static int CompareIdentifier(string left, string right)
            {
                bool leftNumeric = IsNumeric(left);
                bool rightNumeric = IsNumeric(right);
                if (leftNumeric && rightNumeric)
                {
                    int lengthComparison = left.Length.CompareTo(right.Length);
                    return lengthComparison != 0
                        ? lengthComparison
                        : string.CompareOrdinal(left, right);
                }

                if (leftNumeric != rightNumeric)
                {
                    return leftNumeric ? -1 : 1;
                }

                return string.CompareOrdinal(left, right);
            }
        }
    }
}
#endif
