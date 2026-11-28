using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;

// 在手动 Host 上投递直接事件并观察回调、选边与发布，另将 0.4.10 实写图重存后再次运行。
namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    /// <summary>在事件入队前按本实例或测试条件过滤载荷，以验证实际接收者。</summary>
    public sealed class MailboxTestAdapter : MonoBehaviour,
        IEventInboxAdapter<MailboxDamage>, IEventInboxAdapter<MailboxHeal>
    {
        /// <summary>在事件进入 State 的直接收件通道前检查载荷，返回 true 才允许该事件进入本步可见的队列。</summary>
        public bool Accept(in MailboxDamage payload) => payload.Target == GetComponent<StateGraphHost>().GraphInstanceId;
        /// <summary>在事件进入 State 的直接收件通道前检查载荷，返回 true 才允许该事件进入本步可见的队列。</summary>
        public bool Accept(in MailboxHeal payload) => true;
    }

    /// <summary>在手动 Host 上投递直接事件并观察回调、选边与发布，另将 0.4.10 实写图重存后再次运行。</summary>
    public sealed partial class StandardBindingPlayModeTests
    {
        #region Public API

        /// <summary>重置状态和观察计数，隔离各用例的收件与转换结果。</summary>
        [SetUp]
        public void ResetMailboxProbe()
        {
            MailboxProbeLogic.Tick = null;
            MailboxProbeLogic.Enter = MailboxProbeLogic.Exit = null;
            MailboxPlainLogic.Tick = null;
            MailboxProbeLogic.IgnoreDamage = false;
            MailboxAnyLogic.Tick = null;
        }

        /// <summary>解除本用例安装的订阅，避免事件投递到已结束的 Host。</summary>
        [TearDown]
        public void ReleaseMailboxSubscriptions()
        {
            _mailboxAgent.UnsubscribeAll();
            foreach (var gameObject in _objects.OfType<GameObject>())
                if (gameObject != null && gameObject.TryGetComponent<StateGraphHost>(out var host))
                    host.TryStop(out _);
            ResetMailboxProbe();
        }

        /// <summary>同时投递不同类型和经过适配器过滤的事件，核对每条只被对应状态看见一次。</summary>
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

        /// <summary>在回调后继续投递，核对旧批次失效、晚到事件留到下一步且忽略的邮件没有跨步累积。</summary>
        [Test]
        public void MailboxFreezesLateMailAndInvalidatesViewsWithoutAccumulatingIgnoredMail()
        {
            var host = CreateMailboxHost();
            var health = new List<float>();
            StateEventBatch<MailboxDamage> saved = default;
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

        /// <summary>发布未预登记的载荷，检查冻结值独立且只有成功提交后订阅者才收到。</summary>
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
            StateEventOutboxWriter saved = default;
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

        /// <summary>以两层 AnyState 交错更新和恢复，核对两份常驻记忆分别保留。</summary>
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

#if UNITY_EDITOR
        /// <summary>将 0.4.10 保存的普通 Mailbox 图导入当前程序集，由真实 Host 在重存前后各请求一次原有出边。</summary>
        [Test]
        public void Released0410MailboxRunsBeforeAndAfterSavingWithCurrentAssemblies()
        {
            const string folder = "Assets/__P002ReleasedMailbox";
            const string path = folder + "/Mailbox.asset";
            const string fixture = "Packages/com.yunxee.cocoflow/Tests/Runtime/StateGraphHost/Legacy/Released0410Mailbox.asset.bytes";
            UnityEditor.AssetDatabase.CreateFolder("Assets", "__P002ReleasedMailbox");
            try
            {
                var bytes = UnityEditor.AssetDatabase.LoadAssetAtPath<TextAsset>(fixture).bytes;
                System.IO.File.WriteAllBytes(path, bytes);
                System.IO.File.WriteAllText(path + ".meta", "fileFormatVersion: 2\nguid: 5bc4ff4a71ed34d7a91be41c151d9371\n");
                UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
                var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<StateGraphAsset>(path);
                var provider = HostBindingProvider.Build(new[] { typeof(MailboxProbeLogic).Assembly });
                Assert.IsTrue(HostProjectBindings.TryInstall(provider, out var diagnostic), diagnostic.Message);
                Run(asset);
                UnityEditor.EditorUtility.SetDirty(asset);
                UnityEditor.AssetDatabase.SaveAssetIfDirty(asset);
                Resources.UnloadAsset(asset);
                UnityEditor.AssetDatabase.ImportAsset(path, UnityEditor.ImportAssetOptions.ForceSynchronousImport);
                Run(UnityEditor.AssetDatabase.LoadAssetAtPath<StateGraphAsset>(path));
            }
            finally
            {
                UnityEditor.AssetDatabase.DeleteAsset(folder);
            }

            void Run(StateGraphAsset graph)
            {
                Assert.IsFalse(UnityEditor.SerializationUtility.HasManagedReferencesWithMissingTypes(graph));
                var layer = graph.Layers.Single();
                Assert.IsInstanceOf<FieldConfig>(layer.States[0].Config);
                Assert.IsInstanceOf<EmptyStateConfig>(layer.States[1].Config);
                var edge = layer.Transitions.Single();
                Assert.AreEqual(layer.InitialStateId, edge.SourceStateId);
                Assert.AreEqual(layer.States[1].StateId, edge.TargetStateId);
                Assert.AreEqual(1, edge.Priority);
                Assert.AreEqual(0.75d, edge.WindowStartInclusive);
                Assert.AreEqual(1d, edge.WindowEndExclusive);
                var go = new GameObject("Released Mailbox");
                _objects.Add(go);
                var host = go.AddComponent<StateGraphHost>();
                SetField(host, "autoStart", false);
                SetField(host, "driver", StateGraphDriver.Manual);
                SetField(host, "stateGraphAsset", graph);
                Assert.IsTrue(host.TryStart(out var diagnostic), diagnostic.Message);
                var initial = host.ActivePaths[0].ActiveLeaf;
                MailboxProbeLogic.Tick = null;
                Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
                Assert.AreEqual(initial, host.ActivePaths[0].ActiveLeaf);
                MailboxProbeLogic.Tick = (_, context) => Assert.IsTrue(context.TryRequestTransitionTo<MailboxPlainLogic>());
                Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
                Assert.AreEqual(edge.TargetStateId.High, host.ActivePaths[0].ActiveLeaf.High);
                Assert.AreEqual(edge.TargetStateId.Low, host.ActivePaths[0].ActiveLeaf.Low);
                Assert.IsTrue(host.TryStop(out diagnostic), diagnostic.Message);
            }
        }
#endif
        /// <summary>分别给入就绪与未就绪的收件值，核对状态只在满足玩法条件时登记转换。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void MailboxStateLogicRequestsTransitionOnlyWhenReady(bool plain)
        {
            var host = CreateMailboxHost(plain: plain, transition: true);
            var initial = host.ActivePaths[0].ActiveLeaf;
            Assert.IsTrue(host.TryStep(0.1, out _));
            bool ready = false;
            if (plain) MailboxPlainLogic.Tick = context =>
            {
                if (ready) context.TryRequestTransitionTo<MailboxProbeLogic>();
            };
            else MailboxProbeLogic.Tick = (_, context) =>
            {
                if (ready) context.TryRequestTransitionTo<MailboxPlainLogic>();
            };
            Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.AreEqual(initial, host.ActivePaths[0].ActiveLeaf);
            ready = true;
            Assert.IsTrue(host.TryStep(0.1, out _));
            Assert.AreNotEqual(initial, host.ActivePaths[0].ActiveLeaf);
        }

        /// <summary>让多个状态读取同一冻结批次，验证先读取的状态不会消费其他状态的邮件。</summary>
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

        /// <summary>在进入、更新和退出回调各发布事件，核对成功提交后的分发顺序。</summary>
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

        /// <summary>超过收件容量时检查只丢弃多余邮件，Host 仍能完成本步。</summary>
        [Test]
        public void MailboxOverflowDropsExcessMailWithoutFaultingHost()
        {
            var host = CreateMailboxHost();
            var damage = new MailboxDamage(host.GraphInstanceId, 1f);
            for (int i = 0; i < 33; i++) CoCoEventBus.Publish(ref damage);
            MailboxProbeLogic.Tick = (logic, _) => Assert.That(logic.Health, Is.EqualTo(74.4f).Within(0.001f));
            Assert.IsTrue(host.TryStep(0.1, out var diagnostic), diagnostic.Message);
        }

        /// <summary>经过停止重启或暂停后再采样，核对上一次运行的收件不再进入新步。</summary>
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

        #endregion

        #region Internal Logic

        private readonly CoCoEventAgent _mailboxAgent = new CoCoEventAgent();

        private StateGraphHost CreateMailboxHost(bool plain = false, bool adapters = true,
            bool transition = false, int outboxCapacity = 32, bool twoLayers = false, int history = 0,
            bool any = false)
        {
            var provider = HostProjectBindings.Provider;
            if (provider == null)
            {
                provider = HostBindingProvider.Build(new[] { typeof(MailboxProbeLogic).Assembly });
                Assert.IsTrue(HostProjectBindings.TryInstall(provider, out var install), install.Message);
            }
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var layer = new StateGraphLayerRecord(new SerializedId128(1, 1), "Mailbox");
            for (int index = 0; index < (transition ? 2 : 1); index++)
            {
                bool isPlain = index == 0 ? plain : !plain;
                var type = isPlain ? typeof(MailboxPlainLogic) : typeof(MailboxProbeLogic);
                StandardDescriptors.TryCreate(type, type.Name, out var id);
                StateConfig config = isPlain ? new EmptyStateConfig() : new FieldConfig();
                layer.States.Add(new StateGraphStateRecord(new SerializedId128(2, (ulong)index + 1),
                    default, type.Name, new SerializedId128(id.High, id.Low), config));
            }
            layer.InitialStateId = layer.States[0].StateId;
            if (transition)
            {
                var edge = new StateGraphTransitionRecord(new SerializedId128(3, 1),
                    layer.States[0].StateId, layer.States[1].StateId, 1)
                {
                    WindowMode = TransitionWindowMode.ActionProgress,
                    WindowStartInclusive = 0.75, WindowEndExclusive = 1.0
                };
                layer.Transitions.Add(edge);
            }
            asset.Layers.Add(layer);
            if (twoLayers)
            {
                var sibling = new StateGraphLayerRecord(new SerializedId128(1, 2), "Sibling");
                var state = layer.States[0];
                sibling.InitialStateId = new SerializedId128(2, 99);
                sibling.States.Add(new StateGraphStateRecord(sibling.InitialStateId, default,
                    "Observer", state.StateDescriptorId, new FieldConfig()));
                asset.Layers.Add(sibling);
            }
            if (any)
                for (int index = 0; index < asset.Layers.Count; index++)
                {
                    var type = index == 0 ? typeof(MailboxAnyLogic) : typeof(MailboxStatelessAnyLogic);
                    StandardDescriptors.TryCreate(type, type.Name, out var descriptor);
                    StateConfig config = index == 0 ? new FieldConfig() : new EmptyStateConfig();
                    if (config is FieldConfig fields) fields.Set(nameof(MailboxAnyLogic.Increment), 3);
                    asset.Layers[index].AnyState = new StateGraphStateRecord(
                        new SerializedId128(2, (ulong)index + 100), default, type.Name,
                        new SerializedId128(descriptor.High, descriptor.Low), config);
                }
            var gameObject = new GameObject("Mailbox E2E");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<StateGraphHost>();
            SetField(host, "autoStart", false);
            SetField(host, "driver", StateGraphDriver.Manual);
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "eventOutboxCapacity", outboxCapacity);
            SetField(host, "temporalHistoryCapacity", history);
            if (adapters) SetField(host, "eventAdapters", new MonoBehaviour[]
                { gameObject.AddComponent<MailboxTestAdapter>(), gameObject.AddComponent<MailboxTestAdapter>() });
            Assert.IsTrue(host.TryStart(out var start), start.Message);
            return host;
        }

        private static GraphStateRecord<TState> ReadMailboxAnyRecord<TState>(
            StateGraphHost host, Type logicType, ulong stateLow) where TState : unmanaged
        {
            StandardDescriptors.TryCreate(logicType, $"State:{2UL:x16}{stateLow:x16}", out var descriptor);
            Assert.IsTrue(host.CurrentContext.Layout.TryResolveSlot(StandardGraphState.SlotFor(descriptor),
                out StateSlot<GraphStateRecord<TState>> slot));
            return host.CurrentContext.Read(slot);
        }

        #endregion

    }
}
