using System.Collections.Generic;
using CoCoFlow.Editor.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine.UIElements;

// 图编辑器读取编译或目录给出的要件，展示节点需要的输入、输出和数据槽。
namespace CoCoFlow.Editor.StateGraph
{
    /// <summary>为图编辑器展示节点与图的输入、操作和 Context 要件。</summary>
    internal static class StateGraphRequirementPresenter
    {
        #region Public API

        internal static IReadOnlyList<RequirementSection> BuildSections(
            StateGraphStateRecord state,
            GraphDescriptorCatalog catalog,
            GraphAssetCompileResult analysisResult)
        {
            var sections = new List<RequirementSection>();

            if (state != null &&
                StateDescriptorId.TryCreate(
                    state.StateDescriptorId.High,
                    state.StateDescriptorId.Low,
                    out StateDescriptorId stateDescriptorId) &&
                catalog != null &&
                catalog.TryGetStateDescriptor(stateDescriptorId, out StateDescriptor stateDescriptor))
            {
                sections.Add(new RequirementSection(
                    RequirementScopeKind.SelectedState,
                    stateDescriptor.LogicType.Name,
                    stateDescriptor.DescriptorId.ToString(),
                    DescriptorEntries(stateDescriptor, catalog)));
            }

            if (analysisResult?.Succeeded == true)
            {
                sections.Add(BuildCompiledSection(analysisResult, catalog));
            }

            return sections;
        }

        internal static RequirementSection BuildCompiledSection(
            GraphAssetCompileResult result,
            GraphDescriptorCatalog catalog = null)
        {
            var entries = new List<RequirementEntry>();
            foreach (IntentRequirement requirement in result.Graph.IntentRequirements.Requirements)
            {
                entries.Add(new RequirementEntry(
                    RequirementEntryKind.Intent,
                    requirement.ValueType.Name,
                    requirement.IntentId.ToString()));
            }

            foreach (GraphOperationProvision provision in result.Graph.OperationProvides.Provides)
            {
                entries.Add(new RequirementEntry(
                    RequirementEntryKind.Operation,
                    provision.OperationType.Name,
                    provision.OperationId.ToString()));
            }

            foreach (ContextStateBlockRequirement block in result.Graph.ContextStateRequirements.Blocks)
            {
                entries.Add(new RequirementEntry(
                    RequirementEntryKind.ContextBlock,
                    ResolveContextBlockName(catalog, block.BlockId),
                    block.BlockId.ToString()));
            }

            return new RequirementSection(
                RequirementScopeKind.Compiled,
                null,
                null,
                entries);
        }

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

        #endregion

        #region Internal Logic

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

        private static string ResolveContextBlockName(
            GraphDescriptorCatalog catalog,
            StateBlockId blockId)
        {
            if (catalog == null)
            {
                return null;
            }

            IGraphStateSlotRegistration[] slots = catalog.GetSlots(blockId);
            if (slots == null || slots.Length == 0)
            {
                return null;
            }

            var names = new List<string>(slots.Length);
            foreach (IGraphStateSlotRegistration slot in slots)
            {
                if (slot?.ValueType != null)
                {
                    names.Add(slot.ValueType.Name);
                }
            }

            return names.Count > 0 ? string.Join(", ", names) : null;
        }

        private static IReadOnlyList<RequirementEntry> DescriptorEntries(
            StateDescriptor descriptor,
            GraphDescriptorCatalog catalog)
        {
            var entries = new List<RequirementEntry>();
            AddIdEntries(entries, RequirementEntryKind.Intent, descriptor.IntentRequirements, catalog);
            AddIdEntries(entries, RequirementEntryKind.Operation, descriptor.OperationProvides, catalog);
            AddIdEntries(entries, RequirementEntryKind.ContextBlock, descriptor.ContextStateRequirements, catalog);
            return entries;
        }

        private static void AddIdEntries<TId>(
            List<RequirementEntry> entries,
            RequirementEntryKind kind,
            IReadOnlyList<TId> ids,
            GraphDescriptorCatalog catalog)
        {
            for (int index = 0; ids != null && index < ids.Count; index++)
            {
                entries.Add(new RequirementEntry(
                    kind,
                    ResolveIdDisplayName(kind, ids[index], catalog),
                    ids[index].ToString()));
            }
        }

        private static string ResolveIdDisplayName<TId>(
            RequirementEntryKind kind,
            TId id,
            GraphDescriptorCatalog catalog)
        {
            if (catalog == null)
            {
                return null;
            }

            switch (kind)
            {
                case RequirementEntryKind.Intent:
                    return id is IntentId intentId &&
                           catalog.TryGetIntent(intentId, out IGraphIntentRegistration intent)
                        ? intent.ValueType?.Name
                        : null;
                case RequirementEntryKind.Operation:
                    return id is OperationId operationId &&
                           catalog.TryGetOperation(operationId, out IGraphOperationRegistration operation)
                        ? operation.OperationType?.Name
                        : null;
                case RequirementEntryKind.ContextBlock:
                    return id is StateBlockId blockId
                        ? ResolveContextBlockName(catalog, blockId)
                        : null;
                default:
                    return null;
            }
        }

        #endregion
    }

    /// <summary>区分要件列表中的输入、输出与图状态数据。</summary>
    internal enum RequirementEntryKind
    {
        Intent,
        Operation,
        ContextBlock
    }

    /// <summary>为详情面板保存一项编译要件的标识、来源及显示内容。</summary>
    internal readonly struct RequirementEntry
    {
        internal RequirementEntry(
            RequirementEntryKind kind,
            string typeName,
            string fullId)
        {
            Kind = kind;
            TypeName = typeName;
            FullId = fullId;
        }

        internal RequirementEntryKind Kind { get; }
        internal string TypeName { get; }
        internal string FullId { get; }
    }

    /// <summary>区分要件所属的图、图层或所选节点。</summary>
    internal enum RequirementScopeKind
    {
        SelectedState,
        Compiled
    }

    /// <summary>将同一范围的要件组成详情面板的一个分组。</summary>
    internal sealed class RequirementSection
    {
        internal RequirementSection(
            RequirementScopeKind scopeKind,
            string scopeLabel,
            string scopeFullId,
            IReadOnlyList<RequirementEntry> entries)
        {
            ScopeKind = scopeKind;
            ScopeLabel = scopeLabel;
            ScopeFullId = scopeFullId;
            Entries = entries;
        }

        internal RequirementScopeKind ScopeKind { get; }
        internal string ScopeLabel { get; }
        internal string ScopeFullId { get; }
        internal IReadOnlyList<RequirementEntry> Entries { get; }
    }
}
