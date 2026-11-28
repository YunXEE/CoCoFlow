using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Modules.Camera;
using CoCoFlow.Runtime.Modules.Locomotion;
using CoCoFlow.Tests.GameplayBinding;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class DeclarativeGameplayPlayModeTests
    {

        #region Public API

        [SetUp]
        public void Setup() => HostProjectBindings.ResetForTests();

        [TearDown]
        public void Teardown()
        {
            foreach (Object instance in _objects) Object.Destroy(instance);
            _objects.Clear();
            HostProjectBindings.ResetForTests();
        }

        [UnityTest]
        public IEnumerator EnemyAndDamageUseTheStandardHost()
        {
            Install();
            var host = CreateHost<EnemyLogic>("P001.Enemy");
            var enemy = host.gameObject.AddComponent<EnemyIntent>();
            var damage = host.gameObject.AddComponent<DamageOperator>();
            EnemyLogic.Ledger = DamageOperator.Ledger;
            host.gameObject.AddComponent<InteractionReceiver>().PlayerId = 7;
            Set(host, "intentSources", new MonoBehaviour[] { enemy });
            Set(host, "operators", new MonoBehaviour[] { damage });
            Set(host, "eventOutboxCapacity", 98);
            Set(host, "temporalHistoryCapacity", 4);
            Set(host, "operatorRecovery", new[] { new HostOperatorRecovery { component = damage, temporal = true } });
            yield return null;
            Assert.IsTrue(host.TryStart(out var diagnostic), diagnostic.Message);

            var service = new GameplayCoreService();
            CoCoServices.Register(service);
            var started = new List<int>();
            var directed = new List<EventPacket<CombatContactEvent>>();
            var events = new CoCoEventAgent();
            events.Subscribe((ref AttackStarted value) => started.Add(value.Window));
            events.Subscribe((ref EventPacket<CombatContactEvent> packet) => directed.Add(packet));
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
                Assert.AreEqual(0, EnemyLogic.PreviousLedger);
                Assert.AreEqual(1, EnemyLogic.RepeatedRead);
                Assert.IsFalse(EnemyLogic.LastBatch.TryRead(0, out _));
                CollectionAssert.AreEqual(new[] { 23 }, started);
                Assert.AreEqual(1, directed.Count);
                Assert.AreEqual(host.GraphInstanceId, directed[0].Envelope.TargetGraphInstanceId);
                Assert.AreEqual(23, directed[0].Payload.Window);

                CoCoEventBus.Publish(new Interaction { PlayerId = 7, Window = 51 });
                Assert.IsTrue(host.TryStep(.05, out diagnostic), diagnostic.Message);
                Assert.AreEqual(23, damage.PreviousLedger);
                Assert.AreEqual(23, EnemyLogic.PreviousLedger);
                Assert.AreEqual(23, EnemyLogic.LastContact);
                Assert.IsTrue(host.TryBeginTemporalPreview(out diagnostic), diagnostic.Message);
                Assert.IsTrue(host.TryPreviewTemporal(1, out diagnostic), diagnostic.Message);
                Assert.IsTrue(host.TryConfirmTemporalRestore(out diagnostic), diagnostic.Message);
                Assert.IsTrue(host.CurrentContext.Layout.TryResolveSlot(DamageOperator.Ledger, out StateSlot<int> ledger));
                Assert.AreEqual(23, host.CurrentContext.Read(ledger));
                Assert.AreEqual(23, damage.RestoredLedger);

                damage.Reject = true;
                CoCoEventBus.Publish(new Interaction { PlayerId = 7, Window = 87 });
                Assert.IsFalse(host.TryStep(.05, out _));
                Assert.AreEqual(23, EnemyLogic.PreviousLedger);
                Assert.AreEqual(23, damage.PreviousLedger);
                Assert.AreEqual(23, host.CurrentContext.Read(ledger));
                CollectionAssert.AreEqual(new[] { 23, 51 }, started);
                Assert.AreEqual(2, directed.Count);
            }
            finally {
                CoCoServices.Unregister(service); events.UnsubscribeAll(); }
        }

        [UnityTest]
        public IEnumerator CameraAndLocomotionUseTheSameHostStepAndOnlyApplyRequestedOutputs()
        {
            CameraProbeLogic.RequestShot = false;
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
            var cameraOperator = host.gameObject.AddComponent<CameraOperator>();
            Set(cameraOperator, "director", director);
            host.gameObject.AddComponent<CharacterController>();
            var movement = host.gameObject.AddComponent<LocomotionOperator>();
            Set(host, "operators", new MonoBehaviour[] { cameraOperator, movement });

            var child = new GameObject("Shot rig");
            child.transform.SetParent(host.transform, false);
            var rig = child.AddComponent<CameraRig>();
            var camera = child.AddComponent<CinemachineCamera>();
            Set(rig, "cameras", new List<CameraRigCameraEntry> { new CameraRigCameraEntry("Follow", camera) });
            Set(rig, "currentModeId", "Follow");
            rig.SetPriority(8);
            director.RegisterRig(rig);
            var rigMap = AddNumericMapping(director, "rigMappings");
            Set(rigMap, "rigId", 1);
            Set(rigMap, "rig", rig);
            var cameraMap = AddNumericMapping(rigMap, "cameras");
            Set(cameraMap, "cameraId", 2);
            Set(cameraMap, "modeId", "Follow");
            yield return null;
            Assert.IsTrue(host.TryStart(out var diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryStep(.05, out diagnostic), diagnostic.Message);
            Assert.AreEqual(14f, CameraProbeLogic.Sample.PositionX);
            Assert.That(CameraProbeLogic.Sample.PositionY, Is.EqualTo(8f).Within(0.01f));
            Assert.AreEqual(8, rig.Priority);
            float z = host.transform.position.z;
            CameraProbeLogic.RequestShot = true;
            Assert.IsTrue(host.TryStep(.05, out diagnostic), diagnostic.Message);
            Assert.AreEqual(20, rig.Priority);
            Assert.Greater(host.transform.position.z, z);
            rig.SetPriority(37);
            z = host.transform.position.z;
            CameraProbeLogic.RequestShot = false;
            Assert.IsTrue(host.TryStep(.05, out diagnostic), diagnostic.Message);
            Assert.AreEqual(37, rig.Priority);
            Assert.That(host.transform.position.z, Is.EqualTo(z).Within(0.001f));
        }

        [Test]
        public void MissingModeNamesTheSection()
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                new HostOperatorBuilder().Require<IMissingModeSection>());
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

        #endregion

        #region Internal Logic

        private readonly List<Object> _objects = new List<Object>();

        private static void Install()
        {
            var provider = HostBindingProvider.Build(new[] { typeof(EnemyLogic).Assembly, typeof(DamageOperator).Assembly,
                typeof(CameraOperator).Assembly, typeof(LocomotionOperator).Assembly });
            Assert.IsTrue(HostProjectBindings.TryInstall(provider, out var diagnostic), diagnostic.Message);
        }

        private StateGraphHost CreateHost<T>(string stateName) where T : StateLogic
        {
            StandardDescriptors.TryCreate(typeof(T), stateName, out var descriptor);
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var stateId = new SerializedId128(11, 12);
            var layer = new StateGraphLayerRecord(new SerializedId128(21, 22), "Base") { InitialStateId = stateId };
            layer.States.Add(new StateGraphStateRecord(stateId, default, stateName,
                new SerializedId128(descriptor.High, descriptor.Low), new EmptyStateConfig()));
            asset.Layers.Add(layer);
            var root = new GameObject(stateName);
            _objects.Add(root);
            var host = root.AddComponent<StateGraphHost>();
            Set(host, "stateGraphAsset", asset);
            Set(host, "autoStart", false);
            Set(host, "driver", StateGraphDriver.Manual);
            return host;
        }

        private static object AddNumericMapping(object owner, string fieldName)
        {
            var field = owner.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var type = field.FieldType.GetElementType();
            var mapping = Activator.CreateInstance(type, true);
            var values = Array.CreateInstance(type, 1);
            values.SetValue(mapping, 0);
            field.SetValue(owner, values);
            return mapping;
        }

        private static void Set(object instance, string field, object value) =>
            instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(instance, value);

        #endregion
    }
}
