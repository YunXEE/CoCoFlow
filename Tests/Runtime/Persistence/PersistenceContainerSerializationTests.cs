using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Persistence
{
    public sealed class PersistenceContainerSerializationTests : PersistenceContainerTestBase
    {
        [Test]
        public void NestedArraysAndListsRoundTripAndRemainIndependentOfTheSnapshot()
        {
            var handle = Host.GetOrCreate("chest", new ContainerTestChest
            {
                Coins = 8,
                Items = new[] { new ContainerTestItem { Key = "potion", Count = 2 } },
                Rewards = new List<ContainerTestItem> { new ContainerTestItem { Key = "coin", Count = 5 } }
            });
            string json = JsonConvert.SerializeObject(Host.CaptureSection());
            handle.Value.Items[0].Count = 99;
            handle.Value.Rewards.Clear();
            Host.ApplySection(JsonConvert.DeserializeObject<PersistenceContainerSection>(json));
            Assert.AreEqual(8, handle.Value.Coins);
            Assert.AreEqual(2, handle.Value.Items[0].Count);
            Assert.AreEqual("coin", handle.Value.Rewards[0].Key);
            Assert.AreEqual(5, handle.Value.Rewards[0].Count);
            JObject data = JObject.Parse(json);
            Assert.AreEqual(1, (int)data["formatVersion"]);
            Assert.IsNull(data["$type"]);
            var records = (JArray)data["records"];
            Assert.IsNotNull(records);
            var record = (JObject)records[0];
            Assert.IsNotNull(record);
            Assert.AreEqual(PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestChest)), (string)record["typeId"]);
        }

        [Test]
        public void NullAndEmptyCollectionsAndStringsStayDistinct()
        {
            var nulls = Host.GetOrCreate("null", new ContainerTestChest());
            var empty = Host.GetOrCreate("empty", new ContainerTestChest
            {
                Items = Array.Empty<ContainerTestItem>(), Rewards = new List<ContainerTestItem>()
            });
            Host.GetOrCreate("strings", new ContainerTestItem { Key = null });
            Host.ApplySection(JsonConvert.DeserializeObject<PersistenceContainerSection>(JsonConvert.SerializeObject(Host.CaptureSection())));
            Assert.IsNull(nulls.Value.Items);
            Assert.IsNull(nulls.Value.Rewards);
            Assert.IsEmpty(empty.Value.Items);
            Assert.IsEmpty(empty.Value.Rewards);
            Assert.IsTrue(Host.TryGet("strings", out PersistenceContainerHandle<ContainerTestItem> strings));
            Assert.IsNull(strings.Value.Key);
            strings.Value.Key = string.Empty;
            Assert.AreEqual(string.Empty, (string)Host.CaptureSection().Records.Find(record => record.Id == "strings").Data["Key"]);
        }

        [Test]
        public void OnlyPublicWritableDataFieldsAreSerializedAndMissingFieldsUseDefault()
        {
            var handle = Host.GetOrCreate("fields", new ContainerTestFields { Stored = 3, Ignored = 9 });
            JObject data = Host.CaptureSection().Records[0].Data;
            CollectionAssert.AreEquivalent(new[] { "Stored" }, data.Properties().Select(property => property.Name));
            Assert.AreEqual(3, handle.Value.Stored);
            var restored = (ContainerTestFields)PersistenceContainerCodec.Decode(typeof(ContainerTestFields), JObject.Parse("{\"oldField\":17}"));
            Assert.AreEqual(0, restored.Stored);
        }

        [TestCase(typeof(ContainerUnmarked), "ContainerUnmarked")]
        [TestCase(typeof(ContainerMissingSerializable), "ContainerMissingSerializable")]
        [TestCase(typeof(ContainerMissingMarker), "ContainerMissingMarker")]
        [TestCase(typeof(ContainerBadPrivate), ".value")]
        [TestCase(typeof(ContainerBadNested), ".Child")]
        [TestCase(typeof(ContainerBadObject), ".Object")]
        [TestCase(typeof(ContainerBadDictionary), ".Values")]
        [TestCase(typeof(ContainerBadArray), ".Values")]
        [TestCase(typeof(ContainerBadLists), ".Values")]
        public void InvalidShapesReportTheTypeAndFieldPath(Type type, string path)
        {
            Assert.IsFalse(PersistenceDataTypeValidator.TryValidate(type, out string diagnostic));
            StringAssert.Contains(type.FullName, diagnostic);
            StringAssert.Contains(path, diagnostic);
        }

        [TestCase("{}")]
        [TestCase("{\"records\":[]}")]
        [TestCase("null")]
        public void EmptyLegacyContainerSectionsDoNotChangeGraphPayloadOrTheWholeDocumentVersion(string sectionJson)
        {
            string graph = "{\"schemaVersion\":2,\"contextSection\":{\"records\":[{\"stableEntityId\":\"graph-1\",\"contextType\":\"CoCoFlow.StateGraph.ContextFrame\",\"stateGraphContextPayload\":\"AQID\"}]},\"containerSection\":" + sectionJson + "}";
            PersistenceSaveDocument document = PersistenceSaveDocument.MigrateToCurrentSchema(JsonConvert.DeserializeObject<PersistenceSaveDocument>(graph));
            Assert.AreEqual(2, document.schemaVersion);
            Assert.IsTrue(document.contextSection.TryGetRecord("graph-1", out PersistenceContextRecord record));
            Assert.IsTrue(record.TryGetStateGraphContextPayload(out byte[] payload));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, payload);
            Assert.IsEmpty(document.containerSection.Records);
        }

        [Test]
        public void MissingLegacyContainerSectionIsStillNormalizedByTheOriginalSaveDocument()
        {
            var document = JsonConvert.DeserializeObject<PersistenceSaveDocument>("{\"schemaVersion\":1,\"contextSection\":{\"records\":[]}}");
            PersistenceSaveDocument.MigrateToCurrentSchema(document);
            Assert.AreEqual(2, document.schemaVersion);
            Assert.IsEmpty(document.containerSection.Records);
        }

        [Test]
        public void NonemptyLegacyGameplayDataIsWarnedAndDiscardedWhileGraphRecordsRemain()
        {
            LogAssert.Expect(LogType.Warning, "[Persistence] Old Container gameplay data was discarded; Graph data is preserved. No migration was performed.");
            var document = JsonConvert.DeserializeObject<PersistenceSaveDocument>("{\"contextSection\":{\"records\":[{\"stableEntityId\":\"graph-1\"}]},\"containerSection\":{\"records\":[{\"containerId\":\"old\",\"entries\":[]}]}}");
            Assert.IsEmpty(document.containerSection.Records);
            Assert.IsTrue(document.contextSection.TryGetRecord("graph-1", out _));
        }

        [TestCase("{\"formatVersion\":2,\"records\":[]}")]
        [TestCase("{\"formatVersion\":1,\"records\":null}")]
        [TestCase("{\"unexpected\":3}")]
        [TestCase("{\"formatVersion\":1,\"records\":[{}]}")]
        public void UnknownOrMalformedNewFormatsAreRejected(string json)
        {
            Assert.Throws<JsonSerializationException>(() => JsonConvert.DeserializeObject<PersistenceContainerSection>(json));
        }

        [Test]
        public void DuplicateKeysUnknownTypesAndWrongValuesNeverPartiallyReplaceTheCurrentTables()
        {
            var handle = Host.GetOrCreate("keep", new ContainerTestChest { Coins = 8 });
            var section = Host.CaptureSection();
            section.Records.Add(section.Records[0]);
            Assert.Throws<JsonSerializationException>(() => Host.ApplySection(section));
            Assert.AreEqual(8, handle.Value.Coins);
            section = Host.CaptureSection();
            section.Records[0].TypeId = "missing:Data";
            Assert.Throws<ArgumentException>(() => Host.ApplySection(section));
            Assert.AreEqual(8, handle.Value.Coins);
            section = Host.CaptureSection();
            section.Records[0].Data["Coins"] = "wrong";
            Assert.Throws<JsonSerializationException>(() => Host.ApplySection(section));
            Assert.AreEqual(8, handle.Value.Coins);
            section = Host.CaptureSection();
            section.Records[0].Id = " ";
            Assert.Throws<JsonSerializationException>(() => Host.ApplySection(section));
        }
    }
}
