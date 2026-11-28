using CoCoFlow.Runtime.Input.Contracts;
using System.Collections;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Modules.Input;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class RawInputEndToEndPlayModeTests : InputTestFixture
    {
        private const string SubmitAction = "Submit";
        private static readonly ulong High = 0x524157494E505242UL;

        private readonly List<Object> _objects = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            HostProjectBindings.ResetForTests();
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

            RawInputProbeCapture.RejectAfterReading = false;
            RawInputProbeCapture.Records = null;
            HostProjectBindings.ResetForTests();
        }

        [UnityTest]
        public IEnumerator ReaderRawIntentFlowsIntoStateLogicPerTick()
        {
            IntentId intentId = MakeIntentId(1);
            StateDescriptorId descriptorId = MakeDescriptorId(1);

            var provider = new ProbeProvider(intentId, descriptorId);
            Require(HostProjectBindings.TryInstall(
                provider,
                out CoCoDiagnostic installDiagnostic));

            StateGraphAsset asset = CreateSingleStateAsset(intentId, descriptorId);
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();

            var gameObject = new GameObject("RawInputE2E");
            _objects.Add(gameObject);
            var playerInput = gameObject.AddComponent<PlayerInput>();
            InputActionAsset actions = CreateActions();
            _objects.Add(actions);
            playerInput.actions = actions;
            playerInput.defaultActionMap = "Player";
            var reader = gameObject.AddComponent<InputReader>();
            var input = gameObject.AddComponent<InputIntent>();
            input.SetReader(reader);
            var host = gameObject.AddComponent<StateGraphHost>();
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", StateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "intentSources", new MonoBehaviour[] { input });

            yield return null;

            playerInput.actions.FindActionMap("Player").Enable();

            Require(host.TryStart(out CoCoDiagnostic startDiagnostic),
                "host start: " + startDiagnostic.Message);
            Assert.AreEqual(
                RuntimeLifecycleState.Running,
                host.Lifecycle,
                host.LastDiagnostic.Message);

            var records = new List<RawInputRecord>();
            RawInputProbeCapture.Records = records;
            Require(host.TryStep(1f / 60f, out CoCoDiagnostic baseline), baseline.Message);
            records.Clear();

            Press(keyboard.wKey);
            InputSystem.Update();

            Press(keyboard.spaceKey);
            InputSystem.Update();
            Release(keyboard.spaceKey);
            InputSystem.Update();

            Require(host.TryStep(1f / 60f, out CoCoDiagnostic delivery), delivery.Message);
            Assert.That(records.Exists(record => record.Action.ToString() == "Move"), Is.True,
                "State logic should receive the Move record");

            List<RawInputRecord> submitRecords =
                records.FindAll(record => record.Action.ToString() == SubmitAction);
            Assert.That(submitRecords, Has.Count.EqualTo(2),
                "A short press should yield exactly two changes and no Held record");
            Assert.That(submitRecords[0].Phase, Is.EqualTo(RawInputPhase.Performed));
            Assert.That(submitRecords[1].Phase, Is.EqualTo(RawInputPhase.Canceled));
            Assert.That(submitRecords[1].Sequence, Is.GreaterThan(submitRecords[0].Sequence));

            records.Clear();
            Require(host.TryStep(1f / 60f, out CoCoDiagnostic following), following.Message);
            Assert.That(records.Exists(record => record.Action.ToString() == SubmitAction), Is.False,
                "The next successful tick should not replay the committed short press");

            Press(keyboard.spaceKey);
            InputSystem.Update();
            Release(keyboard.spaceKey);
            InputSystem.Update();
            records.Clear();
            RawInputProbeCapture.RejectAfterReading = true;
            ContextRevision beforeFailure = host.CurrentContext.Revision;
            Assert.That(host.TryStep(1f / 60f, out var rejected), Is.False);
            Assert.That(rejected.IsError, Is.True);
            Assert.That(host.CurrentContext.Revision, Is.EqualTo(beforeFailure));
            var sampled = records.FindAll(record => record.Action.ToString() == SubmitAction);
            Assert.That(sampled, Has.Count.EqualTo(2));

            InputSystem.Update();
            Assert.That(input.TrySample(default, out InputIntentContract remaining), Is.True);
            var pending = new List<ulong>();
            foreach (var record in remaining.Changes.ToArray())
                if (record.Action.ToString() == SubmitAction) pending.Add(record.Sequence);
            CollectionAssert.AreEqual(new[] { sampled[0].Sequence, sampled[1].Sequence }, pending);
        }

        private static InputActionAsset CreateActions()
        {
            var asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = asset.AddActionMap("Player");
            InputAction move = map.AddAction("Move", InputActionType.Value);
            move.AddBinding("<Keyboard>/w", null, null, "W");
            map.AddAction(SubmitAction, InputActionType.Button, "<Keyboard>/space");
            return asset;
        }

        private StateGraphAsset CreateSingleStateAsset(
            IntentId intentId,
            StateDescriptorId descriptorId)
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));

            var stateId = new SerializedId128(11, 12);
            var state = new StateGraphStateRecord(
                stateId,
                default,
                "Idle",
                new SerializedId128(descriptorId.High, descriptorId.Low),
                new RawInputProbeConfig());
            var layerId = new SerializedId128(21, 22);
            var layer = new StateGraphLayerRecord(layerId, "Base");
            layer.InitialStateId = stateId;
            layer.States.Add(state);
            asset.Layers.Add(layer);
            return asset;
        }

        private static IntentId MakeIntentId(ulong low)
        {
            Require(IntentId.TryCreate(High, low, out IntentId id));
            return id;
        }

        private static StateDescriptorId MakeDescriptorId(ulong low)
        {
            Require(StateDescriptorId.TryCreate(High, low, out StateDescriptorId id));
            return id;
        }

        private static void SetField(object target, string field, object value)
        {
            typeof(StateGraphHost).GetField(
                    field,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static void Require(bool condition, string message = null)
        {
            Assert.IsTrue(condition, message);
        }

        private sealed class ProbeProvider : IHostBindingProvider
        {
            private const ulong GraphStateDefaultFingerprint = 51471UL;
            private static readonly StateBlockId GraphStateBlockId =
                MakeBlockId(31);
            private static readonly StateSlotId GraphStateSlotId =
                MakeSlotId(32);
            private static readonly LayerId ProbeLayerId;
            private static readonly StateId ProbeStateId;

            static ProbeProvider()
            {
                Require(LayerId.TryCreate(21, 22, out LayerId layer));
                ProbeLayerId = layer;
                Require(StateId.TryCreate(11, 12, out StateId state));
                ProbeStateId = state;
            }

            private static StateBlockId MakeBlockId(ulong low)
            {
                Require(StateBlockId.TryCreate(High, low, out StateBlockId id));
                return id;
            }

            private static StateSlotId MakeSlotId(ulong low)
            {
                Require(StateSlotId.TryCreate(High, low, out StateSlotId id));
                return id;
            }

            private static GraphStateRecord<int> CreateDefaultGraphState()
            {
                Require(ActivationId.TryCreate(1UL, out ActivationId activationId));
                Require(GraphStateRecord<int>.TryCreate(
                    ProbeLayerId,
                    ProbeStateId,
                    true,
                    activationId,
                    0d,
                    0d,
                    true,
                    0UL,
                    0,
                    out GraphStateRecord<int> state));
                return state;
            }

            private readonly IntentId _intentId;
            private readonly StateDescriptorId _descriptorId;
            private readonly GraphDescriptorCatalog _catalog;

            public ProbeProvider(IntentId intentId, StateDescriptorId descriptorId)
            {
                _intentId = intentId;
                _descriptorId = descriptorId;

                var builder = new GraphDescriptorCatalogBuilder();
                CoCoDiagnostic stateDiagnostic = CoCoDiagnostic.None;
                if (!builder.TryRegisterIntent(
                        _intentId,
                        2,
                        new IntentReducerFactoryToken<
                            InputIntentContract,
                            RawInputProbeReducer,
                            RawInputProbeReducerFactory>(1UL),
                        out CoCoDiagnostic intentDiagnostic) ||
                    !builder.TryRegisterStateBlock(
                        GraphStateBlockId,
                        StateBlockOwner.Graph,
                        out CoCoDiagnostic blockDiagnostic) ||
                    !builder.TryRegisterStateSlot(
                        GraphStateBlockId,
                        GraphStateSlotId,
                        ContextProjection.Temporal,
                        ContextRestorePolicy.Stored,
                        CreateDefaultGraphState(),
                        GraphStateDefaultFingerprint,
                        default,
                        null,
                        out CoCoDiagnostic slotDiagnostic) ||
                    !builder.TryRegisterState(
                        _descriptorId,
                        1U,
                        new RawInputProbeConfigFreezer(),
                        new StateRuntimeRegistration<
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

            public GraphDescriptorCatalog Catalog => _catalog;

            public bool TryConfigure(
                HostBindingBuilder bindingBuilder,
                out CoCoDiagnostic diagnostic)
            {
                if (!bindingBuilder.TryRegisterIntent<
                        InputIntentContract, RawInputProbeReducer, RawInputProbeReducerFactory>(
                        _intentId,
                        new RawInputProbeReducerFactory(),
                        1UL,
                        out IntentHandle<InputIntentContract> intent,
                        out diagnostic) ||
                    !bindingBuilder.TryBeginIntentBindings(out diagnostic))
                {
                    return false;
                }

                if (!IntentSourceRequirement<InputIntentContract>.TryCreate(
                        intent,
                        1,
                        out IntentSourceRequirement<InputIntentContract> requirement) ||
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

                var factory = new StateRuntimeFactory<RawInputProbeLogic, RawInputProbeMemory>(
                    context => new RawInputProbeLogic(context, intent),
                    () => new RawInputProbeMemory(),
                    (s, d) => { },
                    m => { },
                    memory => 0UL);
                return bindingBuilder.TryBindState(_descriptorId, factory, out diagnostic);
            }
        }
    }
}
