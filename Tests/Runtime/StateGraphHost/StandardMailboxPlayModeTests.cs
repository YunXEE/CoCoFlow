using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class MailboxTestAdapter : MonoBehaviour,
        ICoCoEventInboxAdapter<MailboxDamage>, ICoCoEventInboxAdapter<MailboxHeal>
    {
        public bool Accept(in MailboxDamage payload) => payload.Target == GetComponent<CoCoStateGraphHost>().GraphInstanceId;
        public bool Accept(in MailboxHeal payload) => true;
    }

    public sealed partial class StandardBindingPlayModeTests
    {
        private readonly CoCoEventAgent _mailboxAgent = new CoCoEventAgent();

        [SetUp]
        public void ResetMailboxProbe()
        {
            MailboxProbeLogic.Tick = null;
            MailboxProbeLogic.Enter = MailboxProbeLogic.Exit = null;
            MailboxPlainLogic.Tick = null;
            MailboxProbeLogic.IgnoreDamage = false;
            MailboxReadyCondition.Calls = 0;
            MailboxReadyCondition.Allowed = true;
            MailboxAnyLogic.Tick = null;
        }

        [TearDown]
        public void ReleaseMailboxSubscriptions()
        {
            _mailboxAgent.UnsubscribeAll();
            foreach (var gameObject in _objects.OfType<GameObject>())
                if (gameObject != null && gameObject.TryGetComponent<CoCoStateGraphHost>(out var host))
                    host.TryStop(out _);
            ResetMailboxProbe();
        }

        private CoCoStateGraphHost CreateMailboxHost(bool plain = false, bool adapters = true,
            bool transition = false, int outboxCapacity = 32, bool twoLayers = false, int history = 0,
            bool any = false)
        {
            var provider = CoCoStateGraphProjectBindings.Provider;
            if (provider == null)
            {
                provider = CoCoStandardBindingProvider.Build(new[] { typeof(MailboxProbeLogic).Assembly });
                Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(provider, out var install), install.Message);
            }
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var layer = new CoCoStateGraphLayerRecord(new CoCoSerializedId128(1, 1), "Mailbox");
            for (int index = 0; index < (transition ? 2 : 1); index++)
            {
                bool isPlain = index == 0 ? plain : !plain;
                var type = isPlain ? typeof(MailboxPlainLogic) : typeof(MailboxProbeLogic);
                StandardDescriptors.TryCreate(type, type.Name, out var id);
                CoCoStateConfig config = isPlain ? new EmptyStateConfig() : new CoCoFieldConfig();
                layer.States.Add(new CoCoStateGraphStateRecord(new CoCoSerializedId128(2, (ulong)index + 1),
                    default, type.Name, new CoCoSerializedId128(id.High, id.Low), config));
            }
            layer.InitialStateId = layer.States[0].StateId;
            if (transition)
            {
                var edge = new CoCoStateGraphTransitionRecord(new CoCoSerializedId128(3, 1),
                    layer.States[0].StateId, layer.States[1].StateId, 1)
                {
                    WindowMode = CoCoTransitionWindowMode.ActionProgress,
                    WindowStartInclusive = 0.75, WindowEndExclusive = 1.0
                };
                var condition = provider.Catalog.ConditionDescriptors.Single(
                    item => item.RuntimeRegistration.ConditionType == typeof(MailboxReadyCondition));
                edge.Conditions.Add(new CoCoStateGraphConditionRecord(
                    new CoCoSerializedId128(condition.DescriptorId.High, condition.DescriptorId.Low),
                    new CoCoEmptyConditionConfig()));
                layer.Transitions.Add(edge);
            }
            asset.Layers.Add(layer);
            if (twoLayers)
            {
                var sibling = new CoCoStateGraphLayerRecord(new CoCoSerializedId128(1, 2), "Sibling");
                var state = layer.States[0];
                sibling.InitialStateId = new CoCoSerializedId128(2, 99);
                sibling.States.Add(new CoCoStateGraphStateRecord(sibling.InitialStateId, default,
                    "Observer", state.StateDescriptorId, new CoCoFieldConfig()));
                asset.Layers.Add(sibling);
            }
            if (any)
                for (int index = 0; index < asset.Layers.Count; index++)
                {
                    var type = index == 0 ? typeof(MailboxAnyLogic) : typeof(MailboxStatelessAnyLogic);
                    StandardDescriptors.TryCreate(type, type.Name, out var descriptor);
                    CoCoStateConfig config = index == 0 ? new CoCoFieldConfig() : new EmptyStateConfig();
                    if (config is CoCoFieldConfig fields) fields.Set(nameof(MailboxAnyLogic.Increment), 3);
                    asset.Layers[index].AnyState = new CoCoStateGraphStateRecord(
                        new CoCoSerializedId128(2, (ulong)index + 100), default, type.Name,
                        new CoCoSerializedId128(descriptor.High, descriptor.Low), config);
                }
            var gameObject = new GameObject("Mailbox E2E");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<CoCoStateGraphHost>();
            SetField(host, "autoStart", false);
            SetField(host, "driver", CoCoStateGraphDriver.Manual);
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "eventOutboxCapacity", outboxCapacity);
            SetField(host, "temporalHistoryCapacity", history);
            if (adapters) SetField(host, "eventAdapters", new MonoBehaviour[]
                { gameObject.AddComponent<MailboxTestAdapter>(), gameObject.AddComponent<MailboxTestAdapter>() });
            Assert.IsTrue(host.TryStart(out var start), start.Message);
            return host;
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        public void MailboxRoutesMultipleEventsAndAdaptersWithoutDuplicates(int count)
        {
            var hosts = Enumerable.Range(0, count).Select(_ => CreateMailboxHost()).ToArray();
            var health = new List<float>();
            MailboxProbeLogic.Tick = (logic, _) => health.Add(logic.Health);
            var damage = new MailboxDamage(hosts[0].GraphInstanceId, 30f);
            CoCoEventBus.Publish(ref damage);
            foreach (var host in hosts) Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.AreEqual(76f, health[0]);
            Assert.That(health.Skip(1), Is.All.EqualTo(100f));
            health.Clear();
            var heal = new MailboxHeal(10f);
            CoCoEventBus.Publish(ref heal);
            foreach (var host in hosts) Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.AreEqual(86f, health[0]);
        }

        [Test]
        public void MailboxFreezesLateMailAndInvalidatesViewsWithoutAccumulatingIgnoredMail()
        {
            var host = CreateMailboxHost();
            var health = new List<float>();
            CoCoStateEventBatch<MailboxDamage> saved = default;
            var damage = new MailboxDamage(host.GraphInstanceId, 30f);
            MailboxProbeLogic.Tick = (logic, context) =>
            {
                health.Add(logic.Health);
                Assert.IsFalse(context.Inbox.TryGetBatch<MailboxNotice>(out _));
                Assert.IsTrue(context.Inbox.TryGetBatch(out saved));
                if (health.Count == 1)
                {
                    var late = new MailboxDamage(host.GraphInstanceId, 30f);
                    CoCoEventBus.Publish(ref late);
                    Assert.AreEqual(1, saved.Count);
                }
            };
            CoCoEventBus.Publish(ref damage);
            Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.IsFalse(saved.IsValid);
            Assert.IsFalse(saved.TryRead(0, out _));
            Assert.IsTrue(host.TryStep(0.1, out _));
            MailboxProbeLogic.IgnoreDamage = true;
            CoCoEventBus.Publish(ref damage);
            Assert.IsTrue(host.TryStep(0.1, out _));
            MailboxProbeLogic.IgnoreDamage = false;
            Assert.IsTrue(host.TryStep(0.1, out _));
            CollectionAssert.AreEqual(new[] { 76f, 52f, 52f, 52f }, health);
        }

        [TestCase(false, 1, false)]
        [TestCase(true, 1, false)]
        [TestCase(false, 0, false)]
        [TestCase(true, 2, true)]
        public void MailboxPublishSnapshotsUnregisteredTypesAndOnlyPublishesAfterCommit(bool fail, int capacity, bool any)
        {
            var host = CreateMailboxHost(adapters: false, outboxCapacity: capacity, any: any);
            if (any) MailboxAnyLogic.Tick = () => throw new InvalidOperationException("Cancel AnyState Tick");
            var received = new List<int>();
            _mailboxAgent.Subscribe((ref MailboxNotice mail) =>
            {
                Assert.IsTrue(host.CurrentContext.IsAlive);
                received.Add(mail.Value);
            });
            CoCoStateEventOutboxWriter saved = default;
            MailboxProbeLogic.Tick = (_, context) =>
            {
                saved = context.Outbox;
                var mail = new MailboxNotice { Value = 7 };
                saved.Publish(in mail);
                mail.Value = 99;
                Assert.IsEmpty(received);
                if (fail && !any) throw new InvalidOperationException("Cancel candidate Tick");
            };
            Assert.AreEqual(!fail && capacity != 0, host.TryStep(0.1, out _));
            CollectionAssert.AreEqual(!fail && capacity != 0 ? new[] { 7 } : Array.Empty<int>(), received);
            Assert.Throws<InvalidOperationException>(() => saved.Publish(new MailboxNotice()));
            if (any) Assert.IsFalse(host.CurrentContext.IsAlive);
            if (fail || capacity == 0) return;
            _mailboxAgent.UnsubscribeAll();
            MailboxProbeLogic.Tick = (_, context) => context.Outbox.Publish(new MailboxNotice());
            for (int i = 0; i < 100; i++) host.TryStep(0.1, out _);
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool stepped = true;
            for (int i = 0; i < 10000; i++) stepped &= host.TryStep(0.1, out _);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(stepped);
            Assert.AreEqual(0, allocated, "Warmed-up delayed Publish must not allocate per Tick.");
        }

        [Test]
        public void AnyStateStandardBindingsKeepIndependentMemoryAndRestoreBothLayers()
        {
            var host = CreateMailboxHost(transition: true, twoLayers: true, history: 4, any: true);
            var received = new List<int>();
            _mailboxAgent.Subscribe((ref MailboxNotice mail) => received.Add(mail.Value));
            var damage = new MailboxDamage(host.GraphInstanceId, 1f);
            CoCoEventBus.Publish(ref damage);
            Assert.IsTrue(host.TryStep(0.1, out var diagnostic), diagnostic.Message);
            var first = ReadMailboxAnyRecord<ValueTuple<int, byte>>(host, typeof(MailboxAnyLogic), 100);
            var second = ReadMailboxAnyRecord<int>(host, typeof(MailboxStatelessAnyLogic), 101);
            Assert.AreEqual(2UL, first.ActivationId.Value);
            Assert.AreEqual(4UL, second.ActivationId.Value);
            Assert.IsFalse(first.IsOnActivePath || first.EnterPending || second.IsOnActivePath || second.EnterPending);
            Assert.AreEqual(12, first.State.Item1);
            var initialLeaf = host.ActivePaths[0].ActiveLeaf;
            MailboxProbeLogic.Tick = (_, context) =>
            {
                if (context.StateId == initialLeaf) context.TryRequestTransitionTo<MailboxPlainLogic>();
            };
            Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
            Assert.AreNotEqual(initialLeaf, host.ActivePaths[0].ActiveLeaf);
            Assert.AreEqual(first.ActivationId,
                ReadMailboxAnyRecord<ValueTuple<int, byte>>(host, typeof(MailboxAnyLogic), 100).ActivationId);
            Assert.IsTrue(host.TryBeginTemporalPreview(out diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryPreviewTemporal(1, out diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryConfirmTemporalRestore(out diagnostic), diagnostic.Message);
            Assert.AreEqual(initialLeaf, host.ActivePaths[0].ActiveLeaf);
            Assert.AreEqual(12,
                ReadMailboxAnyRecord<ValueTuple<int, byte>>(host, typeof(MailboxAnyLogic), 100).State.Item1);
            Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
            CollectionAssert.AreEqual(new[] { 12, 15, 15 }, received);
            Assert.AreEqual(second.ActivationId,
                ReadMailboxAnyRecord<int>(host, typeof(MailboxStatelessAnyLogic), 101).ActivationId);
        }

        private static CoCoGraphStateRecord<TState> ReadMailboxAnyRecord<TState>(
            CoCoStateGraphHost host, Type logicType, ulong stateLow) where TState : unmanaged
        {
            StandardDescriptors.TryCreate(logicType, $"State:{2UL:x16}{stateLow:x16}", out var descriptor);
            Assert.IsTrue(host.CurrentContext.Layout.TryResolveSlot(StandardGraphState.SlotFor(descriptor),
                out CoCoStateSlot<CoCoGraphStateRecord<TState>> slot));
            return host.CurrentContext.Read(slot);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MailboxProgressAndConditionRequireARequestedTransition(bool plain)
        {
            var host = CreateMailboxHost(plain: plain, transition: true);
            var initial = host.ActivePaths[0].ActiveLeaf;
            Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.AreEqual(0, MailboxReadyCondition.Calls);
            if (plain) MailboxPlainLogic.Tick = context => context.TryRequestTransitionTo<MailboxProbeLogic>();
            else MailboxProbeLogic.Tick = (_, context) => context.TryRequestTransitionTo<MailboxPlainLogic>();
            MailboxReadyCondition.Allowed = false;
            Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.AreEqual(initial, host.ActivePaths[0].ActiveLeaf);
            MailboxReadyCondition.Allowed = true;
            Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.AreNotEqual(initial, host.ActivePaths[0].ActiveLeaf);
            Assert.AreEqual(2, MailboxReadyCondition.Calls);
        }

        [Test]
        public void MailboxReadersInDifferentStatesDoNotConsumeEachOthersBatch()
        {
            var host = CreateMailboxHost(twoLayers: true);
            var health = new List<float>();
            MailboxProbeLogic.Tick = (logic, _) => health.Add(logic.Health);
            var damage = new MailboxDamage(host.GraphInstanceId, 30f);
            CoCoEventBus.Publish(ref damage);
            Assert.IsTrue(host.TryStep(0.1, out _));
            CollectionAssert.AreEqual(new[] { 76f, 76f }, health);
        }

        [Test]
        public void MailboxPublishesEnterUpdateExitInOrderAfterCommit()
        {
            var host = CreateMailboxHost(transition: true);
            var received = new List<int>();
            _mailboxAgent.Subscribe((ref MailboxNotice mail) => received.Add(mail.Value));
            MailboxProbeLogic.Enter = context => context.Outbox.Publish(new MailboxNotice { Value = 1 });
            MailboxProbeLogic.Tick = (_, context) =>
            {
                context.Outbox.Publish(new MailboxNotice { Value = 2 });
                context.TryRequestTransitionTo<MailboxPlainLogic>();
            };
            MailboxProbeLogic.Exit = context => context.Outbox.Publish(new MailboxNotice { Value = 3 });
            Assert.IsTrue(host.TryStep(0.1, out var diagnostic), diagnostic.Message);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, received);
        }

        [Test]
        public void MailboxOverflowDropsExcessMailWithoutFaultingHost()
        {
            var host = CreateMailboxHost();
            var damage = new MailboxDamage(host.GraphInstanceId, 1f);
            for (int i = 0; i < 33; i++) CoCoEventBus.Publish(ref damage);
            MailboxProbeLogic.Tick = (logic, _) => Assert.That(logic.Health, Is.EqualTo(74.4f).Within(0.001f));
            Assert.IsTrue(host.TryStep(0.1, out var diagnostic), diagnostic.Message);
        }

        [TestCase(0)]
        [TestCase(2)]
        public void MailboxStopRestartAndSuspendClearPreviousInput(int history)
        {
            var host = CreateMailboxHost(history: history);
            var health = new List<float>();
            MailboxProbeLogic.Tick = (logic, _) => health.Add(logic.Health);
            var damage = new MailboxDamage(host.GraphInstanceId, 30f);
            for (int i = 0; i < 2; i++) Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.IsTrue(history == 0 ? host.TrySuspend(out _) : host.TryBeginTemporalPreview(out _));
            for (int i = 0; i < 33; i++) CoCoEventBus.Publish(ref damage);
            Assert.IsTrue(history == 0 ? host.TryResume(out _) : host.TryCancelTemporalPreview(out _));
            Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.IsTrue(host.TryStop(out _));
            Assert.IsTrue(host.TryStart(out _));
            damage = new MailboxDamage(host.GraphInstanceId, 30f);
            CoCoEventBus.Publish(ref damage);
            Assert.IsTrue(host.TryStep(0.1, out _));
            CollectionAssert.AreEqual(new[] { 100f, 100f, 100f, 76f }, health);
        }
    }
}
