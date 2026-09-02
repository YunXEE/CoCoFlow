using System;
using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.Core
{
    /// <summary>
    /// CoCoFlow Utility 面板（原 CoCoFlowSetupAssistant 重做，D-17）。
    /// 定位：安装依赖、support define（宏）与模块启用状态的常驻工具面板；
    /// 本批次拓展仅语言切换（D10）。内部按 section 构建器组织，预留未来扩展位，
    /// 不建插件框架、不预建空 section（方案 v4 §3.1）。
    ///
    /// 行为等价面与声明非等价项见方案 v4 §3.3 invariant 表；
    /// Apply 入口有 D4 确认门禁（B1）：影响披露 + 显式确认 + 取消零写入。
    /// define/模块状态聚焦 active build target（D-18），UPM 形态遗留手工
    /// UniTask define 以 Warning 披露（#20-b，保 D-02 唯一权威）。
    /// </summary>
    public sealed class CoCoFlowUtility : EditorWindow
    {
        // ==========================================
        // 共享领域常量（原私有常量迁为 internal，供拆分文件共用；方案 v4 §2.1）
        // ==========================================
        internal const string ManifestPath = "Packages/manifest.json";
        internal const string UniTaskPackageName = "com.cysharp.unitask";
        internal const string RecommendedUniTaskGitUrl =
            "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask#2.5.11";
        internal const string AddressablesPackageName = "com.unity.addressables";
        internal const string RecommendedAddressablesVersion =
            AddressablesVersionPolicy.MinimumVersion;
        internal const string RecommendedAddressablesPackage =
            AddressablesPackageName + "@" + RecommendedAddressablesVersion;
        internal const string NewtonsoftPackageName = "com.unity.nuget.newtonsoft-json";
        internal const string NewtonsoftMinimumVersion = "3.2.2";
        internal const string CinemachineAssemblyName = "Unity.Cinemachine";
        internal const string OpenUpmRegistryName = "package.openupm.com";
        internal const string OpenUpmRegistryUrl = "https://package.openupm.com";
        internal const string UniTaskScope = "com.cysharp.unitask";
        internal const string UniTaskDefine = "COCOFLOW_UNITASK_SUPPORT";
        internal const string DotweenDefine = "COCOFLOW_DOTWEEN_SUPPORT";
        internal const string UniTaskDotweenDefine = "UNITASK_DOTWEEN_SUPPORT";
        internal const string UniTaskSupportedRange = CoCoUniTaskVersionPolicy.SupportedRange;

        /// <summary>控制台日志前缀（口径不变，invariant #12）。</summary>
        private const string ConsoleLogPrefix = "[CoCoFlow Setup] ";

        /// <summary>日志条目（双语对原始数据；显示时按语言投影，invariant #12/N2）。</summary>
        private readonly List<CoCoSetupModuleCatalog.BilingualText> _log =
            new List<CoCoSetupModuleCatalog.BilingualText>();

        private CoCoSetupDependencyStatus _status;
        private AddRequest _uniTaskRequest;
        private AddRequest _addressablesRequest;
        private bool _isBusy;

        // 渲染引用（CreateGUI 持有；重建安全，零持久订阅）
        private VisualElement _contentHost;
        private VisualElement _dependenciesRows;
        private Label _definesSummary;
        private VisualElement _definesRows;
        private VisualElement _modulesRows;
        private Label _modulesSummary;
        private Button _applyButton;
        private Button _refreshButton;
        private Button _addressablesButton;
        private Label _addressablesNote;
        private Label _busyRow;
        private VisualElement _logRows;
        private VisualElement _logEmpty;

        [MenuItem("CoCoFlow/Utility Panel")]
        public static void Open()
        {
            var window = GetWindow<CoCoFlowUtility>("CoCoFlow Utility");
            window.minSize = new Vector2(620f, 560f);
            window.RefreshStatus();
            window.Show();
        }

        private void OnEnable()
        {
            RefreshStatus();
            CoCoEditorLocalization.LanguageChanged += OnLanguageChanged; // 唯一订阅点
        }

        private void OnDisable()
        {
            CoCoEditorLocalization.LanguageChanged -= OnLanguageChanged; // 对称退订
            EditorApplication.update -= TickPackageRequest;
            EditorApplication.update -= TickAddressablesPackageRequest;
        }

        public void CreateGUI()
        {
            CoCoEditorElements.ApplyTheme(rootVisualElement);

            if (_status == null)
                RefreshStatusData();

            // 全局滚动容器：内容超出窗口高度时滚动（等价旧 IMGUI BeginScrollView）。
            var scroll = new ScrollView(ScrollViewMode.Vertical) { name = "ccflow-utility-scroll" };
            scroll.style.flexGrow = 1f;
            scroll.style.marginTop = 8f;
            rootVisualElement.Add(scroll);
            _contentHost = scroll;

            BuildHeaderSection();
            BuildDefinesSection();
            BuildDependenciesSection();
            BuildModulesSection();
            BuildActionsSection();
            BuildLogSection();

            RefreshSectionContents();
        }

        // ==========================================
        // Section 构建器（D-17 扩展位；每区自包含，未来新功能=新增构建器）
        // ==========================================

        private void BuildHeaderSection()
        {
            var header = CoCoEditorElements.CreateCard(string.Empty);
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.FlexStart;

            var titleColumn = new VisualElement();
            titleColumn.style.flexGrow = 1f;

            var eyebrow = new Label(CoCoEditorLocalization.Text("CoCoFlow", "CoCoFlow"));
            eyebrow.AddToClassList("ccflow-muted");
            eyebrow.style.unityFontStyleAndWeight = FontStyle.Bold;
            eyebrow.style.fontSize = 11f;
            titleColumn.Add(eyebrow);

            var title = new Label(CoCoEditorLocalization.Text("CoCoFlow Utility", "CoCoFlow Utility"))
            {
                name = "ccflow-utility-title"
            };
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.fontSize = 18f;
            title.style.marginTop = 2f;
            title.style.marginBottom = 4f;
            titleColumn.Add(title);

            var subtitle = new Label(CoCoEditorLocalization.Text(
                "Inspect dependencies, support defines, and module availability. Writes only go through Apply with explicit confirmation.",
                "检查依赖、support define（宏）与模块可用性。写入仅经 Apply 显式确认后执行。"));
            subtitle.AddToClassList("ccflow-muted");
            subtitle.style.whiteSpace = WhiteSpace.Normal;
            titleColumn.Add(subtitle);

            header.Add(titleColumn);
            header.Add(BuildLanguageSwitch());
            _contentHost.Add(header);
        }

        /// <summary>语言切换（D10；两段按钮，当前语言高亮禁用表示选中）。</summary>
        private VisualElement BuildLanguageSwitch()
        {
            var container = new VisualElement { name = "language-switch" };
            container.style.flexDirection = FlexDirection.Row;
            container.style.marginLeft = 8f;
            container.style.flexShrink = 0f;

            var current = CoCoEditorLocalization.CurrentLanguage;

            var english = new Button(() => CoCoEditorLocalization.SetLanguage(
                CoCoEditorLanguage.English))
            {
                text = "EN",
                name = "language-en"
            };
            english.SetEnabled(current != CoCoEditorLanguage.English);
            english.style.minWidth = 40f;

            var chinese = new Button(() => CoCoEditorLocalization.SetLanguage(
                CoCoEditorLanguage.SimplifiedChinese))
            {
                text = "中文",
                name = "language-zh"
            };
            chinese.SetEnabled(current != CoCoEditorLanguage.SimplifiedChinese);
            chinese.style.minWidth = 48f;

            container.Add(english);
            container.Add(chinese);
            return container;
        }

        private void BuildDependenciesSection()
        {
            var card = CoCoEditorElements.CreateCard(CoCoEditorLocalization.Text(
                "Dependencies", "依赖"));
            _dependenciesRows = new VisualElement { name = "dependencies-rows" };
            card.Add(_dependenciesRows);
            _contentHost.Add(card);
        }

        private void BuildDefinesSection()
        {
            var card = CoCoEditorElements.CreateCard(CoCoEditorLocalization.Text(
                "Support Defines (Macros)", "Support Define（宏）"));
            _definesSummary = new Label { name = "defines-summary" };
            _definesSummary.AddToClassList("ccflow-muted");
            _definesSummary.style.marginBottom = 4f;
            card.Add(_definesSummary);
            _definesRows = new VisualElement { name = "defines-rows" };
            card.Add(_definesRows);
            _contentHost.Add(card);
        }

        private void BuildModulesSection()
        {
            var card = CoCoEditorElements.CreateCard(CoCoEditorLocalization.Text(
                "Modules", "模块"));
            _modulesSummary = new Label { name = "modules-summary" };
            _modulesSummary.AddToClassList("ccflow-muted");
            card.Add(_modulesSummary);
            _modulesRows = new VisualElement { name = "modules-rows" };
            card.Add(_modulesRows);
            _contentHost.Add(card);
        }

        private void BuildActionsSection()
        {
            var card = CoCoEditorElements.CreateCard(CoCoEditorLocalization.Text(
                "Actions", "操作"));

            var primaryRow = new VisualElement();
            primaryRow.style.flexDirection = FlexDirection.Row;
            primaryRow.style.marginBottom = 4f;

            _applyButton = CoCoEditorElements.CreatePrimaryButton(
                CoCoEditorLocalization.Text("Apply Recommended Dependencies", "应用推荐依赖"),
                RequestApplyRecommendedDependencies);
            _applyButton.style.minWidth = 220f;

            _refreshButton = new Button(RefreshStatus)
            {
                text = CoCoEditorLocalization.Text("Refresh Status", "刷新状态")
            };
            _refreshButton.style.minWidth = 110f;
            _refreshButton.style.marginLeft = 8f;

            primaryRow.Add(_applyButton);
            primaryRow.Add(_refreshButton);
            card.Add(primaryRow);

            var optionalRow = new VisualElement();
            optionalRow.style.flexDirection = FlexDirection.Row;
            optionalRow.style.alignItems = Align.FlexStart;

            _addressablesButton = new Button(InstallOptionalAddressables)
            {
                text = CoCoEditorLocalization.Text(
                    "Install Supported Addressables " + RecommendedAddressablesVersion,
                    "安装受支持的 Addressables " + RecommendedAddressablesVersion)
            };
            _addressablesButton.style.minWidth = 220f;

            _addressablesNote = new Label(CoCoEditorLocalization.Text(
                "Adds the optional project dependency only; no global support define is written.",
                "仅添加可选项目依赖；不写入任何全局 support define。"));
            _addressablesNote.AddToClassList("ccflow-muted");
            _addressablesNote.style.whiteSpace = WhiteSpace.Normal;
            _addressablesNote.style.flexGrow = 1f;
            _addressablesNote.style.marginLeft = 8f;

            optionalRow.Add(_addressablesButton);
            optionalRow.Add(_addressablesNote);
            card.Add(optionalRow);

            _busyRow = new Label(CoCoEditorLocalization.Text(
                "Package Manager request in progress…", "Package Manager 请求进行中…"));
            _busyRow.AddToClassList("ccflow-muted");
            _busyRow.style.display = DisplayStyle.None;
            _busyRow.style.marginTop = 4f;
            card.Add(_busyRow);

            _contentHost.Add(card);
        }

        private void BuildLogSection()
        {
            var card = CoCoEditorElements.CreateCard(CoCoEditorLocalization.Text(
                "Log", "日志"));
            _logRows = new VisualElement { name = "log-rows" };
            card.Add(_logRows);
            _logEmpty = CoCoEditorElements.CreateEmptyState(
                CoCoEditorLocalization.Text("No actions yet", "暂无操作"),
                CoCoEditorLocalization.Text(
                    "Apply and install results will be listed here.",
                    "Apply 与安装的执行结果将列在这里。"),
                CoCoEditorLocalization.Text(
                    "Use Apply Recommended Dependencies to configure the project.",
                    "使用 Apply Recommended Dependencies 配置项目。"),
                CoCoEditorLocalization.Text(
                    "Refresh Status rescans without writing anything.",
                    "Refresh Status 只重新扫描，不写入任何内容。"));
            card.Add(_logEmpty);
            _contentHost.Add(card);
        }

        // ==========================================
        // 投影与刷新
        // ==========================================

        private void RefreshStatus()
        {
            RefreshStatusData();
            RefreshSectionContents();
        }

        private void RefreshStatusData()
        {
            _status = CoCoSetupStatusScanner.BuildStatus();
        }

        private void RefreshSectionContents()
        {
            if (rootVisualElement == null)
                return;

            RefreshDependenciesRows();
            RefreshDefinesRows();
            RefreshModulesRows();
            RefreshActionStates();
            RefreshLogRows();
        }

        private static CoCoEditorBadgeKind StateToBadgeKind(DependencyRowState state)
        {
            switch (state)
            {
                case DependencyRowState.Error: return CoCoEditorBadgeKind.Error;
                case DependencyRowState.Warning: return CoCoEditorBadgeKind.Warning;
                case DependencyRowState.InfoOptional: return CoCoEditorBadgeKind.Info;
                default: return CoCoEditorBadgeKind.Success;
            }
        }

        /// <summary>
        /// 状态行（两行式）：首行 = 名称 + 徽章；次行 = 完整消息换行。
        /// 长消息不再与名称/徽章挤同一横行；行间以细分隔线区隔。
        /// </summary>
        private static VisualElement BuildStatusRow(
            string name,
            CoCoSetupModuleCatalog.BilingualText message,
            DependencyRowState state)
        {
            var row = new VisualElement();
            row.style.marginTop = 5f;
            row.style.marginBottom = 7f;
            row.style.paddingBottom = 6f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);

            var headLine = new VisualElement();
            headLine.style.flexDirection = FlexDirection.Row;
            headLine.style.alignItems = Align.Center;

            var nameLabel = new Label(name);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.marginRight = 8f;
            headLine.Add(nameLabel);

            headLine.Add(CreateCenteredBadge(StateToBadgeKind(state)));

            row.Add(headLine);

            var messageLabel = new Label(ProjectText(message));
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.marginTop = 3f;
            row.Add(messageLabel);
            return row;
        }

        /// <summary>
        /// 徽章（实例级居中修正：文本 MiddleCenter、容器定高、行内垂直居中；
        /// 不修改冻结的 Editor/Common USS——中文标签与文字基线对齐问题的最小修复）。
        /// </summary>
        private static VisualElement CreateCenteredBadge(CoCoEditorBadgeKind kind, string text = null)
        {
            var badge = CoCoEditorElements.CreateBadge(
                text ?? KindLabel(kind),
                kind);
            badge.style.alignSelf = Align.Center;
            badge.style.height = 20f;
            badge.style.flexShrink = 0f;
            var badgeText = badge.Q<Label>("ccflow-badge-text");
            if (badgeText != null)
            {
                badgeText.style.unityTextAlign = TextAnchor.MiddleCenter;
                badgeText.style.height = 20f;
                badgeText.style.overflow = UnityEngine.UIElements.Overflow.Hidden;
            }
            return badge;
        }

        private static string KindLabel(CoCoEditorBadgeKind kind)
        {
            switch (kind)
            {
                case CoCoEditorBadgeKind.Success:
                    return CoCoEditorLocalization.Text("OK", "成功");
                case CoCoEditorBadgeKind.Warning:
                    return CoCoEditorLocalization.Text("Warn", "警告");
                case CoCoEditorBadgeKind.Error:
                    return CoCoEditorLocalization.Text("Error", "错误");
                case CoCoEditorBadgeKind.Info:
                    return CoCoEditorLocalization.Text("Info", "信息");
                default:
                    return CoCoEditorLocalization.Text("Note", "注记");
            }
        }

        private static string ProjectText(CoCoSetupModuleCatalog.BilingualText text)
        {
            return CoCoEditorLocalization.Text(text.English, text.SimplifiedChinese);
        }

        private void RefreshDependenciesRows()
        {
            if (_dependenciesRows == null || _status == null)
                return;

            _dependenciesRows.Clear();
            _dependenciesRows.Add(BuildStatusRow(
                CoCoEditorLocalization.Text("UniTask", "UniTask"),
                _status.UniTaskMessage,
                _status.UniTaskState));
            _dependenciesRows.Add(BuildStatusRow(
                CoCoEditorLocalization.Text("Addressables (Optional)", "Addressables（可选）"),
                _status.AddressablesMessage,
                _status.AddressablesState));
            _dependenciesRows.Add(BuildStatusRow(
                CoCoEditorLocalization.Text("Newtonsoft", "Newtonsoft"),
                _status.NewtonsoftMessage,
                _status.NewtonsoftState));
            _dependenciesRows.Add(BuildStatusRow(
                CoCoEditorLocalization.Text("Cinemachine", "Cinemachine"),
                _status.CinemachineInstalled
                    ? new CoCoSetupModuleCatalog.BilingualText(
                        "Detected from package dependency.",
                        "已从包依赖检出。")
                    : new CoCoSetupModuleCatalog.BilingualText(
                        "Missing. It should resolve from CoCoFlow package dependencies.",
                        "缺失。应经 CoCoFlow 包依赖解析。"),
                _status.CinemachineInstalled
                    ? DependencyRowState.InfoSuccess
                    : DependencyRowState.Warning));
            _dependenciesRows.Add(BuildStatusRow(
                "DOTween",
                _status.DotweenMessage,
                _status.DotweenState));

            if (_status.HasUniTaskOpenUpmScope)
            {
                _dependenciesRows.Add(BuildStatusRow(
                    CoCoEditorLocalization.Text("OpenUPM", "OpenUPM"),
                    new CoCoSetupModuleCatalog.BilingualText(
                        "UniTask scope is still present and will be removed by Apply Recommended Dependencies.",
                        "UniTask scope 仍存在，Apply Recommended Dependencies 将移除它。"),
                    DependencyRowState.Warning));
            }
        }

        private void RefreshDefinesRows()
        {
            if (_definesRows == null || _status == null)
                return;

            _definesRows.Clear();

            if (!_status.ActiveTargetReadable)
            {
                _definesSummary.text = string.Empty;
                _definesRows.Add(BuildStatusRow(
                    CoCoEditorLocalization.Text("Build Target", "构建目标"),
                    new CoCoSetupModuleCatalog.BilingualText(
                        "Unable to read scripting define symbols of the active build target.",
                        "无法读取 active 构建目标的 scripting define。"),
                    DependencyRowState.Warning));
                return;
            }

            var target = _status.ActiveTargetName;
            var on = 0;
            var auto = 0;
            var off = 0;

            foreach (var define in new[]
                     {
                         CoCoFlowUtility.UniTaskDefine,
                         CoCoFlowUtility.DotweenDefine,
                         CoCoFlowUtility.UniTaskDotweenDefine
                     })
            {
                var semantic = ClassifyDefineRow(define, out var message);
                SemanticToBadge(semantic, out var kind, out var badgeText);
                if (semantic == DefineRowSemantic.On) on++;
                else if (semantic == DefineRowSemantic.Automatic) auto++;
                else off++;
                _definesRows.Add(BuildDefineRow(define, target, kind, badgeText, message));
            }

            _definesSummary.text = CoCoEditorLocalization.Text(
                $"3 macros · {on} on · {(auto > 0 ? auto + " automatic · " : "")}{off} off" +
                $" (target: {target})",
                $"共 3 个宏 · 已开启 {on} · " +
                (auto > 0 ? $"自动管理 {auto} · " : "") +
                $"未开启 {off}（目标：{target}）");
        }

        private enum DefineRowSemantic
        {
            On,
            Automatic,
            Off,
            LegacyCleanup,
            Blocked
        }

        private DefineRowSemantic ClassifyDefineRow(string define, out CoCoSetupModuleCatalog.BilingualText message)
        {
            message = default;

            // UniTask 宏：UPM 形态 = versionDefines 自动管理（D-02）。
            if (define == UniTaskDefine)
            {
                if (_status.UniTaskVersionBlocked)
                {
                    message = new CoCoSetupModuleCatalog.BilingualText(
                        "UniTask UPM version is outside " + UniTaskSupportedRange +
                        "; linked assemblies stay disabled. Fix the dependency version first.",
                        "UniTask UPM 版本超出 " + UniTaskSupportedRange +
                        "；关联程序集保持禁用。请先修正依赖版本。");
                    return DefineRowSemantic.Blocked;
                }

                if (_status.UniTaskDefineAutomatic)
                {
                    if (_status.LegacyUniTaskManualDefinePresent)
                    {
                        message = new CoCoSetupModuleCatalog.BilingualText(
                            "Legacy manual macro detected; versionDefines is the single authority. " +
                            "Apply will remove it (current target only).",
                            "检测到遗留手工宏；versionDefines 为唯一权威。Apply 将清理（仅当前目标）。");
                        return DefineRowSemantic.LegacyCleanup;
                    }

                    message = new CoCoSetupModuleCatalog.BilingualText(
                        "Managed automatically by asmdef versionDefines " + UniTaskSupportedRange +
                        " according to the resolved UniTask version.",
                        "由 asmdef versionDefines " + UniTaskSupportedRange +
                        " 按解析到的 UniTask 版本自动控制。");
                    return DefineRowSemantic.Automatic;
                }
            }

            var present = _status.DefinePresentOnActiveTarget(define);
            if (present)
            {
                message = new CoCoSetupModuleCatalog.BilingualText(
                    "Enabled on the active build target.",
                    "已在 active 构建目标上开启。");
                return DefineRowSemantic.On;
            }

            // 未开启：给出对应原因与出路。
            if (define == UniTaskDefine)
            {
                message = _status.UniTaskInstalled
                    ? new CoCoSetupModuleCatalog.BilingualText(
                        "Not enabled. UniTask (unitypackage) is detected; Apply adds the manual macro.",
                        "未开启。检测到 UniTask（unitypackage）；Apply 将添加手动宏。")
                    : new CoCoSetupModuleCatalog.BilingualText(
                        "Not enabled. UniTask is missing; Apply installs the recommended dependency first.",
                        "未开启。UniTask 缺失；Apply 将先安装推荐依赖。");
            }
            else if (define == DotweenDefine)
            {
                message = new CoCoSetupModuleCatalog.BilingualText(
                    "Not enabled. Install DOTween (with DOTween.Modules), then Apply.",
                    "未开启。请先安装 DOTween（含 DOTween.Modules），再执行 Apply。");
            }
            else
            {
                message = new CoCoSetupModuleCatalog.BilingualText(
                    "Not enabled. Requires UniTask + DOTween (with Modules) + UniTask.DOTween, then Apply.",
                    "未开启。需要 UniTask + DOTween（含 Modules）+ UniTask.DOTween 齐备后执行 Apply。");
            }

            return DefineRowSemantic.Off;
        }

        /// <summary>define 行：宏名 + 明确语义徽章（已开启/自动管理/未开启/遗留需清理/受阻）+ 原因说明。</summary>
        private VisualElement BuildDefineRow(
            string define,
            string target,
            CoCoEditorBadgeKind kind,
            string badgeText,
            CoCoSetupModuleCatalog.BilingualText message)
        {
            var row = new VisualElement();
            row.style.marginTop = 5f;
            row.style.marginBottom = 7f;
            row.style.paddingBottom = 6f;
            row.style.borderBottomWidth = 1f;
            row.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.25f);

            var headLine = new VisualElement();
            headLine.style.flexDirection = FlexDirection.Row;
            headLine.style.alignItems = Align.Center;

            var nameLabel = new Label(define);
            nameLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            nameLabel.style.marginRight = 8f;
            nameLabel.style.alignSelf = Align.Center;
            headLine.Add(nameLabel);

            headLine.Add(CreateCenteredBadge(kind, badgeText));
            row.Add(headLine);

            var messageLabel = new Label(ProjectText(message));
            messageLabel.style.whiteSpace = WhiteSpace.Normal;
            messageLabel.style.marginTop = 3f;
            row.Add(messageLabel);
            return row;
        }

        /// <summary>define 语义 → 徽章 kind + 明确语义词（回答"开没开"）。</summary>
        private static void SemanticToBadge(
            DefineRowSemantic semantic,
            out CoCoEditorBadgeKind kind,
            out string badgeText)
        {
            switch (semantic)
            {
                case DefineRowSemantic.On:
                    kind = CoCoEditorBadgeKind.Success;
                    badgeText = CoCoEditorLocalization.Text("On", "已开启");
                    return;
                case DefineRowSemantic.Automatic:
                    kind = CoCoEditorBadgeKind.Info;
                    badgeText = CoCoEditorLocalization.Text("Auto", "自动管理");
                    return;
                case DefineRowSemantic.LegacyCleanup:
                    kind = CoCoEditorBadgeKind.Warning;
                    badgeText = CoCoEditorLocalization.Text("Legacy", "遗留清理");
                    return;
                case DefineRowSemantic.Blocked:
                    kind = CoCoEditorBadgeKind.Error;
                    badgeText = CoCoEditorLocalization.Text("Blocked", "受阻");
                    return;
                default:
                    kind = CoCoEditorBadgeKind.Warning;
                    badgeText = CoCoEditorLocalization.Text("Off", "未开启");
                    return;
            }
        }

        private void RefreshModulesRows()
        {
            if (_modulesRows == null || _status == null)
                return;

            _modulesRows.Clear();
            var modules = CoCoSetupModuleCatalog.EvaluateModules(_status);
            var enabled = 0;
            foreach (var module in modules)
            {
                if (module.IsEnabled)
                    enabled++;
                _modulesRows.Add(BuildStatusRow(
                    module.Definition.DisplayName,
                    module.BuildMessage(),
                    module.IsEnabled
                        ? DependencyRowState.InfoSuccess
                        : DependencyRowState.Warning));
            }

            _modulesSummary.text = CoCoEditorLocalization.Text(
                enabled + "/" + modules.Count + " enabled",
                "已启用 " + enabled + "/" + modules.Count);
        }

        private void RefreshActionStates()
        {
            if (_applyButton == null || _status == null)
                return;

            _applyButton.SetEnabled(!_isBusy);
            _refreshButton.SetEnabled(!_isBusy);
            _addressablesButton.SetEnabled(!_isBusy && _status.AddressablesInstallRecommended);
            _busyRow.style.display = _isBusy ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void RefreshLogRows()
        {
            if (_logRows == null)
                return;

            _logRows.Clear();
            _logEmpty.style.display = _log.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            foreach (var entry in _log)
            {
                var label = new Label(ProjectText(entry));
                label.style.whiteSpace = WhiteSpace.Normal;
                label.style.marginBottom = 2f;
                _logRows.Add(label);
            }
        }

        private void OnLanguageChanged()
        {
            if (rootVisualElement == null)
                return;

            // 语言切换只重建投影，不重扫描（§2.3）。
            rootVisualElement.Clear();
            CreateGUI();
        }

        // ==========================================
        // 动作：Apply（D4 确认门禁 → 确认后执行原序列，invariant #7/#18）
        // ==========================================

        private void RequestApplyRecommendedDependencies()
        {
            if (_isBusy)
                return;

            CoCoSetupApplyConfirmDialog.Open(
                this,
                CoCoEditorLocalization.Text(
                    "Apply Recommended Dependencies",
                    "应用推荐依赖"),
                new[]
                {
                    CoCoEditorLocalization.Text(
                        "Add or upgrade Newtonsoft dependency to " + NewtonsoftMinimumVersion + ".",
                        "新增或升级 Newtonsoft 依赖至 " + NewtonsoftMinimumVersion + "。"),
                    CoCoEditorLocalization.Text(
                        "Remove the UniTask scope from the OpenUPM registry (empty registry entries are removed too).",
                        "移除 OpenUPM registry 中的 UniTask scope（空 registry 条目一并移除）。"),
                    CoCoEditorLocalization.Text(
                        "Request the recommended UniTask Git URL via Package Manager (may replace a non-recommended source).",
                        "经 Package Manager 请求推荐 UniTask Git URL（可能替换现有非推荐来源）。"),
                    CoCoEditorLocalization.Text(
                        "Add or remove support defines of the active build target (UPM form also cleans the legacy manual UniTask define).",
                        "增删 active 构建目标的 support define（UPM 形态同时清理遗留手工 UniTask 宏）。")
                },
                ApplyRecommendedDependencies);
        }

        private void ApplyRecommendedDependencies()
        {
            _log.Clear();

            var log = new CoCoSetupDependencyActions.MessageCollector();
            try
            {
                CoCoSetupDependencyActions.ConfigureProjectManifest(log);
            }
            catch (Exception ex)
            {
                FlushCollector(log);
                AddLog(
                    "ERROR: Failed to update Packages/manifest.json. " + ex.Message,
                    "错误：更新 Packages/manifest.json 失败。" + ex.Message);
                Debug.LogError(ConsoleLogPrefix + "Failed to update manifest:\n" + ex);
                RefreshStatus();
                return;
            }

            FlushCollector(log);
            try
            {
                _uniTaskRequest = Client.Add(RecommendedUniTaskGitUrl);
                _isBusy = true;
                AddLog(
                    "Requested UniTask Git dependency: " + RecommendedUniTaskGitUrl,
                    "已请求 UniTask Git 依赖：" + RecommendedUniTaskGitUrl);
                EditorApplication.update -= TickPackageRequest;
                EditorApplication.update += TickPackageRequest;
            }
            catch (Exception ex)
            {
                AddLog(
                    "ERROR: Failed to start UniTask install. " + ex.Message,
                    "错误：启动 UniTask 安装失败。" + ex.Message);
                Debug.LogError(ConsoleLogPrefix + "Failed to start UniTask install:\n" + ex);
                FlushCollector(CollectApplyAvailableSupportDefines(false));
                RefreshStatus();
            }

            RefreshActionStates();
        }

        private CoCoSetupDependencyActions.MessageCollector CollectApplyAvailableSupportDefines(
            bool uniTaskInstallSucceeded)
        {
            var log = new CoCoSetupDependencyActions.MessageCollector();
            CoCoSetupDependencyActions.ApplyAvailableSupportDefines(uniTaskInstallSucceeded, log);
            return log;
        }

        private void TickPackageRequest()
        {
            if (_uniTaskRequest == null || !_uniTaskRequest.IsCompleted)
                return;

            EditorApplication.update -= TickPackageRequest;
            _isBusy = false;

            if (_uniTaskRequest.Status == StatusCode.Failure)
            {
                var message = _uniTaskRequest.Error != null
                    ? _uniTaskRequest.Error.message
                    : "Unknown Package Manager error.";
                AddLog(
                    "ERROR: UniTask install failed. " + message,
                    "错误：UniTask 安装失败。" + message);
                Debug.LogError(ConsoleLogPrefix + "UniTask install failed: " + message);
                FlushCollector(CollectApplyAvailableSupportDefines(false));
                RefreshStatus();
                return;
            }

            AddLog(
                "UniTask Git dependency installed.",
                "UniTask Git 依赖已安装。");
            FlushCollector(CollectApplyAvailableSupportDefines(true));
            AssetDatabase.Refresh();
            RefreshStatus();
        }

        // ==========================================
        // 动作：Install Optional Addressables（直接执行；单一加性写入 + 卡内披露，invariant #9）
        // ==========================================

        private void InstallOptionalAddressables()
        {
            if (_isBusy)
                return;

            _log.Clear();
            try
            {
                _addressablesRequest = Client.Add(RecommendedAddressablesPackage);
                _isBusy = true;
                AddLog(
                    "Requested optional Addressables dependency: " + RecommendedAddressablesPackage,
                    "已请求可选 Addressables 依赖：" + RecommendedAddressablesPackage);
                EditorApplication.update -= TickAddressablesPackageRequest;
                EditorApplication.update += TickAddressablesPackageRequest;
            }
            catch (Exception ex)
            {
                _isBusy = false;
                AddLog(
                    "ERROR: Failed to start optional Addressables install. " + ex.Message,
                    "错误：启动可选 Addressables 安装失败。" + ex.Message);
                Debug.LogError(ConsoleLogPrefix + "Failed to start optional Addressables install:\n" + ex);
                RefreshStatus();
            }

            RefreshActionStates();
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
                AddLog(
                    "ERROR: Optional Addressables install failed. " + message,
                    "错误：可选 Addressables 安装失败。" + message);
                Debug.LogError(ConsoleLogPrefix + "Optional Addressables install failed: " + message);
                RefreshStatus();
                return;
            }

            AddLog(
                "Optional Addressables dependency installed.",
                "可选 Addressables 依赖已安装。");
            AssetDatabase.Refresh();
            RefreshStatus();
        }

        // ==========================================
        // 日志（双语对原始数据 + 控制台英文侧，invariant #12）
        // ==========================================

        private void AddLog(string english, string simplifiedChinese)
        {
            _log.Add(new CoCoSetupModuleCatalog.BilingualText(english, simplifiedChinese));
            Debug.Log(ConsoleLogPrefix + english);
            RefreshLogRows();
        }

        private void FlushCollector(CoCoSetupDependencyActions.MessageCollector collector)
        {
            foreach (var message in collector.Messages)
            {
                _log.Add(message);
                Debug.Log(ConsoleLogPrefix + message.English);
            }

            RefreshLogRows();
        }

        // ==========================================
        // 测试绑定的 internal statics（声明保留在窗口类，invariant #14）
        // ==========================================

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
    }

    /// <summary>
    /// Apply 确认对话框（B1/D4：影响披露 + 显式确认 + 取消零写入；轻量模态，ccflow 卡片形态）。
    /// 取消/关闭窗口不触发任何回调。
    /// </summary>
    internal sealed class CoCoSetupApplyConfirmDialog : EditorWindow
    {
        private string _titleText;
        private IReadOnlyList<string> _impactLines;
        private Action _onConfirmed;

        internal static void Open(
            EditorWindow owner,
            string titleText,
            IReadOnlyList<string> impactLines,
            Action onConfirmed)
        {
            var dialog = CreateInstance<CoCoSetupApplyConfirmDialog>();
            dialog._titleText = titleText;
            dialog._impactLines = impactLines;
            dialog._onConfirmed = onConfirmed;
            dialog.titleContent = new UnityEngine.GUIContent(
                CoCoEditorLocalization.Text("CoCoFlow Utility", "CoCoFlow Utility"));

            const float width = 520f;
            const float height = 300f;
            var main = EditorGUIUtility.GetMainWindowPosition();
            dialog.position = new Rect(
                main.x + (main.width - width) * 0.5f,
                main.y + (main.height - height) * 0.5f,
                width,
                height);
            dialog.minSize = new Vector2(width, height);
            dialog.maxSize = new Vector2(width + 120f, height + 160f);
            dialog.ShowModal();
        }

        public void CreateGUI()
        {
            CoCoEditorElements.ApplyTheme(rootVisualElement);

            var card = CoCoEditorElements.CreateCard(_titleText);

            var intro = new Label(CoCoEditorLocalization.Text(
                "This will perform the following writes:",
                "将执行以下写入操作："));
            intro.style.whiteSpace = WhiteSpace.Normal;
            intro.style.marginBottom = 6f;
            card.Add(intro);

            foreach (var line in _impactLines)
            {
                var impact = new Label("• " + line);
                impact.style.whiteSpace = WhiteSpace.Normal;
                impact.style.marginBottom = 3f;
                impact.style.marginLeft = 8f;
                card.Add(impact);
            }

            var cancelNote = new Label(CoCoEditorLocalization.Text(
                "Cancel writes nothing (no manifest, Package Manager, or define changes).",
                "取消不写入任何内容（manifest、Package Manager、define 均不变）。"));
            cancelNote.AddToClassList("ccflow-muted");
            cancelNote.style.whiteSpace = WhiteSpace.Normal;
            cancelNote.style.marginTop = 6f;
            card.Add(cancelNote);

            rootVisualElement.Add(card);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.FlexEnd;
            buttonRow.style.marginTop = 8f;

            var cancelButton = new Button(Close)
            {
                text = CoCoEditorLocalization.Text("Cancel", "取消")
            };

            var confirmButton = CoCoEditorElements.CreatePrimaryButton(
                CoCoEditorLocalization.Text("Confirm", "确认"),
                () =>
                {
                    var callback = _onConfirmed;
                    Close();
                    callback?.Invoke();
                });
            confirmButton.style.marginLeft = 8f;

            buttonRow.Add(cancelButton);
            buttonRow.Add(confirmButton);
            rootVisualElement.Add(buttonRow);
        }
    }
}
