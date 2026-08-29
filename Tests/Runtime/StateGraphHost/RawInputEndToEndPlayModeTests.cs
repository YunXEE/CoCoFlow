using System.Collections;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Input;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    /// <summary>
    /// C4 end-to-end: InputReader (raw channel) -> IntentFrame ->
    /// StateGraph (one Idle state) -> RawInputProbeLogic captures records.
    /// Host driven manually via TryStep. Graph types live in Fixtures.
    /// </summary>
    public sealed class RawInputEndToEndPlayModeTests : InputTestFixture
    {
        private static readonly ulong High = 0x524157494E505242UL; // "RAWINPRB"

        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            CoCoStateGraphProjectBindings.ResetForTests();
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

            RawInputProbeCapture.Records = null;
            CoCoStateGraphProjectBindings.ResetForTests();
        }

        [UnityTest]
        public IEnumerator ReaderRawIntentFlowsIntoStateLogicPerTick()
        {
            CoCoIntentId intentId = MakeIntentId(1);
            CoCoStateDescriptorId descriptorId = MakeDescriptorId(1);

            var provider = new ProbeProvider(intentId, descriptorId);
            Require(CoCoStateGraphProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic installDiagnostic));

            CoCoStateGraphAsset asset = CreateSingleStateAsset(intentId, descriptorId);
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

            var gameObject = new GameObject("RawInputE2E");
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

            yield return null; // Awake/Start

            // InputTestFixture has no paired user; enable the map directly so
            // action callbacks fire (PlayerInput pairing is not under test).
            playerInput.actions.FindActionMap("Player").Enable();

            Require(host.TryStart(out CoCoDiagnostic startDiagnostic),
                "host start: " + startDiagnostic.Message);
            Assert.AreEqual(
                CoCoRuntimeLifecycleState.Running,
                host.Lifecycle,
                host.LastDiagnostic.Message);

            // --- baseline tick: reader sampled, no records yet ---
            RawInputProbeCapture.Records = new List<RawInputRecord>();
            Require(host.TryStep(1f / 60f, out _));
            yield return null;

            // diagnostics: sample the reader directly
            Require(reader.TrySample(default(CoCoTickFrame), out RawInputIntent directIntent));
            Debug.Log($"[E2E] direct sample: enabled={reader.isActiveAndEnabled} count={directIntent.Count} map={directIntent.ActiveMap}");
            for (int i = 0; i < directIntent.Count; i++)
            {
                if (directIntent.TryGet(i, out RawInputRecord rec))
                {
                    Debug.Log($"[E2E] direct[{i}] {rec.Action} {rec.Phase}");
                }
            }

            // --- hold W: Move contributes a Held record every sample ---
            Press(keyboard.wKey);
            yield return new WaitForSeconds(0.05f);
            Require(host.TryStep(1f / 60f, out _));
            InputSystem.Update();
            yield return null;

            Debug.Log("[E2E] after W tick: " + RawInputProbeCapture.Records.Count + " records");
            foreach (RawInputRecord record in RawInputProbeCapture.Records)
            {
                Debug.Log($"[E2E] {record.Action} {record.Phase} ({record.ValueX},{record.ValueY}) #{record.Sequence}");
            }

            Assert.IsNotEmpty(
                RawInputProbeCapture.Records,
                "state logic should receive raw records");
            Assert.IsTrue(
                RawInputProbeCapture.Records.Exists(r => r.Action.ToString() == "Move"),
                "Move record expected: " + Describe(RawInputProbeCapture.Records));

            Object.Destroy(gameObject);
        }

        private static string Describe(List<RawInputRecord> records) =>
            string.Join(", ", records.ConvertAll(r => r.Action + "/" + r.Phase));

        private static InputActionAsset CreateActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");
            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.AddBinding("<Keyboard>/w", null, null, "W");
            return asset;
        }

        private CoCoStateGraphAsset CreateSingleStateAsset(
            CoCoIntentId intentId,
            CoCoStateDescriptorId descriptorId)
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));

            var stateId = new CoCoSerializedId128(11, 12);
            var state = new CoCoStateGraphStateRecord(
                stateId,
                default,
                "Idle",
                new CoCoSerializedId128(descriptorId.High, descriptorId.Low),
                new RawInputProbeConfig());
            var layerId = new CoCoSerializedId128(21, 22);
            var layer = new CoCoStateGraphLayerRecord(layerId, "Base");
            layer.InitialStateId = stateId;
            layer.States.Add(state);
            asset.Layers.Add(layer);
            return asset;
        }

        private static CoCoIntentId MakeIntentId(ulong low)
        {
            Require(CoCoIntentId.TryCreate(High, low, out CoCoIntentId id));
            return id;
        }

        private static CoCoStateDescriptorId MakeDescriptorId(ulong low)
        {
            Require(CoCoStateDescriptorId.TryCreate(High, low, out CoCoStateDescriptorId id));
            return id;
        }

        private static void SetField(object target, string field, object value)
        {
            typeof(CoCoStateGraphHost).GetField(
                    field,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static void Require(bool condition, string message = null)
        {
            Assert.IsTrue(condition, message);
        }

        private sealed class ProbeProvider : ICoCoStateGraphProjectBindingProvider
        {
            private const ulong GraphStateDefaultFingerprint = 51471UL;
            private static readonly CoCoStateBlockId GraphStateBlockId =
                MakeBlockId(31);
            private static readonly CoCoStateSlotId GraphStateSlotId =
                MakeSlotId(32);
            private static readonly CoCoLayerId ProbeLayerId;
            private static readonly CoCoStateId ProbeStateId;

            static ProbeProvider()
            {
                Require(CoCoLayerId.TryCreate(21, 22, out CoCoLayerId layer));
                ProbeLayerId = layer;
                Require(CoCoStateId.TryCreate(11, 12, out CoCoStateId state));
                ProbeStateId = state;
            }

            private static CoCoStateBlockId MakeBlockId(ulong low)
            {
                Require(CoCoStateBlockId.TryCreate(High, low, out CoCoStateBlockId id));
                return id;
            }

            private static CoCoStateSlotId MakeSlotId(ulong low)
            {
                Require(CoCoStateSlotId.TryCreate(High, low, out CoCoStateSlotId id));
                return id;
            }

            private static CoCoGraphStateRecord<int> CreateDefaultGraphState()
            {
                Require(CoCoActivationId.TryCreate(1UL, out CoCoActivationId activationId));
                Require(CoCoGraphStateRecord<int>.TryCreate(
                    ProbeLayerId,
                    ProbeStateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    0,
                    out CoCoGraphStateRecord<int> state));
                return state;
            }

            private readonly CoCoIntentId _intentId;
            private readonly CoCoStateDescriptorId _descriptorId;
            private readonly CoCoGraphDescriptorCatalog _catalog;

            public ProbeProvider(CoCoIntentId intentId, CoCoStateDescriptorId descriptorId)
            {
                _intentId = intentId;
                _descriptorId = descriptorId;

                var builder = new CoCoGraphDescriptorCatalogBuilder();
                CoCoDiagnostic stateDiagnostic = CoCoDiagnostic.None;
                if (!builder.TryRegisterIntent(
                        _intentId,
                        2,
                        new CoCoIntentReducerFactoryToken<
                            RawInputIntent,
                            RawInputProbeReducer,
                            RawInputProbeReducerFactory>(1UL),
                        out CoCoDiagnostic intentDiagnostic) ||
                    !builder.TryRegisterStateBlock(
                        GraphStateBlockId,
                        CoCoStateBlockOwner.Graph,
                        out CoCoDiagnostic blockDiagnostic) ||
                    !builder.TryRegisterStateSlot(
                        GraphStateBlockId,
                        GraphStateSlotId,
                        CoCoContextProjection.Temporal,
                        CoCoContextRestorePolicy.Stored,
                        CreateDefaultGraphState(),
                        GraphStateDefaultFingerprint,
                        default,
                        null,
                        out CoCoDiagnostic slotDiagnostic) ||
                    !builder.TryRegisterState(
                        _descriptorId,
                        1U,
                        new RawInputProbeConfigFreezer(),
                        new CoCoStateRuntimeRegistration<
                            RawInputProbeLogic,
                            RawInputProbeConfigSchema,
                            RawInputProbeMemory>(RawInputProbeSchemas.State),
                        new[] { _intentId },
                        null,
                        new[] { GraphStateBlockId },
                        out stateDiagnostic))
                {
                    Assert.Fail(
                        "catalog: " +
                        (intentDiagnostic.IsError ? intentDiagnostic.Message : stateDiagnostic.Message));
                }

                Require(builder.TryFreeze(out _catalog, out CoCoDiagnostic freeze));
            }

            public CoCoGraphDescriptorCatalog Catalog => _catalog;

            public bool TryConfigure(
                CoCoStateGraphHostBindingBuilder bindingBuilder,
                out CoCoDiagnostic diagnostic)
            {
                if (!bindingBuilder.TryRegisterIntent<
                        RawInputIntent, RawInputProbeReducer, RawInputProbeReducerFactory>(
                        _intentId,
                        new RawInputProbeReducerFactory(),
                        1UL,
                        out CoCoIntentHandle<RawInputIntent> intent,
                        out diagnostic) ||
                    !bindingBuilder.TryBeginIntentBindings(out diagnostic))
                {
                    return false;
                }

                if (!CoCoIntentSourceRequirement<RawInputIntent>.TryCreate(
                        intent,
                        1,
                        out CoCoIntentSourceRequirement<RawInputIntent> requirement) ||
                    !bindingBuilder.TryBindIntentSource(0, requirement, out diagnostic) ||
                    !bindingBuilder.TryBindGraphStateSlot<
                        RawInputProbeMemory,
                        int,
                        RawInputProbeMemoryBinding>(
                        ProbeLayerId,
                        ProbeStateId,
                        GraphStateBlockId,
                        GraphStateSlotId,
                        CreateDefaultGraphState(),
                        GraphStateDefaultFingerprint,
                        new RawInputProbeMemoryBinding(),
                        out diagnostic))
                {
                    return false;
                }

                var factory = new CoCoStateRuntimeFactory<RawInputProbeLogic, RawInputProbeMemory>(
                    context => new RawInputProbeLogic(context, intent),
                    () => new RawInputProbeMemory(),
                    (s, d) => { },
                    m => { },
                    memory => 0UL); // must equal the trusted default's fingerprint
                return bindingBuilder.TryBindState(_descriptorId, factory, out diagnostic);
            }
        }
    }
}
