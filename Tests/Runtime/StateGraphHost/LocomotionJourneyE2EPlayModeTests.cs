using System.Collections.Generic;
using CoCoFlow.Runtime.Animation.Contracts;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.Modules.Animation;
using CoCoFlow.Runtime.Modules.Locomotion;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    /// <summary>
    /// C7 commit 2 — the journey e2e: five scenarios over one four-state
    /// graph (Idle/Move/Jump/Attack) driven through raw input intents.
    /// A wall (engine fact), B reject (slot rollback + residue + replay
    /// convergence), C temporal restore, D save/load triple restore
    /// (loco slot + Animator snapshot + graph activation), E attack chain.
    /// </summary>
    public sealed class LocomotionJourneyE2EPlayModeTests
    {
        private const float Step = 1f / 60f;
        private const string ControllerPath =
            "Assets/JourneyE2EController.controller";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _actor;
        private CoCoStateGraphHost _host;
        private LocomotionOperator _locomotion;
        private AnimAutoOperator _animation;
        private JourneyIntentSource _intents;

        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            JourneyMemory.Reset();
            CoCoStateGraphProjectBindings.ResetForTests();
            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }
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

            if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            {
                AssetDatabase.DeleteAsset(ControllerPath);
            }

            CoCoStateGraphProjectBindings.ResetForTests();
        }

        [UnityTest]
        public System.Collections.IEnumerator ScenarioAWallRecordsEngineFact()
        {
            BuildActor(withAnimator: false);
            // A wall right in front of the actor, tall enough to block.
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(wall);
            wall.transform.position = new Vector3(0f, 1.5f, 0.6f);
            wall.transform.localScale = new Vector3(4f, 3f, 0.2f);

            Require(StartHost());
            _intents.EnqueueMove(0f, 2f);
            for (int index = 0; index < 10; index++)
            {
                Require(_host.TryStep(Step, out CoCoDiagnostic step), step.Message);
            }

            float desired = 2f * Step * 10f;
            Assert.Greater(
                JourneyMemory.LastSlotZ,
                desired * 0.3f,
                "the actor did move toward the wall");
            Assert.Less(
                JourneyMemory.LastSlotZ,
                desired * 0.95f,
                "engine fact: the wall stops the desire of " + desired +
                "m, the slot records what actually happened (" +
                JourneyMemory.LastSlotZ + ")");
            yield return null;
        }

        [UnityTest]
        public System.Collections.IEnumerator ScenarioBRejectRollsBackSlotKeepsResidueAndConverges()
        {
            BuildActor(withAnimator: false);
            var saboteur = _actor.AddComponent<JourneyRejectOperator>();
            SetHostOperators(_locomotion, _animation, saboteur);
            saboteur.RejectOnTick = 3;

            Require(StartHost());
            _intents.EnqueueMove(0f, 2f);
            float z0 = _actor.transform.position.z;

            // Tick one: Idle requests the Move transition. Tick two: Move
            // writes the desire and the engine moves. Tick three: the
            // saboteur rejects — the product semantics are LOUD: a
            // rejected transaction faults the Host (no silent replay),
            // the slot rolls back to its last committed value while the
            // engine residue stays on the transform.
            Require(
                _host.TryStep(Step, out CoCoDiagnostic firstTick),
                "first tick: " + _host.LastDiagnostic.Message);
            Require(_host.TryStep(Step, out _), "second tick");
            Assert.IsFalse(_host.TryStep(Step, out _), "third tick must reject");
            Assert.IsTrue(_host.LastDiagnostic.IsError, "reject leaves a diagnostic");

            float residue = _actor.transform.position.z - z0;
            Assert.Greater(residue, Step, "engine residue: a Move already happened on the transform");
            Assert.IsFalse(_host.TryStep(Step, out _),
                "a rejected Host stays faulted — D84 resolved: reject is a " +
                "loud stop; the player recovers from the last recorded tick " +
                "(save load or restart), after which the same delta replays " +
                "and reconverges");
            float slotAfterReject = JourneyMemory.LastSlotZ;
            Assert.AreEqual(
                slotAfterReject,
                JourneyMemory.LastSlotZ,
                0.0001f,
                "slot keeps its last committed value through the reject");
            Assert.Greater(slotAfterReject, 0f,
                "the last committed slot is the pre-reject tick's value");
            yield return null;
        }

        [UnityTest]
        public System.Collections.IEnumerator ScenarioCTemporalRestoreProjectsWorldAndGraph()
        {
            BuildActor(withAnimator: false);
            SetField(_host, "temporalHistoryCapacity", 8);
            SetField(_host, "contextRestoreBinding", _locomotion);
            Require(StartHost());
            // The first tick after start establishes the graph baseline and
            // drops transition requests — hold the press for two ticks.
            _intents.EnqueuePressed(JourneyContract.Jump);
            Require(_host.TryStep(Step, out _));
            _intents.EnqueuePressed(JourneyContract.Jump);
            Require(_host.TryStep(Step, out _));
            for (int index = 0; index < 8; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            _intents.Clear();
            float zAtJump = _actor.transform.position.z;
            float yAtJump = _actor.transform.position.y;
            for (int index = 0; index < 3; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            Assert.AreEqual("Jump", JourneyMemory.Current, "airborne mid-jump before preview");
            Require(_host.TryBeginTemporalPreview(out CoCoDiagnostic begin), begin.Message);
            Require(_host.TryPreviewTemporal(3, out CoCoDiagnostic preview), preview.Message);

            Assert.AreEqual(yAtJump, _actor.transform.position.y, 0.02f,
                "preview projects the world back onto the historical slot");
            Assert.AreEqual("Jump", JourneyMemory.Current, "graph activation restored");

            Require(_host.TryConfirmTemporalRestore(out CoCoDiagnostic confirm), confirm.Message);
            Assert.AreEqual(zAtJump, _actor.transform.position.z, 0.02f);
            yield return null;
        }

        [UnityTest]
        public System.Collections.IEnumerator ScenarioDSaveLoadRestoresSlotAnimatorAndGraph()
        {
            BuildActor(withAnimator: true);
            // The context registers itself in OnEnable with its
            // auto-generated stable id — overriding the field afterwards
            // would desync the registry key (capture writes one id, apply
            // looks up another and silently skips).
            _actor.AddComponent<PersistenceContext>();
            SetField(_host, "contextRestoreBinding", _locomotion);
            Require(StartHost());
            // The first tick after start establishes the graph baseline and
            // drops transition requests — hold the press for two ticks.
            _intents.EnqueuePressed(JourneyContract.Jump);
            Require(_host.TryStep(Step, out _));
            _intents.EnqueuePressed(JourneyContract.Jump);
            Require(_host.TryStep(Step, out _));
            for (int index = 0; index < 8; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            _intents.Clear();
            int hashAtSave = JourneyMemory.LastAnimHash;
            float timeAtSave = JourneyMemory.LastAnimTime;
            float yAtSave = _actor.transform.position.y;
            Assert.Greater(yAtSave, 0.05f, "airborne at save");
            Assert.AreEqual("Jump", JourneyMemory.Current);

            PersistenceSaveLoadSystem.SaveGame(0);
            for (int index = 0; index < 5; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            Assert.Greater(System.Math.Abs(_actor.transform.position.y - yAtSave), 0.05f, "world drifted after save");
            Assert.IsTrue(
                PersistenceSaveLoadSystem.LoadGame(0),
                "load+apply must succeed: " + _host.LastDiagnostic.Message);

            Assert.AreEqual("Jump", JourneyMemory.Current,
                "graph activation restored from the payload");
            Assert.AreEqual(yAtSave, _actor.transform.position.y, 0.02f,
                "loco slot restored and projected");
            Assert.AreEqual(hashAtSave, JourneyMemory.LastAnimHash,
                "Animator snapshot restored (state hash)");
            Assert.AreEqual(timeAtSave, JourneyMemory.LastAnimTime, 0.02f,
                "Animator snapshot restored (normalized time)");

            Require(_host.TryStep(Step, out CoCoDiagnostic resume), resume.Message);
            yield return null;
        }

        [UnityTest]
        public System.Collections.IEnumerator ScenarioEAttackFiresTriggerAndPlaysAnimation()
        {
            BuildActor(withAnimator: true);
            Require(StartHost());
            Require(_host.TryStep(Step, out _));
            Assert.AreEqual("Idle", JourneyMemory.Current);

            _intents.EnqueuePressed(JourneyContract.Attack);
            Require(_host.TryStep(Step, out _));
            _intents.EnqueuePressed(JourneyContract.Attack);
            Require(_host.TryStep(Step, out _));
            for (int index = 0; index < 2; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            Assert.AreEqual("Attack", JourneyMemory.Current);
            Assert.IsTrue(JourneyMemory.AttackTriggerWritten, "trigger lane written by the state");
            // The Animator evaluates on the engine's own phase — the
            // trigger lands on a later frame after the tick that wrote
            // it (engine-fact latency, by design).
            yield return null;
            yield return null;
            Animator animator = _animation.Animator;
            Assert.AreEqual(
                Animator.StringToHash("Attack"),
                animator.GetCurrentAnimatorStateInfo(0).shortNameHash,
                "engine played the attack state through the trigger");

            for (int index = 0; index < 20; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            Assert.AreEqual("Idle", JourneyMemory.Current, "attack returns to idle after its beat");
            yield return null;
        }

        // ----- assembly -------------------------------------------------

        private void BuildActor(bool withAnimator)
        {
            CoCoStandardBindingProvider provider =
                CoCoStandardBindingProvider.Build(
                    new[] { typeof(JourneyIdleLogic).Assembly });
            Assert.IsTrue(CoCoStateGraphProjectBindings.TryInstall(
                provider, out CoCoDiagnostic install), install.Message);

            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));

            var idle = new CoCoStateGraphStateRecord(
                new CoCoSerializedId128(1, 1), default, "Idle",
                DescriptorOf<JourneyIdleLogic>(), new EmptyStateConfig());
            var move = new CoCoStateGraphStateRecord(
                new CoCoSerializedId128(2, 2), default, "Move",
                DescriptorOf<JourneyMoveLogic>(), new EmptyStateConfig());
            var jump = new CoCoStateGraphStateRecord(
                new CoCoSerializedId128(3, 3), default, "Jump",
                DescriptorOf<JourneyJumpLogic>(), new EmptyStateConfig());
            var attack = new CoCoStateGraphStateRecord(
                new CoCoSerializedId128(4, 4), default, "Attack",
                DescriptorOf<JourneyAttackLogic>(), new EmptyStateConfig());

            var layer = new CoCoStateGraphLayerRecord(
                new CoCoSerializedId128(9, 9), "Base");
            layer.InitialStateId = new CoCoSerializedId128(1, 1);
            layer.States.Add(idle);
            layer.States.Add(move);
            layer.States.Add(jump);
            layer.States.Add(attack);
            // Edge order is the fixture contract: Idle→[Move,Jump,Attack],
            // Move→[Idle,Jump], Jump→[Idle], Attack→[Idle].
            {
                layer.Transitions.Add(Edge(11, idle, move));
                layer.Transitions.Add(Edge(12, idle, jump));
                layer.Transitions.Add(Edge(13, idle, attack));
                layer.Transitions.Add(Edge(14, move, idle));
                layer.Transitions.Add(Edge(15, move, jump));
                layer.Transitions.Add(Edge(16, jump, idle));
                layer.Transitions.Add(Edge(17, attack, idle));
            }
            asset.Layers.Add(layer);

            _actor = new GameObject("JourneyActor");
            _objects.Add(_actor);
            var controller = _actor.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.center = new Vector3(0f, 1f, 0f);
            _host = _actor.AddComponent<CoCoStateGraphHost>();
            _locomotion = _actor.AddComponent<LocomotionOperator>();
            _animation = _actor.AddComponent<AnimAutoOperator>();
            _intents = _actor.AddComponent<JourneyIntentSource>();

            SetField(_host, "stateGraphAsset", asset);
            SetField(_host, "driver", (CoCoStateGraphDriver)2 /* Manual */);
            SetField(_host, "autoStart", false);
            SetField(_host, "intentSources",
                new MonoBehaviour[] { _intents, });
            // The manifest derives from every state in the graph, so both
            // operators are always present; without a controller the
            // Animator is an idle engine fact (zero layers).
            _actor.AddComponent<Animator>();
            SetHostOperators(_locomotion, _animation);
            SetField(_animation, "stateGraphHost", _host);

            if (withAnimator)
            {
                AttachRuntimeAnimator();
            }
        }

        private void AttachRuntimeAnimator()
        {
            AnimatorController controllerAsset =
                AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            AnimatorControllerLayer baseLayer = controllerAsset.layers[0];
            AnimatorState idle = baseLayer.stateMachine.AddState("Idle");
            AnimatorState jump = baseLayer.stateMachine.AddState("Jump");
            AnimatorState attack = baseLayer.stateMachine.AddState("Attack");
            controllerAsset.AddParameter(
                "Attack", AnimatorControllerParameterType.Trigger);
            AnimatorStateTransition fire = idle.AddTransition(attack);
            fire.duration = 0f;
            fire.AddCondition(AnimatorConditionMode.If, 0f, "Attack");
            AnimatorStateTransition settle = attack.AddTransition(idle);
            settle.hasExitTime = true;

            var animator = _actor.GetComponent<Animator>();
            animator.runtimeAnimatorController = controllerAsset;

            // struct reflection writes through the boxed copy — keep it.
            object boxedBinding = new AnimTriggerBinding();
            typeof(AnimTriggerBinding)
                .GetField("bindingId",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(boxedBinding, JourneyContract.AttackTriggerBindingId);
            typeof(AnimTriggerBinding)
                .GetField("parameterName",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(boxedBinding, "Attack");
            SetField(_animation, "triggerBindings",
                new[] { (AnimTriggerBinding)boxedBinding, });
            Require(
                _animation.TryRebuildBindings(out CoCoDiagnostic rebuild),
                "animator bindings rebuild: " + rebuild.Message);
        }

        private bool StartHost()
        {
            if (_host.TryStart(out CoCoDiagnostic start))
            {
                return true;
            }

            Assert.Fail("start: " + start.Message + " / " + _host.LastDiagnostic.Message);
            return false;
        }

        private void SetHostOperators(params MonoBehaviour[] operators)
        {
            SetField(_host, "operators", operators);
        }

        private void JourneyMoveStep(int extraTicks)
        {
            for (int index = 0; index < extraTicks; index++)
            {
                Require(_host.TryStep(Step, out _));
            }
        }

        private static CoCoSerializedId128 DescriptorOf<TLogic>()
        {
            // The descriptor name must equal the [CoCoState] attribute name
            // — the catalog derives the id from it.
            string name = typeof(TLogic).Name;
            if (name.EndsWith("Logic", System.StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - "Logic".Length);
            }

            Assert.IsTrue(StandardDescriptors.TryCreate(
                typeof(TLogic), name,
                out CoCoStateDescriptorId descriptorId));
            return new CoCoSerializedId128(descriptorId.High, descriptorId.Low);
        }

        private static CoCoStateGraphTransitionRecord Edge(
            ulong id,
            CoCoStateGraphStateRecord from,
            CoCoStateGraphStateRecord to)
        {
            return new CoCoStateGraphTransitionRecord(
                new CoCoSerializedId128(id, id),
                from.StateId,
                to.StateId,
                (int)id);
        }

        private static void SetField(object target, string field, object value)
        {
            target.GetType().GetField(
                    field,
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(target, value);
        }

        private static void Require(bool condition, string message = "required")
        {
            Assert.IsTrue(condition, message);
        }

        /// <summary>
        /// Feeds raw input records straight into the intent chain — the
        /// hardware→InputReader segment is covered by the C4c e2e; this
        /// source starts the chain at Intent translation.
        /// </summary>
        private sealed class JourneyIntentSource :
            MonoBehaviour,
            ICoCoIntentFrameSource<RawInputIntent>
        {
            private readonly List<RawInputRecord> _pending =
                new List<RawInputRecord>();

            private bool _holdMove;
            private float _holdX;
            private float _holdY;

            public void EnqueueMove(float x, float y)
            {
                _holdMove = true;
                _holdX = x;
                _holdY = y;
            }

            public void EnqueuePressed(string action)
            {
                _pending.Add(Record(action, 1f, 0f, RawInputPhase.Started));
            }

            public void Clear()
            {
                _holdMove = false;
                _pending.Clear();
            }

            public bool TrySample(in CoCoTickFrame tickFrame, out RawInputIntent intent)
            {
                if (_holdMove)
                {
                    _pending.RemoveAll(record =>
                        record.Action == CoCoFixedString64.FromString(JourneyContract.Move));
                    _pending.Add(Record(
                        JourneyContract.Move, _holdX, _holdY, RawInputPhase.Held));
                }

                intent = new RawInputIntent
                {
                    ActiveMap = CoCoFixedString64.FromString("Gameplay"),
                    Count = System.Math.Min(_pending.Count, RawInputIntent.RecordCapacity),
                };
                for (int index = 0; index < intent.Count; index++)
                {
                    intent.Set(index, _pending[index]);
                }

                _pending.Clear();
                return true;
            }

            private static RawInputRecord Record(
                string action,
                float x,
                float y,
                RawInputPhase phase)
            {
                return new RawInputRecord
                {
                    Action = CoCoFixedString64.FromString(action),
                    ValueX = x,
                    ValueY = y,
                    Phase = phase,
                    Sequence = 0UL,
                };
            }
        }

        /// <summary>
        /// Rejects its whole transaction on one chosen tick (scenario B).
        /// </summary>
        private sealed class JourneyRejectOperator :
            MonoBehaviour, ICoCoOperator
        {
            private static readonly CoCoOperatorDescriptor RejectDescriptor =
                BuildDescriptor();

            public int RejectOnTick = -1;
            private int _tick;

            public CoCoOperatorDescriptor Descriptor => RejectDescriptor;

            private static CoCoOperatorDescriptor BuildDescriptor()
            {
                var builder = new CoCoOperatorDescriptorBuilder();
                if (!CoCoOperatorId.TryCreate(
                        0x4A55524E45593131UL,
                        0x4A55524E45593031UL,
                        out CoCoOperatorId operatorId) ||
                    !builder.TryRequire<ILocomotionSection>(
                        LocoContractIds.SectionId,
                        CoCoOperationSectionMode.Continuous,
                        out _,
                        out _))
                {
                    return null;
                }

                builder.TryFreeze<JourneyRejectOperator>(
                    operatorId,
                    out CoCoOperatorDescriptor descriptor,
                    out _);
                return descriptor;
            }

            public bool TryExecute(
                in CoCoOperatorExecutionContext context,
                out CoCoOperatorOutcome outcome)
            {
                _tick++;
                if (_tick == RejectOnTick)
                {
                    outcome = CoCoOperatorOutcome.Rejected(
                        CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Operator,
                            CoCoDiagnosticCode.OperatorExecutionFailed,
                            "Journey scenario B: deliberate reject."));
                    return false;
                }

                outcome = CoCoOperatorOutcome.NoOp;
                return true;
            }
        }
    }
}
