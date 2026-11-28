using CoCoFlow.Editor.Core;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    internal sealed partial class StateGraphEditorWindow
    {
        #region Public API

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

        #endregion

        #region Internal Logic

        private VisualElement feedbackHost;

        private void DrawRequirements()
        {
            var requirements = new VisualElement { name = "state-graph-requirements" };
            var heading = CoCoEditorElements.CreateHeading(L("Requirements / Host Suggestions", "需求 / Host 建议"));
            texts.Register(heading, "Requirements / Host Suggestions", "需求 / Host 建议");
            requirements.Add(heading);
            StateGraphRequirementPresenter.FillCard(
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

            var status = new Label(controller.AnalysisResult.Succeeded
                ? L("Compilation succeeded.", "编译成功。")
                : L("Compilation blocked.", "编译阻断。"));
            parent.Add(status);

            int errorCount = 0;
            int warningCount = 0;
            int infoCount = 0;
            foreach (GraphDiagnostic diagnostic in controller.AnalysisResult.Diagnostics)
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

            foreach (GraphDiagnostic diagnostic in controller.AnalysisResult.Diagnostics)
            {
                GraphDiagnostic captured = diagnostic;
                parent.Add(CoCoEditorElements.CreateDiagnosticRow(
                    captured.Diagnostic.Message,
                    SeverityToBadgeKind(captured.Diagnostic.Severity),
                    () => controller.Locate(captured.Location)));
            }
        }

        #endregion
    }
}
