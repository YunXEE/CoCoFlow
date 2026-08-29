using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    [Serializable]
    public struct PersistenceVector3Data
    {
        public float x;
        public float y;
        public float z;
    }

    [Serializable]
    public struct PersistenceQuaternionData
    {
        public float x;
        public float y;
        public float z;
        public float w;
    }

    [Serializable]
    public sealed class PersistenceContextRecord
    {
        internal const string StateGraphContextType = "CoCoFlow.StateGraph.ContextFrame";

        public string stableEntityId = string.Empty;
        public string contextType = string.Empty;
        public string ownerId = string.Empty;
        public string entityTypeId = string.Empty;
        public string prefabKey = string.Empty;
        public int lifecycleState;
        public int semanticStateId;
        public int actionStateId;
        public int lastEventSequence;
        public Dictionary<string, string> StringFacts = new Dictionary<string, string>();
        public Dictionary<string, int> IntFacts = new Dictionary<string, int>();
        public Dictionary<string, float> FloatFacts = new Dictionary<string, float>();
        public Dictionary<string, bool> BoolFacts = new Dictionary<string, bool>();
        public Dictionary<string, PersistenceVector3Data> Vector3Facts = new Dictionary<string, PersistenceVector3Data>();
        public Dictionary<string, PersistenceQuaternionData> QuaternionFacts =
            new Dictionary<string, PersistenceQuaternionData>();

        [JsonProperty("stateGraphContextPayload", NullValueHandling = NullValueHandling.Ignore)]
        private byte[] stateGraphContextPayload;

        internal bool IsStateGraphContextRecord =>
            string.Equals(contextType, StateGraphContextType, StringComparison.Ordinal);

        internal bool HasStateGraphContextPayload => stateGraphContextPayload != null;
        internal bool HasUsableStateGraphContextPayload =>
            stateGraphContextPayload != null && stateGraphContextPayload.Length > 0;

        internal static PersistenceContextRecord CreateStateGraphContextRecord(
            string stableEntityId,
            string prefabKey,
            byte[] payload)
        {
            if (string.IsNullOrEmpty(stableEntityId))
            {
                throw new ArgumentException(
                    "A StateGraph persistence record requires a stable entity id.",
                    nameof(stableEntityId));
            }

            if (payload == null || payload.Length == 0)
            {
                throw new ArgumentException(
                    "A StateGraph persistence record requires a non-empty payload.",
                    nameof(payload));
            }

            return new PersistenceContextRecord
            {
                stableEntityId = stableEntityId,
                contextType = StateGraphContextType,
                prefabKey = prefabKey ?? string.Empty,
                stateGraphContextPayload = payload
            };
        }

        internal bool TryGetStateGraphContextPayload(out byte[] payload)
        {
            if (stateGraphContextPayload == null || stateGraphContextPayload.Length == 0)
            {
                payload = null;
                return false;
            }

            payload = stateGraphContextPayload;
            return true;
        }
    }
}
