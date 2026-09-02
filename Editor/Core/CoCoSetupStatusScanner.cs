using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace CoCoFlow.Editor.Core
{
    /// <summary>
    /// Setup 状态扫描结果（数据层）。消息以双语对保存（原始数据），视图按语言投影；
    /// D-18 后 define 检查聚焦 active build target，含 UPM 形态遗留手工 define 识别（#20-b）。
    /// </summary>
    internal sealed class CoCoSetupDependencyStatus
    {
        public string ManifestError { get; set; }
        public string UniTaskDependency { get; set; }
        public string AddressablesDependency { get; set; }
        public string NewtonsoftDependency { get; set; }
        public bool HasUniTaskOpenUpmScope { get; set; }
        public bool UniTaskInstalled { get; set; }
        public bool UniTaskDefineAutomatic { get; set; }
        public bool UniTaskVersionBlocked { get; set; }
        public bool LegacyUniTaskManualDefinePresent { get; set; }
        public bool AddressablesInstalled { get; set; }
        public bool CinemachineInstalled { get; set; }
        public bool DotweenInstalled { get; set; }
        public bool DotweenModulesInstalled { get; set; }
        public bool ActiveTargetReadable { get; set; } = true;
        public string ActiveTargetName { get; set; } = string.Empty;

        public CoCoSetupModuleCatalog.BilingualText UniTaskMessage { get; private set; }
        public CoCoSetupModuleCatalog.BilingualText AddressablesMessage { get; private set; }
        public CoCoSetupModuleCatalog.BilingualText NewtonsoftMessage { get; private set; }
        public CoCoSetupModuleCatalog.BilingualText DotweenMessage { get; private set; }

        /// <summary>行状态语义（Error/Warning/InfoSuccess/InfoOptional，视图映射 BadgeKind）。</summary>
        public DependencyRowState UniTaskState { get; private set; }
        public DependencyRowState AddressablesState { get; private set; }
        public DependencyRowState NewtonsoftState { get; private set; }
        public DependencyRowState DotweenState { get; private set; }

        /// <summary>define → active target 缺失名单（空列表 = 已满足；无键 = 该 define 不在手动检查集）。</summary>
        public Dictionary<string, List<string>> MissingDefineTargets { get; set; } =
            new Dictionary<string, List<string>>();

        public Dictionary<string, bool> AssemblyStates { get; } = new Dictionary<string, bool>();

        public bool AddressablesInstallRecommended =>
            string.IsNullOrEmpty(AddressablesDependency) ||
            AddressablesVersionPolicy.Evaluate(AddressablesDependency) !=
            AddressablesVersionCompatibility.Supported ||
            !AddressablesInstalled;

        public bool DefinePresentOnActiveTarget(string define)
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
                var manifestErrorEn = "Manifest error: " + ManifestError;
                var manifestErrorZh = "manifest 错误：" + ManifestError;
                UniTaskMessage = new CoCoSetupModuleCatalog.BilingualText(
                    manifestErrorEn, manifestErrorZh);
                AddressablesMessage = UniTaskMessage;
                NewtonsoftMessage = UniTaskMessage;
                DotweenMessage = UniTaskMessage;
                UniTaskState = DependencyRowState.Error;
                AddressablesState = DependencyRowState.Error;
                NewtonsoftState = DependencyRowState.Error;
                DotweenState = DependencyRowState.Error;
                return;
            }

            UpdateUniTaskMessage();
            UpdateAddressablesMessage();
            UpdateNewtonsoftMessage();
            UpdateDotweenMessage();
        }

        private void UpdateUniTaskMessage()
        {
            if (string.IsNullOrEmpty(UniTaskDependency))
            {
                UniTaskMessage = UniTaskInstalled
                    ? new CoCoSetupModuleCatalog.BilingualText(
                        "Assembly detected, but project manifest dependency is missing.",
                        "检测到程序集，但项目 manifest 缺少该依赖。")
                    : new CoCoSetupModuleCatalog.BilingualText(
                        "Missing. Apply will add the recommended Git URL.",
                        "缺失。Apply 将添加推荐 Git URL。");
                UniTaskState = DependencyRowState.Warning;
            }
            else if (UniTaskDependency == CoCoFlowUtility.RecommendedUniTaskGitUrl)
            {
                UniTaskMessage = UniTaskInstalled
                    ? new CoCoSetupModuleCatalog.BilingualText(
                        "Installed from recommended Git URL.",
                        "已按推荐 Git URL 安装。")
                    : new CoCoSetupModuleCatalog.BilingualText(
                        "Recommended Git URL is configured; package may still be resolving.",
                        "推荐 Git URL 已配置；包可能仍在解析。");
                UniTaskState = UniTaskInstalled
                    ? DependencyRowState.InfoSuccess
                    : DependencyRowState.Warning;
            }
            else
            {
                UniTaskMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Installed from non-recommended source: " + UniTaskDependency,
                    "安装自非推荐来源：" + UniTaskDependency);
                UniTaskState = DependencyRowState.Warning;
            }

            if (UniTaskVersionBlocked)
            {
                UniTaskMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Installed version is outside " + CoCoUniTaskVersionPolicy.SupportedRange +
                    "; UniTask-linked assemblies are disabled (no assembly-only fallback).",
                    "已安装版本超出 " + CoCoUniTaskVersionPolicy.SupportedRange +
                    "；UniTask 关联程序集处于禁用状态（无 assembly-only 回退）。");
                UniTaskState = DependencyRowState.Error;
            }
            else if (UniTaskDefineAutomatic)
            {
                UniTaskMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Installed (UPM). Support define is managed automatically by asmdef versionDefines " +
                    CoCoUniTaskVersionPolicy.SupportedRange + ".",
                    "已安装（UPM）。support define 由 asmdef versionDefines " +
                    CoCoUniTaskVersionPolicy.SupportedRange + " 自动管理。");
                UniTaskState = DependencyRowState.InfoSuccess;
            }

            // D-02 保持（#20-b）：UPM 形态下 active target 存在遗留手工 define 时，
            // 不得显示健康/自治——手工符号可旁路 versionDefines 版本边界。
            if (UniTaskDefineAutomatic && LegacyUniTaskManualDefinePresent)
            {
                UniTaskMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Legacy manual " + CoCoFlowUtility.UniTaskDefine +
                    " detected on " + ActiveTargetName +
                    "; versionDefines is the single authority. Apply will remove it (current target only).",
                    "在 " + ActiveTargetName + " 检测到遗留手工宏 " + CoCoFlowUtility.UniTaskDefine +
                    "；versionDefines 为唯一权威。Apply 将清理（仅当前目标）。");
                UniTaskState = DependencyRowState.Warning;
            }
        }

        private void UpdateAddressablesMessage()
        {
            if (string.IsNullOrEmpty(AddressablesDependency))
            {
                AddressablesMessage = AddressablesInstalled
                    ? new CoCoSetupModuleCatalog.BilingualText(
                        "Assembly detected without a direct project manifest dependency.",
                        "检测到程序集，但项目 manifest 无直接依赖。")
                    : new CoCoSetupModuleCatalog.BilingualText(
                        "Not installed. Direct Content remains available; install only when the Addressables backend is needed.",
                        "未安装。直连 Content 仍可用；仅在需要 Addressables 后端时安装。");
                AddressablesState = AddressablesInstalled
                    ? DependencyRowState.Warning
                    : DependencyRowState.InfoOptional;
            }
            else
            {
                AddressablesVersionCompatibility compatibility =
                    AddressablesVersionPolicy.Evaluate(AddressablesDependency);
                switch (compatibility)
                {
                    case AddressablesVersionCompatibility.BelowMinimum:
                        AddressablesMessage = new CoCoSetupModuleCatalog.BilingualText(
                            "Version " + AddressablesDependency +
                            " is below the supported range " +
                            AddressablesVersionPolicy.SupportedRange + ".",
                            "版本 " + AddressablesDependency + " 低于支持范围 " +
                            AddressablesVersionPolicy.SupportedRange + "。");
                        AddressablesState = DependencyRowState.Warning;
                        break;
                    case AddressablesVersionCompatibility.AtOrAboveMaximum:
                        AddressablesMessage = new CoCoSetupModuleCatalog.BilingualText(
                            "Version " + AddressablesDependency +
                            " is outside the supported range " +
                            AddressablesVersionPolicy.SupportedRange + ".",
                            "版本 " + AddressablesDependency + " 超出支持范围 " +
                            AddressablesVersionPolicy.SupportedRange + "。");
                        AddressablesState = DependencyRowState.Warning;
                        break;
                    case AddressablesVersionCompatibility.Supported:
                        AddressablesMessage = AddressablesInstalled
                            ? new CoCoSetupModuleCatalog.BilingualText(
                                "Installed at " + AddressablesDependency +
                                " within supported range " +
                                AddressablesVersionPolicy.SupportedRange + ".",
                                "已安装 " + AddressablesDependency + "，位于支持范围 " +
                                AddressablesVersionPolicy.SupportedRange + " 内。")
                            : new CoCoSetupModuleCatalog.BilingualText(
                                "Dependency " + AddressablesDependency +
                                " is configured within supported range " +
                                AddressablesVersionPolicy.SupportedRange +
                                "; the package may still be resolving.",
                                "依赖 " + AddressablesDependency + " 已配置于支持范围 " +
                                AddressablesVersionPolicy.SupportedRange +
                                " 内；包可能仍在解析。");
                        AddressablesState = AddressablesInstalled
                            ? DependencyRowState.InfoSuccess
                            : DependencyRowState.Warning;
                        break;
                    default:
                        AddressablesMessage = new CoCoSetupModuleCatalog.BilingualText(
                            "Could not verify dependency '" + AddressablesDependency +
                            "' against supported range " +
                            AddressablesVersionPolicy.SupportedRange + ".",
                            "无法对照支持范围 " + AddressablesVersionPolicy.SupportedRange +
                            " 验证依赖 '" + AddressablesDependency + "'。");
                        AddressablesState = DependencyRowState.Warning;
                        break;
                }
            }
        }

        private void UpdateNewtonsoftMessage()
        {
            if (string.IsNullOrEmpty(NewtonsoftDependency))
            {
                NewtonsoftMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Missing. Apply will add " + CoCoFlowUtility.NewtonsoftMinimumVersion + ".",
                    "缺失。Apply 将添加 " + CoCoFlowUtility.NewtonsoftMinimumVersion + "。");
                NewtonsoftState = DependencyRowState.Warning;
            }
            else if (CoCoSetupDependencyActions.IsSemanticVersionLower(
                         NewtonsoftDependency,
                         CoCoFlowUtility.NewtonsoftMinimumVersion))
            {
                NewtonsoftMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Version " + NewtonsoftDependency + " is below " +
                    CoCoFlowUtility.NewtonsoftMinimumVersion + ".",
                    "版本 " + NewtonsoftDependency + " 低于 " +
                    CoCoFlowUtility.NewtonsoftMinimumVersion + "。");
                NewtonsoftState = DependencyRowState.Warning;
            }
            else
            {
                NewtonsoftMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Version " + NewtonsoftDependency + " satisfies " +
                    CoCoFlowUtility.NewtonsoftMinimumVersion + ".",
                    "版本 " + NewtonsoftDependency + " 满足 " +
                    CoCoFlowUtility.NewtonsoftMinimumVersion + "。");
                NewtonsoftState = DependencyRowState.InfoSuccess;
            }
        }

        private void UpdateDotweenMessage()
        {
            if (!string.IsNullOrEmpty(ManifestError))
            {
                // 声明非等价项 #19（错误反馈优化）：DOTween 行遇 manifest error
                // 规范化为 Error（旧实现文案为 Manifest error 但行状态仍 Info/Warning）。
                DotweenMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Manifest error: " + ManifestError,
                    "manifest 错误：" + ManifestError);
                DotweenState = DependencyRowState.Error;
                return;
            }

            if (DotweenModulesInstalled)
            {
                DotweenMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Detected with DOTween.Modules.",
                    "检测到 DOTween.Modules。");
                DotweenState = DependencyRowState.InfoSuccess;
            }
            else if (DotweenInstalled)
            {
                DotweenMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "DOTween detected, but DOTween.Modules is missing.",
                    "检测到 DOTween，但缺少 DOTween.Modules。");
                DotweenState = DependencyRowState.Warning;
            }
            else
            {
                DotweenMessage = new CoCoSetupModuleCatalog.BilingualText(
                    "Missing. Install DOTween manually.",
                    "缺失。请手动安装 DOTween。");
                DotweenState = DependencyRowState.Warning;
            }
        }
    }

    /// <summary>依赖行状态语义（视图按 §2.2 映射表转 BadgeKind）。</summary>
    internal enum DependencyRowState
    {
        Error,
        Warning,
        InfoSuccess,
        InfoOptional
    }

    /// <summary>
    /// Setup 状态扫描器：manifest 解析、程序集/类型探测、active target define 检查
    /// （D-18：仅 EditorUserBuildSettings 当前目标；UPM 形态识别遗留手工 UniTask define）。
    /// </summary>
    internal static class CoCoSetupStatusScanner
    {
        public static CoCoSetupDependencyStatus BuildStatus()
        {
            var status = new CoCoSetupDependencyStatus();

            try
            {
                var manifest = CoCoSetupDependencyActions.LoadManifest();
                var root = manifest.Root;
                if (root.TryGetObject("dependencies", out var dependencies))
                {
                    if (dependencies.TryGetString(CoCoFlowUtility.UniTaskPackageName, out var unitaskDependency))
                        status.UniTaskDependency = unitaskDependency;

                    if (dependencies.TryGetString(CoCoFlowUtility.AddressablesPackageName, out var addressablesDependency))
                        status.AddressablesDependency = addressablesDependency;

                    if (dependencies.TryGetString(CoCoFlowUtility.NewtonsoftPackageName, out var newtonsoftDependency))
                        status.NewtonsoftDependency = newtonsoftDependency;
                }

                status.HasUniTaskOpenUpmScope = HasUniTaskOpenUpmScope(root);
            }
            catch (Exception ex)
            {
                status.ManifestError = ex.Message;
            }

            status.UniTaskInstalled = IsAssemblyInstalled("UniTask") ||
                                      IsTypeAvailable("Cysharp.Threading.Tasks.UniTask, UniTask");
            var unitaskForm = CoCoFlowUtility.ClassifyUniTaskForm(
                !string.IsNullOrEmpty(status.UniTaskDependency),
                status.UniTaskInstalled);
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
            status.CinemachineInstalled = IsAssemblyInstalled(CoCoFlowUtility.CinemachineAssemblyName) ||
                                          IsTypeAvailable(
                                              "Unity.Cinemachine.CinemachineCamera, Unity.Cinemachine");
            status.DotweenInstalled = IsDotweenInstalled();
            status.DotweenModulesInstalled = IsAssemblyInstalled("DOTween.Modules");

            // D-18：active target 唯一检查目标。
            var activeSymbols = ReadActiveTargetDefineSymbols(status, out var activeTargetReadable);
            status.ActiveTargetReadable = activeTargetReadable;
            status.MissingDefineTargets = GetMissingDefineTargets(
                unitaskForm,
                activeSymbols,
                status.ActiveTargetName);

            if (unitaskForm == CoCoUniTaskInstallForm.UpmRegistered && status.UniTaskDefineAutomatic && status.UniTaskInstalled)
            {
                // versionDefines is the authority here; record the define as
                // satisfied so module status does not show it as partial.
                // UniTaskInstalled is required: an unresolved dependency cannot
                // emit versionDefines yet (fresh open / failed git fetch).
                status.MissingDefineTargets[CoCoFlowUtility.UniTaskDefine] = new List<string>();
            }
            else if (status.UniTaskVersionBlocked)
            {
                // Blocked UPM version: linked assemblies are disabled; the define
                // must not be reported as enabled on the active target.
                status.MissingDefineTargets[CoCoFlowUtility.UniTaskDefine] =
                    new List<string> { status.ActiveTargetName };
            }

            // D-02 保持（#20-b）：UPM 形态下遗留手工 UniTask define 识别（仅 active target）。
            status.LegacyUniTaskManualDefinePresent =
                unitaskForm == CoCoUniTaskInstallForm.UpmRegistered &&
                activeSymbols != null &&
                activeSymbols.Contains(CoCoFlowUtility.UniTaskDefine);

            FillAssemblyStates(status);
            status.UpdateMessages();
            return status;
        }

        private static List<string> ReadActiveTargetDefineSymbols(
            CoCoSetupDependencyStatus status,
            out bool readable)
        {
            try
            {
                var namedTarget = GetActiveNamedBuildTarget();
                status.ActiveTargetName = namedTarget.TargetName;
                var current = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                readable = true;
                return CoCoSetupDependencyActions.SplitDefines(current);
            }
            catch
            {
                readable = false;
                status.ActiveTargetName = string.Empty;
                return null;
            }
        }

        /// <summary>active build target（D-18 唯一目标）。</summary>
        internal static NamedBuildTarget GetActiveNamedBuildTarget()
        {
            var group = EditorUserBuildSettings.selectedBuildTargetGroup;
            return NamedBuildTarget.FromBuildTargetGroup(group);
        }

        private static Dictionary<string, List<string>> GetMissingDefineTargets(
            CoCoUniTaskInstallForm unitaskForm,
            List<string> activeSymbols,
            string activeTargetName)
        {
            var result = new Dictionary<string, List<string>>();

            // UPM 形态下 UniTask define 由 versionDefines 自动管理，
            // 不要求手动出现在 ScriptingDefineSymbols（否则误报 missing）。
            var manualDefines = unitaskForm == CoCoUniTaskInstallForm.UpmRegistered
                ? new[] { CoCoFlowUtility.DotweenDefine, CoCoFlowUtility.UniTaskDotweenDefine }
                : new[] { CoCoFlowUtility.UniTaskDefine, CoCoFlowUtility.DotweenDefine, CoCoFlowUtility.UniTaskDotweenDefine };

            foreach (var define in manualDefines)
            {
                result[define] = activeSymbols != null && activeSymbols.Contains(define)
                    ? new List<string>()
                    : new List<string> { activeTargetName };
            }

            return result;
        }

        private static void FillAssemblyStates(CoCoSetupDependencyStatus status)
        {
            status.AssemblyStates["UniTask"] = status.UniTaskInstalled;
            status.AssemblyStates["UniTask.DOTween"] = IsAssemblyInstalled("UniTask.DOTween");
            status.AssemblyStates[CoCoFlowUtility.CinemachineAssemblyName] = status.CinemachineInstalled;
            status.AssemblyStates["Unity.Addressables"] = status.AddressablesInstalled;
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
        }

        private static bool HasUniTaskOpenUpmScope(JsonObject root)
        {
            if (!root.TryGetArray("scopedRegistries", out var registries))
                return false;

            foreach (var item in registries.Items)
            {
                if (!(item is JsonObject registry))
                    continue;

                var isOpenUpm = registry.TryGetString("name", out var name) &&
                                name == CoCoFlowUtility.OpenUpmRegistryName;
                isOpenUpm = isOpenUpm || (registry.TryGetString("url", out var url) &&
                                          url == CoCoFlowUtility.OpenUpmRegistryUrl);
                if (!isOpenUpm || !registry.TryGetArray("scopes", out var scopes))
                    continue;

                if (scopes.Items.OfType<JsonString>().Any(scope => scope.Value == CoCoFlowUtility.UniTaskScope))
                    return true;
            }

            return false;
        }

        private static bool IsDotweenInstalled()
        {
            return IsAssemblyInstalled("DOTween") ||
                   IsTypeAvailable("DG.Tweening.Tween, DOTween");
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
    }
}
