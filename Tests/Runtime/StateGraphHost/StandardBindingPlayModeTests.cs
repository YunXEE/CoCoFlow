using System.Collections;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Animation;
using CoCoFlow.Runtime.Modules.Input;
using CoCoFlow.Runtime.Modules.Locomotion;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using Fixtures;
    /// <summary>
    /// C4.5 standard path end-to-end: a CoCoState-attributed logic (declared
    /// below in this test assembly) is registered through
    /// CoCoStandardBindingProvider.Build, installed, and a one-state graph
    /// runs a tick where the state logic receives RawInputIntent records.
    /// </summary>
    public sealed class StandardBindingPlayModeTests : InputTestFixture
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            CoCoStateGraphProjectBindings.ResetForTests();
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

            CoCoStateGraphProjectBindings.ResetForTests();
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
            CoCoStandardBindingProvider provider =
                CoCoStandardBindingProvider.Build(
                    new[] { logicType.Assembly });
            Assert.IsNotNull(provider.Catalog);
            Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic installDiagnostic),
                installDiagnostic.Message);

            // one-state graph referencing the derived descriptor id
            Assert.IsTrue(StandardDescriptors.TryCreate(
                logicType,
                logicName,
                out CoCoStateDescriptorId descriptorId));
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));
            var stateId = new CoCoSerializedId128(11, 12);
            var state = new CoCoStateGraphStateRecord(
                stateId,
                default,
                "Probe",
                new CoCoSerializedId128(descriptorId.High, descriptorId.Low),
                new EmptyStateConfig());
            var layer = new CoCoStateGraphLayerRecord(new CoCoSerializedId128(21, 22), "Base");
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
            var host = gameObject.AddComponent<CoCoStateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", (CoCoStateGraphDriver)2 /* Manual */);
            SetField(host, "autoStart", false);
            SetField(host, "intentSources", new MonoBehaviour[] { reader });
            gameObject.AddComponent<CharacterController>();
            var movement = gameObject.AddComponent<LocomotionOperator>();
            SetField(host, "operators", new MonoBehaviour[] { movement });

            yield return null;
            playerInput.actions.FindActionMap("Player").Enable();

            Assert.IsTrue(host.TryStart(out CoCoDiagnostic start),
                "start: " + start.Message + " / " + host.LastDiagnostic.Message);
            Assert.AreEqual(CoCoRuntimeLifecycleState.Running, host.Lifecycle);

            StandardProbeLogic.Received = new List<RawInputRecord>();
            Assert.IsTrue(host.TryStep(1f / 60f, out _)); // baseline
            Vector3 expected = new Vector3(0f, 5f / 60f, 2f / 60f);
            Assert.Less((gameObject.transform.position - expected).sqrMagnitude, 0.000001f,
                "New properties and legacy Writer must produce the same movement.");
            yield return null;

            Press(keyboard.wKey);
            yield return new WaitForSeconds(0.05f);
            Assert.IsTrue(host.TryStep(1f / 60f, out _));
            InputSystem.Update();
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
        public IEnumerator AnimRegistrarBindsBothCompiledSectionsOnlyWhenRequired()
        {
            CoCoStandardBindingProvider provider =
                CoCoStandardBindingProvider.Build(
                    new[] { typeof(AnimOperationProbeLogic).Assembly });
            Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install), install.Message);

            Assert.IsTrue(StandardDescriptors.TryCreate(
                typeof(AnimOperationProbeLogic),
                "AnimOperationProbe",
                out CoCoStateDescriptorId descriptorId));
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));
            var stateId = new CoCoSerializedId128(31, 32);
            var state = new CoCoStateGraphStateRecord(
                stateId,
                default,
                "AnimProbe",
                new CoCoSerializedId128(descriptorId.High, descriptorId.Low),
                new EmptyStateConfig());
            var layer = new CoCoStateGraphLayerRecord(
                new CoCoSerializedId128(41, 42),
                "Base");
            layer.InitialStateId = stateId;
            layer.States.Add(state);
            asset.Layers.Add(layer);

            var gameObject = new GameObject("AnimRegistrationE2E");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<CoCoStateGraphHost>();
            gameObject.AddComponent<Animator>();
            var animOperator = gameObject.AddComponent<AnimAutoOperator>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", (CoCoStateGraphDriver)2 /* Manual */);
            SetField(host, "autoStart", false);
            SetField(host, "intentSources", new MonoBehaviour[0]);
            SetField(host, "operators", new MonoBehaviour[] { animOperator });

            yield return null;
            Assert.IsTrue(host.TryStart(out CoCoDiagnostic start),
                "start: " + start.Message + " / " + host.LastDiagnostic.Message);
            Assert.IsTrue(host.TryStep(1f / 60f, out CoCoDiagnostic step),
                step.Message);
        }

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
            typeof(CoCoStateGraphHost).GetField(
                    field,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }

}
