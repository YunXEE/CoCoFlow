using CoCoFlow.Runtime.Input.Contracts;
using System.Collections;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Modules.Input;
using CoCoFlow.Runtime.Modules.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    using Fixtures;
    /// <summary>
    /// C4.5 standard path end-to-end: a State-attributed logic (declared
    /// below in this test assembly) is registered through
    /// HostBindingProvider.Build, installed, and a one-state graph
    /// runs a tick where the state logic receives InputIntentContract records.
    /// </summary>
    public sealed partial class StandardBindingPlayModeTests : InputTestFixture
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            HostProjectBindings.ResetForTests();
            StandardProbeLogic.Received = null;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _objects)
            {
                if (obj != null)
                {
                    Object.Destroy(obj);
                }
            }

            HostProjectBindings.ResetForTests();
            StandardProbeLogic.Received = null;
        }

        [UnityTest]
        public IEnumerator AttributedStateRunsThroughStandardBinding() => RunInputState(false);

        [UnityTest]
        public IEnumerator PropertyStateReadsInputAndMovesThroughTheExistingOperator() => RunInputState(true);

        private IEnumerator RunInputState(bool properties)
        {
            System.Type logicType = properties ? typeof(PropertyProbeLogic) : typeof(StandardProbeLogic);
            string logicName = properties ? "PropertyProbe" : "StandardProbe";
            HostBindingProvider provider =
                HostBindingProvider.Build(
                    new[] { logicType.Assembly });
            Assert.IsNotNull(provider.Catalog);
            Assert.IsTrue(HostProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic installDiagnostic),
                installDiagnostic.Message);

            // one-state graph referencing the derived descriptor id
            Assert.IsTrue(StandardDescriptors.TryCreate(
                logicType,
                logicName,
                out StateDescriptorId descriptorId));
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));
            var stateId = new SerializedId128(11, 12);
            var state = new StateGraphStateRecord(
                stateId,
                default,
                "Probe",
                new SerializedId128(descriptorId.High, descriptorId.Low),
                new EmptyStateConfig());
            var layer = new StateGraphLayerRecord(new SerializedId128(21, 22), "Base");
            layer.InitialStateId = stateId;
            layer.States.Add(state);
            asset.Layers.Add(layer);

            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            var gameObject = new GameObject("StandardE2E");
            _objects.Add(gameObject);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            playerInput.actions = CreateActions();
            playerInput.defaultActionMap = "Player";
            var reader = gameObject.AddComponent<InputReader>();
            var input = gameObject.AddComponent<InputIntent>();
            input.SetReader(reader);
            var host = gameObject.AddComponent<StateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", (StateGraphDriver)2 /* Manual */);
            SetField(host, "autoStart", false);
            SetField(host, "intentSources", new MonoBehaviour[] { input });
            gameObject.AddComponent<CharacterController>();
            var movement = gameObject.AddComponent<LocomotionOperator>();
            SetField(host, "operators", new MonoBehaviour[] { movement });

            yield return null;
            playerInput.actions.FindActionMap("Player").Enable();

            StateGraphAsset[] originalGraphs = Resources.FindObjectsOfTypeAll<StateGraphAsset>();
            Assert.IsTrue(host.TryStart(out CoCoDiagnostic start),
                "start: " + start.Message + " / " + host.LastDiagnostic.Message);
            Assert.AreEqual(RuntimeLifecycleState.Running, host.Lifecycle);

            StandardProbeLogic.Received = new List<RawInputRecord>();
            Assert.IsTrue(host.TryStep(1f / 60f, out _)); // baseline
            Vector3 expected = new Vector3(0f, 5f / 60f, 2f / 60f);
            Assert.Less((gameObject.transform.position - expected).sqrMagnitude, 0.000001f,
                "New properties and legacy Writer must produce the same movement.");
            yield return null;

            CollectionAssert.AreEquivalent(originalGraphs, Resources.FindObjectsOfTypeAll<StateGraphAsset>(),
                "Configured copies must be reclaimed after the PlayMode destruction frame.");
            Press(keyboard.wKey);
            InputSystem.Update(); // Resolve initial state checks before the manual Host Tick.
            Assert.IsTrue(host.TryStep(1f / 60f, out _));
            yield return null;

            Assert.IsNotEmpty(
                StandardProbeLogic.Received,
                "attributed state should receive raw records through the " +
                "standard binding");
            Assert.IsTrue(StandardProbeLogic.Received.Exists(
                r => r.Action.ToString() == "Move"));
            if (properties)
            {
                Assert.Greater(gameObject.transform.position.z, 0f);
                Assert.Greater(gameObject.transform.position.y, 0f);
                Assert.IsNotNull(PropertyProbeLogic.Last.Movement);
                StandardProbeLogic.Received = null;
                for (int index = 0; index < 100; index++) host.TryStep(1f / 60f, out _);
                long before = System.GC.GetAllocatedBytesForCurrentThread();
                bool stepped = true;
                for (int index = 0; index < 10000; index++) stepped &= host.TryStep(1f / 60f, out _);
                long allocated = System.GC.GetAllocatedBytesForCurrentThread() - before;
                Assert.IsTrue(stepped);
                Assert.AreEqual(0L, allocated, "Property binding must allocate only during initialization.");
                PropertyProbeLogic.Last = null;
            }
        }

        [UnityTest]
        public IEnumerator MovementOperationBindsWithoutAnIntentSource()
        {
            HostBindingProvider provider =
                HostBindingProvider.Build(
                    new[] { typeof(LocoMoveProbeLogic).Assembly });
            Assert.IsTrue(HostProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install), install.Message);

            Assert.IsTrue(StandardDescriptors.TryCreate(
                typeof(LocoMoveProbeLogic),
                "LocoMoveProbe",
                out StateDescriptorId descriptorId));
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));
            var stateId = new SerializedId128(31, 32);
            var state = new StateGraphStateRecord(
                stateId,
                default,
                "MoveProbe",
                new SerializedId128(descriptorId.High, descriptorId.Low),
                new EmptyStateConfig());
            var layer = new StateGraphLayerRecord(
                new SerializedId128(41, 42),
                "Base");
            layer.InitialStateId = stateId;
            layer.States.Add(state);
            asset.Layers.Add(layer);

            var gameObject = new GameObject("MovementRegistrationE2E");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<StateGraphHost>();
            gameObject.AddComponent<CharacterController>();
            var movement = gameObject.AddComponent<LocomotionOperator>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", (StateGraphDriver)2 /* Manual */);
            SetField(host, "autoStart", false);
            SetField(host, "intentSources", new MonoBehaviour[0]);
            SetField(host, "operators", new MonoBehaviour[] { movement });

            yield return null;
            Assert.IsTrue(host.TryStart(out CoCoDiagnostic start),
                "start: " + start.Message + " / " + host.LastDiagnostic.Message);
            Assert.IsTrue(host.TryStep(1f / 60f, out CoCoDiagnostic step),
                step.Message);
        }

        [UnityTest]
        public IEnumerator ConfigAndMemoryFieldsUseTheRealHost()
        {
            var type = System.Type.GetType("MoveLogic, CoCoFlow002") ?? typeof(ConfigMemoryProbeLogic);
            var provider = HostBindingProvider.Build(new[] { type.Assembly });
            HostProjectBindings.ResetForTests();
            Assert.IsTrue(HostProjectBindings.TryInstall(provider, out _));
            var name = ((StateAttribute)System.Attribute.GetCustomAttribute(type, typeof(StateAttribute))).Name;
            StandardDescriptors.TryCreate(type, name, out var descriptor);
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));
            for (ulong i = 0; i < 10; i++)
            {
                var config = new FieldConfig();
                if (i != 1) config.Set("Speed", (float)(3 + i * 3));
                var stateId = new SerializedId128(11 + i, 12);
                var layer = new StateGraphLayerRecord(new SerializedId128(21 + i, 22), "Layer " + i);
                layer.InitialStateId = stateId;
                layer.States.Add(new StateGraphStateRecord(stateId, default, "Move " + i,
                    new SerializedId128(descriptor.High, descriptor.Low), config));
                asset.Layers.Add(layer);
            }
            var go = new GameObject("Config Memory - MoveLogic");
            var host = go.AddComponent<StateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "autoStart", false);
            SetField(host, "driver", StateGraphDriver.Manual);
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEditor.AssetDatabase.CreateAsset(asset, "Assets/CoCoFlow002/MoveGraph.asset");
                UnityEditor.SceneManagement.EditorSceneManager.SaveScene(go.scene, "Assets/CoCoFlow002/MoveConfig.unity");
                UnityEditor.Selection.activeGameObject = go;
                yield break;
            }
#endif
            SetField(host, "temporalHistoryCapacity", 8);
            _objects.Add(go);
            _objects.Add(asset);
            var seen = (IList)type.GetField("Seen").GetValue(null);
            seen.Clear();
            type.GetField("Impulses").SetValue(null, 0);
            yield return null;
            Assert.IsTrue(host.TryStart(out var diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
            Assert.AreEqual(10, seen.Count);
            for (int i = 0; i < 10; i++)
                Assert.AreEqual((float)(3 + i * 3), type.GetField("Speed").GetValue(seen[i]));
            Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryBeginTemporalPreview(out diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryPreviewTemporal(1, out diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryConfirmTemporalRestore(out diagnostic), diagnostic.Message);
            Assert.IsTrue(host.TryStep(0.1, out diagnostic), diagnostic.Message);
            Assert.AreEqual(10, type.GetField("Impulses").GetValue(null));
            Assert.That((float)type.GetField("Distance").GetValue(seen[0]), Is.EqualTo(0.6f).Within(0.0001f));
        }

        [TestCase(typeof(ConfigMemoryProbeLogic))]
        [TestCase(typeof(ConfigOnlyProbe))]
        [TestCase(typeof(MemoryOnlyProbe))]
        public void FieldMemoryRoundTripDoesNotAllocate(System.Type type)
        {
            HostBindingProvider.Build(new[] { type.Assembly });
            var logic = (StateLogic)System.Activator.CreateInstance(type);
            var memory = (IFieldMemory)System.Activator.CreateInstance(
                typeof(FieldMemory<,>).MakeGenericType(type, StateFields.StoredType(type)));
            var fields = StateFields.Fields(type, typeof(StateMemoryAttribute));
            foreach (var field in fields) field.SetValue(logic, field.FieldType == typeof(bool) ? (object)true : 9f);
            memory.Store(logic);
            foreach (var field in fields) field.SetValue(logic, System.Activator.CreateInstance(field.FieldType));
            memory.Load(logic);
            foreach (var field in fields) Assert.AreEqual(field.FieldType == typeof(bool) ? (object)true : 9f, field.GetValue(logic));
            for (int i = 0; i < 100; i++) { memory.Load(logic); memory.Store(logic); }
            long before = System.GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) { memory.Load(logic); memory.Store(logic); }
            Assert.AreEqual(0, System.GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.IsNotNull(typeof(HostBindingProvider).GetMethod("Build",
                new[] { typeof(IReadOnlyList<System.Reflection.Assembly>) }));
        }

        [TestCase("RemovedSpeed", "3")]
        [TestCase("Speed", "invalid")]
        [TestCase("Distance", "2")]
        public void InvalidConfigFailsDuringFreezing(string path, string value)
        {
            var provider = HostBindingProvider.Build(new[] { typeof(ConfigMemoryProbeLogic).Assembly });
            StandardDescriptors.TryCreate(typeof(ConfigMemoryProbeLogic), "ConfigMemoryProbeLogic", out var id);
            Assert.IsFalse(provider.Catalog.TryFreezeStateConfig(id,
                new FieldConfig { Values = new[] { path + "=" + value } }, out _, out var diagnostic));
            Assert.IsFalse(diagnostic.IsNone);
        }

        public static void PrepareConfigMemoryScene() => new StandardBindingPlayModeTests().ConfigAndMemoryFieldsUseTheRealHost().MoveNext();

        private static InputActionAsset CreateActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");
            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.AddBinding("<Keyboard>/w", null, null, "W");
            return asset;
        }

        private static void SetField(object target, string field, object value)
        {
            typeof(StateGraphHost).GetField(
                    field,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }

}
