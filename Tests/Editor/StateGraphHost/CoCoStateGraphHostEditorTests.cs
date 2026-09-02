using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Editor.StateGraphHost;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Editor.StateGraphHost
{
    internal struct EditorHostIntent
    {
        public int Value;
    }

    internal struct EditorHostEvent
    {
        public int Value;
    }

    internal struct OtherEditorHostEvent
    {
        public int Value;
    }

    internal sealed class EditorHostIntentSourceComponent :
        MonoBehaviour,
        ICoCoIntentFrameSource<EditorHostIntent>
    {
        public bool TrySample(
            in CoCoTickFrame tickFrame,
            out EditorHostIntent intent)
        {
            intent = default;
            return false;
        }
    }

    internal sealed class EditorHostEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<EditorHostEvent, EditorHostIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<EditorHostEvent> packet,
            out EditorHostIntent intent)
        {
            intent = default;
            return false;
        }
    }

    internal sealed class OtherEditorHostEventAdapterComponent :
        MonoBehaviour,
        ICoCoEventToIntentAdapter<OtherEditorHostEvent, EditorHostIntent>
    {
        public bool TryProject(
            in CoCoEventPacket<OtherEditorHostEvent> packet,
            out EditorHostIntent intent)
        {
            intent = default;
            return false;
        }
    }

    internal sealed class EditorHostOperatorComponent :
        MonoBehaviour,
        ICoCoOperator
    {
        public CoCoOperatorDescriptor Descriptor => null;

        public bool TryExecute(
            in CoCoOperatorExecutionContext context,
            out CoCoOperatorOutcome outcome)
        {
            outcome = CoCoOperatorOutcome.NoOp;
            return true;
        }
    }

    internal sealed class EditorHostActorContextComponent :
        MonoBehaviour,
        ICoCoActorContextBinding
    {
        public CoCoActorContextBindingDescriptor Descriptor => null;

        public bool TryCapture(
            in CoCoActorContextCaptureContext context,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return false;
        }
    }

    internal sealed class EditorRestoreNodeComponent :
        MonoBehaviour,
        ICoCoContextRestoreBinding
    {
        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    internal sealed class EditorRestoreDecoratorComponent :
        MonoBehaviour,
        ICoCoContextRestoreBinding,
        ICoCoTemporalDecoratorBinding
    {
        [SerializeField] private MonoBehaviour downstreamRestoreBinding;

        public MonoBehaviour DownstreamRestoreBinding => downstreamRestoreBinding;

        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    public sealed class CoCoStateGraphHostEditorTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
        }

        // ===== 候选发现（BindingCandidates，语义保持） =====

        [Test]
        public void CandidateDiscoveryUsesNearestHostAndDoesNotMutateSerialization()
        {
            CoCoStateGraphHost host = CreateHost("Outer");
            var validObject = CreateChild(host.transform, "Valid", false);
            var source = validObject.AddComponent<EditorHostIntentSourceComponent>();
            var adapter = validObject.AddComponent<EditorHostEventAdapterComponent>();
            var wrongAdapter =
                validObject.AddComponent<OtherEditorHostEventAdapterComponent>();

            var nestedObject = CreateChild(host.transform, "Nested", false);
            nestedObject.AddComponent<CoCoStateGraphHost>();
            var nestedSource =
                nestedObject.AddComponent<EditorHostIntentSourceComponent>();
            var nestedAdapter =
                nestedObject.AddComponent<EditorHostEventAdapterComponent>();

            var outsideObject = CreateObject("Outside");
            var outsideSource =
                outsideObject.AddComponent<EditorHostIntentSourceComponent>();
            var outsideAdapter =
                outsideObject.AddComponent<EditorHostEventAdapterComponent>();

            var serializedHost = new SerializedObject(host);
            int sourceSize =
                serializedHost.FindProperty("intentSources").arraySize;
            int adapterSize =
                serializedHost.FindProperty("eventAdapters").arraySize;
            var results = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingCandidates.FindIntentSources(
                host,
                null,
                results);
            Assert.That(results, Has.Member(source));
            Assert.That(results, Has.No.Member(nestedSource));
            Assert.That(results, Has.No.Member(outsideSource));

            CoCoStateGraphHostBindingCandidates.FindEventAdapters(
                host,
                typeof(EditorHostEvent),
                typeof(EditorHostIntent),
                null,
                results);
            Assert.That(results, Has.Member(adapter));
            Assert.That(results, Has.No.Member(wrongAdapter));
            Assert.That(results, Has.No.Member(nestedAdapter));
            Assert.That(results, Has.No.Member(outsideAdapter));

            serializedHost.Update();
            Assert.That(
                serializedHost.FindProperty("intentSources").arraySize,
                Is.EqualTo(sourceSize));
            Assert.That(
                serializedHost.FindProperty("eventAdapters").arraySize,
                Is.EqualTo(adapterSize));
        }

        [Test]
        public void ExplicitBindingArraysPreserveSerializedOrder()
        {
            CoCoStateGraphHost host = CreateHost("Serialized");
            var firstObject = CreateChild(host.transform, "First", false);
            var secondObject = CreateChild(host.transform, "Second", false);
            var first = firstObject.AddComponent<EditorHostIntentSourceComponent>();
            var second = secondObject.AddComponent<EditorHostIntentSourceComponent>();
            var adapter = secondObject.AddComponent<EditorHostEventAdapterComponent>();

            var serializedHost = new SerializedObject(host);
            SerializedProperty sources =
                serializedHost.FindProperty("intentSources");
            sources.arraySize = 2;
            sources.GetArrayElementAtIndex(0).objectReferenceValue = second;
            sources.GetArrayElementAtIndex(1).objectReferenceValue = first;
            SerializedProperty adapters =
                serializedHost.FindProperty("eventAdapters");
            adapters.arraySize = 1;
            adapters.GetArrayElementAtIndex(0).objectReferenceValue = adapter;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();

            serializedHost = new SerializedObject(host);
            sources = serializedHost.FindProperty("intentSources");
            adapters = serializedHost.FindProperty("eventAdapters");
            Assert.That(
                sources.GetArrayElementAtIndex(0).objectReferenceValue,
                Is.SameAs(second));
            Assert.That(
                sources.GetArrayElementAtIndex(1).objectReferenceValue,
                Is.SameAs(first));
            Assert.That(
                adapters.GetArrayElementAtIndex(0).objectReferenceValue,
                Is.SameAs(adapter));
        }

        // ===== Debugger 数据层（D11：直接命中，无私有反射） =====

        [Test]
        public void RejectedRefreshPreservesLastSnapshotAndMarksRetainedStale()
        {
            const ulong contentFingerprint = 0xD39UL;
            const double committedSeconds = 1.25d;
            CoCoStateGraphHost host = CreateHost("Debugger");
            CoCoStateGraphHostDebugSnapshot committed = CreateDebugSnapshot(
                contentFingerprint,
                committedSeconds);
            var state = new CoCoStateGraphHostDebuggerState();
            const BindingFlags instancePrivate =
                BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo snapshotField =
                typeof(CoCoStateGraphHostDebuggerState).GetField(
                    "_snapshot",
                    instancePrivate);
            Assert.That(snapshotField, Is.Not.Null);
            state.ObserveIdentity(host);
            snapshotField.SetValue(state, committed);
            Assert.That(
                state.Freshness,
                Is.EqualTo(CoCoDebuggerSnapshotFreshness.None));

            bool accepted = state.TryRefresh(host);

            Assert.That(accepted, Is.False);
            var retained = (CoCoStateGraphHostDebugSnapshot)snapshotField
                .GetValue(state);
            Assert.That(retained, Is.SameAs(committed));
            Assert.That(retained.ContentFingerprint, Is.EqualTo(contentFingerprint));
            Assert.That(retained.Seconds, Is.EqualTo(committedSeconds));
            Assert.That(
                state.Freshness,
                Is.EqualTo(CoCoDebuggerSnapshotFreshness.RetainedStale));
            Assert.That(state.LastRefreshDiagnostic.IsError, Is.True);
            Assert.That(
                state.LastRefreshDiagnostic.Code,
                Is.EqualTo(CoCoDiagnosticCode.InvalidLifecycleTransition));
        }

        [Test]
        public void FirstRejectedRefreshWithoutSnapshotStaysNone()
        {
            CoCoStateGraphHost host = CreateHost("Freshless Debugger");
            var state = new CoCoStateGraphHostDebuggerState();
            state.ObserveIdentity(host);

            Assert.That(state.TryRefresh(host), Is.False);
            Assert.That(
                state.Freshness,
                Is.EqualTo(CoCoDebuggerSnapshotFreshness.None));
            Assert.That(state.Snapshot, Is.Null);
        }

        [Test]
        public void TraceFilterBuilderKeepsStateAndTransitionIdentityModesExclusive()
        {
            Assert.That(
                CoCoStateId.TryCreate(0x11UL, 0x12UL, out CoCoStateId stateId),
                Is.True);
            Assert.That(
                CoCoTransitionId.TryCreate(
                    0x21UL,
                    0x22UL,
                    out CoCoTransitionId transitionId),
                Is.True);

            Assert.That(
                CoCoStateGraphHostDebuggerState.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.All,
                    "ignored",
                    out CoCoStateFlowTraceFilter all,
                    out string allValidation),
                Is.True);
            Assert.That(all.Kinds, Is.EqualTo(CoCoStateFlowTraceKind.All));
            Assert.That(all.StateId.IsValid, Is.False);
            Assert.That(all.TransitionId.IsValid, Is.False);
            Assert.That(allValidation, Is.Empty);

            Assert.That(
                CoCoStateGraphHostDebuggerState.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.StateId,
                    $"  {stateId.ToString().ToUpperInvariant()}  ",
                    out CoCoStateFlowTraceFilter state,
                    out string stateValidation),
                Is.True);
            Assert.That(state.StateId, Is.EqualTo(stateId));
            Assert.That(state.TransitionId.IsValid, Is.False);
            Assert.That(stateValidation, Is.Empty);

            Assert.That(
                CoCoStateGraphHostDebuggerState.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.TransitionId,
                    $"  {transitionId}  ",
                    out CoCoStateFlowTraceFilter transition,
                    out string transitionValidation),
                Is.True);
            Assert.That(transition.StateId.IsValid, Is.False);
            Assert.That(transition.TransitionId, Is.EqualTo(transitionId));
            Assert.That(transitionValidation, Is.Empty);

            Assert.That(
                CoCoStateGraphHostDebuggerState.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.StateId,
                    "   ",
                    out _,
                    out string blankValidation),
                Is.False);
            Assert.That(blankValidation, Is.Not.Empty);
            Assert.That(
                CoCoStateGraphHostDebuggerState.TryBuildTraceFilter(
                    CoCoStateGraphHostTraceFilterMode.TransitionId,
                    "not-an-id",
                    out _,
                    out string invalidValidation),
                Is.False);
            Assert.That(invalidValidation, Is.Not.Empty);
        }

        [Test]
        public void DebuggerHostIdentityChangeResetsTraceFilter()
        {
            CoCoStateGraphHost first = CreateHost("First Debugger Host");
            CoCoStateGraphHost second = CreateHost("Second Debugger Host");
            var state = new CoCoStateGraphHostDebuggerState();

            state.ObserveIdentity(first);
            state.SetTraceFilter(
                CoCoStateGraphHostTraceFilterMode.TransitionId,
                "00000000000000000000000000000001");

            state.ObserveIdentity(first);
            Assert.That(
                state.TraceFilterMode,
                Is.EqualTo(CoCoStateGraphHostTraceFilterMode.TransitionId));
            Assert.That(state.TraceFilterText, Is.Not.Empty);

            state.ObserveIdentity(second);
            Assert.That(
                state.TraceFilterMode,
                Is.EqualTo(CoCoStateGraphHostTraceFilterMode.All));
            Assert.That(state.TraceFilterText, Is.EqualTo(string.Empty));
            Assert.That(
                state.Freshness,
                Is.EqualTo(CoCoDebuggerSnapshotFreshness.None));
        }

        [Test]
        public void SnapshotRowsProjectSectionsAndLayerDetails()
        {
            CoCoStateGraphHostDebugSnapshot snapshot = CreateDebugSnapshot(
                0xABUL,
                2.5d);
            var state = new CoCoStateGraphHostDebuggerState();
            const BindingFlags instancePrivate =
                BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(CoCoStateGraphHostDebuggerState).GetField(
                    "_snapshot",
                    instancePrivate)
                .SetValue(state, snapshot);

            List<CoCoDebuggerSnapshotRow> rows = state.BuildSnapshotRows();

            Assert.That(rows, Is.Not.Empty);
            Assert.That(rows[0].Section, Is.Not.Empty);
            Assert.That(rows[0].Key, Is.Not.Empty);
            Assert.That(rows[0].Value, Is.Not.Empty);
        }

        [Test]
        public void TraceRowsGroupEntriesByKind()
        {
            CoCoStateFlowTraceEntry[] entries =
            {
                CreateTraceEntry(CoCoStateFlowTraceKind.ActivePath),
                CreateTraceEntry(CoCoStateFlowTraceKind.OperatorOutcome),
                CreateTraceEntry(CoCoStateFlowTraceKind.EventPublished),
            };
            var state = new CoCoStateGraphHostDebuggerState();
            SeedTraceEntries(state, entries, entries.Length);

            List<CoCoDebuggerTraceRow> rows = state.BuildTraceRows();

            Assert.That(rows, Is.Not.Empty);
            int headers = 0;
            for (int index = 0; index < rows.Count; index++)
            {
                if (rows[index].IsGroupHeader)
                {
                    headers++;
                    Assert.That(rows[index].Text, Is.Not.Empty);
                }
                else
                {
                    Assert.That(rows[index].Text, Does.Contain("Tick"));
                }
            }

            Assert.That(headers, Is.EqualTo(3));
        }

        [Test]
        public void TraceRowsEmptyWhenNothingVisible()
        {
            var state = new CoCoStateGraphHostDebuggerState();
            Assert.That(state.BuildTraceRows(), Is.Empty);
        }

        // ===== BindingRules（D3 authoring hints） =====

        [Test]
        public void IntentSourceHintsCoverNullWrongInterfaceAndBoundary()
        {
            CoCoStateGraphHost host = CreateHost("Hints");
            GameObject inside = CreateChild(host.transform, "Inside", false);
            GameObject outside = CreateObject("Outside");
            var insideSource =
                inside.AddComponent<EditorHostIntentSourceComponent>();
            var outsideSource =
                outside.AddComponent<EditorHostIntentSourceComponent>();
            var plain = inside.AddComponent<EditorHostOperatorComponent>();

            CoCoBindingHint? insideHint =
                CoCoStateGraphHostBindingRules.BuildIntentSourceHint(
                    host, insideSource);
            Assert.That(insideHint.HasValue, Is.False);

            CoCoBindingHint? nullHint =
                CoCoStateGraphHostBindingRules.BuildIntentSourceHint(host, null);
            Assert.That(nullHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Info));

            CoCoBindingHint? wrongHint =
                CoCoStateGraphHostBindingRules.BuildIntentSourceHint(host, plain);
            Assert.That(wrongHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Error));
            Assert.That(wrongHint.Value.Target, Is.SameAs(plain));

            CoCoBindingHint? boundaryHint =
                CoCoStateGraphHostBindingRules.BuildIntentSourceHint(
                    host, outsideSource);
            Assert.That(boundaryHint.Value.Kind, Is.EqualTo(
                CoCoBindingHintKind.Warning));
            Assert.That(boundaryHint.Value.Target, Is.SameAs(outsideSource));
        }

        [Test]
        public void OperatorHintsCoverWrongInterfaceAndBoundary()
        {
            CoCoStateGraphHost host = CreateHost("Operator Hints");
            GameObject inside = CreateChild(host.transform, "Inside", false);
            GameObject outside = CreateObject("Outside");
            var operatorComponent = inside.AddComponent<EditorHostOperatorComponent>();
            var outsideOperator =
                outside.AddComponent<EditorHostOperatorComponent>();
            var notOperator = inside.AddComponent<EditorHostIntentSourceComponent>();

            Assert.That(
                CoCoStateGraphHostBindingRules.BuildOperatorHint(
                    host, operatorComponent).HasValue,
                Is.False);
            Assert.That(
                CoCoStateGraphHostBindingRules.BuildOperatorHint(
                    host, notOperator).Value.Kind,
                Is.EqualTo(CoCoBindingHintKind.Error));
            Assert.That(
                CoCoStateGraphHostBindingRules.BuildOperatorHint(
                    host, outsideOperator).Value.Kind,
                Is.EqualTo(CoCoBindingHintKind.Warning));
        }

        [Test]
        public void ActorContextHintsCoverWrongInterfaceAndBoundary()
        {
            CoCoStateGraphHost host = CreateHost("Actor Hints");
            GameObject inside = CreateChild(host.transform, "Inside", false);
            GameObject outside = CreateObject("Outside");
            var actor = inside.AddComponent<EditorHostActorContextComponent>();
            var outsideActor =
                outside.AddComponent<EditorHostActorContextComponent>();
            var notActor = inside.AddComponent<EditorHostOperatorComponent>();

            Assert.That(
                CoCoStateGraphHostBindingRules.BuildActorContextHint(
                    host, actor).HasValue,
                Is.False);
            Assert.That(
                CoCoStateGraphHostBindingRules.BuildActorContextHint(
                    host, null).HasValue,
                Is.False);
            Assert.That(
                CoCoStateGraphHostBindingRules.BuildActorContextHint(
                    host, notActor).Value.Kind,
                Is.EqualTo(CoCoBindingHintKind.Error));
            Assert.That(
                CoCoStateGraphHostBindingRules.BuildActorContextHint(
                    host, outsideActor).Value.Kind,
                Is.EqualTo(CoCoBindingHintKind.Warning));
        }

        [Test]
        public void DuplicateReferencesReportSecondAndLaterOccurrences()
        {
            CoCoStateGraphHost host = CreateHost("Duplicates");
            GameObject child = CreateChild(host.transform, "Child", false);
            var source = child.AddComponent<EditorHostIntentSourceComponent>();

            var references = new List<MonoBehaviour> { source, null, source };
            List<MonoBehaviour> duplicates =
                CoCoStateGraphHostBindingRules.FindDuplicateReferences(references);

            Assert.That(duplicates, Is.EqualTo(new[] { source }));
        }

        // ===== Restore 链（D5：预览 + 候选 + 校验） =====

        [Test]
        public void RestoreChainPreviewStopsAtNonBindingNodeAndMarksBreak()
        {
            GameObject rootObject = CreateObject("Chain");
            var root = rootObject.AddComponent<EditorRestoreDecoratorComponent>();
            var plain = CreateChild(rootObject.transform, "Plain", false)
                .AddComponent<EditorHostOperatorComponent>();
            var serializedRoot = new SerializedObject(root);
            serializedRoot.FindProperty("downstreamRestoreBinding")
                .objectReferenceValue = plain;
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            var nodes =
                new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
            CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(root, nodes);

            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[0].IsRoot, Is.True);
            Assert.That(nodes[0].ImplementsContract, Is.True);
            Assert.That(nodes[1].ImplementsContract, Is.False);

            CoCoBindingHint? breakHint =
                CoCoStateGraphHostBindingRules.BuildRestoreChainBreakHint(nodes);
            Assert.That(breakHint.HasValue, Is.True);
            Assert.That(breakHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Error));
            Assert.That(breakHint.Value.Target, Is.SameAs(plain));
        }

        [Test]
        public void RestoreChainPreviewGuardsAgainstCycles()
        {
            GameObject rootObject = CreateObject("Cycle");
            var root = rootObject.AddComponent<EditorRestoreDecoratorComponent>();
            var serializedRoot = new SerializedObject(root);
            serializedRoot.FindProperty("downstreamRestoreBinding")
                .objectReferenceValue = root;
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            var nodes =
                new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
            CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(root, nodes);

            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[1].IsRepeat, Is.True);
            CoCoBindingHint? breakHint =
                CoCoStateGraphHostBindingRules.BuildRestoreChainBreakHint(nodes);
            Assert.That(breakHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Error));
        }

        [Test]
        public void RestoreChainCandidatesCollectInsideBoundaryAndSortByHierarchy()
        {
            CoCoStateGraphHost host = CreateHost("Wire");
            var rootChild = CreateChild(host.transform, "B_Root", false);
            var deepChild = CreateChild(rootChild.transform, "A_Deep", false);
            var root = rootChild.AddComponent<EditorRestoreNodeComponent>();
            var deep = deepChild.AddComponent<EditorRestoreNodeComponent>();
            var outsideObject = CreateObject("OutsideRestore");
            outsideObject.AddComponent<EditorRestoreNodeComponent>();

            var chain = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingRules.CollectRestoreChainCandidates(host, chain);

            Assert.That(chain, Is.EqualTo(new[] { deep, root }));
            Assert.That(
                CoCoStateGraphHostBindingRules.TryValidateRestoreChain(
                    chain, out CoCoBindingHint failure),
                Is.True);
            Assert.That(failure.Kind, Is.EqualTo(CoCoBindingHintKind.Info));
        }

        [Test]
        public void RestoreChainValidationFailsOnEmptyCandidates()
        {
            CoCoStateGraphHost host = CreateHost("EmptyWire");
            var chain = new List<MonoBehaviour>();

            Assert.That(
                CoCoStateGraphHostBindingRules.TryValidateRestoreChain(
                    chain, out CoCoBindingHint failure),
                Is.False);
            Assert.That(failure.Kind, Is.EqualTo(CoCoBindingHintKind.Warning));
        }

        // ===== 场景候选（现状保持：全场景；Actor Context 边界内） =====

        [Test]
        public void SceneCandidateScansIncludeOutsideBoundaryAndExcludeAssigned()
        {
            CoCoStateGraphHost host = CreateHost("SceneScan");
            GameObject inside = CreateChild(host.transform, "Inside", false);
            GameObject outside = CreateObject("Outside");
            var insideSource =
                inside.AddComponent<EditorHostIntentSourceComponent>();
            var outsideSource =
                outside.AddComponent<EditorHostIntentSourceComponent>();
            var outsideOperator =
                outside.AddComponent<EditorHostOperatorComponent>();

            var results = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingRules.CollectSceneIntentSources(
                new[] { insideSource },
                results);
            Assert.That(results, Has.No.Member(insideSource));
            Assert.That(results, Has.Member(outsideSource));

            CoCoStateGraphHostBindingRules.CollectSceneOperators(null, results);
            Assert.That(results, Has.Member(outsideOperator));
        }

        [Test]
        public void ActorContextCandidatesStayInsideBoundary()
        {
            CoCoStateGraphHost host = CreateHost("ActorScan");
            GameObject inside = CreateChild(host.transform, "Inside", false);
            GameObject outside = CreateObject("Outside");
            var insideActor =
                inside.AddComponent<EditorHostActorContextComponent>();
            outside.AddComponent<EditorHostActorContextComponent>();

            var results = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingRules.CollectActorContextCandidates(
                host,
                null,
                results);

            Assert.That(results, Is.EqualTo(new[] { insideActor }));
        }

        // ===== 辅助 =====

        private CoCoStateGraphHost CreateHost(string name)
        {
            GameObject gameObject = CreateObject(name);
            return gameObject.AddComponent<CoCoStateGraphHost>();
        }

        private GameObject CreateChild(Transform parent, string name, bool active)
        {
            GameObject child = CreateObject(name);
            child.transform.SetParent(parent, false);
            child.SetActive(active);
            return child;
        }

        private GameObject CreateObject(string name)
        {
            var gameObject = new GameObject(name);
            _objects.Add(gameObject);
            return gameObject;
        }

        private static CoCoStateGraphHostDebugSnapshot CreateDebugSnapshot(
            ulong contentFingerprint,
            double seconds)
        {
            const BindingFlags instancePrivate =
                BindingFlags.Instance | BindingFlags.NonPublic;
            ConstructorInfo snapshotConstructor =
                typeof(CoCoStateGraphHostDebugSnapshot).GetConstructors(
                    instancePrivate)[0];
            Type graphType = snapshotConstructor.GetParameters()[0].ParameterType;
            ConstructorInfo graphConstructor =
                graphType.GetConstructors(instancePrivate)[0];
            object[] graphArguments = CreateDefaultArguments(graphConstructor);
            graphArguments[0] = 1U;
            graphArguments[1] = contentFingerprint;
            graphArguments[3] = 0xCA7A10UL;
            graphArguments[10] = seconds;
            object graph = graphConstructor.Invoke(graphArguments);
            object[] snapshotArguments =
                CreateDefaultArguments(snapshotConstructor);
            snapshotArguments[0] = graph;
            return (CoCoStateGraphHostDebugSnapshot)snapshotConstructor.Invoke(
                snapshotArguments);
        }

        private static object[] CreateDefaultArguments(ConstructorInfo constructor)
        {
            ParameterInfo[] parameters = constructor.GetParameters();
            var arguments = new object[parameters.Length];
            for (int index = 0; index < parameters.Length; index++)
            {
                Type parameterType = parameters[index].ParameterType;
                arguments[index] = parameterType.IsArray
                    ? Array.CreateInstance(
                        parameterType.GetElementType(),
                        0)
                    : parameterType.IsValueType
                        ? Activator.CreateInstance(parameterType)
                        : null;
            }

            return arguments;
        }

        /// <summary>
        /// 经 internal 构造器构造指定 Kind 的 Trace 条目（分组与格式化不依赖
        /// IsValid；Runtime internal 契约，与快照构造同一反射口径）。
        /// </summary>
        private static CoCoStateFlowTraceEntry CreateTraceEntry(
            CoCoStateFlowTraceKind kind)
        {
            const BindingFlags instanceNonPublic =
                BindingFlags.Instance | BindingFlags.NonPublic;
            ConstructorInfo constructor =
                typeof(CoCoStateFlowTraceEntry).GetConstructors(instanceNonPublic)[0];
            object[] arguments = CreateDefaultArguments(constructor);
            arguments[0] = kind;
            var entry = (CoCoStateFlowTraceEntry)constructor.Invoke(arguments);
            Assert.That(entry.Kind, Is.EqualTo(kind));
            return entry;
        }

        private static void SeedTraceEntries(
            CoCoStateGraphHostDebuggerState state,
            CoCoStateFlowTraceEntry[] entries,
            int visibleCount)
        {
            const BindingFlags instancePrivate =
                BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(CoCoStateGraphHostDebuggerState).GetField(
                    "_traceEntries",
                    instancePrivate)
                .SetValue(state, entries);
            typeof(CoCoStateGraphHostDebuggerState).GetField(
                    "_visibleTraceCount",
                    instancePrivate)
                .SetValue(state, visibleCount);
        }
    }
}
