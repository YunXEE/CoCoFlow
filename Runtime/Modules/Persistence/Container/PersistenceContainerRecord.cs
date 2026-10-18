using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    [Serializable]
    public sealed class PersistenceContainerRecord
    {
        [JsonProperty("typeId")] public string TypeId { get; set; } = string.Empty;
        [JsonProperty("id")] public string Id { get; set; } = string.Empty;
        [JsonProperty("data")] public JObject Data { get; set; } = new JObject();
    }
}
