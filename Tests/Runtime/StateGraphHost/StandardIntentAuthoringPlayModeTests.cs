using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Locomotion.Contracts;
using CoCoFlow.Runtime.Modules.Locomotion;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class StandardIntentAuthoringPlayModeTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private CoCoStandardBindingProvider _provider;

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            IntentAuthoringCapture.Reset();
            _provider = CoCoStandardBindingProvider.BuildForAssemblyOf<AuthorSingleLogic>();
            Assert.That(CoCoStateGraphProjectBindings.TryInstall(_provider, out CoCoDiagnostic diagnostic),
                Is.True, diagnostic.Message);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (Object obj in _objects) if (obj != null) Object.DestroyImmediate(obj);
            _objects.Clear();
            CoCoStateGraphProjectBindings.ResetForTests();
            IntentAuthoringCapture.Reset();
        }

        [Test]
        public void GeneratedIntentFreeStateCompilesAndRunsOnStandardHost()
        {
            CoCoStateGraphHost host = CreateHost(typeof(GeneratedIntentFreeLogic));
            StartAndStep(host);
            Assert.That(host.CurrentContext.IsAlive, Is.True);
            Assert.That(host.IntentSources, Is.Empty);
        }

        [Test]
        public void SingleIntentReadsAndMovesThroughRealOperatorAndCommitsEngineFacts()
        {
            CoCoStateGraphHost host = CreateHost(typeof(AuthorSingleLogic));
            var source = host.gameObject.AddComponent<AuthorMoveSource>();
            source.Value = 4f;
            Set(host, "intentSources", new MonoBehaviour[] { source });
            AddLocomotion(host);
            Vector3 before = host.transform.position;
            StartAndStep(host);
            Assert.That(source.Samples, Is.EqualTo(1));
            Assert.That(IntentAuthoringCapture.ReadMove, Is.True);
            Assert.That(IntentAuthoringCapture.Move, Is.EqualTo(4f));
            Assert.That(IntentAuthoringCapture.Wrote, Is.True);
            Assert.That(host.transform.position.z - before.z, Is.EqualTo(0.08f).Within(0.002f));
            Assert.That(host.CurrentContext.Layout.TryResolveSlot(LocoContractIds.StateSlotId,
                out CoCoStateSlot<LocomotionState> slot), Is.True);
            Assert.That(host.CurrentContext.Read(slot).PositionZ, Is.EqualTo(host.transform.position.z).Within(0.0001f));
            Assert.That(IntentAuthoringCapture.SavedAuthor.Move(0f, 99f), Is.False, "callback writer must expire");
            Assert.That(host.transform.position.z - before.z, Is.EqualTo(0.08f).Within(0.002f));
        }

        [Test]
        public void MultipleIntentsMapByTypeWhenHostSourcesAreInReverseOrder()
        {
            CoCoStateGraphHost host = CreateHost(typeof(AuthorMultipleLogic));
            var move = host.gameObject.AddComponent<AuthorMoveSource>();
            var look = host.gameObject.AddComponent<AuthorLookSource>();
            Set(host, "intentSources", new MonoBehaviour[] { look, move });
            AddLocomotion(host);
            Vector3 before = host.transform.position;
            StartAndStep(host);
            Assert.That(IntentAuthoringCapture.ReadMove && IntentAuthoringCapture.ReadLook, Is.True);
            Assert.That(IntentAuthoringCapture.Move, Is.EqualTo(2f));
            Assert.That(IntentAuthoringCapture.Look, Is.EqualTo(3f));
            Assert.That(host.transform.position.x - before.x, Is.EqualTo(0.06f).Within(0.002f));
            Assert.That(host.transform.position.z - before.z, Is.EqualTo(0.04f).Within(0.002f));
            Assert.That(move.Samples, Is.EqualTo(1));
            Assert.That(look.Samples, Is.EqualTo(1));
        }

        [Test]
        public void UnionOfStatesBindsOnceButEachStateCanReadOnlyItsOwnDeclaration()
        {
            CoCoStateGraphHost host = CreateHost(typeof(AuthorSingleLogic), typeof(AuthorLookLogic));
            var move = host.gameObject.AddComponent<AuthorMoveSource>();
            var look = host.gameObject.AddComponent<AuthorLookSource>();
            Set(host, "intentSources", new MonoBehaviour[] { look, move });
            AddLocomotion(host);
            Vector3 before = host.transform.position;
            StartAndStep(host);
            Assert.That(IntentAuthoringCapture.ReadMove, Is.True);
            Assert.That(IntentAuthoringCapture.UndeclaredRead, Is.False);
            Assert.That(IntentAuthoringCapture.LegacyUndeclaredRead, Is.False);
            Assert.That(move.Samples, Is.EqualTo(1));
            Assert.That(look.Samples, Is.EqualTo(1));
            Assert.That(host.transform.position.z - before.z, Is.EqualTo(0.04f).Within(0.002f));
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic next), Is.True, next.Message);
            Assert.That(IntentAuthoringCapture.ReadLook, Is.True);
            Assert.That(host.transform.position.x - before.x, Is.EqualTo(0.06f).Within(0.002f));
            Assert.That(host.transform.position.z - before.z, Is.EqualTo(0.04f).Within(0.002f));
            Assert.That(move.Samples, Is.EqualTo(2));
            Assert.That(look.Samples, Is.EqualTo(2));
        }

        [Test]
        public void IndependentHostsUseIndependentHandlesAndSamplesFromSharedProvider()
        {
            CoCoStateGraphHost first = CreateHost(typeof(AuthorSingleLogic));
            CoCoStateGraphHost second = CreateHost(typeof(AuthorSingleLogic));
            var a = first.gameObject.AddComponent<AuthorMoveSource>();
            var b = second.gameObject.AddComponent<AuthorMoveSource>();
            a.Value = 2f; b.Value = 8f;
            Set(first, "intentSources", new MonoBehaviour[] { a });
            Set(second, "intentSources", new MonoBehaviour[] { b });
            AddLocomotion(first); AddLocomotion(second);
            StartAndStep(first); StartAndStep(second);
            Assert.That(first.transform.position.z, Is.EqualTo(0.04f).Within(0.002f));
            Assert.That(second.transform.position.z, Is.EqualTo(0.16f).Within(0.002f));
            Assert.That(a.Samples, Is.EqualTo(1)); Assert.That(b.Samples, Is.EqualTo(1));
        }

        [Test]
        public void MissingSampleDoesNotReusePreviousFrozenIntent()
        {
            CoCoStateGraphHost host = CreateHost(typeof(AuthorSingleLogic));
            var source = host.gameObject.AddComponent<AuthorMoveSource>();
            Set(host, "intentSources", new MonoBehaviour[] { source });
            AddLocomotion(host); StartAndStep(host);
            float previous = host.transform.position.z;
            source.HasValue = false;
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic step), Is.True, step.Message);
            Assert.That(IntentAuthoringCapture.ReadMove, Is.False);
            Assert.That(host.transform.position.z, Is.EqualTo(previous).Within(0.0001f));
        }

        [TestCase("missing", CoCoDiagnosticCode.DescriptorTypeMismatch)]
        [TestCase("wrong-type", CoCoDiagnosticCode.DescriptorTypeMismatch)]
        [TestCase("duplicate", CoCoDiagnosticCode.DuplicateIdentifier)]
        [TestCase("repeated-reference", CoCoDiagnosticCode.DuplicateIdentifier)]
        [TestCase("foreign-host", CoCoDiagnosticCode.DescriptorTypeMismatch)]
        [TestCase("nested-host", CoCoDiagnosticCode.DescriptorTypeMismatch)]
        [TestCase("null", CoCoDiagnosticCode.DescriptorTypeMismatch)]
        public void InvalidSourceContractRejectsBeforeSampling(string scenario, CoCoDiagnosticCode expected)
        {
            CoCoStateGraphHost host = CreateHost(typeof(AuthorSingleLogic));
            AddLocomotion(host);
            var source = host.gameObject.AddComponent<AuthorMoveSource>();
            MonoBehaviour[] sources = Array.Empty<MonoBehaviour>();
            switch (scenario)
            {
                case "wrong-type": sources = new MonoBehaviour[] { host.gameObject.AddComponent<AuthorLookSource>() }; break;
                case "duplicate": sources = new MonoBehaviour[] { source, host.gameObject.AddComponent<AuthorMoveSource>() }; break;
                case "repeated-reference": sources = new MonoBehaviour[] { source, source }; break;
                case "foreign-host":
                    CoCoStateGraphHost other = CreateHost(typeof(GeneratedIntentFreeLogic));
                    sources = new MonoBehaviour[] { other.gameObject.AddComponent<AuthorMoveSource>() }; break;
                case "nested-host":
                    CoCoStateGraphHost nested = CreateHost(typeof(GeneratedIntentFreeLogic));
                    nested.transform.SetParent(host.transform);
                    sources = new MonoBehaviour[] { nested.gameObject.AddComponent<AuthorMoveSource>() }; break;
                case "null": sources = new MonoBehaviour[] { null }; break;
            }
            Set(host, "intentSources", sources);
            Assert.That(host.TryStart(out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.Code, Is.EqualTo(expected), failure.Message);
            Assert.That(failure.Message, Does.Contain("Intent Source"));
            Assert.That(host.Lifecycle, Is.EqualTo(CoCoRuntimeLifecycleState.Created));
            Assert.That(source.Samples, Is.Zero);
            Assert.That(host.CurrentContext.IsAlive, Is.False);
        }

        [Test]
        public void AuthorProxyCannotWriteUndeclaredOperationOrMoveActorDirectly()
        {
            CoCoStateGraphHost host = CreateHost(typeof(AuthorUndeclaredOperationLogic));
            Assert.That(host.TryStart(out CoCoDiagnostic start), Is.True, start.Message);
            Vector3 before = host.transform.position;
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic failure), Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(IntentAuthoringCapture.Wrote, Is.False);
            Assert.That(host.transform.position, Is.EqualTo(before));
            Assert.That(host.CurrentContext.IsAlive, Is.False);
        }

        private CoCoStateGraphHost CreateHost(params Type[] logicTypes)
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            _objects.Add(asset);
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var layer = new CoCoStateGraphLayerRecord(new CoCoSerializedId128(602, 1), "AuthorLayer");
            for (int index = 0; index < logicTypes.Length; index++)
            {
                Type type = logicTypes[index];
                string name = type.GetCustomAttribute<CoCoStateAttribute>().Name;
                Assert.That(StandardDescriptors.TryCreate(type, name, out CoCoStateDescriptorId id), Is.True);
                var stateId = new CoCoSerializedId128(601, (ulong)(index + 1));
                if (index == 0) layer.InitialStateId = stateId;
                layer.States.Add(new CoCoStateGraphStateRecord(stateId, default, name,
                    new CoCoSerializedId128(id.High, id.Low), new EmptyStateConfig()));
                if (index > 0)
                    layer.Transitions.Add(new CoCoStateGraphTransitionRecord(new CoCoSerializedId128(603, (ulong)index),
                        layer.States[index - 1].StateId, stateId, index));
            }
            asset.Layers.Add(layer);
            var actor = new GameObject("IntentAuthorHost");
            actor.transform.position = new Vector3(_objects.Count * 10f, 0f, 0f);
            _objects.Add(actor);
            var host = actor.AddComponent<CoCoStateGraphHost>();
            Set(host, "autoStart", false);
            Set(host, "driver", (CoCoStateGraphDriver)2);
            Set(host, "stateGraphAsset", asset);
            Set(host, "intentSources", Array.Empty<MonoBehaviour>());
            Set(host, "operators", Array.Empty<MonoBehaviour>());
            return host;
        }

        private static void AddLocomotion(CoCoStateGraphHost host)
        {
            var controller = host.gameObject.AddComponent<CharacterController>();
            controller.height = 2f;
            controller.center = new Vector3(0f, 1f, 0f);
            var locomotion = host.gameObject.AddComponent<LocomotionOperator>();
            Set(host, "operators", new MonoBehaviour[] { locomotion });
        }
        private static void StartAndStep(CoCoStateGraphHost host)
        {
            Assert.That(host.TryStart(out CoCoDiagnostic start), Is.True, start.Message);
            Assert.That(host.TryStep(0.02d, out CoCoDiagnostic step), Is.True, step.Message);
        }
        private static void Set(CoCoStateGraphHost host, string name, object value) =>
            typeof(CoCoStateGraphHost).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(host, value);
    }
}
