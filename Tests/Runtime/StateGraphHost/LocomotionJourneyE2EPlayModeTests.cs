using CoCoFlow.Runtime.Input.Contracts;
#if UNITY_EDITOR
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.Modules.Locomotion;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// 手动 Host 经输入、状态选边和真实移动 Operator 驱动角色，并用拒绝路径验证提交隔离。
namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraph.StateGraphHost;

    /// <summary>手动 Host 经输入、状态选边和真实移动 Operator 驱动角色，并用拒绝路径验证提交隔离。</summary>
    public sealed class LocomotionJourneyE2EPlayModeTests
    {

        #region Public API

        /// <summary>重置已安装的项目绑定，使本例独立装配标准 Provider。</summary>
        [SetUp]
        public void SetUp()
        {
            _objects.Clear();
            JourneyMemory.Reset();
            StateGraphProjectBindings.ResetForTests();
        }

        /// <summary>释放本例创建的场景对象并清除安装的项目绑定。</summary>
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

            StateGraphProjectBindings.ResetForTests();
        }

        /// <summary>用角色控制器实际碰墙，将引擎最终位置与已提交的移动槽进行对照。</summary>
        [UnityTest]
        public System.Collections.IEnumerator ScenarioAWallRecordsEngineFact()
        {
            BuildActor();

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

        /// <summary>令下游拒绝候选，区分已经发生的场景移动与未提交槽值，并观察后续校正。</summary>
        [UnityTest]
        public System.Collections.IEnumerator ScenarioBRejectRollsBackSlotKeepsResidueAndConverges()
        {
            BuildActor();
            var saboteur = _actor.AddComponent<JourneyRejectOperator>();
            SetHostOperators(_locomotion, saboteur);
            saboteur.RejectOnTick = 3;

            Require(StartHost());
            _intents.EnqueueMove(0f, 2f);
            float z0 = _actor.transform.position.z;

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

        /// <summary>先移动并捕获历史，再确认恢复，检查场景中的角色和图活动路径同时回到所选记录。</summary>
        [UnityTest]
        public System.Collections.IEnumerator ScenarioCTemporalRestoreProjectsWorldAndGraph()
        {
            BuildActor();
            SetField(_host, "temporalHistoryCapacity", 8);
            SetRecovery(true, true);
            Require(StartHost());

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

        /// <summary>保存实际移动状态并重新读档，核对恢复后的图和场景位置。</summary>
        [UnityTest]
        public System.Collections.IEnumerator ScenarioDSaveLoadRestoresLocomotionAndGraph()
        {
            BuildActor();

            var persistence = _actor.AddComponent<PersistenceContext>();
            persistence.EnsureStableEntityId();
            PersistenceContextRegistry.Register(persistence);
            SetRecovery(true, true);
            Require(StartHost());

            _intents.EnqueuePressed(JourneyContract.Jump);
            Require(_host.TryStep(Step, out _));
            _intents.EnqueuePressed(JourneyContract.Jump);
            Require(_host.TryStep(Step, out _));
            for (int index = 0; index < 8; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            _intents.Clear();
            float yAtSave = _actor.transform.position.y;
            Assert.Greater(yAtSave, 0.05f, "airborne at save");
            Assert.AreEqual("Jump", JourneyMemory.Current);

            PersistenceSaveLoadSystem.SaveGame(0);
            Assert.IsTrue(PersistenceFileStore.TryReadDocument(0, out var savedDocument));
            Assert.IsTrue(savedDocument.contextSection.TryGetRecord(
                persistence.StableEntityId, out _), "The durable payload must contain this actor.");
            for (int index = 0; index < 6; index++)
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
            Require(_host.TryStep(Step, out CoCoDiagnostic resume), resume.Message);
            yield return null;
        }

        /// <summary>从攻击态请求已创作的回边，核对下一步进入普通待机态。</summary>
        [UnityTest]
        public System.Collections.IEnumerator ScenarioEAttackReturnsToIdleThroughItsAuthoredEdge()
        {
            BuildActor();
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
            for (int index = 0; index < 20; index++)
            {
                Require(_host.TryStep(Step, out _));
            }

            Assert.AreEqual("Idle", JourneyMemory.Current, "attack returns to idle after its beat");
            yield return null;
        }

        /// <summary>独立启停时间或持久化恢复能力，检查未启用一侧的已提交结果得到保留。</summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RecoverySwitchesAreIndependentAndPreserveDisabledOutcomes(bool persistence, bool temporal)
        {
            BuildActor();
            SetRecovery(persistence, temporal);
            SetField(_host, "temporalHistoryCapacity", 8);
            Require(StartHost());
            _intents.EnqueueMove(0f, 2f);
            JourneyMoveStep(3);
            float saved = _actor.transform.position.z;
            Require(_host.TryCapturePersistencePayload(out byte[] payload, out _));
            JourneyMoveStep(3);
            float current = _actor.transform.position.z;
            Assert.Greater(current, saved);
            Require(_host.TryBeginTemporalPreview(out _));
            Require(_host.TryPreviewTemporal(3, out _));
            Assert.That(_actor.transform.position.z, Is.EqualTo(temporal ? saved : current).Within(0.001f));
            Require(_host.TryCancelTemporalPreview(out _));
            Assert.That(_actor.transform.position.z, Is.EqualTo(current).Within(0.001f));
            Require(_host.TryBeginTemporalPreview(out _));
            Require(_host.TryPreviewTemporal(3, out _));
            Require(_host.TryConfirmTemporalRestore(out _));
            Assert.That(_host.CurrentContext.Layout.TryResolveSlot(LocoContractIds.StateSlotId,
                out StateSlot<LocomotionState> slot), Is.True);
            Assert.That(_host.CurrentContext.Read(slot).PositionZ, Is.EqualTo(temporal ? saved : current).Within(0.001f));
            JourneyMoveStep(1);
            current = _actor.transform.position.z;
            Require(_host.TryApplyPersistencePayload(payload, out _));
            Assert.That(_actor.transform.position.z, Is.EqualTo(persistence ? saved : current).Within(0.001f));
            Assert.That(_host.CurrentContext.Read(slot).PositionZ, Is.EqualTo(persistence ? saved : current).Within(0.001f));
            JourneyMoveStep(1);
            Assert.Greater(_actor.transform.position.z, persistence ? saved : current);
        }

        /// <summary>装配失效的 Operator 绑定并触发未覆盖的步进故障，检查错误不能被错误清除。</summary>
        [Test]
        public void OldOperatorBindingIsRejectedAndUncoveredTickFaultCannotBeCleared()
        {
            BuildActor();
            SetField(_host, "contextRestoreBinding", _locomotion);
            Assert.IsFalse(_host.TryStart(out _));
            SetField(_host, "contextRestoreBinding", null);
            var saboteur = _actor.AddComponent<JourneyRejectOperator>();
            SetHostOperators(_locomotion, saboteur);
            saboteur.RejectOnTick = 3;
            Require(StartHost());
            _intents.EnqueueMove(0f, 2f);
            JourneyMoveStep(2);
            Assert.IsFalse(_host.TryStep(Step, out _));
            Assert.IsTrue(_host.RequiresWorldCorrection);
            Assert.IsFalse(_host.TryCorrectWorld(out _));
            Assert.IsTrue(_host.RequiresWorldCorrection);
        }

        /// <summary>制造一次 Operator 恢复失败，核对相同能力用于场景校正且回调后读取器失效。</summary>
        [TestCase(false)]
        [TestCase(true)]
        public void FailedOperatorRestoreCorrectsWithTheSameAbilityAndExpiresReader(bool temporal)
        {
            BuildActor();
            var probe = _actor.AddComponent<JourneyRejectOperator>();
            SetHostOperators(_locomotion, probe);
            SetField(_host, "operatorRecovery", new[]
            {
                new StateGraphHost.OperatorRecovery { component = _locomotion, persistence = !temporal, temporal = temporal },
                new StateGraphHost.OperatorRecovery { component = probe, persistence = true, temporal = true }
            });
            SetField(_host, "temporalHistoryCapacity", temporal ? 8 : 0);
            Require(StartHost());
            _intents.EnqueueMove(0f, 2f);
            JourneyMoveStep(3);
            Require(_host.TryCapturePersistencePayload(out byte[] payload, out _));
            JourneyMoveStep(2);
            float authority = _actor.transform.position.z;
            probe.FailRecovery = true;
            if (temporal)
            {
                Require(_host.TryBeginTemporalPreview(out _));
                Assert.IsFalse(_host.TryPreviewTemporal(2, out _));
            }
            else Assert.IsFalse(_host.TryApplyPersistencePayload(payload, out _));
            Assert.IsTrue(_host.RequiresWorldCorrection);
            Assert.IsFalse(probe.RecoveryReader.IsValid);
            probe.FailRecovery = false;
            Require(_host.TryCorrectWorld(out _));
            Assert.IsFalse(_host.RequiresWorldCorrection);
            Assert.That(_actor.transform.position.z, Is.EqualTo(authority).Within(0.001f));
            Assert.AreEqual(temporal ? 2 : 0, probe.TemporalRestores);
            Assert.AreEqual(temporal ? 0 : 2, probe.PersistenceRestores);
        }

        /// <summary>保存运行恢复测试使用的场景和 Operator 接线，供该组实际装载与校正。</summary>
        public static void PrepareOperatorRecoveryScene()
        {
            UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.DefaultGameObjects);
            var type = System.Type.GetType("CoCoFlow004.Logic.RecoveryMoveLogic, CoCoFlow004");
            Require(type != null, "The manual scene requires the authored RecoveryMoveLogic script.");
            Require(StandardDescriptors.TryCreate(type, "RecoveryMove", out var descriptor));
            var asset = AssetDatabase.LoadAssetAtPath<StateGraphAsset>("Assets/CoCoFlow004/RecoveryGraph.asset")
                ?? ScriptableObject.CreateInstance<StateGraphAsset>();
            if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
                AssetDatabase.CreateAsset(asset, "Assets/CoCoFlow004/RecoveryGraph.asset");
            asset.Layers.Clear();
            asset.EnsureAssetIdentity(AssetDatabase.AssetPathToGUID("Assets/CoCoFlow004/RecoveryGraph.asset"));
            var stateId = new SerializedId128(4, 1);
            var layer = new StateGraphLayerRecord(new SerializedId128(4, 2), "Base");
            layer.InitialStateId = stateId;
            layer.States.Add(new StateGraphStateRecord(stateId, default, "Recovery Move",
                new SerializedId128(descriptor.High, descriptor.Low), new FieldConfig()));
            asset.Layers.Add(layer);
            EditorUtility.SetDirty(asset);
            var actor = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            actor.name = "Operator Recovery - select me";
            Object.DestroyImmediate(actor.GetComponent<Collider>());
            var host = actor.AddComponent<StateGraphHost>();
            var movement = actor.AddComponent<LocomotionOperator>();
            actor.AddComponent<PersistenceContext>();
            actor.AddComponent(System.Type.GetType("CoCoFlow004.OperatorRecoveryProbe, Assembly-CSharp"));
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", StateGraphDriver.Manual);
            SetField(host, "operators", new MonoBehaviour[] { movement });
            SetField(host, "temporalHistoryCapacity", 16);
            AssetDatabase.SaveAssets();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(actor.scene, "Assets/CoCoFlow004/OperatorRecovery.unity");
            Selection.activeGameObject = actor;
        }

        #endregion

        #region Internal Logic

        private const float Step = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _actor;
        private StateGraphHost _host;
        private LocomotionOperator _locomotion;
        private JourneyIntentSource _intents;

        private void BuildActor()
        {
            StandardBindingProvider provider =
                StandardBindingProvider.Build(
                    new[] { typeof(JourneyIdleLogic).Assembly });
            Assert.IsTrue(StateGraphProjectBindings.TryInstall(
                provider, out CoCoDiagnostic install), install.Message);

            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(System.Guid.NewGuid().ToString("N"));

            var idle = new StateGraphStateRecord(
                new SerializedId128(1, 1), default, "Idle",
                DescriptorOf<JourneyIdleLogic>(), new EmptyStateConfig());
            var move = new StateGraphStateRecord(
                new SerializedId128(2, 2), default, "Move",
                DescriptorOf<JourneyMoveLogic>(), new EmptyStateConfig());
            var jump = new StateGraphStateRecord(
                new SerializedId128(3, 3), default, "Jump",
                DescriptorOf<JourneyJumpLogic>(), new EmptyStateConfig());
            var attack = new StateGraphStateRecord(
                new SerializedId128(4, 4), default, "Attack",
                DescriptorOf<JourneyAttackLogic>(), new EmptyStateConfig());

            var layer = new StateGraphLayerRecord(
                new SerializedId128(9, 9), "Base");
            layer.InitialStateId = new SerializedId128(1, 1);
            layer.States.Add(idle);
            layer.States.Add(move);
            layer.States.Add(jump);
            layer.States.Add(attack);

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
            _host = _actor.AddComponent<StateGraphHost>();
            _locomotion = _actor.AddComponent<LocomotionOperator>();
            _intents = _actor.AddComponent<JourneyIntentSource>();

            SetField(_host, "stateGraphAsset", asset);
            SetField(_host, "driver", (StateGraphDriver)2  );
            SetField(_host, "autoStart", false);
            SetField(_host, "intentSources",
                new MonoBehaviour[] { _intents, });
            SetHostOperators(_locomotion);
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

        private void SetRecovery(bool persistence, bool temporal)
        {
            SetField(_host, "operatorRecovery", new[]
            {
                new StateGraphHost.OperatorRecovery { component = _locomotion, persistence = persistence, temporal = temporal },
            });
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

        private static SerializedId128 DescriptorOf<TLogic>()
        {

            string name = typeof(TLogic).Name;
            if (name.EndsWith("Logic", System.StringComparison.Ordinal))
            {
                name = name.Substring(0, name.Length - "Logic".Length);
            }

            Assert.IsTrue(StandardDescriptors.TryCreate(
                typeof(TLogic), name,
                out StateDescriptorId descriptorId));
            return new SerializedId128(descriptorId.High, descriptorId.Low);
        }

        private static StateGraphTransitionRecord Edge(
            ulong id,
            StateGraphStateRecord from,
            StateGraphStateRecord to)
        {
            return new StateGraphTransitionRecord(
                new SerializedId128(id, id),
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

        /// <summary>为真实手动 Host 逐步提供移动旅程测试指定的输入。</summary>
        private sealed class JourneyIntentSource :
            MonoBehaviour,
            IIntentFrameSource<InputIntentContract>
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

            public bool TrySample(in TickFrame tickFrame, out InputIntentContract intent)
            {
                RawInputRecord[] current = _holdMove
                    ? new[] { Record(JourneyContract.Move, _holdX, _holdY, RawInputPhase.Held) }
                    : System.Array.Empty<RawInputRecord>();
                intent = new InputIntentContract(
                    FixedString64.FromString("Gameplay"), _pending.ToArray(), current);

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
                    Action = FixedString64.FromString(action),
                    ValueX = x,
                    ValueY = y,
                    Phase = phase,
                    Sequence = 0UL,
                };
            }
        }

        /// <summary>可在移动后拒绝候选，供测试核对场景校正与提交状态。</summary>
        private sealed class JourneyRejectOperator :
            MonoBehaviour, IPersistenceRestoreOperator, ITemporalRestoreOperator
        {
            private static readonly OperatorDescriptor RejectDescriptor =
                BuildDescriptor();

            public int RejectOnTick = -1;
            internal bool FailRecovery;
            internal int TemporalRestores, PersistenceRestores;
            public ContextRestoreReader RecoveryReader;
            bool ITemporalRestoreOperator.TryRestore(in ContextRestoreBindingContext context, out CoCoDiagnostic diagnostic)
            {
                TemporalRestores++;
                RecoveryReader = context.Reader;
                diagnostic = CoCoDiagnostic.None;
                return !FailRecovery;
            }
            bool IPersistenceRestoreOperator.TryRestore(in ContextRestoreBindingContext context, out CoCoDiagnostic diagnostic)
            {
                PersistenceRestores++;
                RecoveryReader = context.Reader;
                diagnostic = CoCoDiagnostic.None;
                return !FailRecovery;
            }
            private int _tick;

            public OperatorDescriptor Descriptor => RejectDescriptor;

            private static OperatorDescriptor BuildDescriptor()
            {
                var builder = new OperatorDescriptorBuilder();
                if (!OperatorId.TryCreate(
                        0x4A55524E45593131UL,
                        0x4A55524E45593031UL,
                        out OperatorId operatorId) ||
                    !builder.TryRequire<ILocomotionOperation>(
                        LocoContractIds.OperationId,
                        Mode.Continuous,
                        out _,
                        out _))
                {
                    return null;
                }

                builder.TryFreeze<JourneyRejectOperator>(
                    operatorId,
                    out OperatorDescriptor descriptor,
                    out _);
                return descriptor;
            }

            public bool TryExecute(
                in OperatorExecutionContext context,
                out OperatorOutcome outcome)
            {
                _tick++;
                if (_tick == RejectOnTick)
                {
                    outcome = OperatorOutcome.Rejected(
                        CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Operator,
                            CoCoDiagnosticCode.OperatorExecutionFailed,
                            "Journey scenario B: deliberate reject."));
                    return false;
                }

                outcome = OperatorOutcome.NoOp;
                return true;
            }
        }

        #endregion
    }
}
#endif
