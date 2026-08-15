using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Gameplay.Item;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Samples.Gameplay.Tests.Runtime
{
    public sealed class GameplayPersistenceModuleTests
    {
        [TearDown]
        public void TearDown()
        {
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
        }

        [Test]
        public void LegacyRecordUsesOldAdapterWhenRunningStateGraphHostIsCoLocated()
        {
            var root = new GameObject("Persistence Legacy With StateGraph Host Test");
            Component host = null;
            try
            {
                root.SetActive(false);
                host = root.AddComponent(ResolveStateGraphHostType());
                SetPrivateField(host, "autoStart", false);
                SetHostLifecycleToRunning(host);

                var provider = root.AddComponent<ItemContextProvider>();
                var persistenceContext = root.AddComponent<PersistenceContext>();
                SetPrivateField(
                    persistenceContext,
                    "stableEntityId",
                    "scene.item.legacy-with-host");
                root.SetActive(true);

                object lifecycle = host.GetType()
                    .GetProperty("Lifecycle", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(host);
                Assert.AreEqual("Running", lifecycle?.ToString());

                var record = new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.legacy-with-host",
                    contextType = typeof(ItemContext).AssemblyQualifiedName,
                    lifecycleState = (int)CoCoLifecycleState.Active,
                    semanticStateId = (int)ItemSemanticState.Opened
                };
                record.StringFacts["item.state"] = "Opened";

                Assert.IsTrue(persistenceContext.TryApply(record));
                Assert.AreEqual(ItemSemanticState.Opened, provider.Context.ItemState);
                Assert.AreEqual(
                    "scene.item.legacy-with-host",
                    provider.Context.Identity.StableEntityId);
            }
            finally
            {
                if (host != null)
                {
                    SetPrivateField(host, "_runtime", null);
                }

                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterContextAdapterRestoresDurableFacts()
        {
            var source = new CharacterContext();
            source.Identity.StableEntityId = "actor.player";
            source.MarkAlive();
            source.Motion.position = new Vector3(1f, 2f, 3f);
            source.Motion.rotation = Quaternion.Euler(0f, 45f, 0f);
            source.Resources.MaxHealth = 200f;
            source.Resources.CurrentHealth = 75f;

            Assert.IsTrue(PersistenceContextAdapterRegistry.TryCapture(
                source.Identity.StableEntityId,
                source,
                out var record));

            var target = new CharacterContext();
            Assert.IsTrue(PersistenceContextAdapterRegistry.TryApply(record, target));

            Assert.AreEqual("actor.player", target.Identity.StableEntityId);
            Assert.AreEqual(CoCoLifecycleState.Active, target.Lifecycle.State);
            Assert.AreEqual((int)CharacterSemanticState.Alive, target.SemanticStateId);
            Assert.AreEqual(new Vector3(1f, 2f, 3f), target.Motion.position);
            Assert.AreEqual(200f, target.Resources.MaxHealth);
            Assert.AreEqual(75f, target.Resources.CurrentHealth);
        }

        [Test]
        public void ItemContextAdapterRestoresOpenedPayloadFacts()
        {
            var source = new ItemContext();
            source.Identity.StableEntityId = "item.chest";
            source.Payload.itemId = "item.gem.red";
            source.Payload.count = 2;
            source.SetOpened();

            Assert.IsTrue(PersistenceContextAdapterRegistry.TryCapture(
                source.Identity.StableEntityId,
                source,
                out var record));

            var target = new ItemContext();
            Assert.IsTrue(PersistenceContextAdapterRegistry.TryApply(record, target));

            Assert.AreEqual("item.chest", target.Identity.StableEntityId);
            Assert.AreEqual(ItemSemanticState.Opened, target.ItemState);
            Assert.AreEqual(CoCoLifecycleState.Active, target.Lifecycle.State);
            Assert.AreEqual("item.gem.red", target.Payload.itemId);
            Assert.AreEqual(2, target.Payload.count);
        }

        [Test]
        public void ContainerBridgeGrantsContainerRewardAndItemContextCanStayOpened()
        {
            var root = new GameObject("Persistence Bridge Test");
            var catalog = ScriptableObject.CreateInstance<PersistenceContainerCatalog>();
            try
            {
                catalog.rewardDefinitions.Add(new PersistenceRewardDefinition
                {
                    rewardId = "reward.chest.gem",
                    entries = new List<PersistenceContainerEntryTemplate>
                    {
                        new PersistenceContainerEntryTemplate
                        {
                            entryType = PersistenceContainerEntryType.Item,
                            definitionId = "item.gem.red",
                            count = 1
                        }
                    }
                });
                ConfigureContainerCatalog(catalog);

                var store = root.AddComponent<PersistenceContainerStore>();
                store.SetCatalog(catalog);
                store.EnsureContainer(
                    PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                    PersistenceContainerStore.DefaultItemStorageDefinitionId,
                    PersistenceContainerType.ItemStorage);
                var bridge = root.AddComponent<PersistenceContainerBridge>();
                bridge.SetActorId("actor.chest");
                bridge.SetContainerId(PersistenceContainerStore.DefaultPlayerInventoryContainerId);

                var itemContext = new ItemContext();
                itemContext.SetOpened();

                Assert.IsTrue(bridge.RequestGrantReward("reward.chest.gem"));
                Assert.AreEqual(
                    1,
                    store.GetItemCount(
                        PersistenceContainerStore.DefaultPlayerInventoryContainerId,
                        "item.gem.red"));
                Assert.AreEqual(ItemSemanticState.Opened, itemContext.ItemState);
            }
            finally
            {
                Object.DestroyImmediate(catalog);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PendingDocumentAppliesWhenPersistenceContextRegisters()
        {
            var root = new GameObject("Persistence Pending Context Test");
            try
            {
                var provider = root.AddComponent<ItemContextProvider>();
                var persistenceContext = root.AddComponent<PersistenceContext>();
                SetPrivateField(persistenceContext, "stableEntityId", "scene.item.pending");

                var record = new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.pending",
                    lifecycleState = (int)CoCoLifecycleState.Active,
                    semanticStateId = (int)ItemSemanticState.Opened
                };
                record.StringFacts["item.state"] = "Opened";

                var contextSection = new PersistenceContextSection();
                contextSection.AddOrReplace(record);
                PersistenceSession.SetPendingDocument(PersistenceSaveDocument.Create(
                    0,
                    contextSection,
                    new PersistenceContainerSection()));

                PersistenceContextRegistry.Register(persistenceContext);

                Assert.AreEqual(ItemSemanticState.Opened, provider.Context.ItemState);
                Assert.AreEqual("scene.item.pending", provider.Context.Identity.StableEntityId);
            }
            finally
            {
                PersistenceSession.ClearPendingDocument();
                Object.DestroyImmediate(root);
            }
        }

        private static void ConfigureContainerCatalog(PersistenceContainerCatalog catalog)
        {
            catalog.itemDefinitions.Add(new PersistenceItemDefinition
            {
                itemId = "item.gem.red",
                displayName = "Red Gem",
                stackable = true,
                maxStack = 99
            });
            catalog.containerDefinitions.Add(new PersistenceContainerDefinition
            {
                definitionId = PersistenceContainerStore.DefaultItemStorageDefinitionId,
                containerType = PersistenceContainerType.ItemStorage,
                sameTypeTransfersOnly = true,
                acceptedEntryTypes = new List<PersistenceContainerEntryType>
                {
                    PersistenceContainerEntryType.Item
                }
            });
        }

        private static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(
                field,
                $"Field {fieldName} was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static System.Type ResolveStateGraphHostType()
        {
            System.Type type = System.Type.GetType(
                "CoCoFlow.Runtime.Core.CoCoStateGraphHost, CoCoFlow.Runtime.StateGraphHost");
            Assert.IsNotNull(type, "CoCoStateGraphHost runtime type could not be resolved.");
            return type;
        }

        private static void SetHostLifecycleToRunning(Component host)
        {
            System.Type runtimeType = System.Type.GetType(
                "CoCoFlow.Runtime.Core.CoCoStateGraphRuntime, " +
                "CoCoFlow.Runtime.Core.StateGraph");
            Assert.IsNotNull(runtimeType, "CoCoStateGraphRuntime type could not be resolved.");
            object runtime =
                System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                    runtimeType);
            FieldInfo lifecycleField = runtimeType.GetField(
                "_lifecycle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(lifecycleField);
            lifecycleField.SetValue(
                runtime,
                System.Enum.Parse(lifecycleField.FieldType, "Running"));
            SetPrivateField(host, "_runtime", runtime);
        }
    }
}
