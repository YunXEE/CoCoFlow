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
        private const string PackageName = "com.yunxee.cocoflow";
        private const string ManifestPath = "Packages/manifest.json";
        private const string UniTaskPackageName = "com.cysharp.unitask";
        private const string RecommendedUniTaskGitUrl = "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11";
        private const string NewtonsoftPackageName = "com.unity.nuget.newtonsoft-json";
        private const string NewtonsoftMinimumVersion = "3.2.2";
        private const string CinemachineAssemblyName = "Unity.Cinemachine";
        private const string SplinesAssemblyName = "Unity.Splines";
        private const string OpenUpmRegistryName = "package.openupm.com";
        private const string OpenUpmRegistryUrl = "https://package.openupm.com";
        private const string UniTaskScope = "com.cysharp.unitask";
        private const string UniTaskDefine = "COCOFLOW_UNITASK_SUPPORT";
        private const string DotweenDefine = "COCOFLOW_DOTWEEN_SUPPORT";
        private const string FusionDefine = "COCOFLOW_FUSION_SUPPORT";
        private const string UniTaskDotweenDefine = "UNITASK_DOTWEEN_SUPPORT";

        private static readonly AddonDefinition[] Addons =
        {
            new AddonDefinition(
                "network",
                "Network Add-on",
                "Samples~/Network/CoCoFlow/Network",
                "Samples~/Network/README.md",
                "Assets/CoCoFlow/Network",
                new[] { UniTaskDefine, DotweenDefine, FusionDefine },
                new[] { "UniTask", "DOTween", "DOTween.Modules", "Fusion.Unity" })
        };

        private readonly List<string> _log = new List<string>();
        private DependencyStatus _status;
        private Vector2 _scrollPosition;
        private AddonInstallMode _networkInstallMode = AddonInstallMode.MergeMissing;
        private AddRequest _uniTaskRequest;
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
        }

        private void OnGUI()
        {
            if (_status == null)
                RefreshStatus();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawHeader();
            DrawDependencies();
            DrawDefines();
            DrawActions();
            DrawAddons();
            DrawLog();
            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("CoCoFlow Setup Assistant", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Configure project dependencies, enable CoCoFlow support defines, and install optional add-ons.",
                MessageType.Info);
        }

        private void DrawDependencies()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Dependencies", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                DrawStatusLine("UniTask", _status.UniTaskMessage, _status.UniTaskState);
                DrawStatusLine("Newtonsoft", _status.NewtonsoftMessage, _status.NewtonsoftState);
                DrawStatusLine("Cinemachine", _status.CinemachineInstalled ? "Detected from package dependency." : "Missing. It should resolve from CoCoFlow package dependencies.", _status.CinemachineInstalled ? MessageType.Info : MessageType.Warning);
                DrawStatusLine("Splines", _status.SplinesInstalled ? "Detected from package dependency." : "Missing. It should resolve from CoCoFlow package dependencies.", _status.SplinesInstalled ? MessageType.Info : MessageType.Warning);
                DrawStatusLine("DOTween", _status.DotweenInstalled ? "Detected." : "Missing. Install DOTween manually.", _status.DotweenInstalled ? MessageType.Info : MessageType.Warning);
                DrawStatusLine("Photon Fusion", _status.FusionInstalled ? "Detected." : "Missing. Install Photon Fusion manually.", _status.FusionInstalled ? MessageType.Info : MessageType.Warning);

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
                DrawDefineLine(FusionDefine);
                DrawDefineLine(UniTaskDotweenDefine);
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
        }

        private void DrawAddons()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Add-ons", EditorStyles.boldLabel);

            foreach (var addon in Addons)
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(addon.DisplayName, EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("Destination", addon.DestinationAssetPath);
                    EditorGUILayout.LabelField("Required Defines", string.Join(", ", addon.RequiredSupportDefines));

                    var missingDefines = addon.RequiredSupportDefines.Where(define => !_status.DefinePresentOnAllTargets(define)).ToArray();
                    var missingAssemblies = addon.RequiredAssemblies.Where(assembly => !_status.AssemblyAvailable(assembly)).ToArray();
                    if (missingDefines.Length == 0 && missingAssemblies.Length == 0)
                    {
                        EditorGUILayout.HelpBox("Dependencies are ready.", MessageType.Info);
                    }
                    else
                    {
                        if (missingAssemblies.Length > 0)
                            EditorGUILayout.LabelField("Missing Assemblies", string.Join(", ", missingAssemblies), EditorStyles.wordWrappedLabel);
                        if (missingDefines.Length > 0)
                            EditorGUILayout.LabelField("Missing Defines", string.Join(", ", missingDefines), EditorStyles.wordWrappedLabel);
                        EditorGUILayout.HelpBox("Can be installed now, but compilation stays disabled until missing dependencies/defines are ready.", MessageType.Warning);
                    }

                    _networkInstallMode = (AddonInstallMode)EditorGUILayout.EnumPopup("Install Mode", _networkInstallMode);
                    using (new EditorGUI.DisabledScope(_isBusy || _networkInstallMode == AddonInstallMode.Skip))
                    {
                        if (GUILayout.Button("Install Selected Add-on", GUILayout.Height(28f)))
                            InstallAddon(addon, _networkInstallMode);
                    }
                }
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
                DrawStatusLine(define, "Missing on: " + string.Join(", ", missing.ToArray()), MessageType.Warning);
            else
                DrawStatusLine(define, "Present on all checked build targets.", MessageType.Info);
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
                File.WriteAllText(ManifestPath, manifest.Root.ToJson(0) + Environment.NewLine, new UTF8Encoding(false));
                AddLog("Updated Packages/manifest.json.");
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
            var defines = new List<string>();
            if (uniTaskInstallSucceeded || IsAssemblyInstalled("UniTask") || IsTypeAvailable("Cysharp.Threading.Tasks.UniTask, UniTask"))
                defines.Add(UniTaskDefine);

            if (IsDotweenInstalled())
            {
                defines.Add(DotweenDefine);
                defines.Add(UniTaskDotweenDefine);
            }

            if (IsFusionInstalled())
                defines.Add(FusionDefine);

            if (defines.Count == 0)
            {
                AddLog("No support defines were added because dependencies are not available yet.");
                return;
            }

            AddDefinesToAllValidTargets(defines.ToArray());
        }

        private void AddDefinesToAllValidTargets(params string[] definesToAdd)
        {
            var changedTargets = new List<string>();
            var skippedTargets = new List<string>();

            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                    continue;

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
                AddLog("Added support defines to: " + string.Join(", ", changedTargets.ToArray()));
            else
                AddLog("Support defines were already configured for checked build targets.");

            if (skippedTargets.Count > 0)
                AddLog("Skipped unsupported build targets: " + string.Join(", ", skippedTargets.ToArray()));
        }

        private void InstallAddon(AddonDefinition addon, AddonInstallMode mode)
        {
            try
            {
                if (mode == AddonInstallMode.Skip)
                {
                    AddLog(addon.DisplayName + " install skipped.");
                    return;
                }

                var source = FindInstallSource(addon);
                if (source == null)
                {
                    AddLog("ERROR: Could not find source for " + addon.DisplayName + ".");
                    return;
                }

                var destinationRoot = GetProjectAbsolutePath(addon.DestinationAssetPath);
                if (Directory.Exists(destinationRoot) && mode == AddonInstallMode.ReplaceExisting)
                {
                    FileUtil.DeleteFileOrDirectory(addon.DestinationAssetPath);
                    FileUtil.DeleteFileOrDirectory(addon.DestinationAssetPath + ".meta");
                    AddLog("Removed existing " + addon.DestinationAssetPath + ".");
                }

                CopyDirectory(source.NetworkRoot, destinationRoot, mode == AddonInstallMode.ReplaceExisting, addon.DisplayName);
                CopyReadme(source.ReadmePath, Path.Combine(destinationRoot, "README.md"), mode == AddonInstallMode.ReplaceExisting);

                AssetDatabase.Refresh();
                var installedFolder = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(addon.DestinationAssetPath);
                if (installedFolder != null)
                {
                    Selection.activeObject = installedFolder;
                    EditorGUIUtility.PingObject(installedFolder);
                }

                AddLog(addon.DisplayName + " installed from " + source.Description + ".");
                RefreshStatus();
            }
            catch (Exception ex)
            {
                AddLog("ERROR: " + addon.DisplayName + " install failed. " + ex.Message);
                Debug.LogError("[CoCoFlow Setup] Add-on install failed:\n" + ex);
            }
        }

        private InstallSource FindInstallSource(AddonDefinition addon)
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(CoCoFlowSetupAssistant).Assembly);
            if (packageInfo != null && packageInfo.name == PackageName && !string.IsNullOrEmpty(packageInfo.resolvedPath))
            {
                var networkRoot = Path.Combine(packageInfo.resolvedPath, addon.SourceRelativePath);
                if (Directory.Exists(networkRoot))
                {
                    return new InstallSource(
                        networkRoot,
                        Path.Combine(packageInfo.resolvedPath, addon.ReadmeRelativePath),
                        "package Samples~");
                }
            }

            var samplesRoot = Path.Combine(Application.dataPath, "Samples", "CoCoFlow");
            if (!Directory.Exists(samplesRoot))
                return null;

            foreach (var networkRoot in Directory.GetDirectories(samplesRoot, "Network", SearchOption.AllDirectories))
            {
                if (!NormalizePath(networkRoot).EndsWith("/CoCoFlow/Network", StringComparison.Ordinal))
                    continue;

                var addOnRoot = Directory.GetParent(Directory.GetParent(networkRoot).FullName).FullName;
                return new InstallSource(networkRoot, Path.Combine(addOnRoot, "README.md"), "imported Assets/Samples copy");
            }

            return null;
        }

        private void CopyDirectory(string sourceRoot, string destinationRoot, bool overwrite, string label)
        {
            Directory.CreateDirectory(destinationRoot);

            var copied = 0;
            var skipped = 0;

            foreach (var sourceFile in Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories))
            {
                var relativePath = GetRelativePath(sourceRoot, sourceFile);
                var destinationFile = Path.Combine(destinationRoot, relativePath);
                var destinationDirectory = Path.GetDirectoryName(destinationFile);

                if (!Directory.Exists(destinationDirectory))
                    Directory.CreateDirectory(destinationDirectory);

                if (File.Exists(destinationFile) && !overwrite)
                {
                    skipped++;
                    continue;
                }

                File.Copy(sourceFile, destinationFile, overwrite);
                copied++;
            }

            AddLog(label + ": copied " + copied + " file(s).");
            if (skipped > 0)
                AddLog(label + ": skipped " + skipped + " existing file(s).");
        }

        private void CopyReadme(string sourceReadmePath, string destinationReadmePath, bool overwrite)
        {
            if (string.IsNullOrEmpty(sourceReadmePath) || !File.Exists(sourceReadmePath))
            {
                AddLog("README.md was not found in the add-on source.");
                return;
            }

            if (File.Exists(destinationReadmePath) && !overwrite)
            {
                AddLog("Skipped existing README.md.");
                return;
            }

            File.Copy(sourceReadmePath, destinationReadmePath, overwrite);
            AddLog("Copied README.md.");
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
            status.CinemachineInstalled = IsAssemblyInstalled(CinemachineAssemblyName) || IsTypeAvailable("Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
            status.SplinesInstalled = IsAssemblyInstalled(SplinesAssemblyName) || IsTypeAvailable("UnityEngine.Splines.SplineContainer, Unity.Splines");
            status.DotweenInstalled = IsDotweenInstalled();
            status.FusionInstalled = IsFusionInstalled();
            status.MissingDefineTargets = GetMissingDefineTargets(new[] { UniTaskDefine, DotweenDefine, FusionDefine, UniTaskDotweenDefine });

            status.AssemblyStates["UniTask"] = status.UniTaskInstalled;
            status.AssemblyStates[CinemachineAssemblyName] = status.CinemachineInstalled;
            status.AssemblyStates[SplinesAssemblyName] = status.SplinesInstalled;
            status.AssemblyStates["DOTween"] = status.DotweenInstalled;
            status.AssemblyStates["DOTween.Modules"] = IsAssemblyInstalled("DOTween.Modules");
            status.AssemblyStates["Fusion.Unity"] = status.FusionInstalled;

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
                   IsAssemblyInstalled("DOTween.Modules") ||
                   IsTypeAvailable("DG.Tweening.Tween, DOTween");
        }

        private static bool IsFusionInstalled()
        {
            return IsAssemblyInstalled("Fusion.Unity") ||
                   IsTypeAvailable("Fusion.NetworkRunner, Fusion.Unity");
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

        private static Dictionary<string, List<string>> GetMissingDefineTargets(string[] requiredDefines)
        {
            var result = new Dictionary<string, List<string>>();
            foreach (var define in requiredDefines)
                result[define] = new List<string>();

            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown)
                    continue;

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

        private static JsonObject GetOrCreateObject(JsonObject parent, string key, ManifestDocument manifest)
        {
            if (parent.TryGetObject(key, out var obj))
                return obj;

            obj = new JsonObject();
            parent.Set(key, obj);
            manifest.Changed = true;
            return obj;
        }

        private static string GetProjectAbsolutePath(string assetPath)
        {
            var projectRoot = Directory.GetParent(Application.dataPath).FullName;
            return Path.Combine(projectRoot, assetPath);
        }

        private static string GetRelativePath(string root, string file)
        {
            var normalizedRoot = NormalizePath(root).TrimEnd('/') + "/";
            var normalizedFile = NormalizePath(file);

            if (!normalizedFile.StartsWith(normalizedRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("File is outside source root: " + file);

            return normalizedFile.Substring(normalizedRoot.Length);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
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

        private enum AddonInstallMode
        {
            MergeMissing,
            ReplaceExisting,
            Skip
        }

        private sealed class AddonDefinition
        {
            public AddonDefinition(
                string id,
                string displayName,
                string sourceRelativePath,
                string readmeRelativePath,
                string destinationAssetPath,
                string[] requiredSupportDefines,
                string[] requiredAssemblies)
            {
                Id = id;
                DisplayName = displayName;
                SourceRelativePath = sourceRelativePath;
                ReadmeRelativePath = readmeRelativePath;
                DestinationAssetPath = destinationAssetPath;
                RequiredSupportDefines = requiredSupportDefines;
                RequiredAssemblies = requiredAssemblies;
            }

            public string Id { get; }
            public string DisplayName { get; }
            public string SourceRelativePath { get; }
            public string ReadmeRelativePath { get; }
            public string DestinationAssetPath { get; }
            public string[] RequiredSupportDefines { get; }
            public string[] RequiredAssemblies { get; }
        }

        private sealed class InstallSource
        {
            public InstallSource(string networkRoot, string readmePath, string description)
            {
                NetworkRoot = networkRoot;
                ReadmePath = readmePath;
                Description = description;
            }

            public string NetworkRoot { get; }
            public string ReadmePath { get; }
            public string Description { get; }
        }

        private sealed class DependencyStatus
        {
            public string ManifestError { get; set; }
            public string UniTaskDependency { get; set; }
            public string NewtonsoftDependency { get; set; }
            public bool HasUniTaskOpenUpmScope { get; set; }
            public bool UniTaskInstalled { get; set; }
            public bool CinemachineInstalled { get; set; }
            public bool SplinesInstalled { get; set; }
            public bool DotweenInstalled { get; set; }
            public bool FusionInstalled { get; set; }
            public string UniTaskMessage { get; private set; }
            public string NewtonsoftMessage { get; private set; }
            public MessageType UniTaskState { get; private set; }
            public MessageType NewtonsoftState { get; private set; }
            public Dictionary<string, List<string>> MissingDefineTargets { get; set; } = new Dictionary<string, List<string>>();
            public Dictionary<string, bool> AssemblyStates { get; } = new Dictionary<string, bool>();

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
                    NewtonsoftMessage = "Manifest error: " + ManifestError;
                    UniTaskState = MessageType.Error;
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
}
#endif
