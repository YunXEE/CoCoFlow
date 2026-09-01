using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.Common
{
    /// <summary>徽章语义（D3）：语义状态不单靠颜色，组件同时携带可读文本。</summary>
    public enum CoCoEditorBadgeKind
    {
        Neutral,
        Success,
        Warning,
        Error,
        Info
    }

    /// <summary>
    /// ccflow 视觉语言组件工厂（D1/D12：public Editor-only，组合式构造，无继承）。
    /// 只负责视觉组件构造，不持有业务数据、不管生命周期（边界见方案责任表）。
    /// </summary>
    public static class CoCoEditorElements
    {
        private const string ThemePath =
            "Packages/com.yunxee.cocoflow/Editor/Common/CoCoEditorCommon.uss";

        private const string BadgeTextName = "ccflow-badge-text";

        private static bool _themeMissingReported;

        /// <summary>
        /// 应用共享主题：加载 USS（去重）并为根节点挂 ccflow-root。
        /// 幂等：重复调用不重复加载样式表。
        /// 失败处置（方案 §3.2）：USS 缺失时 Debug.LogError 记录一次并以裸控件继续，不 crash 不静默。
        /// （零业务依赖：Common 不引用任何 Runtime 程序集，故用 UnityEngine.Debug 而非 CoCoLog。）
        /// </summary>
        public static void ApplyTheme(VisualElement root)
        {
            if (root == null)
            {
                return;
            }

            if (!root.ClassListContains("ccflow-root"))
            {
                root.AddToClassList("ccflow-root");
            }

            var styleSheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemePath);
            if (styleSheet == null)
            {
                if (!_themeMissingReported)
                {
                    _themeMissingReported = true;
                    Debug.LogError(
                        $"[CoCoEditorElements] ccflow theme style sheet missing at {ThemePath}; panels fall back to bare controls.");
                }

                return;
            }

            if (!root.styleSheets.Contains(styleSheet))
            {
                root.styleSheets.Add(styleSheet);
            }
        }

        /// <summary>节标题（ccflow-heading：13px bold + 底线）。</summary>
        public static Label CreateHeading(string text)
        {
            var label = new Label(text);
            label.AddToClassList("ccflow-heading");
            return label;
        }

        /// <summary>卡片容器（ccflow-card）+ 粗体标题；调用方继续向返回容器 Add 内容。</summary>
        public static VisualElement CreateCard(string title)
        {
            var card = new VisualElement();
            card.AddToClassList("ccflow-card");

            var titleLabel = new Label(title) { name = "ccflow-card-title" };
            titleLabel.AddToClassList("ccflow-card__title");
            card.Add(titleLabel);
            return card;
        }

        /// <summary>
        /// 徽章（ccflow-badge ccflow-badge--{kind}）。内部文本 Label 名为 ccflow-badge-text，
        /// 消费者可经 Query&lt;Label&gt;(BadgeTextName) 更新文本。
        /// </summary>
        public static VisualElement CreateBadge(string text, CoCoEditorBadgeKind kind)
        {
            var badge = new VisualElement();
            badge.AddToClassList("ccflow-badge");
            SetBadgeKind(badge, kind);

            var label = new Label(text) { name = BadgeTextName };
            badge.Add(label);
            return badge;
        }

        /// <summary>切换徽章语义（更换修饰类，保留文本）。</summary>
        public static void SetBadgeKind(VisualElement badge, CoCoEditorBadgeKind kind)
        {
            if (badge == null)
            {
                return;
            }

            badge.RemoveFromClassList("ccflow-badge--neutral");
            badge.RemoveFromClassList("ccflow-badge--success");
            badge.RemoveFromClassList("ccflow-badge--warning");
            badge.RemoveFromClassList("ccflow-badge--error");
            badge.RemoveFromClassList("ccflow-badge--info");
            badge.AddToClassList(KindToClassName(kind));
        }

        /// <summary>主操作按钮（ccflow-button--primary：蓝底白字；每决策区仅一个，见 D3/D4）。</summary>
        public static Button CreatePrimaryButton(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("ccflow-button--primary");
            return button;
        }

        /// <summary>危险操作按钮（ccflow-button--danger：淡红面；调用方须自备预览/披露/显式确认边界，D4）。</summary>
        public static Button CreateDangerButton(string text, Action clicked)
        {
            var button = new Button(clicked) { text = text };
            button.AddToClassList("ccflow-button--danger");
            return button;
        }

        /// <summary>
        /// 空状态（ccflow-empty）：为什么没有内容 + 推荐首步 + 次要替代（design-system 空状态三段式）。
        /// </summary>
        public static VisualElement CreateEmptyState(
            string title,
            string message,
            string firstStep = null,
            string alternative = null)
        {
            var container = new VisualElement();
            container.AddToClassList("ccflow-empty");

            var titleLabel = new Label(title) { name = "ccflow-empty-title" };
            titleLabel.AddToClassList("ccflow-empty__title");
            container.Add(titleLabel);

            var messageLabel = new Label(message) { name = "ccflow-empty-message" };
            messageLabel.AddToClassList("ccflow-empty__message");
            container.Add(messageLabel);

            if (!string.IsNullOrEmpty(firstStep))
            {
                var firstStepLabel = new Label(firstStep) { name = "ccflow-empty-first-step" };
                firstStepLabel.AddToClassList("ccflow-empty__first-step");
                container.Add(firstStepLabel);
            }

            if (!string.IsNullOrEmpty(alternative))
            {
                var alternativeLabel = new Label(alternative) { name = "ccflow-empty-alternative" };
                alternativeLabel.AddToClassList("ccflow-empty__alternative");
                container.Add(alternativeLabel);
            }

            return container;
        }

        /// <summary>
        /// 分级诊断行（ccflow-diagnostic-row）：徽章 + 消息 + 可选定位动作。
        /// kind 使用 Common 自己的语义（Common 不依赖 Runtime severity；
        /// P03 侧 severity→kind 映射归 P03，见方案 §2.1）。
        /// </summary>
        public static VisualElement CreateDiagnosticRow(
            string message,
            CoCoEditorBadgeKind kind,
            Action locate = null)
        {
            var row = new VisualElement();
            row.AddToClassList("ccflow-diagnostic-row");
            row.Add(CreateBadge(string.Empty, kind));

            var messageLabel = new Label(message) { name = "ccflow-diagnostic-message" };
            messageLabel.AddToClassList("ccflow-diagnostic-row__message");
            row.Add(messageLabel);

            if (locate != null)
            {
                var locateButton = new Button(locate) { text = "Locate" };
                locateButton.AddToClassList("ccflow-diagnostic-row__locate");
                row.Add(locateButton);
            }

            return row;
        }

        /// <summary>操作反馈区容器（ccflow-feedback）。</summary>
        public static VisualElement CreateFeedbackHost()
        {
            var host = new VisualElement { name = "ccflow-feedback" };
            host.AddToClassList("ccflow-feedback");
            return host;
        }

        private static string KindToClassName(CoCoEditorBadgeKind kind)
        {
            switch (kind)
            {
                case CoCoEditorBadgeKind.Success: return "ccflow-badge--success";
                case CoCoEditorBadgeKind.Warning: return "ccflow-badge--warning";
                case CoCoEditorBadgeKind.Error: return "ccflow-badge--error";
                case CoCoEditorBadgeKind.Info: return "ccflow-badge--info";
                default: return "ccflow-badge--neutral";
            }
        }
    }
}
