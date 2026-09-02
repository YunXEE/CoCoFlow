using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace CoCoFlow.Editor.Core
{
    /// <summary>
    /// Setup 依赖动作执行侧（D-02 状态机执行；目标集 = active build target，D-18）。
    /// 纯逻辑静态服务：所有用户可见输出经双语消息收集器返回，由窗口决定日志/渲染。
    /// </summary>
    internal static class CoCoSetupDependencyActions
    {
        /// <summary>双语消息收集（原始数据侧；窗口按语言投影，控制台始终打英文侧）。</summary>
        internal sealed class MessageCollector
        {
            private readonly List<CoCoSetupModuleCatalog.BilingualText> _messages =
                new List<CoCoSetupModuleCatalog.BilingualText>();

            public IReadOnlyList<CoCoSetupModuleCatalog.BilingualText> Messages => _messages;

            public void Add(string english, string simplifiedChinese)
            {
                _messages.Add(new CoCoSetupModuleCatalog.BilingualText(english, simplifiedChinese));
            }
        }

        public static ManifestDocument LoadManifest()
        {
            if (!File.Exists(CoCoFlowUtility.ManifestPath))
                throw new FileNotFoundException(
                    "Could not find " + CoCoFlowUtility.ManifestPath + ".");

            var text = File.ReadAllText(CoCoFlowUtility.ManifestPath);
            var root = new JsonParser(text).Parse();
            if (!(root is JsonObject rootObject))
                throw new InvalidDataException("Project manifest root must be a JSON object.");

            return new ManifestDocument(rootObject);
        }

        public static bool IsValidManifestJson(string text)
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

        internal static JsonObject GetOrCreateObject(
            JsonObject parent,
            string key,
            ManifestDocument manifest)
        {
            if (parent.TryGetObject(key, out var obj))
                return obj;

            obj = new JsonObject();
            parent.Set(key, obj);
            manifest.Changed = true;
            return obj;
        }

        /// <summary>
        /// manifest 推荐项编辑（Newtonsoft 增/升版 + OpenUPM UniTask scope 移除）。
        /// 返回是否写入磁盘（changed）；日志经 collector（invariant #7 前段）。
        /// </summary>
        public static bool ConfigureProjectManifest(MessageCollector log)
        {
            var manifest = LoadManifest();
            var root = manifest.Root;

            var dependencies = GetOrCreateObject(root, "dependencies", manifest);
            if (!dependencies.TryGetString(CoCoFlowUtility.NewtonsoftPackageName, out var existingNewtonsoft))
            {
                dependencies.Set(
                    CoCoFlowUtility.NewtonsoftPackageName,
                    new JsonString(CoCoFlowUtility.NewtonsoftMinimumVersion));
                manifest.Changed = true;
                log.Add(
                    "Added Newtonsoft dependency " + CoCoFlowUtility.NewtonsoftMinimumVersion + ".",
                    "已添加 Newtonsoft 依赖 " + CoCoFlowUtility.NewtonsoftMinimumVersion + "。");
            }
            else if (IsSemanticVersionLower(existingNewtonsoft, CoCoFlowUtility.NewtonsoftMinimumVersion))
            {
                dependencies.Set(
                    CoCoFlowUtility.NewtonsoftPackageName,
                    new JsonString(CoCoFlowUtility.NewtonsoftMinimumVersion));
                manifest.Changed = true;
                log.Add(
                    "Updated Newtonsoft from " + existingNewtonsoft + " to " +
                    CoCoFlowUtility.NewtonsoftMinimumVersion + ".",
                    "已将 Newtonsoft 从 " + existingNewtonsoft + " 升级到 " +
                    CoCoFlowUtility.NewtonsoftMinimumVersion + "。");
            }
            else
            {
                log.Add(
                    "Newtonsoft dependency already satisfies " + CoCoFlowUtility.NewtonsoftMinimumVersion +
                    " (" + existingNewtonsoft + ").",
                    "Newtonsoft 依赖已满足 " + CoCoFlowUtility.NewtonsoftMinimumVersion +
                    "（" + existingNewtonsoft + "）。");
            }

            RemoveUniTaskOpenUpmScope(root, manifest, log);

            if (manifest.Changed)
            {
                var nextManifest = manifest.Root.ToJson(0) + Environment.NewLine;
                if (!CoCoAtomicFileTransaction.TryReplaceUtf8(
                        CoCoFlowUtility.ManifestPath,
                        nextManifest,
                        IsValidManifestJson,
                        out var backupPath,
                        out var error))
                {
                    throw new IOException("Atomic manifest replacement failed: " + error);
                }

                log.Add(
                    "Updated Packages/manifest.json atomically. Backup: " + backupPath + ".",
                    "已原子更新 Packages/manifest.json。备份：" + backupPath + "。");
            }
            else
            {
                log.Add(
                    "Packages/manifest.json already has recommended non-UniTask entries.",
                    "Packages/manifest.json 已含推荐的非 UniTask 条目。");
            }

            return manifest.Changed;
        }

        public static void RemoveUniTaskOpenUpmScope(
            JsonObject root,
            ManifestDocument manifest,
            MessageCollector log)
        {
            if (!root.TryGetArray("scopedRegistries", out var registries))
                return;

            for (var registryIndex = registries.Items.Count - 1; registryIndex >= 0; registryIndex--)
            {
                if (!(registries.Items[registryIndex] is JsonObject registry))
                    continue;

                var isOpenUpm = registry.TryGetString("name", out var name) &&
                                name == CoCoFlowUtility.OpenUpmRegistryName;
                isOpenUpm = isOpenUpm || (registry.TryGetString("url", out var url) &&
                                          url == CoCoFlowUtility.OpenUpmRegistryUrl);
                if (!isOpenUpm || !registry.TryGetArray("scopes", out var scopes))
                    continue;

                for (var scopeIndex = scopes.Items.Count - 1; scopeIndex >= 0; scopeIndex--)
                {
                    if (scopes.Items[scopeIndex] is JsonString scope &&
                        scope.Value == CoCoFlowUtility.UniTaskScope)
                    {
                        scopes.Items.RemoveAt(scopeIndex);
                        manifest.Changed = true;
                        log.Add(
                            "Removed UniTask scope from OpenUPM registry.",
                            "已从 OpenUPM registry 移除 UniTask scope。");
                    }
                }

                if (scopes.Items.Count == 0)
                {
                    registries.Items.RemoveAt(registryIndex);
                    manifest.Changed = true;
                    log.Add(
                        "Removed empty OpenUPM registry entry.",
                        "已移除空的 OpenUPM registry 条目。");
                }
            }
        }

        /// <summary>
        /// D-02 define 状态机执行（目标集 = active target，D-18）：
        /// UPM 形态 ⇒ 清理遗留手工 UniTask define + versionDefines 唯一权威；
        /// blocked 版本 ⇒ 无 fallback 显式 ERROR；assembly-only ⇒ 手动 define 允许。
        /// </summary>
        public static void ApplyAvailableSupportDefines(
            bool uniTaskInstallSucceeded,
            MessageCollector log)
        {
            string unitaskDependency = ReadManifestUniTaskDependency();
            bool assemblyAvailable = uniTaskInstallSucceeded ||
                                     IsAssemblyInstalled("UniTask") ||
                                     IsTypeAvailable("Cysharp.Threading.Tasks.UniTask, UniTask");
            var form = CoCoFlowUtility.ClassifyUniTaskForm(
                !string.IsNullOrEmpty(unitaskDependency),
                assemblyAvailable);
            var compatibility = CoCoUniTaskVersionPolicy.Evaluate(unitaskDependency);
            bool uniTaskUsable;

            if (form == CoCoUniTaskInstallForm.UpmRegistered)
            {
                // UPM 形态：无论版本是否兼容，都必须移除遗留全局 define，
                // 防止其旁路 versionDefines 的版本边界；失败 = 显式 partial/error。
                RemoveDefinesFromActiveTarget(CoCoFlowUtility.UniTaskDefine, log);

                if (compatibility == CoCoUniTaskVersionCompatibility.BelowMinimum ||
                    compatibility == CoCoUniTaskVersionCompatibility.AtOrAboveMaximum)
                {
                    log.Add(
                        "ERROR: UniTask UPM version is outside " + CoCoFlowUtility.UniTaskSupportedRange +
                        " (" + unitaskDependency + "). UniTask-linked assemblies stay disabled;" +
                        " assembly-only fallback is not allowed.",
                        "错误：UniTask UPM 版本超出 " + CoCoFlowUtility.UniTaskSupportedRange +
                        "（" + unitaskDependency + "）。UniTask 关联程序集保持禁用；" +
                        "不允许 assembly-only 回退。");
                    uniTaskUsable = false;
                }
                else
                {
                    log.Add(
                        "UniTask support define is managed automatically by asmdef versionDefines " +
                        CoCoFlowUtility.UniTaskSupportedRange + ".",
                        "UniTask support define 由 asmdef versionDefines " +
                        CoCoFlowUtility.UniTaskSupportedRange + " 自动管理。");
                    uniTaskUsable = true;
                }
            }
            else
            {
                uniTaskUsable = form == CoCoUniTaskInstallForm.AssemblyOnly;
                if (uniTaskUsable)
                {
                    log.Add(
                        "UniTask detected as assembly-only (unitypackage). Manual support define is required and allowed.",
                        "UniTask 检测为 assembly-only（unitypackage）。需要且允许手动 support define。");
                }
            }

            string[] defines = CoCoFlowUtility.SelectAvailableSupportDefines(
                uniTaskUsable,
                IsDotweenInstalled(),
                IsAssemblyInstalled("DOTween.Modules"),
                IsAssemblyInstalled("UniTask.DOTween"));

            if (form == CoCoUniTaskInstallForm.UpmRegistered)
            {
                // UniTask define 已由 versionDefines 管理，不进入手动集合。
                defines = defines.Where(define => define != CoCoFlowUtility.UniTaskDefine).ToArray();
            }

            if (defines.Length == 0)
            {
                log.Add(
                    "No support defines were added because dependencies are not available yet.",
                    "依赖尚不可用，未添加任何 support define。");
                return;
            }

            AddDefinesToActiveTarget(defines, log);
        }

        public static string ReadManifestUniTaskDependency()
        {
            try
            {
                var root = LoadManifest().Root;
                if (root.TryGetObject("dependencies", out var dependencies) &&
                    dependencies.TryGetString(CoCoFlowUtility.UniTaskPackageName, out var dependency))
                    return dependency;
            }
            catch
            {
                // manifest 缺失/损坏时按无 UPM 注册处理，交由程序集探测。
            }

            return null;
        }

        private static void AddDefinesToActiveTarget(string[] definesToAdd, MessageCollector log)
        {
            try
            {
                var namedTarget = CoCoSetupStatusScanner.GetActiveNamedBuildTarget();
                var current = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                var updated = AddDefines(current, definesToAdd);

                if (updated == current)
                {
                    log.Add(
                        "Support defines were already configured for the active build target (" +
                        namedTarget.TargetName + ").",
                        "active 构建目标（" + namedTarget.TargetName + "）的 support define 已配置。");
                }
                else
                {
                    PlayerSettings.SetScriptingDefineSymbols(namedTarget, updated);
                    log.Add(
                        "Added support defines to active build target " + namedTarget.TargetName + ".",
                        "已向 active 构建目标 " + namedTarget.TargetName + " 添加 support define。");
                }
            }
            catch (Exception ex)
            {
                log.Add(
                    "Skipped active build target (" + ex.GetType().Name + ").",
                    "已跳过 active 构建目标（" + ex.GetType().Name + "）。");
            }
        }

        private static void RemoveDefinesFromActiveTarget(string defineToRemove, MessageCollector log)
        {
            try
            {
                var namedTarget = CoCoSetupStatusScanner.GetActiveNamedBuildTarget();
                var current = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
                var defines = SplitDefines(current);
                if (!defines.Contains(defineToRemove))
                    return;

                var updated = string.Join(
                    ";",
                    defines.Where(define => define != defineToRemove).ToArray());
                PlayerSettings.SetScriptingDefineSymbols(namedTarget, updated);
                log.Add(
                    "Removed stale manual define " + defineToRemove + " from active build target " +
                    namedTarget.TargetName + "; versionDefines is now the single authority.",
                    "已从 active 构建目标 " + namedTarget.TargetName + " 移除遗留手工宏 " + defineToRemove +
                    "；versionDefines 现为唯一权威。");
            }
            catch (Exception)
            {
                // 清理失败 = 显式 partial/error，不得显示成功（D-02 状态机红线）。
                log.Add(
                    "ERROR: Legacy define cleanup incomplete - failed on the active build target. " +
                    "Stale manual defines remain and must be resolved manually.",
                    "错误：遗留宏清理未完成——active 构建目标上操作失败。" +
                    "遗留手工宏仍然存在，需手动处理。");
            }
        }

        public static string AddDefines(string current, IEnumerable<string> definesToAdd)
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

        public static List<string> SplitDefines(string defines)
        {
            return defines
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(define => define.Trim())
                .Where(define => !string.IsNullOrEmpty(define))
                .Distinct()
                .ToList();
        }

        internal static bool IsSemanticVersionLower(string current, string minimum)
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
