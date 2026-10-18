using System;
using System.Collections;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Persistence
{
    public sealed class PersistenceContainerLifecycleTests : PersistenceContainerTestBase
    {
        [UnityTest]
        public IEnumerator SameObjectAwakePreparesInitialDataBeforeRegistrationAndStartUsesTheSharedHandle()
        {
            PersistenceContainer first = NewContainer<ContainerTestChest>("shared", active: false);
            var firstBusiness = first.gameObject.AddComponent<ContainerTestChestBusiness>();
            firstBusiness.InitialCoins = 8;
            first.gameObject.SetActive(true);
            PersistenceContainer second = NewContainer<ContainerTestChest>("shared", active: false);
            var secondBusiness = second.gameObject.AddComponent<ContainerTestChestBusiness>();
            secondBusiness.InitialCoins = 99;
            second.gameObject.SetActive(true);
            yield return null;
            Assert.AreSame(first, firstBusiness.GetComponent<PersistenceContainer>());
            Assert.IsTrue(firstBusiness.BoundAtStart);
            Assert.IsTrue(secondBusiness.BoundAtStart);
            Assert.AreEqual(8, second.GetHandle<ContainerTestChest>().Value.Coins);
            Assert.AreEqual(3, firstBusiness.TakeCoins(3));
            Assert.AreEqual(5, second.GetHandle<ContainerTestChest>().Value.Coins);
            first.gameObject.SetActive(false);
            first.gameObject.SetActive(true);
            yield return null;
            Assert.AreEqual(1, firstBusiness.AwakeCount);
            Assert.AreEqual(1, firstBusiness.StartCount);
            Assert.AreEqual(5, first.GetHandle<ContainerTestChest>().Value.Coins);
        }

        [Test]
        public void DisableUnbindsTheComponentButRetainsTheHostRowAndCachedHandle()
        {
            PersistenceContainer container = NewContainer("chest", new ContainerTestChest { Coins = 5 });
            var handle = container.GetHandle<ContainerTestChest>();
            container.gameObject.SetActive(false);
            Assert.IsFalse(container.IsBound);
            Assert.IsTrue(handle.IsValid);
            Assert.IsFalse(container.TryGetHandle<ContainerTestChest>(out _));
            handle.Value.Coins = 2;
            container.gameObject.SetActive(true);
            Assert.AreEqual(2, container.GetHandle<ContainerTestChest>().Value.Coins);
        }

        [Test]
        public void ChangingAnUnboundIdCreatesAnotherBindingWithoutMovingTheOldRowOrHandle()
        {
            PersistenceContainer container = NewContainer("old", new ContainerTestChest { Coins = 4 });
            var old = container.GetHandle<ContainerTestChest>();
            Assert.Throws<InvalidOperationException>(() => container.SetId("new"));
            Assert.Throws<InvalidOperationException>(() => container.SetInitialData(new ContainerTestChest()));
            container.gameObject.SetActive(false);
            container.SetId(id: "new");
            container.gameObject.SetActive(true);
            var next = container.GetHandle<ContainerTestChest>();
            next.Value.Coins = 10;
            Assert.AreEqual("old", old.Id);
            Assert.AreEqual(4, old.Value.Coins);
            Assert.AreEqual(10, next.Value.Coins);
        }

        [Test]
        public void CandidateSnapshotsDoNotAliasBusinessArraysAndWrongTypesRemainArgumentErrors()
        {
            var initial = new ContainerTestChest { Items = new[] { new ContainerTestItem { Count = 2 } } };
            PersistenceContainer container = NewContainer("chest", initial, false);
            initial.Items[0].Count = 9;
            Assert.Throws<ArgumentException>(() => container.SetInitialData(new ContainerTestQuest()));
            Assert.Throws<ArgumentException>(() => container.GetHandle<ContainerTestQuest>());
            Assert.Throws<ArgumentException>(() => container.TryGetHandle<ContainerTestQuest>(out _));
            Assert.Throws<InvalidOperationException>(() => container.GetHandle<ContainerTestChest>());
            container.gameObject.SetActive(true);
            Assert.AreEqual(2, container.GetHandle<ContainerTestChest>().Value.Items[0].Count);
        }

        [Test]
        public void LoadingReplacesExistingValuesRecreatesOnlyActiveMissingKeysAndInvalidatesOrphans()
        {
            PersistenceContainer active = NewContainer("active", new ContainerTestChest { Coins = 3 });
            PersistenceContainer disabled = NewContainer("disabled", new ContainerTestChest { Coins = 4 });
            var activeHandle = active.GetHandle<ContainerTestChest>();
            var disabledHandle = disabled.GetHandle<ContainerTestChest>();
            disabled.gameObject.SetActive(false);
            var orphan = Host.GetOrCreate("orphan", new ContainerTestChest { Coins = 9 });
            activeHandle.Value.Coins = 88;
            Host.ApplySection(new PersistenceContainerSection());
            Assert.AreEqual(3, activeHandle.Value.Coins);
            Assert.IsFalse(disabledHandle.IsValid);
            Assert.IsFalse(orphan.IsValid);
            disabled.gameObject.SetActive(true);
            Assert.AreEqual(4, disabledHandle.Value.Coins);
            var saved = Host.CaptureSection();
            saved.Records.Find(record => record.Id == "active").Data["Coins"] = 20;
            Host.ApplySection(saved);
            Assert.AreEqual(20, activeHandle.Value.Coins);
        }

        [Test]
        public void MissingSharedRowUsesCurrentBindingOrderWithoutRerunningBusinessInitialization()
        {
            PersistenceContainer first = NewContainer("shared", new ContainerTestChest { Coins = 3 });
            PersistenceContainer second = NewContainer("shared", new ContainerTestChest { Coins = 99 });
            Host.ApplySection(new PersistenceContainerSection());
            Assert.AreEqual(3, second.GetHandle<ContainerTestChest>().Value.Coins);
            first.gameObject.SetActive(false);
            first.gameObject.SetActive(true);
            Host.ApplySection(new PersistenceContainerSection());
            Assert.AreEqual(99, first.GetHandle<ContainerTestChest>().Value.Coins);
        }

        [Test]
        public void MissingHostOrInvalidConfigurationLogsAnExplicitRegistrationFailure()
        {
            PersistenceContainer container = NewContainer<ContainerTestChest>("chest", active: false);
            UnityEngine.Object.DestroyImmediate(Host.gameObject);
            LogAssert.Expect(LogType.Error, new Regex("Container 'chest' registration failed: PersistenceContainerHost must be ready"));
            container.gameObject.SetActive(true);
            Assert.IsFalse(container.IsBound);
        }

        [Test]
        public void InspectorInitialJsonIsDecodedUsingTheExplicitLockedType()
        {
            GameObject root = NewObject("authored", false);
            var container = root.AddComponent<PersistenceContainer>();
            SetField(container, "id", "authored");
            SetField(container, "dataTypeId", PersistenceDataTypeRegistry.GetTypeId(typeof(ContainerTestChest)));
            SetField(container, "initialDataJson", "{\"Coins\":12,\"Items\":[{\"Key\":\"key\",\"Count\":2}]}");
            root.SetActive(true);
            Assert.AreEqual(typeof(ContainerTestChest), container.DataType);
            Assert.AreEqual(12, container.GetHandle<ContainerTestChest>().Value.Coins);
            Assert.AreEqual("key", container.GetHandle<ContainerTestChest>().Value.Items[0].Key);
        }
    }
}
