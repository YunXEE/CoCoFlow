using System;
using System.Reflection;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Gameplay.Enemy;
using CoCoFlow.Runtime.Gameplay.Item;
using CoCoFlow.Runtime.Modules.Input;
using CoCoFlow.Runtime.Modules.Persistence;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public class CoCoFlowContextLifecycleTests
    {
        #region Public API


        [TearDown]
        public void TearDown()
        {
            CoCoServices.ClearAll();
        }



        [Test]
        public void LifecycleRejectsDestroyedToActive()
        {
            var lifecycle = new CoCoLifecycleContext();

            Assert.IsTrue(lifecycle.TryTransitionTo(CoCoLifecycleState.Active));
            Assert.IsTrue(lifecycle.TryTransitionTo(CoCoLifecycleState.Destroyed));
            Assert.IsFalse(lifecycle.TryTransitionTo(CoCoLifecycleState.Active));
            Assert.Throws<InvalidOperationException>(
                () => lifecycle.TransitionTo(CoCoLifecycleState.Active));
        }

        [Test]
        public void RemovedGenericStateAndControllerTypesDoNotExist()
        {
            var coreAssembly = typeof(CoCoStateMachineController).Assembly;

            Assert.IsNull(coreAssembly.GetType("CoCoFlow.Runtime.Core.CoCoState`1"));
            Assert.IsNull(coreAssembly.GetType("CoCoFlow.Runtime.Core.CoCoStateMachineController`1"));
        }

        [Test]
        public void LegacyStateMachineChangesStatesWithoutContext()
        {
            var root = new GameObject("Legacy StateMachine Test");
            try
            {
                var first = root.AddComponent<LegacyTestStateA>();
                var second = root.AddComponent<LegacyTestStateB>();
                var controller = root.AddComponent<CoCoStateMachineController>();

                controller.ChangeState<LegacyTestStateA>();
                Assert.IsTrue(first.Initialized);
                Assert.IsTrue(first.Entered);

                controller.ChangeState<LegacyTestStateB>();
                Assert.IsTrue(first.Exited);
                Assert.IsTrue(second.Initialized);
                Assert.IsTrue(second.Entered);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ExistingControllerPassesProviderContextToStateLifecycle()
        {
            var root = new GameObject("Single Controller Context Test");
            try
            {
                var provider = root.AddComponent<TestCharacterProvider>();
                var state = root.AddComponent<ContextLifecycleState>();
                var controller = root.AddComponent<CoCoStateMachineController>();

                controller.SetContextProvider(provider);
                controller.ChangeState<ContextLifecycleState>();
                controller.UpdateStateMachine();
                controller.FixedUpdateStateMachine();
                controller.ExitStateMachine();

                Assert.AreSame(provider.Context, controller.Context);
                Assert.AreSame(provider.Context, state.EnterContext);
                Assert.AreSame(provider.Context, state.UpdateContext);
                Assert.AreSame(provider.Context, state.FixedUpdateContext);
                Assert.AreSame(provider.Context, state.ExitContext);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SpecializedCharacterContextCanDriveControllerDecision()
        {
            var root = new GameObject("Specialized Character Context Test");
            var agent = new EventAgent();
            var eventReceived = false;

            try
            {
                var provider = root.AddComponent<TestCharacterProvider>();
                root.AddComponent<CharacterIdleTestState>();
                root.AddComponent<CharacterAttackTestState>();
                var controller = root.AddComponent<TestDecisionStateMachineController>();
                controller.SetContextProvider(provider);

                provider.Context.Lifecycle.TransitionTo(CoCoLifecycleState.Active);
                provider.Context.Intent.attack = true;

                agent.Subscribe<TestCharacterDamagedEvent>((ref TestCharacterDamagedEvent evt) =>
                {
                    eventReceived = evt.Context == provider.Context;
                });

                controller.UpdateStateMachine();

                Assert.AreEqual(typeof(CharacterAttackTestState), controller.CurrentStateType);
                Assert.IsTrue(eventReceived);
                Assert.IsTrue(provider.Context.Resources.IsDead);
                Assert.AreEqual((int)CharacterSemanticState.Dead, provider.Context.SemanticStateId);
                Assert.AreEqual(CoCoLifecycleState.Disabled, provider.Context.Lifecycle.State);
                Assert.AreEqual(1, provider.Context.DecisionStamp);
            }
            finally
            {
                agent.UnsubscribeAll();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterContextIsSharedRootForPlayerEnemyAndNpcSpecializations()
        {
            CharacterContext player = new PlayerLikeCharacterContext
            {
                HasLocalCameraAuthority = true
            };
            CharacterContext enemy = new EnemyLikeCharacterContext
            {
                ThreatScore = 1f
            };
            CharacterContext merchant = new MerchantLikeCharacterContext
            {
                ShopInventoryId = "merchant.basic"
            };

            player.MarkAlive();
            player.Intent.move = Vector2.right;
            player.Intent.attack = true;

            enemy.MarkAlive();
            enemy.Perception.currentTargetId = "player.local";

            merchant.MarkAlive();
            merchant.Intent.interact = true;

            Assert.AreEqual(CoCoLifecycleState.Active, player.Lifecycle.State);
            Assert.AreEqual(CoCoLifecycleState.Active, enemy.Lifecycle.State);
            Assert.AreEqual(CoCoLifecycleState.Active, merchant.Lifecycle.State);
            Assert.IsTrue(player.Intent.attack);
            Assert.AreEqual("player.local", enemy.Perception.currentTargetId);
            Assert.IsTrue(merchant.Intent.interact);
            Assert.IsTrue(((PlayerLikeCharacterContext)player).HasLocalCameraAuthority);
            Assert.AreEqual(1f, ((EnemyLikeCharacterContext)enemy).ThreatScore);
            Assert.AreEqual("merchant.basic", ((MerchantLikeCharacterContext)merchant).ShopInventoryId);
        }

        [Test]
        public void CharacterNavigationIsIndependentContextForNavigationFacts()
        {
            var root = new GameObject("Character Navigation Context Test");
            try
            {
                var lifecycle = root.AddComponent<CharacterLifeCycle>();
                var navigation = root.AddComponent<CharacterNavigation>();
                var characterController = root.AddComponent<CoCoStateMachineController>();
                var navigationController = root.AddComponent<CoCoStateMachineController>();

                characterController.SetContextProvider(lifecycle);
                navigationController.SetContextProvider(navigation);

                navigation.Context.TryClaimControl("EnemySpline");
                navigation.Context.SetDestination(
                    new Vector3(3f, 0f, 2f),
                    2f,
                    0.25f,
                    CharacterNavigationMode.Patrol);

                Assert.AreSame(lifecycle.Context, characterController.Context);
                Assert.AreSame(navigation.Context, navigationController.Context);
                Assert.IsInstanceOf<ICoCoContext>(navigation.Context);
                Assert.AreEqual("EnemySpline", navigation.Context.ControlOwner);
                Assert.IsTrue(navigation.Context.HasDestination);
                Assert.AreEqual(CharacterNavigationMode.Patrol, navigation.Context.Mode);
                Assert.IsNull(typeof(CharacterContext).GetProperty("Navigation"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnemyBrainRequiresIntentData()
        {
            LogAssert.Expect(
                LogType.Error,
                new Regex("EnemyBrain.*缺少 EnemyIntentData"));

            var root = new GameObject("Enemy Brain Missing Intent Test");
            try
            {
                var brain = root.AddComponent<EnemyBrain>();

                Assert.IsFalse(brain.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EnemyBrainWritesTargetIntentAndNavigationFacts()
        {
            var enemy = new GameObject("Enemy Brain Facts Test");
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var intent = ScriptableObject.CreateInstance<EnemyIntentData>();
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.SetActive(false);
                target.name = "Target Player";
                target.layer = 6;
                enemy.transform.position = Vector3.zero;
                target.transform.position = new Vector3(0f, 0f, 4f);

                var lifecycle = enemy.AddComponent<CharacterLifeCycle>();
                var navigation = enemy.AddComponent<CharacterNavigation>();
                var brain = enemy.AddComponent<EnemyBrain>();
                brain.SetIntentData(intent);
                brain.SetConfigData(config);
                brain.SetCharacterContextProvider(lifecycle);
                brain.SetNavigationProvider(navigation);

                enemy.SetActive(true);
                Physics.SyncTransforms();

                Assert.IsTrue(brain.Tick(true));
                Assert.AreSame(target.transform, lifecycle.Context.Perception.currentTarget);
                Assert.AreSame(target.transform, lifecycle.Context.Intent.desiredTarget);
                Assert.IsTrue(lifecycle.Context.Intent.hasMovePosition);
                Assert.IsFalse(lifecycle.Context.Intent.attack);
                Assert.AreEqual(CharacterNavigationMode.Chase, navigation.Context.Mode);
                Assert.AreEqual("EnemyBrain", navigation.Context.ControlOwner);
                Assert.IsTrue(navigation.Context.HasDestination);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(intent);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void EnemyBrainReleasesNavigationAfterTargetLost()
        {
            var enemy = new GameObject("Enemy Brain Lost Target Test");
            var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            var intent = ScriptableObject.CreateInstance<EnemyIntentData>();
            var config = ScriptableObject.CreateInstance<EnemyConfigData>();

            try
            {
                enemy.SetActive(false);
                target.layer = 6;
                enemy.transform.position = Vector3.zero;
                target.transform.position = new Vector3(0f, 0f, 40f);
                SetPrivateField(intent, "disengageDelay", 0f);

                var lifecycle = enemy.AddComponent<CharacterLifeCycle>();
                var navigation = enemy.AddComponent<CharacterNavigation>();
                var brain = enemy.AddComponent<EnemyBrain>();
                brain.SetIntentData(intent);
                brain.SetConfigData(config);
                brain.SetCharacterContextProvider(lifecycle);
                brain.SetNavigationProvider(navigation);

                lifecycle.Context.Perception.currentTarget = target.transform;
                lifecycle.Context.Perception.currentTargetId = "target";
                lifecycle.Context.Perception.lastKnownPosition = new Vector3(0f, 0f, 4f);
                navigation.Context.TryClaimControl("EnemyBrain", 10);
                navigation.Context.SetDestination(
                    lifecycle.Context.Perception.lastKnownPosition,
                    config.ChaseSpeed,
                    0.1f,
                    CharacterNavigationMode.Chase);

                enemy.SetActive(true);
                Physics.SyncTransforms();

                Assert.IsTrue(brain.Tick(true));
                Assert.IsNull(lifecycle.Context.Perception.currentTarget);
                Assert.IsFalse(lifecycle.Context.Intent.hasMovePosition);
                Assert.IsFalse(navigation.Context.HasAnyControl);
                Assert.IsFalse(navigation.Context.HasDestination);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(intent);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(enemy);
            }
        }

        [Test]
        public void CharacterAndItemModelDoesNotIntroducePlayerEnemyOrChestContexts()
        {
            var characterAssembly = typeof(CharacterContext).Assembly;
            var itemAssembly = typeof(ItemContext).Assembly;

            Assert.IsNull(characterAssembly.GetType("CoCoFlow.Runtime.Gameplay.Character.PlayerContext"));
            Assert.IsNull(characterAssembly.GetType("CoCoFlow.Runtime.Gameplay.Character.EnemyContext"));
            Assert.IsNull(itemAssembly.GetType("CoCoFlow.Runtime.Gameplay.Item.ChestContext"));
            Assert.IsNull(itemAssembly.GetType("CoCoFlow.Runtime.Gameplay.Item.ChestState"));

            foreach (var type in itemAssembly.GetTypes())
            {
                Assert.IsFalse(type.Name.Contains("Chest"), $"{type.FullName} should use Item naming.");
            }
        }



        [Test]
        public void ItemContextModelsReusableContainerLifecycleWithoutChestNaming()
        {
            var item = new ItemContext();
            item.Identity.StableEntityId = "item.scene.container.01";
            item.Payload.itemId = "loot.currency";
            item.Payload.count = 5;

            item.SetLocked();
            Assert.AreEqual(ItemSemanticState.Locked, item.ItemState);
            Assert.AreEqual(CoCoLifecycleState.Active, item.Lifecycle.State);

            item.SetAvailable();
            Assert.AreEqual(ItemSemanticState.Available, item.ItemState);
            Assert.AreEqual((int)ItemSemanticState.Available, item.SemanticStateId);

            item.SetOpening();
            Assert.AreEqual(ItemSemanticState.Opening, item.ItemState);
            Assert.AreEqual((int)ItemSemanticState.Opening, item.ActionStateId);

            item.SetOpened();
            Assert.AreEqual(ItemSemanticState.Opened, item.ItemState);
            Assert.AreEqual(0, item.ActionStateId);
            Assert.IsTrue(item.Payload.HasPayload);

            item.SetConsumed();
            Assert.AreEqual(ItemSemanticState.Consumed, item.ItemState);
            Assert.AreEqual(CoCoLifecycleState.Consumed, item.Lifecycle.State);
        }

        [Test]
        public void ItemContextProviderWritesInteractionRequestsIntoItemIntent()
        {
            var root = new GameObject("Item Intent Provider Test");
            try
            {
                var provider = root.AddComponent<ItemContextProvider>();

                Assert.IsInstanceOf<ICoCoContextProvider<ItemContext>>(provider);
                Assert.IsInstanceOf<ICoCoIntentSource<ItemIntent>>(provider);
                Assert.AreSame(provider.Context.Intent, provider.Intent);

                provider.RequestOpen("actor.player");
                Assert.IsTrue(provider.Context.Intent.openRequested);
                Assert.AreEqual("actor.player", provider.Context.Intent.actorId);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                provider.RequestUnlock("actor.player");
                Assert.IsTrue(provider.Context.Intent.unlockRequested);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                provider.RequestUse("actor.player");
                Assert.IsTrue(provider.Context.Intent.useRequested);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                provider.ClearIntent();
                Assert.IsFalse(provider.Context.Intent.openRequested);
                Assert.IsFalse(provider.Context.Intent.unlockRequested);
                Assert.IsFalse(provider.Context.Intent.useRequested);
                Assert.AreEqual(string.Empty, provider.Context.Intent.actorId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemInputDriverMapsExternalItemIntentIntoItemContext()
        {
            var root = new GameObject("Item Input Driver Test");
            try
            {
                var provider = root.AddComponent<ItemContextProvider>();
                var source = root.AddComponent<TestItemIntentSource>();
                var driver = root.AddComponent<ItemInputDriver>();
                driver.SetContextProvider(provider);
                driver.SetItemIntentSource(source);

                source.Intent.openRequested = true;
                source.Intent.actorId = "actor.player";

                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsTrue(provider.Context.Intent.openRequested);
                Assert.AreEqual("actor.player", provider.Context.Intent.actorId);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                driver.ClearIntent();
                Assert.IsFalse(provider.Context.Intent.openRequested);
                Assert.AreEqual(string.Empty, provider.Context.Intent.actorId);

                driver.RequestUnlock("actor.player");
                driver.RequestUse("actor.player");
                Assert.IsTrue(provider.Context.Intent.unlockRequested);
                Assert.IsTrue(provider.Context.Intent.useRequested);
                Assert.AreEqual(ItemSemanticState.Inactive, provider.Context.ItemState);

                Assert.IsNull(typeof(ItemInputDriver).GetMethod(
                    "ChangeState",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }



        [Test]
        public void EventEnvelopeRepresentsItemOpenedAndFireSequences()
        {
            var itemOpened = CoCoEventEnvelope.Create(
                eventTypeId: "Item.Opened",
                sourceEntityId: "actor.player.a",
                sequence: 7,
                tick: 320,
                reliable: true,
                targetEntityId: "item.scene.container.01",
                payloadTypeId: "ItemOpenedEvent",
                payload: "loot.currency");

            Assert.IsTrue(itemOpened.IsValid);
            Assert.IsTrue(itemOpened.HasTarget);
            Assert.IsTrue(itemOpened.HasPayload);
            Assert.AreEqual("actor.player.a", itemOpened.sourceEntityId);
            Assert.AreEqual("item.scene.container.01", itemOpened.targetEntityId);
            Assert.AreEqual(320, itemOpened.tick);
            Assert.IsTrue(itemOpened.reliable);

            var fire = CoCoEventEnvelope.Create(
                eventTypeId: "Character.Fire",
                sourceEntityId: "actor.player.a",
                sequence: 128,
                tick: 512,
                reliable: false);

            Assert.IsTrue(fire.IsValid);
            Assert.AreEqual(128, fire.sequence);
            Assert.AreEqual("Character.Fire", fire.eventTypeId);
            Assert.IsFalse(fire.HasTarget);
            Assert.IsFalse(fire.HasPayload);
        }

        [Test]
        public void EventBusPublishesTypedEventAndEnvelopeForNetworkBridge()
        {
            var agent = new EventAgent();
            var typedObserved = false;
            var envelopeObserved = false;

            try
            {
                agent.Subscribe<ItemOpenedEvent>((ref ItemOpenedEvent evt) =>
                {
                    typedObserved = evt.ItemId == "loot.currency";
                });
                agent.Subscribe<CoCoEventEnvelope>((ref CoCoEventEnvelope envelope) =>
                {
                    envelopeObserved = envelope.eventTypeId == "Item.Opened" &&
                                       envelope.sourceEntityId == "actor.player.a" &&
                                       envelope.targetEntityId == "item.scene.container.01" &&
                                       envelope.sequence == 7;
                });

                var itemContext = new ItemContext();
                itemContext.Identity.StableEntityId = "item.scene.container.01";
                itemContext.SetOpened();

                var itemOpenedEvent = new ItemOpenedEvent
                {
                    Context = itemContext,
                    ItemId = "loot.currency",
                    EventSequence = itemContext.NextEventSequence()
                };
                var envelope = CoCoEventEnvelope.Create(
                    eventTypeId: "Item.Opened",
                    sourceEntityId: "actor.player.a",
                    sequence: 7,
                    tick: 320,
                    reliable: true,
                    targetEntityId: itemContext.Identity.StableEntityId,
                    payloadTypeId: nameof(ItemOpenedEvent),
                    payload: itemOpenedEvent.ItemId);

                CoCoEventBus.PublishWithEnvelope(ref itemOpenedEvent, ref envelope);

                Assert.IsTrue(typedObserved);
                Assert.IsTrue(envelopeObserved);
                Assert.AreEqual(ItemSemanticState.Opened, itemContext.ItemState);
            }
            finally
            {
                agent.UnsubscribeAll();
            }
        }

        [Test]
        public void PersistenceUniqueIdMapsToStableEntityIdWithoutOwningRuntimeId()
        {
            var root = new GameObject("Persistence Identity Test");
            try
            {
                var savable = root.AddComponent<TestSavableEntity>();
                SetPrivateField(savable, "uniqueID", "scene.item.001");

                Assert.IsInstanceOf<ICoCoStableEntityIdProvider>(savable);
                Assert.AreEqual(savable.UniqueID, savable.StableEntityId);

                var context = new ItemContext();
                context.Identity.StableEntityId = savable.StableEntityId;
                context.Identity.RuntimeInstanceId = "runtime.host.42";

                Assert.IsTrue(context.Identity.HasStableEntityId);
                Assert.IsTrue(context.Identity.HasRuntimeInstanceId);
                Assert.AreEqual("scene.item.001", context.Identity.StableEntityId);
                Assert.AreEqual("runtime.host.42", context.Identity.RuntimeInstanceId);
                Assert.AreNotEqual(context.Identity.StableEntityId, context.Identity.RuntimeInstanceId);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EventSequenceIsSeparateFromStableAndRuntimeEntityIds()
        {
            var context = new CharacterContext();
            context.Identity.StableEntityId = "scene.actor.template";
            context.Identity.RuntimeInstanceId = "runtime.actor.001";

            var first = context.NextEventSequence();
            var second = context.NextEventSequence();
            var envelope = CoCoEventEnvelope.Create(
                eventTypeId: "Character.Fire",
                sourceEntityId: context.Identity.RuntimeInstanceId,
                sequence: second,
                tick: 900,
                reliable: false);

            Assert.AreEqual(1, first);
            Assert.AreEqual(2, second);
            Assert.AreEqual(2, envelope.sequence);
            Assert.AreEqual("runtime.actor.001", envelope.sourceEntityId);
            Assert.AreNotEqual(context.Identity.StableEntityId, envelope.sourceEntityId);
        }



        [Test]
        public void InputReaderIsCoreIntentSourceWithoutGameplayOrStateMachineAuthority()
        {
            var inputReaderType = typeof(InputReader);
            var intentSourceType = typeof(ICoCoIntentSource<CoCoInputIntent>);
            var referencedAssemblies = inputReaderType.Assembly.GetReferencedAssemblies();

            Assert.IsTrue(intentSourceType.IsAssignableFrom(inputReaderType));
            Assert.IsNull(inputReaderType.GetMethod(
                "ChangeState",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic));

            foreach (var assemblyName in referencedAssemblies)
            {
                Assert.AreNotEqual(
                    "CoCoFlow.Runtime.Gameplay.Character",
                    assemblyName.Name,
                    "Input module must not depend on Character gameplay.");
            }

            var intent = new CoCoInputIntent
            {
                move = Vector2.right,
                look = Vector2.up,
                zoom = Vector2.one,
                performedAction = "Attack",
                canceledAction = "Aim",
                performedSequence = 10,
                canceledSequence = 4
            };

            intent.ClearDiscrete();
            Assert.AreEqual(Vector2.right, intent.move);
            Assert.AreEqual(string.Empty, intent.performedAction);
            Assert.AreEqual(string.Empty, intent.canceledAction);

            intent.Clear();
            Assert.AreEqual(Vector2.zero, intent.move);
            Assert.AreEqual(0, intent.performedSequence);
            Assert.AreEqual(0, intent.canceledSequence);
        }

        [Test]
        public void CharacterInputDriverMapsCoreInputIntentIntoCharacterIntent()
        {
            var root = new GameObject("Character Input Driver Test");
            try
            {
                var lifecycle = root.AddComponent<CharacterLifeCycle>();
                var source = root.AddComponent<TestInputIntentSource>();
                var driver = root.AddComponent<CharacterInputDriver>();
                driver.SetContextProvider(lifecycle);
                driver.SetInputIntentSource(source);

                source.Intent.move = new Vector2(0.75f, -0.25f);
                source.Intent.look = new Vector2(0.5f, 0.25f);
                source.Intent.performedAction = "Attack";
                source.Intent.performedSequence = 1;

                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.AreEqual(source.Intent.move, lifecycle.Context.Intent.move);
                Assert.AreEqual(source.Intent.look, lifecycle.Context.Intent.look);
                Assert.IsTrue(lifecycle.Context.Intent.attack);
                Assert.IsFalse(lifecycle.Context.Intent.interact);

                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsFalse(lifecycle.Context.Intent.attack);
                Assert.IsFalse(lifecycle.Context.Intent.interact);

                source.Intent.performedAction = "Interact";
                source.Intent.performedSequence = 2;
                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsFalse(lifecycle.Context.Intent.attack);
                Assert.IsTrue(lifecycle.Context.Intent.interact);

                source.Intent.performedAction = "UseSkill";
                source.Intent.performedSequence = 3;
                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.IsFalse(lifecycle.Context.Intent.interact);
                Assert.IsTrue(lifecycle.Context.Intent.useSkill);

                Assert.IsNull(typeof(CharacterInputDriver).GetMethod(
                    "ChangeState",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterInputDriverCanResolveActiveInputSourceFromCoreServices()
        {
            var root = new GameObject("Character Input Driver Service Test");
            try
            {
                var lifecycle = root.AddComponent<CharacterLifeCycle>();
                var source = root.AddComponent<TestInputIntentSource>();
                var driver = root.AddComponent<CharacterInputDriver>();

                source.Intent.performedAction = "Jump";
                source.Intent.performedSequence = 1;
                source.Intent.move = Vector2.up;
                CoCoServices.Register<ICoCoIntentSource<CoCoInputIntent>>(source);

                Assert.IsTrue(InvokePrivateBool(driver, "SampleInput"));
                Assert.AreEqual(Vector2.up, lifecycle.Context.Intent.move);
                Assert.IsTrue(lifecycle.Context.Intent.jump);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CharacterLifeCycleProvidesDefaultCharacterContextAndKeepsEvents()
        {
            var root = new GameObject("Character LifeCycle Test");
            var deathEventReceived = false;

            try
            {
                var lifecycle = root.AddComponent<CharacterLifeCycle>();
                var controller = root.AddComponent<CoCoStateMachineController>();
                lifecycle.OnDeath += () => deathEventReceived = true;

                lifecycle.TakeDamage(lifecycle.Context.Resources.MaxHealth);

                Assert.AreSame(lifecycle.Context, controller.Context);
                Assert.IsTrue(deathEventReceived);
                Assert.IsTrue(lifecycle.IsDead);
                Assert.AreEqual(0f, lifecycle.CurrentHealth);
                Assert.AreEqual((int)CharacterSemanticState.Dead, lifecycle.Context.SemanticStateId);
                Assert.AreEqual(CoCoLifecycleState.Disabled, lifecycle.Context.Lifecycle.State);

                lifecycle.Revive(0.5f);

                Assert.IsFalse(lifecycle.IsDead);
                Assert.AreEqual(CoCoLifecycleState.Active, lifecycle.Context.Lifecycle.State);
                Assert.AreEqual(lifecycle.Context.Resources.MaxHealth * 0.5f, lifecycle.CurrentHealth);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemStateUsesExistingControllerAndProvider()
        {
            var root = new GameObject("Item Context State Test");
            var agent = new EventAgent();
            var openedMatchesState = false;
            var consumedMatchesState = false;

            try
            {
                var provider = root.AddComponent<ItemContextProvider>();
                provider.Context.Payload.itemId = "item.test";
                provider.Context.SetLocked();

                root.AddComponent<ItemIntentTestState>();
                var controller = root.AddComponent<CoCoStateMachineController>();
                controller.SetContextProvider(provider);
                controller.ChangeState<ItemIntentTestState>();

                agent.Subscribe<ItemOpenedEvent>((ref ItemOpenedEvent evt) =>
                {
                    openedMatchesState = evt.Context == provider.Context &&
                                         evt.Context.ItemState == ItemSemanticState.Opened;
                });
                agent.Subscribe<ItemConsumedEvent>((ref ItemConsumedEvent evt) =>
                {
                    consumedMatchesState = evt.Context == provider.Context &&
                                           evt.Context.Lifecycle.State == CoCoLifecycleState.Consumed;
                });

                provider.Context.Intent.openRequested = true;
                controller.UpdateStateMachine();
                Assert.AreEqual(ItemSemanticState.Locked, provider.Context.ItemState);

                provider.Context.Intent.unlockRequested = true;
                provider.Context.Intent.openRequested = true;
                controller.UpdateStateMachine();

                Assert.AreEqual(ItemSemanticState.Opened, provider.Context.ItemState);
                Assert.IsTrue(openedMatchesState);

                provider.Context.Intent.useRequested = true;
                controller.UpdateStateMachine();

                Assert.AreEqual(ItemSemanticState.Consumed, provider.Context.ItemState);
                Assert.AreEqual(CoCoLifecycleState.Consumed, provider.Context.Lifecycle.State);
                Assert.IsTrue(consumedMatchesState);
            }
            finally
            {
                agent.UnsubscribeAll();
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemPersistenceFactsComeFromEntityContext()
        {
            var context = new ItemContext();
            context.Identity.StableEntityId = "item.persist";
            context.SetOpened();

            CoCoEntityContext entityContext = context;

            Assert.AreEqual("item.persist", entityContext.Identity.StableEntityId);
            Assert.AreEqual(CoCoLifecycleState.Active, entityContext.Lifecycle.State);
            Assert.AreEqual((int)ItemSemanticState.Opened, entityContext.SemanticStateId);
        }



        #endregion

        #region Internal Logic


        private sealed class LegacyTestStateA : CoCoStateMachineBase
        {
            public bool Initialized { get; private set; }
            public bool Entered { get; private set; }
            public bool Exited { get; private set; }

            public override void Init(CoCoStateMachineController targetController)
            {
                base.Init(targetController);
                Initialized = true;
            }

            public override void Enter()
            {
                base.Enter();
                Entered = true;
            }

            public override void Exit()
            {
                base.Exit();
                Exited = true;
            }
        }

        private sealed class LegacyTestStateB : CoCoStateMachineBase
        {
            public bool Initialized { get; private set; }
            public bool Entered { get; private set; }

            public override void Init(CoCoStateMachineController targetController)
            {
                base.Init(targetController);
                Initialized = true;
            }

            public override void Enter()
            {
                base.Enter();
                Entered = true;
            }
        }

        private sealed class TestCharacterContext : CharacterContext
        {
            public int DecisionStamp;
        }

        private sealed class PlayerLikeCharacterContext : CharacterContext
        {
            public bool HasLocalCameraAuthority;
        }

        private sealed class EnemyLikeCharacterContext : CharacterContext
        {
            public float ThreatScore;
        }

        private sealed class MerchantLikeCharacterContext : CharacterContext
        {
            public string ShopInventoryId;
        }

        private sealed class TestInputIntentSource :
            MonoBehaviour,
            ICoCoIntentSource<CoCoInputIntent>
        {
            public CoCoInputIntent Intent { get; } = new CoCoInputIntent();
        }

        private sealed class TestItemIntentSource :
            MonoBehaviour,
            ICoCoIntentSource<ItemIntent>
        {
            public ItemIntent Intent { get; } = new ItemIntent();
        }

        private sealed class TestSavableEntity : SavableEntityBase
        {
            public override void LoadState() { }
            public override void SaveState() { }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = FindPrivateField(target.GetType(), fieldName);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }

        private static FieldInfo FindPrivateField(Type type, string fieldName)
        {
            while (type != null)
            {
                var field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return field;

                type = type.BaseType;
            }

            return null;
        }

        private static bool InvokePrivateBool(object target, string methodName)
        {
            var method = target.GetType().GetMethod(
                methodName,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(method);
            return (bool)method.Invoke(target, null);
        }

        private sealed class TestCharacterProvider :
            MonoBehaviour,
            ICoCoContextProvider<TestCharacterContext>
        {
            public TestCharacterContext Context { get; } = new TestCharacterContext();
        }

        private sealed class ContextLifecycleState : CoCoStateMachineBase
        {
            public ICoCoContext EnterContext { get; private set; }
            public ICoCoContext UpdateContext { get; private set; }
            public ICoCoContext FixedUpdateContext { get; private set; }
            public ICoCoContext ExitContext { get; private set; }

            public override void Enter(ICoCoContext context)
            {
                base.Enter(context);
                EnterContext = context;
            }

            public override void OnStateUpdate(ICoCoContext context)
            {
                base.OnStateUpdate(context);
                UpdateContext = context;
            }

            public override void OnStateFixedUpdate(ICoCoContext context)
            {
                base.OnStateFixedUpdate(context);
                FixedUpdateContext = context;
            }

            public override void Exit(ICoCoContext context)
            {
                base.Exit(context);
                ExitContext = context;
            }
        }

        private sealed class TestDecisionStateMachineController : CoCoStateMachineController
        {
            protected override Type EvaluateStateType(ICoCoContext context)
            {
                if (context is not TestCharacterContext characterContext)
                {
                    return typeof(CharacterIdleTestState);
                }

                if (characterContext.Intent.attack)
                {
                    characterContext.DecisionStamp++;
                    return typeof(CharacterAttackTestState);
                }

                return typeof(CharacterIdleTestState);
            }
        }

        private sealed class CharacterIdleTestState : CoCoStateMachineBase { }

        private struct TestCharacterDamagedEvent
        {
            public CharacterContext Context;
        }

        private sealed class CharacterAttackTestState : CoCoStateMachineBase
        {
            public override void Enter(ICoCoContext context)
            {
                base.Enter(context);
                var characterContext = (CharacterContext)context;

                characterContext.Resources.ApplyDamage(characterContext.Resources.MaxHealth);
                if (characterContext.Resources.IsDead)
                {
                    characterContext.MarkDeadDisabled();
                }

                var evt = new TestCharacterDamagedEvent { Context = characterContext };
                CoCoEventBus.Publish(ref evt);
            }
        }

        private sealed class ItemIntentTestState : CoCoStateMachineBase
        {
            public override void OnStateUpdate(ICoCoContext context)
            {
                base.OnStateUpdate(context);
                var itemContext = (ItemContext)context;

                if (itemContext.Intent.unlockRequested &&
                    itemContext.ItemState == ItemSemanticState.Locked)
                {
                    itemContext.SetAvailable();
                }

                if (itemContext.Intent.openRequested &&
                    itemContext.ItemState == ItemSemanticState.Available)
                {
                    itemContext.SetOpening();
                    itemContext.SetOpened();
                    var evt = new ItemOpenedEvent
                    {
                        Context = itemContext,
                        ItemId = itemContext.Payload.itemId,
                        EventSequence = itemContext.NextEventSequence()
                    };
                    CoCoEventBus.Publish(ref evt);
                }

                if (itemContext.Intent.useRequested &&
                    itemContext.ItemState == ItemSemanticState.Opened)
                {
                    itemContext.SetConsumed();
                    var evt = new ItemConsumedEvent
                    {
                        Context = itemContext,
                        ItemId = itemContext.Payload.itemId,
                        EventSequence = itemContext.NextEventSequence()
                    };
                    CoCoEventBus.Publish(ref evt);
                }

                itemContext.Intent.Clear();
            }
        }

        #endregion
    }
}
