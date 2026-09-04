using System.Collections.Generic;
using CoCoFlow.Editor.Common;
using UnityEngine;
using UnityEngine.UIElements;

namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>
    /// 需求/Host 建议的结构化呈现器（维护者反馈：全量 32 位十六进制 ID 不可读）。
    /// 窗口「需求 / Host 建议」卡与 Inspector「Host 需求（manifest）」卡共用；
    /// 类型名 + 短 ID 徽章，完整 ID 放 tooltip。
    /// </summary>
    internal static class CoCoStateGraphRequirementPresenter
    {
        internal static void FillCard(
            VisualElement card,
            IReadOnlyList<CoCoRequirementSection> sections,
            bool compiledAvailable)
        {
            if (sections == null || sections.Count == 0)
            {
                var empty = new Label(CoCoEditorLocalization.Text(
                    "No requirement data yet. Select a State or run Analyze.",
                    "暂无需求数据。选中一个 State 或运行 Analyze。"));
                empty.AddToClassList("sg-muted");
                card.Add(empty);
                return;
            }

            foreach (CoCoRequirementSection section in sections)
            {
                AddSectionTitle(card, section);
                if (section.Entries.Count == 0)
                {
                    var none = new Label(CoCoEditorLocalization.Text("No requirements.", "无需求。"));
                    none.AddToClassList("sg-muted");
                    card.Add(none);
                    continue;
                }

                foreach (CoCoRequirementEntry entry in section.Entries)
                {
                    card.Add(BuildEntryRow(entry));
                }
            }

            if (!compiledAvailable)
            {
                var hint = new Label(CoCoEditorLocalization.Text(
                    "Compiled host requirements are unavailable until analysis succeeds.",
                    "分析成功后才会显示编译产出的 Host 需求。"));
                hint.AddToClassList("sg-muted");
                card.Add(hint);
            }
        }

        private static void AddSectionTitle(VisualElement card, CoCoRequirementSection section)
        {
            string kindText;
            switch (section.ScopeKind)
            {
                case CoCoRequirementScopeKind.SelectedState:
                    kindText = CoCoEditorLocalization.Text("Selected State", "选中 State");
                    break;
                case CoCoRequirementScopeKind.SelectedCondition:
                    kindText = CoCoEditorLocalization.Text("Condition", "条件");
                    break;
                default:
                    kindText = CoCoEditorLocalization.Text("Compiled · Host Requirements", "编译结果 · Host 需求");
                    break;
            }

            string title = section.ScopeLabel == null
                ? kindText
                : $"{kindText} · {section.ScopeLabel}  [{ShortId(section.ScopeFullId)}]";
            var label = CoCoEditorElements.CreateHeading(title);
            card.Add(label);
        }

        private static VisualElement BuildEntryRow(CoCoRequirementEntry entry)
        {
            var row = new VisualElement();
            row.AddToClassList("sg-requirement-row");

            row.Add(CoCoEditorElements.CreateBadge(
                KindText(entry.Kind),
                KindBadge(entry.Kind)));

            var text = entry.TypeName == null
                ? $"[{ShortId(entry.FullId)}]"
                : $"{entry.TypeName}  [{ShortId(entry.FullId)}]";
            var body = new Label(text);
            body.AddToClassList("sg-requirement-row__label");
            body.tooltip = entry.FullId;
            row.Add(body);
            return row;
        }

        private static string KindText(CoCoRequirementEntryKind kind)
        {
            switch (kind)
            {
                case CoCoRequirementEntryKind.Intent:
                    return CoCoEditorLocalization.Text("Intent", "Intent");
                case CoCoRequirementEntryKind.Operation:
                    return CoCoEditorLocalization.Text("Operation", "Operation");
                default:
                    return CoCoEditorLocalization.Text("Context", "Context");
            }
        }

        private static CoCoEditorBadgeKind KindBadge(CoCoRequirementEntryKind kind)
        {
            switch (kind)
            {
                case CoCoRequirementEntryKind.Intent:
                    return CoCoEditorBadgeKind.Info;
                case CoCoRequirementEntryKind.Operation:
                    return CoCoEditorBadgeKind.Success;
                default:
                    return CoCoEditorBadgeKind.Neutral;
            }
        }

        private static string ShortId(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);
    }
}
