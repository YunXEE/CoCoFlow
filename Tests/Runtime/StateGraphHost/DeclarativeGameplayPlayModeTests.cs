using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Camera;
using CoCoFlow.Tests.GameplayBinding;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class DeclarativeGameplayPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void Setup() => CoCoStateGraphProjectBindings.ResetForTests();

        [TearDown]
        public void Teardown()
        {
            foreach (Object instance in _objects) Object.Destroy(instance);
            _objects.Clear();
            CoCoStateGraphProjectBindings.ResetForTests();
        }

        [UnityTest]
        public IEnumerator EnemyAndDamageUseTheStandardHost()
        {
            Install();
            var host = CreateHost<EnemyLogic>("P001.Enemy");
            var enemy = host.gameObject.AddComponent<EnemyIntent>();
            var damage = host.gameObject.AddComponent<DamageOperator>();
            host.gameObject.AddComponent<InteractionReceiver>().PlayerId = 7;
            Set(host, "intentSources", new MonoBehaviour[] { enemy });
            Set(host, "operators", new MonoBehaviour[] { damage });
            Set(host, "eventOutboxCapacity", 98);
            Set(host, "temporalHistoryCapacity", 4);
            Set(host, "operatorRecovery", new[] { new CoCoStateGraphHost.OperatorRecovery { component = damage, temporal = true } });
            yield return null;
            Assert.IsTrue(host.TryStart(out var diagnostic), diagnostic.Message);

            var service = new GameplayCoreService();
            CoCoServices.Register(service);
            var started = new List<int>();
            var directed = new List<CoCoEventPacket<CombatContactEvent>>();
            var events = new CoCoEventAgent();
            events.Subscribe((ref AttackStarted value) => started.Add(value.Window));
            events.Subscribe((ref CoCoEventPacket<CombatContactEvent> packet) => directed.Add(packet));
            try
            {
                CoCoEventBus.Publish(new Interaction { PlayerId = 8, Window = 99 });
                CoCoEventBus.Publish(new Interaction { PlayerId = 7, Window = 23 });
                Assert.IsTrue(host.TryStep(.05, out diagnostic), diagnostic.Message);
                Assert.That(EnemyLogic.DirectService, Is.SameAs(service));
                Assert.That(EnemyLogic.HelperService, Is.SameAs(service));
                Assert.AreEqual("Sentinel-42", EnemyLogic.Target);
                Assert.AreEqual(23, damage.LastWindow);
                Assert.AreEqual(24, damage.LastPulse);
                Assert.AreEqual(0, damage.PreviousLedger);
                Assert.AreEqual(1, EnemyLogic.RepeatedRead);
                Assert.IsFalse(EnemyLogic.LastBatch.TryRead(0, out _));
                CollectionAssert.AreEqual(new[] { 23 }, started);
                Assert.AreEqual(1, directed.Count);
                Assert.AreEqual(host.GraphInstanceId, directed[0].Envelope.TargetGraphInstanceId);
                Assert.AreEqual(23, directed[0].Payload.Window);

                CoCoEventBus.Publish(new Interaction { PlayerId = 7, Window = 51 });
                Assert.IsTrue(host.TryStep(.05, out diagnostic), diagnostic.Message);
                Assert.AreEqual(23, damage.PreviousLedger);
                Assert.AreEqual(23, EnemyLogic.LastContact);
                Assert.IsTrue(host.TryBeginTemporalPreview(out diagnostic), diagnostic.Message);
                Assert.IsTrue(host.TryPreviewTemporal(1, out diagnostic), diagnostic.Message);
                Assert.IsTrue(host.TryConfirmTemporalRestore(out diagnostic), diagnostic.Message);
                Assert.IsTrue(host.CurrentContext.Layout.TryResolveSlot(DamageOperator.Ledger, out CoCoStateSlot<int> ledger));
                Assert.AreEqual(23, host.CurrentContext.Read(ledger));
                Assert.AreEqual(23, damage.RestoredLedger);

                damage.Reject = true;
                CoCoEventBus.Publish(new Interaction { PlayerId = 7, Window = 87 });
                Assert.IsFalse(host.TryStep(.05, out _));
                CollectionAssert.AreEqual(new[] { 23, 51 }, started);
                Assert.AreEqual(2, directed.Count);
            }
            finally {
                CoCoServices.Unregister(service); events.UnsubscribeAll(); }
        }

        [UnityTest]
        public IEnumerator CameraUsesTheSameIntentPath()
        {
            Install();
            var host = CreateHost<CameraProbeLogic>("P001.Camera");
            host.transform.position = new Vector3(14, 8, 3);
            host.gameObject.AddComponent<UnityEngine.Camera>();
            var brain = host.gameObject.AddComponent<CinemachineBrain>();
            var director = host.gameObject.AddComponent<CameraDirector>();
            Set(director, "brain", brain);
            var source = host.gameObject.AddComponent<CameraIntent>();
            Set(source, "director", director);
            Set(host, "intentSources", new MonoBehaviour[] { source });
            yield return null;
            Assert.IsTrue(host.TryStart(out var diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryStep(.05, out diagnostic), diagnostic.Message);
            Assert.AreEqual(14f, CameraProbeLogic.Sample.PositionX);
            Assert.AreEqual(8f, CameraProbeLogic.Sample.PositionY);
        }

        [Test]
        public void MissingModeNamesTheSection()
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                new CoCoStandardOperatorBuilder().Require<IMissingModeSection>());
            StringAssert.Contains(nameof(IMissingModeSection), error.Message);
        }

        [UnityTest]
        public IEnumerator MissingAndConflictingSourcesNameThePayload()
        {
            Install();
            var host = CreateHost<EnemyLogic>("P001.Enemy");
            var damage = host.gameObject.AddComponent<DamageOperator>();
            Set(host, "operators", new MonoBehaviour[] { damage });
            yield return null;
            Assert.IsFalse(host.TryStart(out var diagnostic));
            StringAssert.Contains(nameof(EnemyIntentData), diagnostic.Message);

            var first = host.gameObject.AddComponent<EnemyIntent>();
            var other = new GameObject("Conflicting enemy input");
            other.transform.SetParent(host.transform);
            var second = other.AddComponent<EnemyIntent>();
            Set(host, "intentSources", new MonoBehaviour[] { first, second });
            Assert.IsFalse(host.TryStart(out diagnostic));
            StringAssert.Contains(nameof(EnemyIntentData), diagnostic.Message);
        }

        private static void Install()
        {
            var provider = CoCoStandardBindingProvider.Build(new[] { typeof(EnemyLogic).Assembly, typeof(DamageOperator).Assembly });
            Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(provider, out var diagnostic), diagnostic.Message);
        }

        private CoCoStateGraphHost CreateHost<T>(string stateName) where T : CoCoStateLogic
        {
            StandardDescriptors.TryCreate(typeof(T), stateName, out var descriptor);
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var stateId = new CoCoSerializedId128(11, 12);
            var layer = new CoCoStateGraphLayerRecord(new CoCoSerializedId128(21, 22), "Base") { InitialStateId = stateId };
            layer.States.Add(new CoCoStateGraphStateRecord(stateId, default, stateName,
                new CoCoSerializedId128(descriptor.High, descriptor.Low), new EmptyStateConfig()));
            asset.Layers.Add(layer);
            var root = new GameObject(stateName);
            _objects.Add(root);
            var host = root.AddComponent<CoCoStateGraphHost>();
            Set(host, "stateGraphAsset", asset);
            Set(host, "autoStart", false);
            Set(host, "driver", CoCoStateGraphDriver.Manual);
            return host;
        }

        private static void Set(object instance, string field, object value) =>
            instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
