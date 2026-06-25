using System;
using System.Collections.Generic;

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
    }
}
