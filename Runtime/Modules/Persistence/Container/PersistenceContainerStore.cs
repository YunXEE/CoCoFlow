using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using Newtonsoft.Json;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    public sealed class PersistenceContainerStore : MonoBehaviour
    {
        public const string DefaultPlayerInventoryContainerId = "container.player.inventory";
        public const string DefaultQuestBookContainerId = "container.player.questbook";
        public const string DefaultEventLogContainerId = "container.world.events";
        public const string DefaultWorldFactContainerId = "container.world.facts";

        public const string DefaultItemStorageDefinitionId = "container.definition.item_storage";
        public const string DefaultQuestBookDefinitionId = "container.definition.quest_book";
        public const string DefaultEventLogDefinitionId = "container.definition.event_log";
        public const string DefaultFactSetDefinitionId = "container.definition.fact_set";

        [SerializeField] private PersistenceContainerCatalog catalog;
        [SerializeField] private PersistenceContainerSection section = new PersistenceContainerSection();

        private readonly EventAgent _eventAgent = new EventAgent();
        private static PersistenceContainerStore _activeStore;

        public static PersistenceContainerStore ActiveStore => _activeStore;
        public PersistenceContainerCatalog Catalog => catalog;
        public PersistenceContainerSection Section => section;

        #region Public API

        public static PersistenceContainerSection CaptureActiveSection()
        {
            return _activeStore != null ? _activeStore.section : new PersistenceContainerSection();
        }

        public static void ApplyActiveSection(PersistenceContainerSection nextSection)
        {
            if (_activeStore != null && nextSection != null)
            {
                _activeStore.section = nextSection;
            }
        }

        public void SetCatalog(PersistenceContainerCatalog nextCatalog)
        {
            catalog = nextCatalog;
        }

        public void MaterializeStartupContainers()
        {
            if (catalog == null) return;

            for (int i = 0; i < catalog.containerTemplates.Count; i++)
            {
                var template = catalog.containerTemplates[i];
                if (template != null && template.materializeOnNewGame)
                {
                    MaterializeContainer(template.templateId);
                }
            }
        }

        public PersistenceContainerRecord MaterializeContainer(
            string templateId,
            string containerIdOverride = "")
        {
            if (catalog == null || string.IsNullOrEmpty(templateId)) return null;

            var template = catalog.FindTemplate(templateId);
            if (template == null) return null;

            string containerId = ResolveMaterializedContainerId(template, containerIdOverride);
            if (section.TryGetRecord(containerId, out var existing))
            {
                return existing;
            }

            var definition = catalog.FindContainerDefinition(template.definitionId);
            var containerType = definition != null
                ? definition.containerType
                : PersistenceContainerType.ItemStorage;
            var record = section.GetOrAddRecord(containerId, template.definitionId, containerType);
            record.templateId = template.templateId;
            record.ownerId = template.ownerId;
            record.authorityId = template.authorityId;
            record.scope = template.scope;
            record.materialized = true;
            CopyTags(template.tags, record.tags);

            for (int i = 0; i < template.entries.Count; i++)
            {
                AddEntryToContainer(record.containerId, template.entries[i]);
            }

            RollLootTables(record.containerId, template);
            return record;
        }

        public PersistenceContainerRecord EnsureContainer(
            string containerId,
            string definitionId,
            PersistenceContainerType containerType,
            string ownerId = "",
            string authorityId = "",
            string scope = "")
        {
            var record = section.GetOrAddRecord(containerId, definitionId, containerType);
            if (!string.IsNullOrEmpty(ownerId)) record.ownerId = ownerId;
            if (!string.IsNullOrEmpty(authorityId)) record.authorityId = authorityId;
            if (!string.IsNullOrEmpty(scope)) record.scope = scope;
            return record;
        }

        public bool TryGetContainer(string containerId, out PersistenceContainerRecord record)
        {
            return section.TryGetRecord(containerId, out record);
        }

        public bool AddEntryToContainer(
            string containerId,
            PersistenceContainerEntryTemplate entryTemplate)
        {
            if (entryTemplate == null || string.IsNullOrEmpty(containerId)) return false;

            return entryTemplate.entryType switch
            {
                PersistenceContainerEntryType.Item => AddItemToContainer(
                    containerId,
                    entryTemplate.definitionId,
                    entryTemplate.count,
                    entryTemplate.slotIndex),
                PersistenceContainerEntryType.QuestProgress => ActivateQuest(
                    containerId,
                    entryTemplate.definitionId,
                    ParseQuestStatus(entryTemplate.state)),
                PersistenceContainerEntryType.EventState => SetEventState(
                    containerId,
                    entryTemplate.definitionId,
                    entryTemplate.state),
                PersistenceContainerEntryType.Fact => SetFactBool(
                    containerId,
                    entryTemplate.definitionId,
                    entryTemplate.boolValue),
                _ => false
            };
        }

        public bool AddItemToContainer(
            string containerId,
            string itemId,
            int count,
            int slotIndex = -1)
        {
            if (string.IsNullOrEmpty(containerId) ||
                string.IsNullOrEmpty(itemId) ||
                count <= 0)
            {
                return false;
            }

            var container = EnsureContainer(
                containerId,
                DefaultItemStorageDefinitionId,
                PersistenceContainerType.ItemStorage);
            var entryTemplate = new PersistenceContainerEntryTemplate
            {
                entryType = PersistenceContainerEntryType.Item,
                definitionId = itemId,
                count = count,
                slotIndex = slotIndex
            };

            var itemDefinition = catalog != null ? catalog.FindItem(itemId) : null;
            if (!CanAcceptItemCount(container, entryTemplate, itemDefinition)) return false;

            if (IsStackable(itemDefinition))
            {
                int maxStack = ResolveMaxStack(itemDefinition);
                while (count > 0)
                {
                    var stack = FindOpenItemStack(container, itemId, slotIndex, maxStack);
                    if (stack == null) break;

                    int accepted = Mathf.Min(maxStack - stack.count, count);
                    stack.count += accepted;
                    stack.revision++;
                    container.revision++;
                    count -= accepted;
                }
            }

            while (count > 0)
            {
                int nextCount = IsStackable(itemDefinition)
                    ? Mathf.Min(ResolveMaxStack(itemDefinition), count)
                    : 1;
                entryTemplate.count = nextCount;
                container.entries.Add(CreateEntry(entryTemplate));
                container.revision++;
                count -= nextCount;
            }

            return true;
        }

        public bool RemoveItemFromContainer(
            string containerId,
            string itemId,
            int count)
        {
            if (string.IsNullOrEmpty(containerId) ||
                string.IsNullOrEmpty(itemId) ||
                count <= 0)
            {
                return false;
            }

            if (!section.TryGetRecord(containerId, out var container)) return false;
            if (GetItemCount(containerId, itemId) < count) return false;

            for (int i = container.entries.Count - 1; i >= 0 && count > 0; i--)
            {
                var entry = container.entries[i];
                if (entry == null ||
                    entry.entryType != PersistenceContainerEntryType.Item ||
                    entry.definitionId != itemId)
                {
                    continue;
                }

                int removed = Mathf.Min(entry.count, count);
                entry.count -= removed;
                entry.revision++;
                container.revision++;
                count -= removed;

                if (entry.count <= 0)
                {
                    container.entries.RemoveAt(i);
                }
            }

            return count == 0;
        }

        public int GetItemCount(string containerId, string itemId)
        {
            if (!section.TryGetRecord(containerId, out var container)) return 0;

            int count = 0;
            for (int i = 0; i < container.entries.Count; i++)
            {
                var entry = container.entries[i];
                if (entry != null &&
                    entry.entryType == PersistenceContainerEntryType.Item &&
                    entry.definitionId == itemId)
                {
                    count += entry.count;
                }
            }

            return count;
        }

        public bool TransferItem(
            string sourceContainerId,
            string targetContainerId,
            string itemId,
            int count)
        {
            if (string.IsNullOrEmpty(sourceContainerId) ||
                string.IsNullOrEmpty(targetContainerId) ||
                string.IsNullOrEmpty(itemId) ||
                count <= 0)
            {
                return false;
            }

            if (!section.TryGetRecord(sourceContainerId, out var source) ||
                !section.TryGetRecord(targetContainerId, out var target) ||
                !CanTransferBetween(source, target) ||
                GetItemCount(sourceContainerId, itemId) < count)
            {
                return false;
            }

            if (!RemoveItemFromContainer(sourceContainerId, itemId, count)) return false;
            if (AddItemToContainer(targetContainerId, itemId, count)) return true;

            AddItemToContainer(sourceContainerId, itemId, count);
            return false;
        }

        public bool ActivateQuest(
            string questBookContainerId,
            string questId,
            PersistenceQuestStatus initialStatus = PersistenceQuestStatus.Active)
        {
            if (string.IsNullOrEmpty(questBookContainerId) || string.IsNullOrEmpty(questId)) return false;

            var progress = GetOrAddQuestProgress(questBookContainerId, questId);
            if (progress == null) return false;
            if (progress.status == PersistenceQuestStatus.Inactive)
            {
                progress.status = initialStatus;
            }

            return true;
        }

        public PersistenceQuestProgressRecord GetOrAddQuestProgress(
            string questBookContainerId,
            string questId)
        {
            var questBook = EnsureContainer(
                questBookContainerId,
                DefaultQuestBookDefinitionId,
                PersistenceContainerType.QuestBook);
            var entry = FindEntry(questBook, PersistenceContainerEntryType.QuestProgress, questId);
            if (entry == null)
            {
                var template = new PersistenceContainerEntryTemplate
                {
                    entryType = PersistenceContainerEntryType.QuestProgress,
                    definitionId = questId
                };
                if (!CanAcceptEntry(questBook, template)) return null;

                entry = CreateEntry(template);
                questBook.entries.Add(entry);
                questBook.revision++;
            }

            entry.questProgress ??= new PersistenceQuestProgressRecord { questId = questId };
            entry.questProgress.questId = questId;
            return entry.questProgress;
        }

        public bool ApplyQuestEvent(
            string questBookContainerId,
            PersistenceQuestObjectiveType objectiveType,
            IReadOnlyList<string> targetTags,
            string itemId = "")
        {
            if (catalog == null ||
                string.IsNullOrEmpty(questBookContainerId) ||
                !section.TryGetRecord(questBookContainerId, out var questBook))
            {
                return false;
            }

            bool advanced = false;
            for (int i = 0; i < questBook.entries.Count; i++)
            {
                var entry = questBook.entries[i];
                var progress = entry?.questProgress;
                if (entry == null ||
                    entry.entryType != PersistenceContainerEntryType.QuestProgress ||
                    progress == null ||
                    !progress.IsActive)
                {
                    continue;
                }

                var quest = catalog.FindQuest(progress.questId);
                if (quest == null ||
                    progress.currentStepIndex < 0 ||
                    progress.currentStepIndex >= quest.steps.Count)
                {
                    continue;
                }

                var step = quest.steps[progress.currentStepIndex];
                if (step == null || step.objectiveType != objectiveType) continue;
                if (!string.IsNullOrEmpty(step.itemId) && step.itemId != itemId) continue;
                if (!string.IsNullOrEmpty(step.targetTag) &&
                    !Contains(targetTags, step.targetTag))
                {
                    continue;
                }

                progress.currentCount++;
                entry.revision++;
                questBook.revision++;
                advanced = true;
                if (progress.currentCount >= Mathf.Max(1, step.requiredCount))
                {
                    progress.currentStepIndex++;
                    progress.currentCount = 0;
                    progress.status = progress.currentStepIndex >= quest.steps.Count
                        ? PersistenceQuestStatus.Completed
                        : PersistenceQuestStatus.Active;
                }
            }

            return advanced;
        }

        public bool SetEventState(
            string eventLogContainerId,
            string eventId,
            string state)
        {
            if (string.IsNullOrEmpty(eventLogContainerId) || string.IsNullOrEmpty(eventId)) return false;

            var eventLog = EnsureContainer(
                eventLogContainerId,
                DefaultEventLogDefinitionId,
                PersistenceContainerType.EventLog);
            var template = new PersistenceContainerEntryTemplate
            {
                entryType = PersistenceContainerEntryType.EventState,
                definitionId = eventId,
                state = state
            };
            if (!CanAcceptEntry(eventLog, template)) return false;

            var entry = FindEntry(eventLog, PersistenceContainerEntryType.EventState, eventId);
            if (entry == null)
            {
                entry = CreateEntry(template);
                eventLog.entries.Add(entry);
            }

            entry.state = state;
            entry.revision++;
            eventLog.revision++;
            return true;
        }

        public bool SetFactBool(
            string factContainerId,
            string factId,
            bool value)
        {
            if (string.IsNullOrEmpty(factContainerId) || string.IsNullOrEmpty(factId)) return false;

            var factSet = EnsureContainer(
                factContainerId,
                DefaultFactSetDefinitionId,
                PersistenceContainerType.FactSet);
            var template = new PersistenceContainerEntryTemplate
            {
                entryType = PersistenceContainerEntryType.Fact,
                definitionId = factId,
                boolValue = value
            };
            if (!CanAcceptEntry(factSet, template)) return false;

            var entry = FindEntry(factSet, PersistenceContainerEntryType.Fact, factId);
            if (entry == null)
            {
                entry = CreateEntry(template);
                factSet.entries.Add(entry);
            }

            entry.BoolValues["value"] = value;
            entry.revision++;
            factSet.revision++;
            return true;
        }

        public bool GetFactBool(
            string factContainerId,
            string factId,
            bool defaultValue = false)
        {
            if (!section.TryGetRecord(factContainerId, out var factSet)) return defaultValue;
            var entry = FindEntry(factSet, PersistenceContainerEntryType.Fact, factId);
            if (entry == null) return defaultValue;
            return entry.BoolValues.TryGetValue("value", out bool value) ? value : defaultValue;
        }

        public bool GrantRewardToContainer(
            string containerId,
            string rewardId)
        {
            var reward = catalog != null ? catalog.FindReward(rewardId) : null;
            if (reward == null || string.IsNullOrEmpty(containerId)) return false;

            bool granted = false;
            for (int i = 0; i < reward.entries.Count; i++)
            {
                granted |= AddEntryToContainer(containerId, reward.entries[i]);
            }

            return granted;
        }

        public bool ApplyCommand(
            PersistenceContainerCommandRequested command,
            out string reason)
        {
            reason = string.Empty;
            if (command == null)
            {
                reason = "Command is null.";
                return false;
            }

            bool applied = command.commandType switch
            {
                PersistenceContainerCommandType.MaterializeContainer => MaterializeContainer(
                    command.templateId,
                    command.targetContainerId) != null,
                PersistenceContainerCommandType.AddEntry => AddEntryToContainer(
                    ResolveCommandTargetContainer(command),
                    command.entryTemplate),
                PersistenceContainerCommandType.AddItem => AddItemToContainer(
                    ResolveCommandTargetContainer(command),
                    command.definitionId,
                    command.count,
                    command.slotIndex),
                PersistenceContainerCommandType.TransferItem => TransferItem(
                    command.sourceContainerId,
                    command.targetContainerId,
                    command.definitionId,
                    command.count),
                PersistenceContainerCommandType.GrantReward => GrantRewardToContainer(
                    ResolveCommandTargetContainer(command),
                    command.rewardId),
                PersistenceContainerCommandType.ApplyQuestEvent => ApplyQuestEvent(
                    ResolveCommandTargetContainer(command),
                    command.questObjectiveType,
                    command.tags,
                    command.definitionId),
                PersistenceContainerCommandType.SetEventState => SetEventState(
                    ResolveCommandTargetContainer(command),
                    command.definitionId,
                    command.state),
                PersistenceContainerCommandType.SetFactBool => SetFactBool(
                    ResolveCommandTargetContainer(command),
                    command.definitionId,
                    command.boolValue),
                _ => false
            };

            if (!applied)
            {
                reason = $"Command {command.commandType} was rejected by the container store.";
            }

            return applied;
        }

        #endregion

        #region Internal Logic

        private void OnEnable()
        {
            _activeStore = this;
            _eventAgent.Subscribe<PersistenceContainerCommandRequested>(OnCommandRequested);
            if (PersistenceSession.PendingDocument?.containerSection != null)
            {
                section = PersistenceSession.PendingDocument.containerSection;
            }
        }

        private void OnDisable()
        {
            _eventAgent.UnsubscribeAll();
            if (ReferenceEquals(_activeStore, this))
            {
                _activeStore = null;
            }
        }

        private void OnCommandRequested(ref PersistenceContainerCommandRequested command)
        {
            if (!ReferenceEquals(_activeStore, this)) return;
            if (command == null) return;

            bool applied = ApplyCommand(command, out string reason);
            if (applied)
            {
                PublishCommandApplied(command);
            }
            else
            {
                PublishCommandRejected(command, reason);
            }
        }

        private static void PublishCommandApplied(PersistenceContainerCommandRequested command)
        {
            var applied = new PersistenceContainerCommandApplied
            {
                commandId = command.commandId,
                commandType = command.commandType,
                actorId = command.actorId,
                sourceContainerId = command.sourceContainerId,
                targetContainerId = command.targetContainerId,
                sequence = command.sequence,
                tick = command.tick
            };
            AddChangedContainer(applied.changedContainerIds, command.sourceContainerId);
            AddChangedContainer(applied.changedContainerIds, command.targetContainerId);

            string sourceEntityId = ResolveEnvelopeSource(command.actorId);
            string payload = JsonConvert.SerializeObject(applied);
            var envelope = CoCoEventEnvelope.Create(
                PersistenceContainerEventIds.CommandApplied,
                sourceEntityId,
                command.sequence,
                command.tick,
                command.reliable,
                ResolveEnvelopeTarget(command),
                nameof(PersistenceContainerCommandApplied),
                payload);
            CoCoEventBus.PublishWithEnvelope(ref applied, ref envelope);
        }

        private static void PublishCommandRejected(
            PersistenceContainerCommandRequested command,
            string reason)
        {
            var rejected = new PersistenceContainerCommandRejected
            {
                commandId = command.commandId,
                commandType = command.commandType,
                actorId = command.actorId,
                sourceContainerId = command.sourceContainerId,
                targetContainerId = command.targetContainerId,
                sequence = command.sequence,
                tick = command.tick,
                reason = reason
            };

            string sourceEntityId = ResolveEnvelopeSource(command.actorId);
            string payload = JsonConvert.SerializeObject(rejected);
            var envelope = CoCoEventEnvelope.Create(
                PersistenceContainerEventIds.CommandRejected,
                sourceEntityId,
                command.sequence,
                command.tick,
                command.reliable,
                ResolveEnvelopeTarget(command),
                nameof(PersistenceContainerCommandRejected),
                payload);
            CoCoEventBus.PublishWithEnvelope(ref rejected, ref envelope);
        }

        private static string ResolveCommandTargetContainer(PersistenceContainerCommandRequested command)
        {
            return !string.IsNullOrEmpty(command.targetContainerId)
                ? command.targetContainerId
                : command.sourceContainerId;
        }

        private static string ResolveEnvelopeSource(string actorId)
        {
            return !string.IsNullOrEmpty(actorId)
                ? actorId
                : nameof(PersistenceContainerStore);
        }

        private static string ResolveEnvelopeTarget(PersistenceContainerCommandRequested command)
        {
            return !string.IsNullOrEmpty(command.targetContainerId)
                ? command.targetContainerId
                : command.sourceContainerId;
        }

        private static void AddChangedContainer(List<string> containerIds, string containerId)
        {
            if (string.IsNullOrEmpty(containerId) || containerIds.Contains(containerId)) return;
            containerIds.Add(containerId);
        }

        private void RollLootTables(string containerId, PersistenceContainerTemplate template)
        {
            if (catalog == null || template == null) return;

            var random = new System.Random(template.seed == 0 ? template.templateId.GetHashCode() : template.seed);
            for (int tableIndex = 0; tableIndex < template.lootTableIds.Count; tableIndex++)
            {
                var table = catalog.FindLootTable(template.lootTableIds[tableIndex]);
                if (table == null || table.weightedEntries.Count == 0) continue;

                for (int roll = 0; roll < Mathf.Max(1, table.rolls); roll++)
                {
                    var selected = SelectWeightedEntry(table.weightedEntries, random);
                    if (selected != null)
                    {
                        AddEntryToContainer(containerId, selected.entry);
                    }
                }
            }
        }

        private bool CanAcceptEntry(
            PersistenceContainerRecord container,
            PersistenceContainerEntryTemplate entry)
        {
            if (container == null || entry == null) return false;

            var definition = ResolveDefinition(container);
            PersistenceItemDefinition itemDefinition = null;
            if (entry.entryType == PersistenceContainerEntryType.Item && catalog != null)
            {
                itemDefinition = catalog.FindItem(entry.definitionId);
            }

            if (!AcceptsEntryType(definition, container.containerType, entry.entryType))
            {
                return false;
            }

            if (definition.capacity >= 0 && container.entries.Count >= definition.capacity)
            {
                if (entry.entryType == PersistenceContainerEntryType.Item)
                {
                    if (!CanStackIntoExistingItemEntry(container, entry, itemDefinition)) return false;
                }
                else
                {
                    var existing = FindEntry(container, entry.entryType, entry.definitionId);
                    if (existing == null) return false;
                }
            }

            if (entry.entryType == PersistenceContainerEntryType.Item &&
                catalog != null)
            {
                if (!definition.acceptedItemTags.Matches(itemDefinition?.tags))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanAcceptItemCount(
            PersistenceContainerRecord container,
            PersistenceContainerEntryTemplate entry,
            PersistenceItemDefinition itemDefinition)
        {
            if (!CanAcceptEntry(container, entry)) return false;

            var definition = ResolveDefinition(container);
            if (definition.capacity < 0) return true;

            int remainingCount = entry.count;
            if (IsStackable(itemDefinition))
            {
                int maxStack = ResolveMaxStack(itemDefinition);
                for (int i = 0; i < container.entries.Count && remainingCount > 0; i++)
                {
                    var existingEntry = container.entries[i];
                    if (existingEntry == null ||
                        existingEntry.entryType != PersistenceContainerEntryType.Item ||
                        existingEntry.definitionId != entry.definitionId ||
                        (entry.slotIndex >= 0 && existingEntry.slotIndex != entry.slotIndex))
                    {
                        continue;
                    }

                    remainingCount -= Mathf.Max(0, maxStack - existingEntry.count);
                }
            }

            if (remainingCount <= 0) return true;

            int requiredNewEntries = IsStackable(itemDefinition)
                ? Mathf.CeilToInt((float)remainingCount / ResolveMaxStack(itemDefinition))
                : remainingCount;
            return container.entries.Count + requiredNewEntries <= definition.capacity;
        }

        private bool CanTransferBetween(
            PersistenceContainerRecord source,
            PersistenceContainerRecord target)
        {
            var sourceDefinition = ResolveDefinition(source);
            var targetDefinition = ResolveDefinition(target);
            return AllowsTransferTo(sourceDefinition, source.containerType, target.containerType) &&
                   AllowsTransferTo(targetDefinition, target.containerType, source.containerType);
        }

        private PersistenceContainerDefinition ResolveDefinition(PersistenceContainerRecord container)
        {
            var definition = catalog != null
                ? catalog.FindContainerDefinition(container.definitionId)
                : null;
            if (definition != null) return definition;

            return CreateDefaultDefinition(container.containerType, container.definitionId);
        }

        private static PersistenceContainerDefinition CreateDefaultDefinition(
            PersistenceContainerType containerType,
            string definitionId)
        {
            var definition = new PersistenceContainerDefinition
            {
                definitionId = definitionId,
                containerType = containerType,
                sameTypeTransfersOnly = true
            };
            definition.acceptedEntryTypes.Add(DefaultEntryTypeFor(containerType));
            return definition;
        }

        private static bool AcceptsEntryType(
            PersistenceContainerDefinition definition,
            PersistenceContainerType containerType,
            PersistenceContainerEntryType entryType)
        {
            if (definition.acceptedEntryTypes.Count == 0)
            {
                return DefaultEntryTypeFor(containerType) == entryType;
            }

            return definition.acceptedEntryTypes.Contains(entryType);
        }

        private static bool AllowsTransferTo(
            PersistenceContainerDefinition definition,
            PersistenceContainerType sourceType,
            PersistenceContainerType targetType)
        {
            if (definition.transferableContainerTypes.Count > 0)
            {
                return definition.transferableContainerTypes.Contains(targetType);
            }

            return !definition.sameTypeTransfersOnly || sourceType == targetType;
        }

        private static bool CanStackIntoExistingItemEntry(
            PersistenceContainerRecord container,
            PersistenceContainerEntryTemplate entry,
            PersistenceItemDefinition itemDefinition)
        {
            if (!IsStackable(itemDefinition)) return false;
            return FindOpenItemStack(
                container,
                entry.definitionId,
                entry.slotIndex,
                ResolveMaxStack(itemDefinition)) != null;
        }

        private static bool IsStackable(PersistenceItemDefinition itemDefinition)
        {
            return itemDefinition == null || itemDefinition.stackable;
        }

        private static int ResolveMaxStack(PersistenceItemDefinition itemDefinition)
        {
            return itemDefinition != null ? Mathf.Max(1, itemDefinition.maxStack) : int.MaxValue;
        }

        private static PersistenceContainerEntryRecord FindEntry(
            PersistenceContainerRecord container,
            PersistenceContainerEntryType entryType,
            string definitionId)
        {
            if (container == null) return null;

            for (int i = 0; i < container.entries.Count; i++)
            {
                var entry = container.entries[i];
                if (entry != null &&
                    entry.entryType == entryType &&
                    entry.definitionId == definitionId)
                {
                    return entry;
                }
            }

            return null;
        }

        private static PersistenceContainerEntryRecord FindOpenItemStack(
            PersistenceContainerRecord container,
            string itemId,
            int slotIndex,
            int maxStack)
        {
            if (container == null) return null;

            for (int i = 0; i < container.entries.Count; i++)
            {
                var entry = container.entries[i];
                if (entry != null &&
                    entry.entryType == PersistenceContainerEntryType.Item &&
                    entry.definitionId == itemId &&
                    entry.count < maxStack &&
                    (slotIndex < 0 || entry.slotIndex == slotIndex))
                {
                    return entry;
                }
            }

            return null;
        }

        private static PersistenceContainerEntryRecord CreateEntry(
            PersistenceContainerEntryTemplate template)
        {
            var entry = new PersistenceContainerEntryRecord
            {
                entryId = Guid.NewGuid().ToString("N"),
                entryType = template.entryType,
                definitionId = template.definitionId,
                count = Mathf.Max(1, template.count),
                slotIndex = template.slotIndex,
                state = template.state
            };
            CopyTags(template.tags, entry.tags);

            if (template.entryType == PersistenceContainerEntryType.QuestProgress)
            {
                entry.questProgress = new PersistenceQuestProgressRecord
                {
                    questId = template.definitionId,
                    status = ParseQuestStatus(template.state)
                };
            }

            entry.StringValues["value"] = template.stringValue;
            entry.IntValues["value"] = template.intValue;
            entry.FloatValues["value"] = template.floatValue;
            entry.BoolValues["value"] = template.boolValue;
            return entry;
        }

        private static PersistenceWeightedContainerEntry SelectWeightedEntry(
            IReadOnlyList<PersistenceWeightedContainerEntry> entries,
            System.Random random)
        {
            int total = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                total += Mathf.Max(0, entries[i]?.weight ?? 0);
            }

            if (total <= 0) return null;

            int cursor = random.Next(0, total);
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                if (entry == null) continue;

                cursor -= Mathf.Max(0, entry.weight);
                if (cursor < 0) return entry;
            }

            return null;
        }

        private static PersistenceContainerEntryType DefaultEntryTypeFor(PersistenceContainerType containerType)
        {
            return containerType switch
            {
                PersistenceContainerType.QuestBook => PersistenceContainerEntryType.QuestProgress,
                PersistenceContainerType.EventLog => PersistenceContainerEntryType.EventState,
                PersistenceContainerType.FactSet => PersistenceContainerEntryType.Fact,
                _ => PersistenceContainerEntryType.Item
            };
        }

        private static PersistenceQuestStatus ParseQuestStatus(string state)
        {
            return Enum.TryParse(state, out PersistenceQuestStatus status)
                ? status
                : PersistenceQuestStatus.Inactive;
        }

        private static string ResolveMaterializedContainerId(
            PersistenceContainerTemplate template,
            string containerIdOverride)
        {
            if (!string.IsNullOrEmpty(containerIdOverride)) return containerIdOverride;
            if (!string.IsNullOrEmpty(template.defaultContainerId)) return template.defaultContainerId;
            return template.templateId;
        }

        private static bool Contains(IReadOnlyList<string> tags, string tag)
        {
            if (tags == null) return false;
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] == tag) return true;
            }

            return false;
        }

        private static void CopyTags(IReadOnlyList<string> source, List<string> target)
        {
            target.Clear();
            if (source == null) return;

            for (int i = 0; i < source.Count; i++)
            {
                target.Add(source[i]);
            }
        }

        #endregion
    }
}
