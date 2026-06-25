using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Events;

namespace CoCoFlow.Runtime.Addon.NetworkSamples
{
    public sealed class PersistenceNetworkContainerEventBridge : MonoBehaviour
    {
        [SerializeField] private bool forwardLocalCommands = true;
        [SerializeField] private bool forwardLocalResults;
        [SerializeField] private UnityEvent<string> commandJsonReady = new UnityEvent<string>();
        [SerializeField] private UnityEvent<string> appliedJsonReady = new UnityEvent<string>();
        [SerializeField] private UnityEvent<string> rejectedJsonReady = new UnityEvent<string>();

        private readonly EventAgent eventAgent = new EventAgent();
        private bool suppressForward;

        public UnityEvent<string> CommandJsonReady => commandJsonReady;
        public UnityEvent<string> AppliedJsonReady => appliedJsonReady;
        public UnityEvent<string> RejectedJsonReady => rejectedJsonReady;

        #region Public API

        public void ReceiveRemoteCommandJson(string commandJson)
        {
            var command = Deserialize<PersistenceContainerCommandRequested>(commandJson);
            if (command == null) return;

            PublishRemote(
                command,
                PersistenceContainerEventIds.CommandRequested,
                nameof(PersistenceContainerCommandRequested),
                commandJson,
                command.actorId,
                command.targetContainerId,
                command.sequence,
                command.tick,
                command.reliable);
        }

        public void ReceiveRemoteAppliedJson(string appliedJson)
        {
            var applied = Deserialize<PersistenceContainerCommandApplied>(appliedJson);
            if (applied == null) return;

            PublishRemote(
                applied,
                PersistenceContainerEventIds.CommandApplied,
                nameof(PersistenceContainerCommandApplied),
                appliedJson,
                applied.actorId,
                applied.targetContainerId,
                applied.sequence,
                applied.tick,
                true);
        }

        public void ReceiveRemoteRejectedJson(string rejectedJson)
        {
            var rejected = Deserialize<PersistenceContainerCommandRejected>(rejectedJson);
            if (rejected == null) return;

            PublishRemote(
                rejected,
                PersistenceContainerEventIds.CommandRejected,
                nameof(PersistenceContainerCommandRejected),
                rejectedJson,
                rejected.actorId,
                rejected.targetContainerId,
                rejected.sequence,
                rejected.tick,
                true);
        }

        #endregion

        #region Internal Logic

        private void OnEnable()
        {
            eventAgent.Subscribe<PersistenceContainerCommandRequested>(OnCommandRequested);
            eventAgent.Subscribe<PersistenceContainerCommandApplied>(OnCommandApplied);
            eventAgent.Subscribe<PersistenceContainerCommandRejected>(OnCommandRejected);
        }

        private void OnDisable()
        {
            eventAgent.UnsubscribeAll();
        }

        private void OnCommandRequested(ref PersistenceContainerCommandRequested command)
        {
            if (suppressForward || !forwardLocalCommands || command == null) return;
            commandJsonReady.Invoke(JsonConvert.SerializeObject(command));
        }

        private void OnCommandApplied(ref PersistenceContainerCommandApplied applied)
        {
            if (suppressForward || !forwardLocalResults || applied == null) return;
            appliedJsonReady.Invoke(JsonConvert.SerializeObject(applied));
        }

        private void OnCommandRejected(ref PersistenceContainerCommandRejected rejected)
        {
            if (suppressForward || !forwardLocalResults || rejected == null) return;
            rejectedJsonReady.Invoke(JsonConvert.SerializeObject(rejected));
        }

        private void PublishRemote<T>(
            T eventData,
            string eventTypeId,
            string payloadTypeId,
            string payload,
            string sourceEntityId,
            string targetEntityId,
            int sequence,
            int tick,
            bool reliable)
        {
            suppressForward = true;
            try
            {
                string resolvedSource = string.IsNullOrEmpty(sourceEntityId)
                    ? nameof(PersistenceNetworkContainerEventBridge)
                    : sourceEntityId;
                var envelope = CoCoEventEnvelope.Create(
                    eventTypeId,
                    resolvedSource,
                    sequence > 0 ? sequence : 1,
                    tick,
                    reliable,
                    targetEntityId,
                    payloadTypeId,
                    payload);
                CoCoEventBus.PublishWithEnvelope(ref eventData, ref envelope);
            }
            finally
            {
                suppressForward = false;
            }
        }

        private static T Deserialize<T>(string json) where T : class
        {
            return string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<T>(json);
        }

        #endregion
    }
}
