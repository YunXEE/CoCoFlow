using System.Collections.Generic;
using CoCoFlow.Editor.Core;
using UnityEngine;
using UnityEngine.UIElements;

// 图编辑器读取编译或目录给出的要件，展示节点需要的输入、输出和数据槽。
namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>为图编辑器展示节点与图的输入、操作和 Context 要件。</summary>
    internal static class StateGraphRequirementPresenter
    {
        internal static void FillCard(
            VisualElement card,
            IReadOnlyList<RequirementSection> sections,
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

            foreach (RequirementSection section in sections)
            {
                AddSectionTitle(card, section);
                if (section.Entries.Count == 0)
                {
                    var none = new Label(CoCoEditorLocalization.Text("No requirements.", "无需求。"));
                    none.AddToClassList("sg-muted");
                    card.Add(none);
                    continue;
                }

                foreach (RequirementEntry entry in section.Entries)
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

        private static void AddSectionTitle(VisualElement card, RequirementSection section)
        {
            string kindText;
            switch (section.ScopeKind)
            {
                case RequirementScopeKind.SelectedState:
                    kindText = CoCoEditorLocalization.Text("Selected State", "选中 State");
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

        private static VisualElement BuildEntryRow(RequirementEntry entry)
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

        private static string KindText(RequirementEntryKind kind)
        {
            switch (kind)
            {
                case RequirementEntryKind.Intent:
                    return CoCoEditorLocalization.Text("Intent", "Intent");
                case RequirementEntryKind.Operation:
                    return CoCoEditorLocalization.Text("Operation", "Operation");
                default:
                    return CoCoEditorLocalization.Text("Context", "Context");
            }
        }

        private static CoCoEditorBadgeKind KindBadge(RequirementEntryKind kind)
        {
            switch (kind)
            {
                case RequirementEntryKind.Intent:
                    return CoCoEditorBadgeKind.Info;
                case RequirementEntryKind.Operation:
                    return CoCoEditorBadgeKind.Success;
                default:
                    return CoCoEditorBadgeKind.Neutral;
            }
        }

        private static string ShortId(string value) =>
            string.IsNullOrEmpty(value) || value.Length <= 8 ? value : value.Substring(0, 8);
    }
}
