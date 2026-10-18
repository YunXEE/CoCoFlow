using System;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Persistence
{
    public sealed class PersistenceContainerHostTests : PersistenceContainerTestBase
    {
        [Test]
        public void SameTypeAndIdShareTheActualStructWhileDifferentTypesHaveIndependentRows()
        {
            var first = Host.GetOrCreate("shared", new ContainerTestChest { Coins = 10 });
            var second = Host.GetOrCreate("shared", new ContainerTestChest { Coins = 999 });
            var quest = Host.GetOrCreate("shared", new ContainerTestQuest { Progress = 4 });
            ref ContainerTestChest data = ref first.Value;
            data.Coins -= 3;
            Assert.AreEqual(7, second.Value.Coins);
            Assert.AreEqual(4, quest.Value.Progress);
            Assert.AreEqual(7, (int)Host.CaptureSection().Records.Find(record => record.TypeId.Contains(nameof(ContainerTestChest))).Data["Coins"]);
        }

        [Test]
        public void OrdinalKeysPreserveCaseAndWhitespaceWithoutTrimming()
        {
            Host.GetOrCreate("ID", new ContainerTestQuest { Progress = 1 });
            Host.GetOrCreate("id", new ContainerTestQuest { Progress = 2 });
            Host.GetOrCreate(" id ", new ContainerTestQuest { Progress = 3 });
            Assert.IsTrue(Host.TryGet("ID", out PersistenceContainerHandle<ContainerTestQuest> upper));
            Assert.IsTrue(Host.TryGet("id", out PersistenceContainerHandle<ContainerTestQuest> lower));
            Assert.AreEqual(1, upper.Value.Progress);
            Assert.AreEqual(2, lower.Value.Progress);
            Assert.AreEqual(3, Host.CaptureSection().Records.Count);
        }

        [Test]
        public void MissingTryGetDoesNotCreateRowsAndDefaultHandlesFailExplicitly()
        {
            Assert.IsFalse(Host.TryGet("missing", out PersistenceContainerHandle<ContainerTestChest> handle));
            Assert.IsFalse(handle.IsValid);
            Assert.Throws<InvalidOperationException>(() => { _ = handle.Value.Coins; });
            Assert.IsEmpty(Host.CaptureSection().Records);
        }

        [Test]
        public void StructCopiesDoNotWriteScalarsButRefAndNestedArrayWritesAreVisible()
        {
            var handle = Host.GetOrCreate("chest", new ContainerTestChest
            {
                Coins = 4, Items = new[] { new ContainerTestItem { Key = "potion", Count = 2 } }
            });
            ContainerTestChest copy = handle.Value;
            copy.Coins = 99;
            Assert.AreEqual(99, copy.Coins);
            Assert.AreEqual(4, handle.Value.Coins);
            ref ContainerTestChest data = ref handle.Value;
            data.Items[0].Count = 6;
            data.Coins++;
            Assert.IsTrue(Host.TryGet("chest", out PersistenceContainerHandle<ContainerTestChest> other));
            Assert.AreEqual(5, other.Value.Coins);
            Assert.AreEqual(6, other.Value.Items[0].Count);
        }

        [Test]
        public void FirstRegistrationCopiesCandidateCollectionsAndLaterInitialDataIsIgnored()
        {
            var candidate = new ContainerTestChest { Items = new[] { new ContainerTestItem { Count = 2 } } };
            var handle = Host.GetOrCreate("chest", candidate);
            candidate.Items[0].Count = 100;
            Host.GetOrCreate("chest", new ContainerTestChest { Coins = 50 });
            Assert.AreEqual(2, handle.Value.Items[0].Count);
            Assert.AreEqual(0, handle.Value.Coins);
            var snapshot = Host.CaptureSection();
            handle.Value.Items[0].Count = 3;
            var items = snapshot.Records[0].Data["Items"];
            Assert.IsNotNull(items);
            var item = items[0];
            Assert.IsNotNull(item);
            Assert.AreEqual(2, (int)item["Count"]);
        }

        [TestCase("")]
        [TestCase("  ")]
        [TestCase(null)]
        public void EmptyIdsAreArgumentErrorsEvenForTryGet(string id)
        {
            Assert.Throws<ArgumentException>(() => Host.GetOrCreate(id, new ContainerTestChest()));
            Assert.Throws<ArgumentException>(() => Host.TryGet<ContainerTestChest>(id, out _));
        }

        [Test]
        public void InvalidTypesAndDuplicateHostsAreRejectedWithoutReplacingTheLiveHost()
        {
            Assert.Throws<ArgumentException>(() => Host.GetOrCreate("bad", new ContainerUnmarked()));
            Assert.Throws<ArgumentException>(() => Host.TryGet<ContainerUnmarked>("bad", out _));
            LogAssert.Expect(LogType.Error, "[Persistence] Only one PersistenceContainerHost may be active.");
            var duplicate = NewObject("duplicate").AddComponent<PersistenceContainerHost>();
            Assert.AreSame(Host, PersistenceContainerHost.ActiveHost);
            Assert.Throws<InvalidOperationException>(() => duplicate.GetOrCreate("id", new ContainerTestChest()));
        }

        [Test]
        public void HostConsumesAPendingSectionBeforeTheFirstContainerRegisters()
        {
            Host.GetOrCreate("loaded", new ContainerTestChest { Coins = 7 });
            var section = Host.CaptureSection();
            UnityEngine.Object.DestroyImmediate(Host.gameObject);
            PersistenceSession.SetPendingDocument(PersistenceSaveDocument.Create(0, new PersistenceContextSection(), section));
            Host = NewObject("Replacement host").AddComponent<PersistenceContainerHost>();
            var handle = Host.GetOrCreate("loaded", new ContainerTestChest { Coins = 99 });
            Assert.AreEqual(7, handle.Value.Coins);
        }

        [Test]
        public void DestroyingTheHostInvalidatesCachedHandles()
        {
            var handle = Host.GetOrCreate("chest", new ContainerTestChest());
            UnityEngine.Object.DestroyImmediate(Host.gameObject);
            Assert.IsFalse(handle.IsValid);
            Assert.Throws<InvalidOperationException>(() => { _ = handle.Value.Coins; });
        }
    }
}
