using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using Newtonsoft.Json;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    public sealed class PersistenceContainerBridge : MonoBehaviour
    {
        [SerializeField] private string actorId = string.Empty;
        [SerializeField] private string containerId = string.Empty;
        [SerializeField] private bool reliable = true;

        private int _sequence;

        public string ActorId => ResolveActorId();
        public string ContainerId => ResolveContainerId();

        #region Public API

        public void SetActorId(string nextActorId)
        {
            actorId = nextActorId;
        }

        public void SetContainerId(string nextContainerId)
        {
            containerId = nextContainerId;
        }

        public void SetReliable(bool isReliable)
        {
            reliable = isReliable;
        }

        public bool RequestMaterializeContainer(string templateId, string targetContainerId = "")
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.MaterializeContainer,
                templateId = templateId,
                targetContainerId = string.IsNullOrEmpty(targetContainerId)
                    ? ResolveContainerId()
                    : targetContainerId
            });
        }

        public bool RequestAddEntry(PersistenceContainerEntryTemplate entry)
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.AddEntry,
                targetContainerId = ResolveContainerId(),
                entryTemplate = entry
            });
        }

        public bool RequestAddItem(string itemId, int count, int slotIndex = -1)
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.AddItem,
                targetContainerId = ResolveContainerId(),
                definitionId = itemId,
                count = count,
                slotIndex = slotIndex
            });
        }

        public bool RequestTransferItemTo(string targetContainerId, string itemId, int count)
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.TransferItem,
                sourceContainerId = ResolveContainerId(),
                targetContainerId = targetContainerId,
                definitionId = itemId,
                count = count
            });
        }

        public bool RequestGrantReward(string rewardId, string targetContainerId = "")
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.GrantReward,
                targetContainerId = string.IsNullOrEmpty(targetContainerId)
                    ? ResolveContainerId()
                    : targetContainerId,
                rewardId = rewardId
            });
        }

        public bool RequestQuestEvent(
            PersistenceQuestObjectiveType objectiveType,
            IEnumerable<string> targetTags,
            string itemId = "",
            string questBookContainerId = "")
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.ApplyQuestEvent,
                targetContainerId = string.IsNullOrEmpty(questBookContainerId)
                    ? PersistenceContainerStore.DefaultQuestBookContainerId
                    : questBookContainerId,
                questObjectiveType = objectiveType,
                tags = CopyTags(targetTags),
                definitionId = itemId
            });
        }

        public bool RequestEntityKilled(
            IEnumerable<string> entityTags,
            string questBookContainerId = "")
        {
            return RequestQuestEvent(
                PersistenceQuestObjectiveType.EntityKilled,
                entityTags,
                string.Empty,
                questBookContainerId);
        }

        public bool RequestItemDelivered(
            string itemId,
            IEnumerable<string> targetTags,
            string questBookContainerId = "")
        {
            return RequestQuestEvent(
                PersistenceQuestObjectiveType.ItemDelivered,
                targetTags,
                itemId,
                questBookContainerId);
        }

        public bool RequestSetFactBool(
            string factId,
            bool value,
            string factContainerId = "")
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.SetFactBool,
                targetContainerId = string.IsNullOrEmpty(factContainerId)
                    ? ResolveContainerId()
                    : factContainerId,
                definitionId = factId,
                boolValue = value
            });
        }

        public bool RequestSetEventState(
            string eventId,
            string state,
            string eventLogContainerId = "")
        {
            return PublishCommand(new PersistenceContainerCommandRequested
            {
                commandType = PersistenceContainerCommandType.SetEventState,
                targetContainerId = string.IsNullOrEmpty(eventLogContainerId)
                    ? ResolveContainerId()
                    : eventLogContainerId,
                definitionId = eventId,
                state = state
            });
        }

        #endregion

        #region Internal Logic

        private bool PublishCommand(PersistenceContainerCommandRequested command)
        {
            if (command == null) return false;

            PrepareCommand(command);
            string payload = JsonConvert.SerializeObject(command);
            var envelope = CoCoEventEnvelope.Create(
                PersistenceContainerEventIds.CommandRequested,
                command.actorId,
                command.sequence,
                command.tick,
                command.reliable,
                ResolveEnvelopeTarget(command),
                nameof(PersistenceContainerCommandRequested),
                payload);
            CoCoEventBus.PublishWithEnvelope(ref command, ref envelope);
            return true;
        }

        private void PrepareCommand(PersistenceContainerCommandRequested command)
        {
            if (string.IsNullOrEmpty(command.commandId))
            {
                command.commandId = Guid.NewGuid().ToString("N");
            }

            if (string.IsNullOrEmpty(command.actorId))
            {
                command.actorId = ResolveActorId();
            }

            if (command.sequence <= 0)
            {
                command.sequence = ++_sequence;
            }

            command.reliable = reliable;
        }

        private string ResolveContainerId()
        {
            return !string.IsNullOrEmpty(containerId)
                ? containerId
                : PersistenceContainerStore.DefaultPlayerInventoryContainerId;
        }

        private string ResolveActorId()
        {
            if (!string.IsNullOrEmpty(actorId)) return actorId;
            if (TryGetComponent<ICoCoStableEntityIdProvider>(out var stableIdProvider) &&
                !string.IsNullOrEmpty(stableIdProvider.StableEntityId))
            {
                return stableIdProvider.StableEntityId;
            }

            return gameObject != null ? gameObject.name : nameof(PersistenceContainerBridge);
        }

        private static string ResolveEnvelopeTarget(PersistenceContainerCommandRequested command)
        {
            return !string.IsNullOrEmpty(command.targetContainerId)
                ? command.targetContainerId
                : command.sourceContainerId;
        }

        private static List<string> CopyTags(IEnumerable<string> tags)
        {
            var result = new List<string>();
            if (tags == null) return result;

            foreach (string tag in tags)
            {
                result.Add(tag);
            }

            return result;
        }

        #endregion
    }
}
