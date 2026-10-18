using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    [Serializable, JsonConverter(typeof(PersistenceContainerSectionJsonConverter))]
    public sealed class PersistenceContainerSection
    {
        public const int CurrentFormatVersion = 1;
        public int FormatVersion { get; set; } = CurrentFormatVersion;
        public List<PersistenceContainerRecord> Records { get; set; } = new List<PersistenceContainerRecord>();
    }
}
