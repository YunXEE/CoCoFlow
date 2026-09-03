using System;
using System.Collections.Generic;
using CoCoFlow.Editor.StateGraphHost;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
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

    /// <summary>普通 Restore 节点：只实现基础契约，不可作为链中游。</summary>
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

    /// <summary>装饰器节点：实现契约 + downstream seam（可作链中游）。</summary>
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

            CoCoStateGraphHostBindingCandidates.FindOperators(
                host,
                null,
                results);
            Assert.That(results, Has.No.Member(outsideAdapter));

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

        // ===== Debugger（v4：当前帧 + Temporal Ring + 持久化帧） =====

        [Test]
        public void TemporalSnapshotPreservesLogicalDepthOrderAndUsage()
        {
            CoCoTemporalFrameInfo newest = CreateTemporalFrame(1UL, 12UL, 3UL);
            CoCoTemporalFrameInfo middle = CreateTemporalFrame(1UL, 11UL, 2UL);
            CoCoTemporalFrameInfo oldest = CreateTemporalFrame(1UL, 10UL, 1UL);
            CoCoStateGraphHostTemporalDebugSnapshot snapshot =
                CreateTemporalSnapshot(5, newest, middle, oldest);

            Assert.That(snapshot.Capacity, Is.EqualTo(5));
            Assert.That(snapshot.Count, Is.EqualTo(3));
            Assert.That(snapshot.GetFrame(0), Is.EqualTo(newest));
            Assert.That(snapshot.GetFrame(1), Is.EqualTo(middle));
            Assert.That(snapshot.GetFrame(2), Is.EqualTo(oldest));
        }

        [Test]
        public void RejectedRefreshRetainsLastTemporalSnapshotAsStale()
        {
            CoCoStateGraphHost host = CreateHost("Debugger");
            CoCoStateGraphHostTemporalDebugSnapshot committed =
                CreateTemporalSnapshot(
                    4,
                    CreateTemporalFrame(1UL, 4UL, 4UL));
            var state = new CoCoStateGraphHostDebuggerState();
            state.ObserveIdentity(host);
            state.SeedSnapshotForTests(committed);

            Assert.That(state.TryRefresh(host), Is.False);
            Assert.That(state.Snapshot, Is.SameAs(committed));
            Assert.That(
                state.Freshness,
                Is.EqualTo(CoCoDebuggerSnapshotFreshness.RetainedStale));
            Assert.That(state.LastRefreshDiagnostic.IsError, Is.True);
        }

        [Test]
        public void HostIdentityChangeClearsDebuggerSelectionAndSnapshots()
        {
            CoCoStateGraphHost first = CreateHost("First Debugger Host");
            CoCoStateGraphHost second = CreateHost("Second Debugger Host");
            var state = new CoCoStateGraphHostDebuggerState();
            state.ObserveIdentity(first);
            state.SeedSnapshotForTests(CreateTemporalSnapshot(
                3,
                CreateTemporalFrame(1UL, 3UL, 3UL),
                CreateTemporalFrame(1UL, 2UL, 2UL)));
            state.SelectDepth(1);

            state.ObserveIdentity(second);

            Assert.That(state.Snapshot, Is.Null);
            Assert.That(state.SelectedDepth, Is.EqualTo(0));
            Assert.That(state.HasPersistedFrame, Is.False);
            Assert.That(
                state.Freshness,
                Is.EqualTo(CoCoDebuggerSnapshotFreshness.None));
        }

        [Test]
        public void PersistedFrameMatchesOnlyItsExactRingFrame()
        {
            CoCoTemporalFrameInfo newest = CreateTemporalFrame(1UL, 8UL, 8UL);
            CoCoTemporalFrameInfo persisted = CreateTemporalFrame(1UL, 7UL, 7UL);
            var state = new CoCoStateGraphHostDebuggerState();
            state.SeedSnapshotForTests(CreateTemporalSnapshot(
                4,
                newest,
                persisted));
            state.SeedPersistedFrameForTests(
                persisted,
                2,
                DateTimeOffset.Parse("2026-09-03T08:00:00Z"));

            Assert.That(state.FindPersistedDepth(), Is.EqualTo(1));

            state.SeedPersistedFrameForTests(
                CreateTemporalFrame(1UL, 7UL, 99UL),
                2,
                DateTimeOffset.Parse("2026-09-03T08:00:00Z"));
            Assert.That(state.FindPersistedDepth(), Is.EqualTo(-1));
        }

        [Test]
        public void PersistedFrameOutsideRingStillBuildsIndependentDetails()
        {
            var state = new CoCoStateGraphHostDebuggerState();
            state.SeedSnapshotForTests(CreateTemporalSnapshot(
                2,
                CreateTemporalFrame(1UL, 2UL, 2UL)));
            state.SeedPersistedFrameForTests(
                CreateTemporalFrame(9UL, 1UL, 1UL),
                1,
                DateTimeOffset.Parse("2026-09-03T08:00:00Z"));

            Assert.That(state.HasPersistedFrame, Is.True);
            Assert.That(state.FindPersistedDepth(), Is.EqualTo(-1));
            Assert.That(state.BuildPersistedFrameRows(), Is.Not.Empty);
        }

        [Test]
        public void PersistedFrameRowsShowUtcThenLocalWrittenTime()
        {
            DateTimeOffset writtenUtc =
                DateTimeOffset.Parse("2026-09-03T08:00:00Z");
            var state = new CoCoStateGraphHostDebuggerState();
            state.SeedPersistedFrameForTests(
                CreateTemporalFrame(1UL, 1UL, 1UL),
                1,
                writtenUtc);

            List<CoCoDebuggerKeyValueRow> rows =
                state.BuildPersistedFrameRows();

            Assert.That(rows[1].Key, Does.Contain("UTC"));
            Assert.That(
                rows[1].Value,
                Is.EqualTo("2026-09-03 08:00:00Z"));
            Assert.That(
                rows[2].Key == "Written at (Local)" ||
                rows[2].Key == "写盘时间（本地）",
                Is.True);
            Assert.That(
                rows[2].Value,
                Is.EqualTo(writtenUtc.ToLocalTime().ToString(
                    "yyyy-MM-dd HH:mm:ss zzz",
                    System.Globalization.CultureInfo.InvariantCulture)));
        }

        [Test]
        public void TemporalRingCreatesOneSelectableNodePerConfiguredSlot()
        {
            var ring = new CoCoTemporalRingElement();
            ring.SetData(
                CreateTemporalSnapshot(
                    5,
                    CreateTemporalFrame(1UL, 2UL, 2UL)),
                0,
                -1);

            Assert.That(
                ring.Query<Button>(
                        className: "ccflow-temporal-ring__slot")
                    .ToList()
                    .Count,
                Is.EqualTo(5));
        }

        [Test]
        public void SceneMarkerReadsHostTransformWithoutDirtyingIt()
        {
            CoCoStateGraphHost host = CreateHost("Marker");
            host.transform.position = new Vector3(3f, 4f, 5f);
            EditorUtility.ClearDirty(host);
            EditorUtility.ClearDirty(host.gameObject);

            Assert.That(
                CoCoStateGraphDebuggerWindow.TryGetMarkerWorldPosition(
                    host,
                    out Vector3 position),
                Is.True);
            Assert.That(position, Is.EqualTo(new Vector3(3f, 4f, 5f)));
            Assert.That(EditorUtility.IsDirty(host), Is.False);
            Assert.That(EditorUtility.IsDirty(host.gameObject), Is.False);
        }

        [Test]
        public void DebuggerResolvesHostFromSelectedGameObjectOrChild()
        {
            CoCoStateGraphHost host = CreateHost("Selected Host");
            GameObject child = CreateChild(host.transform, "Selected Child", true);
            GameObject unrelated = CreateObject("Unrelated");

            Assert.That(
                CoCoStateGraphDebuggerWindow.ResolveHost(host.gameObject),
                Is.SameAs(host));
            Assert.That(
                CoCoStateGraphDebuggerWindow.ResolveHost(host),
                Is.SameAs(host));
            Assert.That(
                CoCoStateGraphDebuggerWindow.ResolveHost(child),
                Is.SameAs(host));
            Assert.That(
                CoCoStateGraphDebuggerWindow.ResolveHost(child.transform),
                Is.SameAs(host));
            Assert.That(
                CoCoStateGraphDebuggerWindow.ResolveHost(unrelated),
                Is.Null);
        }

        [Test]
        public void FollowSelectionRetainsHostForTransientUnrelatedSelection()
        {
            CoCoStateGraphHost host = CreateHost("Retained Host");
            GameObject unrelated = CreateObject("Transient Selection");

            Assert.That(
                CoCoStateGraphDebuggerWindow.ResolveFollowTarget(
                    unrelated,
                    host),
                Is.SameAs(host));
            Assert.That(
                CoCoStateGraphDebuggerWindow.ResolveFollowTarget(null, host),
                Is.SameAs(host));
        }

        [Test]
        public void TargetLocatorRebindsEquivalentHostAfterInstanceReplacement()
        {
            string rootName =
                "CoCoDebuggerLocator-" + Guid.NewGuid().ToString("N");
            GameObject originalRoot = CreateObject(rootName);
            GameObject originalHostObject = CreateChild(
                originalRoot.transform,
                "Player",
                true);
            CoCoStateGraphHost originalHost =
                originalHostObject.AddComponent<CoCoStateGraphHost>();

            Assert.That(
                CoCoStateGraphDebuggerTargetLocator.TryCapture(
                    originalHost,
                    out CoCoStateGraphDebuggerTargetLocator locator),
                Is.True);

            string serializedLocator = JsonUtility.ToJson(locator);
            CoCoStateGraphDebuggerTargetLocator restoredLocator =
                JsonUtility.FromJson<CoCoStateGraphDebuggerTargetLocator>(
                    serializedLocator);

            Object.DestroyImmediate(originalRoot);
            GameObject replacementRoot = CreateObject(rootName);
            GameObject replacementHostObject = CreateChild(
                replacementRoot.transform,
                "Player",
                true);
            CoCoStateGraphHost replacementHost =
                replacementHostObject.AddComponent<CoCoStateGraphHost>();

            Assert.That(
                restoredLocator.TryResolve(
                    out CoCoStateGraphHost resolvedHost),
                Is.True);
            Assert.That(resolvedHost, Is.SameAs(replacementHost));
            Assert.That(resolvedHost, Is.Not.SameAs(originalHost));
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
            // Runtime 冻结要求每个 Intent Source 索引恰好绑定一次：null 条目 → Error。
            Assert.That(nullHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Error));

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
        public void OperatorHintsCoverNullWrongInterfaceAndBoundary()
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
            // Runtime 逐条校验 Operator 数组：null 条目导致启动拒绝 → Error（P2-03）。
            Assert.That(
                CoCoStateGraphHostBindingRules.BuildOperatorHint(
                    host, null).Value.Kind,
                Is.EqualTo(CoCoBindingHintKind.Error));
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
        public void DuplicateIndicesReportSecondAndLaterOccurrences()
        {
            CoCoStateGraphHost host = CreateHost("Duplicates");
            GameObject child = CreateChild(host.transform, "Child", false);
            var source = child.AddComponent<EditorHostIntentSourceComponent>();

            var references = new List<MonoBehaviour> { source, null, source };
            List<int> duplicates =
                CoCoStateGraphHostBindingRules.FindDuplicateIndices(references);

            // 仅第二次及以后出现者被标记；首次出现保持无警告。
            Assert.That(duplicates, Is.EqualTo(new[] { 2 }));
        }

        // ===== Restore 链（D5：预览 + 候选 + WirePlan） =====

        [Test]
        public void RestoreChainPreviewStopsAtNonBindingNodeAndMarksBreak()
        {
            CoCoStateGraphHost host = CreateHost("Chain");
            GameObject rootObject = CreateObject("ChainRoot");
            rootObject.transform.SetParent(host.transform, false);
            var root = rootObject.AddComponent<EditorRestoreDecoratorComponent>();
            var plain = CreateChild(rootObject.transform, "Plain", false)
                .AddComponent<EditorHostOperatorComponent>();
            var serializedRoot = new SerializedObject(root);
            serializedRoot.FindProperty("downstreamRestoreBinding")
                .objectReferenceValue = plain;
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            var nodes =
                new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
            CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(
                root, host, nodes);

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
        public void RestoreChainPreviewMarksOutOfBoundaryDownstream()
        {
            CoCoStateGraphHost host = CreateHost("BoundaryChain");
            GameObject insideObject = CreateChild(host.transform, "Inside", false);
            var inside = insideObject.AddComponent<EditorRestoreDecoratorComponent>();
            var outside = CreateObject("OutsideRestore")
                .AddComponent<EditorRestoreNodeComponent>();
            var serialized = new SerializedObject(inside);
            serialized.FindProperty("downstreamRestoreBinding")
                .objectReferenceValue = outside;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var nodes =
                new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
            CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(
                inside, host, nodes);

            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[1].IsInsideBoundary, Is.False);
            CoCoBindingHint? breakHint =
                CoCoStateGraphHostBindingRules.BuildRestoreChainBreakHint(nodes);
            Assert.That(breakHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Error));
            Assert.That(breakHint.Value.Target, Is.SameAs(outside));
        }

        [Test]
        public void RestoreChainPreviewGuardsAgainstCycles()
        {
            CoCoStateGraphHost host = CreateHost("Cycle");
            GameObject rootObject = CreateChild(host.transform, "CycleRoot", false);
            var root = rootObject.AddComponent<EditorRestoreDecoratorComponent>();
            var serializedRoot = new SerializedObject(root);
            serializedRoot.FindProperty("downstreamRestoreBinding")
                .objectReferenceValue = root;
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            var nodes =
                new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
            CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(
                root, host, nodes);

            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[1].IsRepeat, Is.True);
            CoCoBindingHint? breakHint =
                CoCoStateGraphHostBindingRules.BuildRestoreChainBreakHint(nodes);
            Assert.That(breakHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Error));
        }

        [Test]
        public void RestoreChainPreviewReportsDestroyedDownstreamWithoutThrowing()
        {
            CoCoStateGraphHost host = CreateHost("DestroyedChain");
            GameObject rootObject = CreateChild(host.transform, "Root", false);
            GameObject tailObject = CreateChild(host.transform, "Tail", false);
            var root = rootObject.AddComponent<EditorRestoreDecoratorComponent>();
            var tail = tailObject.AddComponent<EditorRestoreNodeComponent>();
            var serialized = new SerializedObject(root);
            serialized.FindProperty("downstreamRestoreBinding")
                .objectReferenceValue = tail;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            // 失销下游：托管包装仍在（ReferenceEquals 非空）但 Unity 伪装 null。
            Object.DestroyImmediate(tail);

            var nodes =
                new List<CoCoStateGraphHostBindingRules.CoCoRestoreChainNode>();
            Assert.DoesNotThrow(() =>
                CoCoStateGraphHostBindingRules.BuildRestoreChainPreview(
                    root, host, nodes));
            Assert.That(nodes.Count, Is.EqualTo(2));
            Assert.That(nodes[1].IsDestroyed, Is.True);

            CoCoBindingHint? breakHint =
                CoCoStateGraphHostBindingRules.BuildRestoreChainBreakHint(nodes);
            Assert.That(breakHint.HasValue, Is.True);
            Assert.That(breakHint.Value.Kind, Is.EqualTo(CoCoBindingHintKind.Error));
        }

        [Test]
        public void InspectorChainPreviewRendersDestroyedDownstreamWithoutThrowing()
        {
            CoCoStateGraphHost host = CreateHost("DestroyedRender");
            GameObject rootObject = CreateChild(host.transform, "Root", false);
            GameObject tailObject = CreateChild(host.transform, "Tail", false);
            var root = rootObject.AddComponent<EditorRestoreDecoratorComponent>();
            var tail = tailObject.AddComponent<EditorRestoreNodeComponent>();
            var serializedHost = new SerializedObject(host);
            serializedHost.FindProperty("contextRestoreBinding")
                .objectReferenceValue = root;
            serializedHost.ApplyModifiedPropertiesWithoutUndo();
            var serializedRoot = new SerializedObject(root);
            serializedRoot.FindProperty("downstreamRestoreBinding")
                .objectReferenceValue = tail;
            serializedRoot.ApplyModifiedPropertiesWithoutUndo();

            // 失销下游：托管包装仍在但 Unity 伪装 null——旧实现在此渲染时抛异常。
            Object.DestroyImmediate(tail);

            UnityEditor.Editor editor = UnityEditor.Editor.CreateEditor(host);
            try
            {
                VisualElement inspectorRoot = null;
                Assert.DoesNotThrow(() =>
                {
                    inspectorRoot =
                        ((CoCoStateGraphHostEditor)editor).CreateInspectorGUI();
                });
                Assert.That(inspectorRoot, Is.Not.Null);

                // 链预览渲染：两行（root + 失销占位），第二行加粗，不断链 Error 诊断存在。
                System.Collections.Generic.List<Label> chainRows =
                    inspectorRoot.Query<Label>(name: "ccflow-chain-text")
                        .ToList();
                Assert.That(chainRows.Count, Is.EqualTo(2));
                Assert.That(
                    chainRows[1].style.unityFontStyleAndWeight.value,
                    Is.EqualTo(FontStyle.Bold));
                System.Collections.Generic.List<Label> diagnostics =
                    inspectorRoot.Query<Label>(
                        name: "ccflow-diagnostic-message").ToList();
                Assert.That(diagnostics, Is.Not.Empty);
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void RestoreChainCandidatesSortByHierarchyParentBeforeChild()
        {
            CoCoStateGraphHost host = CreateHost("Wire");
            var rootChild = CreateChild(host.transform, "B_Root", false);
            var deepChild = CreateChild(rootChild.transform, "A_Deep", false);
            var root = rootChild.AddComponent<EditorRestoreNodeComponent>();
            var deep = deepChild.AddComponent<EditorRestoreNodeComponent>();

            var chain = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingRules.CollectRestoreChainCandidates(host, chain);

            // 层级路径排序：父路径 Wire/B_Root 先于子路径 Wire/B_Root/A_Deep。
            Assert.That(chain, Is.EqualTo(new[] { root, deep }));
        }

        [Test]
        public void WirePlanResolvesTargetsForDecoratorChainWithPlainTail()
        {
            CoCoStateGraphHost host = CreateHost("WirePlan");
            var rootChild = CreateChild(host.transform, "Root", false);
            var tailChild = CreateChild(host.transform, "Tail", false);
            var root = rootChild.AddComponent<EditorRestoreDecoratorComponent>();
            var tail = tailChild.AddComponent<EditorRestoreDecoratorComponent>();

            var chain = new List<MonoBehaviour> { root, tail };
            Assert.That(
                CoCoStateGraphHostBindingRules.TryBuildRestoreWirePlan(
                    host, chain,
                    out CoCoStateGraphHostBindingRules.CoCoRestoreWirePlan plan,
                    out CoCoBindingHint failure),
                Is.True,
                failure.English);
            Assert.That(plan.Root, Is.SameAs(root));
            Assert.That(plan.Upstreams.Count, Is.EqualTo(1));
            Assert.That(plan.Upstreams[0], Is.SameAs(root));
            Assert.That(plan.Downstreams[0], Is.SameAs(tail));
            // 尾节点具备 downstream 字段 → 列入清空目标。
            Assert.That(plan.TailToClear, Is.SameAs(tail));
            Assert.That(
                CoCoStateGraphHostBindingRules.FindDownstreamProperty(root),
                Is.Not.Null);
        }

        [Test]
        public void WirePlanRejectsNonDecoratorMidNodeWithZeroWritePlan()
        {
            CoCoStateGraphHost host = CreateHost("WirePlanBad");
            var rootChild = CreateChild(host.transform, "Root", false);
            var tailChild = CreateChild(host.transform, "Tail", false);
            // root 为普通节点（无 decorator/字段）却位于中游 → 成链不可能。
            var root = rootChild.AddComponent<EditorRestoreNodeComponent>();
            var tail = tailChild.AddComponent<EditorRestoreNodeComponent>();

            var chain = new List<MonoBehaviour> { root, tail };
            Assert.That(
                CoCoStateGraphHostBindingRules.TryBuildRestoreWirePlan(
                    host, chain,
                    out CoCoStateGraphHostBindingRules.CoCoRestoreWirePlan plan,
                    out CoCoBindingHint failure),
                Is.False);
            Assert.That(failure.Kind, Is.EqualTo(CoCoBindingHintKind.Warning));
            Assert.That(failure.Target, Is.SameAs(root));
            Assert.That(plan.Root, Is.Null);
            Assert.That(plan.Upstreams, Is.Null);
        }

        [Test]
        public void WirePlanFailsOnEmptyCandidates()
        {
            CoCoStateGraphHost host = CreateHost("EmptyWire");
            var chain = new List<MonoBehaviour>();

            Assert.That(
                CoCoStateGraphHostBindingRules.TryBuildRestoreWirePlan(
                    host, chain,
                    out _,
                    out CoCoBindingHint failure),
                Is.False);
            Assert.That(failure.Kind, Is.EqualTo(CoCoBindingHintKind.Warning));
        }

        // ===== 菜单候选（方案 v3 §2.2：边界过滤 + 最近宿主 + 已分配排除） =====

        [Test]
        public void MenuCandidatesStayInsideBoundaryAndExcludeAssignedAndNested()
        {
            CoCoStateGraphHost host = CreateHost("SceneScan");
            GameObject inside = CreateChild(host.transform, "Inside", false);
            var insideSource =
                inside.AddComponent<EditorHostIntentSourceComponent>();
            var insideOperator =
                inside.AddComponent<EditorHostOperatorComponent>();

            var nestedObject = CreateChild(host.transform, "Nested", false);
            nestedObject.AddComponent<CoCoStateGraphHost>();
            var nestedSource =
                nestedObject.AddComponent<EditorHostIntentSourceComponent>();

            GameObject outside = CreateObject("Outside");
            outside.AddComponent<EditorHostIntentSourceComponent>();
            outside.AddComponent<EditorHostOperatorComponent>();

            var results = new List<MonoBehaviour>();
            CoCoStateGraphHostBindingRules.CollectIntentSourceCandidates(
                host,
                new[] { insideSource },
                results);
            // 边界内、未分配、非嵌套宿主才出现；越界与嵌套不出现（P2-02 回归签名方案）。
            Assert.That(results, Is.Empty);

            CoCoStateGraphHostBindingRules.CollectIntentSourceCandidates(
                host,
                null,
                results);
            Assert.That(results, Is.EqualTo(new[] { insideSource }));
            Assert.That(results, Has.No.Member(nestedSource));

            CoCoStateGraphHostBindingRules.CollectOperatorCandidates(
                host,
                new[] { insideOperator },
                results);
            Assert.That(results, Is.Empty);

            CoCoStateGraphHostBindingRules.CollectOperatorCandidates(
                host,
                null,
                results);
            Assert.That(results, Is.EqualTo(new[] { insideOperator }));
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

        private static CoCoStateGraphHostTemporalDebugSnapshot
            CreateTemporalSnapshot(
                int capacity,
                params CoCoTemporalFrameInfo[] frames)
        {
            return new CoCoStateGraphHostTemporalDebugSnapshot(
                null,
                capacity,
                frames);
        }

        private static CoCoTemporalFrameInfo CreateTemporalFrame(
            ulong graphInstance,
            ulong tick,
            ulong revision)
        {
            Assert.That(
                CoCoGraphInstanceId.TryCreate(
                    graphInstance,
                    out CoCoGraphInstanceId graphInstanceId),
                Is.True);
            Assert.That(
                CoCoTimelineId.TryCreate(
                    0x11UL,
                    0x12UL,
                    out CoCoTimelineId timelineId),
                Is.True);
            Assert.That(
                CoCoClockDomainId.TryCreate(
                    0x13UL,
                    out CoCoClockDomainId clockDomainId),
                Is.True);
            Assert.That(
                CoCoTimelinePosition.TryCreate(
                    tick * 0.1d,
                    out CoCoTimelinePosition position),
                Is.True);
            Assert.That(
                CoCoTickFrame.TryCreate(
                    0.1d,
                    timelineId,
                    position,
                    new CoCoTimelineTick(tick),
                    clockDomainId,
                    new CoCoExecutionSequence(tick),
                    new CoCoTimelineEpoch(1UL),
                    out CoCoTickFrame tickFrame,
                    out CoCoDiagnostic diagnostic),
                Is.True,
                diagnostic.Message);
            return new CoCoTemporalFrameInfo(
                graphInstanceId,
                tickFrame,
                new CoCoContextRevision(revision),
                CoCoContextFrameOrigin.Commit());
        }
    }
}
