using System.Collections;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.Modules.Locomotion;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    /// <summary>
    /// C6.5 end-to-end: standard binding auto-registers the locomotion
    /// section (via LocomotionSectionRegistrar on the operator), an
    /// attributed state writes the section through the typed field
    /// resolver, and LocomotionOperator moves the actor through the
    /// engine-fact segment into the committed slot.
    /// </summary>
    public sealed class LocomotionSectionE2EPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            CoCoStateGraphProjectBindings.ResetForTests();
            LocoMoveProbeLogic.LastDelta = null;
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
        }

        [UnityTest]
        public IEnumerator SectionAutoRegistrationMovesActorThroughOperator()
        {
            // Standard provider scans the fixture assembly: state probe +
            // operator sections (the LocomotionOperator attribute points at
            // the registrar; the fixture assembly sees it via package
            // reference).
            CoCoStandardBindingProvider provider =
                CoCoStandardBindingProvider.Build(
                    new[] { typeof(LocoMoveProbeLogic).Assembly });
            Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic install), install.Message);

            Assert.IsTrue(StandardDescriptors.TryCreate(
                typeof(LocoMoveProbeLogic),
                "LocoMoveProbe",
                out CoCoStateDescriptorId descriptorId));
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));
            var stateId = new CoCoSerializedId128(11, 12);
            var state = new CoCoStateGraphStateRecord(
                stateId, default, "Probe",
                new CoCoSerializedId128(descriptorId.High, descriptorId.Low),
                new EmptyStateConfig());
            var layer = new CoCoStateGraphLayerRecord(
                new CoCoSerializedId128(21, 22), "Base");
            layer.InitialStateId = stateId;
            layer.States.Add(state);
            asset.Layers.Add(layer);

            var gameObject = new GameObject("LocoE2E");
            _objects.Add(gameObject);
            var host = gameObject.AddComponent<CoCoStateGraphHost>();
            var controller = gameObject.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.center = new Vector3(0f, 1f, 0f);
            var locomotion = gameObject.AddComponent<LocomotionOperator>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", (CoCoStateGraphDriver)2 /* Manual */);
            SetField(host, "autoStart", false);
            SetField(host, "intentSources", new MonoBehaviour[0]);
            SetField(host, "operators", new MonoBehaviour[] { locomotion });

            yield return null;
            Assert.IsTrue(host.TryStart(out CoCoDiagnostic start),
                "start: " + start.Message + " / " + host.LastDiagnostic.Message);
            Assert.AreEqual(CoCoRuntimeLifecycleState.Running, host.Lifecycle);

            float before = gameObject.transform.position.z;
            Assert.IsTrue(host.TryStep(0.0167f, out _)); // writes MoveZ=2 → delta ≈ 0.033
            yield return null;

            float after = gameObject.transform.position.z;
            Debug.Log($"[LocoE2E] z {before:F4} → {after:F4}, lastDelta={LocoMoveProbeLogic.LastDelta}");
            Assert.Greater(after, before + 0.01f, "actor should have moved forward");
            Assert.IsNotNull(LocoMoveProbeLogic.LastDelta, "state should have written the section");
            CoCoContextFrame committed = host.CurrentContext;
            Assert.IsTrue(committed.IsAlive, "the tick should commit a ContextFrame");
            Assert.IsTrue(committed.Layout.TryResolveSlot(
                LocoContractIds.StateSlotId,
                out CoCoStateSlot<LocomotionState> locoSlot));
            LocomotionState facts = committed.Read(locoSlot);
            Assert.AreEqual(after, facts.PositionZ, 0.0001f,
                "the committed locomotion facts must match the engine Transform");
            Object.Destroy(gameObject);
        }

        private static void SetField(object target, string field, object value)
        {
            target.GetType().GetField(
                    field,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }
    }

}
