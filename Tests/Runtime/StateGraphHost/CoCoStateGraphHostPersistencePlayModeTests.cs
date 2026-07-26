using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public sealed class CoCoStateGraphHostPersistencePlayModeTests
    {
        private const string StableEntityId = "pre13.stategraph.actor";

        private readonly List<Object> _objects = new List<Object>();
        private string _saveDirectory;
        private string _previousSaveDirectory;

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
            _previousSaveDirectory = PersistenceFileStore.SaveDirectoryOverride;
            _saveDirectory = Path.Combine(
                Application.temporaryCachePath,
                "cocoflow-pre13-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_saveDirectory);
            PersistenceFileStore.SaveDirectoryOverride = _saveDirectory;
        }

        [TearDown]
        public void TearDown()
        {
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
            PersistenceFileStore.SaveDirectoryOverride = _previousSaveDirectory;
            for (int index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }

            _objects.Clear();
            if (!string.IsNullOrEmpty(_saveDirectory) &&
                Directory.Exists(_saveDirectory))
            {
                Directory.Delete(_saveDirectory, true);
            }

            _saveDirectory = null;
            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
        }

        [Test]
        public void SaveGameLoadsDurableContextIntoSiblingAndReseedsHistory()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            AttachPersistence(source, StableEntityId);
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 10);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] firstDepthPayload,
                    out CoCoDiagnostic firstDepthCapture),
                firstDepthCapture);
            StepWithActorValue(source, 20);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] deeperHistoryPayload,
                    out CoCoDiagnostic deeperHistoryCapture),
                deeperHistoryCapture);
            Assert.That(
                deeperHistoryPayload.Length,
                Is.EqualTo(firstDepthPayload.Length),
                "Persistence payload size must not grow with retained Temporal history.");

            CoCoContextFrame savedContext = source.Host.CurrentContext;
            CoCoGraphInstanceId savedGraphInstance = source.Host.GraphInstanceId;
            CoCoTickFrame savedTick = savedContext.Header.TickFrame;
            CoCoContextRevision savedRevision = savedContext.Revision;
            CoCoGraphStateRecord<int> savedGraphState =
                TemporalHostTestHarness.ReadGraphState(
                    savedContext,
                    source.Ids.GraphStateSlotId);

            PersistenceSaveLoadSystem.SaveGame(0);
            Assert.That(File.Exists(PersistenceFileStore.GetSaveFilePath(0)), Is.True);
            Assert.That(
                PersistenceFileStore.TryReadDocument(
                    0,
                    out PersistenceSaveDocument savedDocument),
                Is.True);
            Assert.That(
                savedDocument.schemaVersion,
                Is.EqualTo(PersistenceSaveDocument.CurrentSchemaVersion));

            Object.DestroyImmediate(source.GameObject);
            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            AttachPersistence(target, StableEntityId);
            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            StepWithActorValue(target, 100);
            StepWithActorValue(target, 200);
            CoCoGraphInstanceId targetGraphInstance = target.Host.GraphInstanceId;
            CoCoTimelineId targetTimeline =
                TemporalHostTestHarness.GetRuntime(target.Host).Clock.TimelineId;
            ulong previousRevision = target.Host.CurrentContext.Revision.Value;

            Assert.That(targetGraphInstance, Is.Not.EqualTo(savedGraphInstance));
            Assert.That(PersistenceSaveLoadSystem.LoadGame(0), Is.True);

            CoCoContextFrame restored = target.Host.CurrentContext;
            Assert.That(
                restored.Header.Identity.GraphInstanceId,
                Is.EqualTo(targetGraphInstance));
            Assert.That(
                restored.Header.TickFrame.TimelineId,
                Is.EqualTo(targetTimeline));
            Assert.That(restored.Header.TickFrame.Tick, Is.EqualTo(savedTick.Tick));
            Assert.That(
                restored.Header.TickFrame.TimelinePosition,
                Is.EqualTo(savedTick.TimelinePosition));
            Assert.That(restored.Header.TickFrame.DeltaTime, Is.EqualTo(savedTick.DeltaTime));
            Assert.That(restored.Revision.Value, Is.EqualTo(previousRevision + 1UL));
            Assert.That(restored.Origin.IsRestore, Is.True);
            Assert.That(
                restored.Origin.SourceGraphInstanceId,
                Is.EqualTo(savedGraphInstance));
            Assert.That(
                restored.Origin.SourceTimelineEpoch,
                Is.EqualTo(savedTick.TimelineEpoch));
            Assert.That(restored.Origin.SourceTick, Is.EqualTo(savedTick.Tick));
            Assert.That(restored.Origin.SourceRevision, Is.EqualTo(savedRevision));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    restored,
                    target.Ids.ActorStateSlotId),
                Is.EqualTo(20));
            Assert.That(
                TemporalHostTestHarness.ReadGraphState(
                    restored,
                    target.Ids.GraphStateSlotId),
                Is.EqualTo(savedGraphState));
            Assert.That(target.Binding.Value, Is.EqualTo(20));
            Assert.That(target.Binding.ApplyCount, Is.EqualTo(1));
            Assert.That(target.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(
                target.Binding.LastApplyKind,
                Is.EqualTo(CoCoContextRestoreApplyKind.Confirm));
            Assert.That(target.Host.TemporalState.Count, Is.EqualTo(1));
            Assert.That(target.Host.TemporalState.CanConfirm, Is.False);
            Assert.That(
                target.Host.TryBeginTemporalPreview(out CoCoDiagnostic tooEarly),
                Is.False);
            Assert.That(tooEarly.IsError, Is.True);

            Require(target.Host.TryStep(0.1d, out CoCoDiagnostic nextTick), nextTick);
            Assert.That(target.Host.TemporalState.Count, Is.EqualTo(2));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    target.Host.CurrentContext,
                    target.Ids.ActorStateSlotId),
                Is.EqualTo(20));
        }

        [Test]
        public void SaveGameLoadsEarlierDurableContextIntoSameHost()
        {
            TemporalHostTestScenario scenario = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            AttachPersistence(scenario, StableEntityId);
            Require(scenario.Host.TryStart(out CoCoDiagnostic start), start);
            StepWithActorValue(scenario, 31);
            CoCoContextFrame saved = scenario.Host.CurrentContext;
            CoCoContextRevision savedRevision = saved.Revision;
            CoCoGraphInstanceId graphInstance = scenario.Host.GraphInstanceId;
            PersistenceSaveLoadSystem.SaveGame(0);

            StepWithActorValue(scenario, 32);
            ulong targetRevision = scenario.Host.CurrentContext.Revision.Value;
            Assert.That(PersistenceSaveLoadSystem.LoadGame(0), Is.True);

            CoCoContextFrame restored = scenario.Host.CurrentContext;
            Assert.That(
                restored.Header.Identity.GraphInstanceId,
                Is.EqualTo(graphInstance));
            Assert.That(
                restored.Origin.SourceGraphInstanceId,
                Is.EqualTo(graphInstance));
            Assert.That(
                restored.Origin.SourceRevision,
                Is.EqualTo(savedRevision));
            Assert.That(restored.Revision.Value, Is.EqualTo(targetRevision + 1UL));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    restored,
                    scenario.Ids.ActorStateSlotId),
                Is.EqualTo(31));
            Assert.That(scenario.Binding.Value, Is.EqualTo(31));
            Assert.That(scenario.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(scenario.Host.TemporalState.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PendingStateGraphRecordAppliesAfterManualHostStart()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            AttachPersistence(source, StableEntityId);
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 55);
            PersistenceSaveDocument pending = PersistenceSession.Capture(0);
            CoCoGraphInstanceId sourceGraphInstance = source.Host.GraphInstanceId;

            Object.DestroyImmediate(source.GameObject);
            PersistenceSession.SetPendingDocument(pending);
            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            AttachPersistence(target, StableEntityId);
            Assert.That(target.Host.CurrentContext.IsAlive, Is.False);

            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            yield return null;
            yield return null;

            Assert.That(target.Host.GraphInstanceId, Is.Not.EqualTo(sourceGraphInstance));
            Assert.That(target.Host.CurrentContext.IsAlive, Is.True);
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    target.Host.CurrentContext,
                    target.Ids.ActorStateSlotId),
                Is.EqualTo(55));
            Assert.That(target.Binding.Value, Is.EqualTo(55));
            Assert.That(target.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(target.Host.TemporalState.Count, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator PendingStateGraphRecordAppliesAfterAutomaticHostStart()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            AttachPersistence(source, StableEntityId);
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 56);
            PersistenceSaveDocument pending = PersistenceSession.Capture(0);

            Object.DestroyImmediate(source.GameObject);
            PersistenceSession.SetPendingDocument(pending);
            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            target.GameObject.SetActive(false);
            SetField(target.Host, "autoStart", true);
            AttachPersistence(target, StableEntityId);
            target.GameObject.SetActive(true);

            yield return null;
            yield return null;
            yield return null;

            Assert.That(
                target.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    target.Host.CurrentContext,
                    target.Ids.ActorStateSlotId),
                Is.EqualTo(56));
            Assert.That(target.Binding.Value, Is.EqualTo(56));
            Assert.That(target.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(target.Host.TemporalState.Count, Is.EqualTo(1));
        }

        [Test]
        public void InvalidPayloadAndRejectedBindingNeverPublishAuthority()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            Assert.That(
                source.Host.TryCapturePersistencePayload(
                    out _,
                    out CoCoDiagnostic missingContext),
                Is.False);
            Assert.That(missingContext.IsError, Is.True);
            StepWithActorValue(source, 33);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic captured),
                captured);

            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            StepWithActorValue(target, 100);
            StepWithActorValue(target, 200);
            CoCoContextFrame previous = target.Host.CurrentContext;
            ulong previousRevision = previous.Revision.Value;
            int previousActor = TemporalHostTestHarness.ReadActorValue(
                previous,
                target.Ids.ActorStateSlotId);
            int previousHistoryCount = target.Host.TemporalState.Count;

            byte[] trailing = new byte[payload.Length + 1];
            Buffer.BlockCopy(payload, 0, trailing, 0, payload.Length);
            Assert.That(
                target.Host.TryApplyPersistencePayload(
                    trailing,
                    out CoCoDiagnostic trailingFailure),
                Is.False);
            Assert.That(trailingFailure.IsError, Is.True);
            AssertAuthorityUnchanged(
                target,
                previousRevision,
                previousActor,
                previousHistoryCount);

            byte[] wrongGraph = (byte[])payload.Clone();
            wrongGraph[8] ^= 0x01;
            Assert.That(
                target.Host.TryApplyPersistencePayload(
                    wrongGraph,
                    out CoCoDiagnostic graphFailure),
                Is.False);
            Assert.That(graphFailure.IsError, Is.True);
            AssertAuthorityUnchanged(
                target,
                previousRevision,
                previousActor,
                previousHistoryCount);

            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => candidate[0] ^= 0x01,
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => candidate[4] ^= 0x01,
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt64(
                    candidate,
                    24,
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(double.NaN))),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt64(
                    candidate,
                    32,
                    unchecked((ulong)BitConverter.DoubleToInt64Bits(
                        double.PositiveInfinity))),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt32(candidate, 40, uint.MaxValue),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => candidate[44 + 12] ^= 0x01,
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt64(
                    candidate,
                    44 + 72,
                    ulong.MaxValue),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt64(
                    candidate,
                    44 + 72,
                    ulong.MaxValue - 1UL),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt64(
                    candidate,
                    44 + 80,
                    ulong.MaxValue),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt64(
                    candidate,
                    44 + 88,
                    ulong.MaxValue),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt32(
                    candidate,
                    44 + 104,
                    uint.MaxValue),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt32(
                    candidate,
                    44 + 108 + 32,
                    1U),
                previousRevision,
                previousActor,
                previousHistoryCount);
            AssertPayloadRejectedWithoutPublication(
                target,
                payload,
                candidate => WriteUInt32(
                    candidate,
                    44 + 108 + 44,
                    uint.MaxValue),
                previousRevision,
                previousActor,
                previousHistoryCount);

            target.Binding.Failure = TemporalRestoreFixtureFailure.Reject;
            Assert.That(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic bindingFailure),
                Is.False);
            Assert.That(bindingFailure.IsError, Is.True);
            AssertAuthorityUnchanged(
                target,
                previousRevision,
                previousActor,
                previousHistoryCount);
            Assert.That(target.Host.RequiresWorldCorrection, Is.True);
            Assert.That(target.Host.Fault.IsFaulted, Is.True);
        }

        [Test]
        public void FailedCaptureDoesNotOverwriteExistingSlot()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            AttachPersistence(source, StableEntityId);
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 71);
            PersistenceSaveLoadSystem.SaveGame(0);
            string path = PersistenceFileStore.GetSaveFilePath(0);
            byte[] original = File.ReadAllBytes(path);

            Object.DestroyImmediate(source.GameObject);
            TemporalHostTestScenario unstarted = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            AttachPersistence(unstarted, StableEntityId);
            Assert.That(unstarted.Host.CurrentContext.IsAlive, Is.False);

            LogAssert.Expect(
                LogType.Error,
                new Regex(
                    @"^\[SaveLoadSystem\] 保存失败: Persistence Context capture failed"));
            PersistenceSaveLoadSystem.SaveGame(0);

            Assert.That(File.ReadAllBytes(path), Is.EqualTo(original));
        }

        [Test]
        public void PreviewAndReentryRejectPersistenceWithoutNestedPublication()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 82);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic captured),
                captured);

            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            StepWithActorValue(target, 10);
            StepWithActorValue(target, 20);
            Require(
                target.Host.TryBeginTemporalPreview(
                    out CoCoDiagnostic beginPreview),
                beginPreview);
            Assert.That(
                target.Host.TryCapturePersistencePayload(
                    out _,
                    out CoCoDiagnostic previewCapture),
                Is.False);
            Assert.That(previewCapture.IsError, Is.True);
            Assert.That(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic previewApply),
                Is.False);
            Assert.That(previewApply.IsError, Is.True);
            Require(
                target.Host.TryCancelTemporalPreview(
                    out CoCoDiagnostic cancelPreview),
                cancelPreview);

            bool nestedResult = true;
            CoCoDiagnostic nestedDiagnostic = CoCoDiagnostic.None;
            target.Binding.ApplyCallback = applyKind =>
            {
                if (applyKind == CoCoContextRestoreApplyKind.Confirm)
                {
                    nestedResult = target.Host.TryApplyPersistencePayload(
                        payload,
                        out nestedDiagnostic);
                }
            };

            Require(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic outerApply),
                outerApply);
            Assert.That(nestedResult, Is.False);
            Assert.That(nestedDiagnostic.IsError, Is.True);
            Assert.That(target.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(target.Host.TemporalState.Count, Is.EqualTo(1));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    target.Host.CurrentContext,
                    target.Ids.ActorStateSlotId),
                Is.EqualTo(82));
        }

        [Test]
        public void DurableCodecCallbacksCannotReenterThrowOrDestroyThroughCapture()
        {
            Assert.That(
                CoCoCodecId.TryCreate(
                    0xC013UL,
                    1UL,
                    out CoCoCodecId codecId),
                Is.True);
            var codec = new PersistenceInt32Codec(
                new CoCoCodecDescriptor(codecId, 1U));
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true,
                    actorCodec: codec));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 91);
            StepWithActorValue(source, 92);
            ulong revision = source.Host.CurrentContext.Revision.Value;
            int historyCount = source.Host.TemporalState.Count;

            bool nestedCapture = true;
            bool nestedStop = true;
            bool nestedDispose = true;
            CoCoDiagnostic nestedCaptureDiagnostic = CoCoDiagnostic.None;
            CoCoDiagnostic nestedStopDiagnostic = CoCoDiagnostic.None;
            CoCoDiagnostic nestedDisposeDiagnostic = CoCoDiagnostic.None;
            codec.EncodeCallback = () =>
            {
                nestedCapture = source.Host.TryCapturePersistencePayload(
                    out _,
                    out nestedCaptureDiagnostic);
                nestedStop = source.Host.TryStop(out nestedStopDiagnostic);
                nestedDispose = source.Host.TryDispose(out nestedDisposeDiagnostic);
            };

            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic capture),
                capture);
            Assert.That(payload, Is.Not.Null.And.Not.Empty);
            Assert.That(nestedCapture, Is.False);
            Assert.That(nestedStop, Is.False);
            Assert.That(nestedDispose, Is.False);
            Assert.That(nestedCaptureDiagnostic.IsError, Is.True);
            Assert.That(nestedStopDiagnostic.IsError, Is.True);
            Assert.That(nestedDisposeDiagnostic.IsError, Is.True);
            Assert.That(
                source.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            AssertAuthorityUnchanged(source, revision, 92, historyCount);

            codec.EncodeCallback = () =>
                throw new InvalidOperationException("Pre13 codec capture failure.");
            Assert.DoesNotThrow(() =>
                Assert.That(
                    source.Host.TryCapturePersistencePayload(
                        out byte[] rejectedPayload,
                        out CoCoDiagnostic rejected),
                    Is.False));
            Assert.That(source.Host.LastDiagnostic.IsError, Is.True);
            Assert.That(
                source.Host.Lifecycle,
                Is.EqualTo(CoCoRuntimeLifecycleState.Running));
            AssertAuthorityUnchanged(source, revision, 92, historyCount);

            codec.EncodeCallback = null;
            TemporalHostTestScenario destroyed = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            Require(
                destroyed.Host.TryStart(out CoCoDiagnostic destroyedStart),
                destroyedStart);
            StepWithActorValue(destroyed, 93);
            codec.EncodeCallback = () =>
                Object.DestroyImmediate(destroyed.GameObject);
            bool destroyedCapture = true;
            CoCoDiagnostic destroyedDiagnostic = CoCoDiagnostic.None;
            Assert.DoesNotThrow(() =>
                destroyedCapture =
                    destroyed.Host.TryCapturePersistencePayload(
                        out _,
                        out destroyedDiagnostic));
            Assert.That(destroyedCapture, Is.False);
            Assert.That(destroyedDiagnostic.IsError, Is.True);
            Assert.That(destroyed.Host == null, Is.True);
            Assert.That(destroyed.GameObject == null, Is.True);
            codec.EncodeCallback = null;
        }

        [Test]
        public void BindingThrowAndDestructionNeverPublishImportedAuthority()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 101);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic captured),
                captured);

            TemporalHostTestScenario throwing = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            Require(
                throwing.Host.TryStart(out CoCoDiagnostic throwingStart),
                throwingStart);
            StepWithActorValue(throwing, 201);
            ulong throwingRevision = throwing.Host.CurrentContext.Revision.Value;
            int throwingHistory = throwing.Host.TemporalState.Count;
            throwing.Binding.Failure = TemporalRestoreFixtureFailure.Throw;
            Assert.That(
                throwing.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic throwingFailure),
                Is.False);
            Assert.That(throwingFailure.IsError, Is.True);
            AssertAuthorityUnchanged(
                throwing,
                throwingRevision,
                201,
                throwingHistory);

            TemporalHostTestScenario destroyedBinding = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            Require(
                destroyedBinding.Host.TryStart(
                    out CoCoDiagnostic destroyedStart),
                destroyedStart);
            StepWithActorValue(destroyedBinding, 202);
            ulong destroyedRevision =
                destroyedBinding.Host.CurrentContext.Revision.Value;
            int destroyedHistory =
                destroyedBinding.Host.TemporalState.Count;
            destroyedBinding.Binding.Failure =
                TemporalRestoreFixtureFailure.Destroy;
            Assert.That(
                destroyedBinding.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic destroyedFailure),
                Is.False);
            Assert.That(destroyedFailure.IsError, Is.True);
            Assert.That(destroyedBinding.Binding == null, Is.True);
            AssertAuthorityUnchanged(
                destroyedBinding,
                destroyedRevision,
                202,
                destroyedHistory);
        }

        [Test]
        public void ParticipantResetFailureCancelsWithoutPublishingAuthority()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 3,
                    withDurableProjection: true));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 111);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic captured),
                captured);

            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 3));
            var participant =
                target.GameObject.AddComponent<RejectingTemporalParticipant>();
            TemporalHostTestHarness.SetRestoreBinding(target.Host, participant);
            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            StepWithActorValue(target, 211);
            ulong revision = target.Host.CurrentContext.Revision.Value;
            int historyCount = target.Host.TemporalState.Count;
            participant.RejectAuthorityReset = true;

            Assert.That(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic failure),
                Is.False);
            Assert.That(failure.IsError, Is.True);
            Assert.That(participant.AuthorityResetPrepareCount, Is.EqualTo(1));
            Assert.That(participant.AuthorityResetCancelCount, Is.EqualTo(1));
            Assert.That(participant.ApplyCount, Is.Zero);
            AssertAuthorityUnchanged(
                target,
                revision,
                211,
                historyCount);
            Assert.That(target.Host.Fault.IsFaulted, Is.False);
        }

        [Test]
        public void ZeroCapacityImportLazilyAttachesResetParticipantWithoutEnablingHistory()
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 0,
                    withDurableProjection: true));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 121);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic captured),
                captured);

            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 0));
            var participant =
                target.GameObject.AddComponent<RejectingTemporalParticipant>();
            participant.Configure(target.Binding);
            TemporalHostTestHarness.SetRestoreBinding(target.Host, participant);
            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            StepWithActorValue(target, 221);

            Assert.That(participant.AttachCount, Is.Zero);
            Assert.That(participant.ForwardPrepareCount, Is.Zero);
            Assert.That(participant.PublishCount, Is.Zero);
            Assert.That(participant.CleanupCount, Is.Zero);

            Require(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic firstImport),
                firstImport);

            Assert.That(participant.AttachCount, Is.EqualTo(1));
            Assert.That(participant.AuthorityResetPrepareCount, Is.EqualTo(1));
            Assert.That(participant.AuthorityResetCommitCount, Is.EqualTo(1));
            Assert.That(participant.AuthorityResetCancelCount, Is.Zero);
            Assert.That(participant.ApplyCount, Is.EqualTo(1));
            Assert.That(participant.CleanupCount, Is.EqualTo(1));
            Assert.That(target.Binding.ConfirmCount, Is.EqualTo(1));
            Assert.That(target.Binding.LastAppliedValue, Is.EqualTo(121));
            Assert.That(
                target.Host.TemporalState.Mode,
                Is.EqualTo(CoCoTemporalMode.Disabled));
            Assert.That(target.Host.TemporalState.Capacity, Is.Zero);
            Assert.That(target.Host.TemporalState.Count, Is.Zero);

            StepWithActorValue(target, 121);
            Assert.That(participant.ForwardPrepareCount, Is.Zero);
            Assert.That(participant.PublishCount, Is.Zero);
            Assert.That(participant.CleanupCount, Is.EqualTo(1));

            Require(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic secondImport),
                secondImport);
            Assert.That(participant.AttachCount, Is.EqualTo(1));
            Assert.That(participant.AuthorityResetPrepareCount, Is.EqualTo(2));
            Assert.That(participant.AuthorityResetCommitCount, Is.EqualTo(2));
            Assert.That(participant.ApplyCount, Is.EqualTo(2));
            Assert.That(participant.CleanupCount, Is.EqualTo(2));
            Assert.That(target.Binding.ConfirmCount, Is.EqualTo(2));

            Object.DestroyImmediate(target.GameObject);
            Assert.That(participant.DetachCount, Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ZeroCapacityParticipantAttachmentFailureDoesNotEnterRestoreBarrier(
            bool throws)
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 0,
                    withDurableProjection: true));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 131);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic captured),
                captured);

            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 0));
            var participant =
                target.GameObject.AddComponent<RejectingTemporalParticipant>();
            participant.Configure(target.Binding);
            participant.RejectAttachment = !throws;
            participant.ThrowAttachment = throws;
            TemporalHostTestHarness.SetRestoreBinding(target.Host, participant);
            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            StepWithActorValue(target, 231);
            ulong revision = target.Host.CurrentContext.Revision.Value;

            Assert.That(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic failure),
                Is.False);

            Assert.That(failure.IsError, Is.True);
            Assert.That(participant.AttachCount, Is.EqualTo(1));
            Assert.That(
                participant.DetachCount,
                Is.EqualTo(throws ? 1 : 0));
            Assert.That(participant.AuthorityResetPrepareCount, Is.Zero);
            Assert.That(participant.ApplyCount, Is.Zero);
            AssertAuthorityUnchanged(target, revision, 231, 0);
            Assert.That(target.Host.Fault.IsFaulted, Is.False);
            Assert.That(target.Host.RequiresWorldCorrection, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ZeroCapacityParticipantResetFailureCancelsAndKeepsAttachment(
            bool throws)
        {
            TemporalHostTestScenario source = Track(
                TemporalHostTestHarness.Create(
                    historyCapacity: 0,
                    withDurableProjection: true));
            Require(source.Host.TryStart(out CoCoDiagnostic sourceStart), sourceStart);
            StepWithActorValue(source, 141);
            Require(
                source.Host.TryCapturePersistencePayload(
                    out byte[] payload,
                    out CoCoDiagnostic captured),
                captured);

            TemporalHostTestScenario target = Track(
                TemporalHostTestHarness.CreateSibling(
                    source,
                    historyCapacity: 0));
            var participant =
                target.GameObject.AddComponent<RejectingTemporalParticipant>();
            participant.Configure(target.Binding);
            participant.RejectAuthorityReset = !throws;
            participant.ThrowAuthorityReset = throws;
            TemporalHostTestHarness.SetRestoreBinding(target.Host, participant);
            Require(target.Host.TryStart(out CoCoDiagnostic targetStart), targetStart);
            StepWithActorValue(target, 241);
            ulong revision = target.Host.CurrentContext.Revision.Value;

            Assert.That(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic failure),
                Is.False);

            Assert.That(failure.IsError, Is.True);
            Assert.That(participant.AttachCount, Is.EqualTo(1));
            Assert.That(participant.DetachCount, Is.Zero);
            Assert.That(participant.AuthorityResetPrepareCount, Is.EqualTo(1));
            Assert.That(participant.AuthorityResetCancelCount, Is.EqualTo(1));
            Assert.That(participant.ApplyCount, Is.Zero);
            AssertAuthorityUnchanged(target, revision, 241, 0);
            Assert.That(target.Host.Fault.IsFaulted, Is.False);

            participant.RejectAuthorityReset = false;
            participant.ThrowAuthorityReset = false;
            Require(
                target.Host.TryApplyPersistencePayload(
                    payload,
                    out CoCoDiagnostic retry),
                retry);
            Assert.That(participant.AttachCount, Is.EqualTo(1));
            Assert.That(participant.AuthorityResetCommitCount, Is.EqualTo(1));
            Assert.That(target.Binding.ConfirmCount, Is.EqualTo(1));
        }

        private TemporalHostTestScenario Track(TemporalHostTestScenario scenario)
        {
            if (!_objects.Contains(scenario.Asset))
            {
                _objects.Add(scenario.Asset);
            }

            _objects.Add(scenario.GameObject);
            return scenario;
        }

        private static PersistenceContext AttachPersistence(
            TemporalHostTestScenario scenario,
            string stableEntityId)
        {
            bool wasActive = scenario.GameObject.activeSelf;
            scenario.GameObject.SetActive(false);
            var persistence = scenario.GameObject.AddComponent<PersistenceContext>();
            SetField(persistence, "stableEntityId", stableEntityId);
            SetField(persistence, "prefabKey", "pre13.stategraph.actor");
            scenario.GameObject.SetActive(wasActive);
            return persistence;
        }

        private static void AssertAuthorityUnchanged(
            TemporalHostTestScenario scenario,
            ulong expectedRevision,
            int expectedActor,
            int expectedHistoryCount)
        {
            Assert.That(
                scenario.Host.CurrentContext.Revision.Value,
                Is.EqualTo(expectedRevision));
            Assert.That(
                TemporalHostTestHarness.ReadActorValue(
                    scenario.Host.CurrentContext,
                    scenario.Ids.ActorStateSlotId),
                Is.EqualTo(expectedActor));
            Assert.That(
                scenario.Host.TemporalState.Count,
                Is.EqualTo(expectedHistoryCount));
        }

        private static void AssertPayloadRejectedWithoutPublication(
            TemporalHostTestScenario scenario,
            byte[] source,
            Action<byte[]> mutate,
            ulong expectedRevision,
            int expectedActor,
            int expectedHistoryCount)
        {
            byte[] candidate = (byte[])source.Clone();
            mutate(candidate);
            Assert.That(
                scenario.Host.TryApplyPersistencePayload(
                    candidate,
                    out CoCoDiagnostic diagnostic),
                Is.False);
            Assert.That(diagnostic.IsError, Is.True);
            AssertAuthorityUnchanged(
                scenario,
                expectedRevision,
                expectedActor,
                expectedHistoryCount);
        }

        private static void WriteUInt32(
            byte[] destination,
            int offset,
            uint value)
        {
            destination[offset] = (byte)value;
            destination[offset + 1] = (byte)(value >> 8);
            destination[offset + 2] = (byte)(value >> 16);
            destination[offset + 3] = (byte)(value >> 24);
        }

        private static void WriteUInt64(
            byte[] destination,
            int offset,
            ulong value)
        {
            WriteUInt32(destination, offset, (uint)value);
            WriteUInt32(destination, offset + 4, (uint)(value >> 32));
        }

        private static void StepWithActorValue(
            TemporalHostTestScenario scenario,
            int value)
        {
            scenario.Binding.Value = value;
            Require(
                scenario.Host.TryStep(0.1d, out CoCoDiagnostic diagnostic),
                diagnostic);
        }

        private static void SetField(
            object target,
            string fieldName,
            object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(
                    target.GetType().FullName,
                    fieldName);
            }

            field.SetValue(target, value);
        }

        private static void Require(bool succeeded, CoCoDiagnostic diagnostic)
        {
            if (!succeeded)
            {
                Assert.Fail(diagnostic.Message);
            }
        }

        private sealed class PersistenceInt32Codec :
            ICoCoContextValueCodec<int>
        {
            internal PersistenceInt32Codec(CoCoCodecDescriptor descriptor)
            {
                Descriptor = descriptor;
            }

            public CoCoCodecDescriptor Descriptor { get; }
            public int MaxEncodedSize => 4;
            internal Action EncodeCallback { get; set; }

            public bool TryEncode(
                in int value,
                Span<byte> destination,
                out int bytesWritten)
            {
                if (destination.Length < 4)
                {
                    bytesWritten = 0;
                    return false;
                }

                uint encoded = unchecked((uint)value);
                destination[0] = (byte)encoded;
                destination[1] = (byte)(encoded >> 8);
                destination[2] = (byte)(encoded >> 16);
                destination[3] = (byte)(encoded >> 24);
                bytesWritten = 4;
                EncodeCallback?.Invoke();
                return true;
            }

            public bool TryDecode(
                ReadOnlySpan<byte> source,
                out int value,
                out int bytesRead)
            {
                if (source.Length < 4)
                {
                    value = default;
                    bytesRead = 0;
                    return false;
                }

                value =
                    source[0] |
                    source[1] << 8 |
                    source[2] << 16 |
                    source[3] << 24;
                bytesRead = 4;
                return true;
            }
        }

        private sealed class RejectingTemporalParticipant :
            MonoBehaviour,
            ICoCoContextRestoreBinding,
            ICoCoStateGraphTemporalParticipant,
            ICoCoTemporalDecoratorBinding
        {
            private CoCoStateGraphHost _host;
            private MonoBehaviour _downstreamComponent;
            private ICoCoContextRestoreBinding _downstream;
            private bool _resetPrepared;

            internal bool RejectAttachment { get; set; }
            internal bool ThrowAttachment { get; set; }
            internal bool RejectAuthorityReset { get; set; }
            internal bool ThrowAuthorityReset { get; set; }
            internal int AttachCount { get; private set; }
            internal int DetachCount { get; private set; }
            internal int AuthorityResetPrepareCount { get; private set; }
            internal int AuthorityResetCommitCount { get; private set; }
            internal int AuthorityResetCancelCount { get; private set; }
            internal int ApplyCount { get; private set; }
            internal int ForwardPrepareCount { get; private set; }
            internal int PublishCount { get; private set; }
            internal int CleanupCount { get; private set; }

            MonoBehaviour ICoCoTemporalDecoratorBinding.DownstreamRestoreBinding =>
                _downstreamComponent;

            internal void Configure(MonoBehaviour downstream)
            {
                _downstreamComponent = downstream;
                _downstream = downstream as ICoCoContextRestoreBinding;
            }

            public bool TryApply(
                in CoCoContextRestoreBindingContext context,
                out CoCoDiagnostic diagnostic)
            {
                ApplyCount++;
                if (!context.IsValid ||
                    context.ApplyKind == CoCoContextRestoreApplyKind.Confirm &&
                    !_resetPrepared)
                {
                    diagnostic = ParticipantError(
                        "Restore context is invalid or reset was not prepared.");
                    return false;
                }

                if (_downstream != null &&
                    !_downstream.TryApply(context, out diagnostic))
                {
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            bool ICoCoStateGraphTemporalParticipant.TryAttachTemporalHost(
                CoCoStateGraphHost host,
                int historyCapacity,
                out CoCoDiagnostic diagnostic)
            {
                AttachCount++;
                if (ThrowAttachment)
                {
                    _host = host;
                    throw new InvalidOperationException(
                        "Temporal attachment threw for the test.");
                }

                if (RejectAttachment ||
                    host == null ||
                    historyCapacity != 0 && historyCapacity < 2)
                {
                    diagnostic = ParticipantError(
                        "Temporal attachment is invalid.");
                    return false;
                }

                _host = host;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            bool ICoCoStateGraphTemporalParticipant.IsTemporalParticipantLive(
                CoCoStateGraphHost host) =>
                _host != null && ReferenceEquals(_host, host);

            bool ICoCoStateGraphTemporalParticipant.TryPrepareForwardCapture(
                in CoCoTemporalFrameInfo candidate,
                out CoCoDiagnostic diagnostic)
            {
                ForwardPrepareCount++;
                diagnostic = candidate.IsValid
                    ? CoCoDiagnostic.None
                    : ParticipantError("Forward candidate is invalid.");
                return !diagnostic.IsError;
            }

            void ICoCoStateGraphTemporalParticipant.PublishForwardCaptureNoFail()
            {
                PublishCount++;
            }

            void ICoCoStateGraphTemporalParticipant.CancelPreparedCaptureNoFail()
            {
            }

            bool ICoCoStateGraphTemporalParticipant.TryPrepareAuthorityReset(
                in CoCoTemporalFrameInfo targetAuthority,
                out CoCoDiagnostic diagnostic)
            {
                AuthorityResetPrepareCount++;
                _resetPrepared = targetAuthority.IsValid;
                if (ThrowAuthorityReset)
                {
                    throw new InvalidOperationException(
                        "Authority reset threw for the test.");
                }

                if (!_resetPrepared || RejectAuthorityReset)
                {
                    diagnostic = ParticipantError(
                        "Authority reset was rejected for the test.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            void ICoCoStateGraphTemporalParticipant
                .CommitPreparedAuthorityResetNoFail()
            {
                AuthorityResetCommitCount++;
                _resetPrepared = false;
            }

            void ICoCoStateGraphTemporalParticipant
                .CancelPreparedAuthorityResetNoFail()
            {
                if (_resetPrepared)
                {
                    AuthorityResetCancelCount++;
                    _resetPrepared = false;
                }
            }

            bool ICoCoStateGraphTemporalParticipant.TryBeginPreview(
                int historyCount,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            bool ICoCoStateGraphTemporalParticipant.TryPrepareProjection(
                CoCoContextRestoreApplyKind applyKind,
                int historyDepth,
                in CoCoTemporalFrameInfo source,
                in CoCoTickFrame targetTickFrame,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            void ICoCoStateGraphTemporalParticipant.FinishProjectionNoFail(
                bool succeeded)
            {
            }

            bool ICoCoStateGraphTemporalParticipant.CanConfirmPreview(
                int historyDepth) => true;

            bool ICoCoStateGraphTemporalParticipant.TryPrepareBranchCapture(
                int historyDepth,
                in CoCoTemporalFrameInfo branchHead,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            void ICoCoStateGraphTemporalParticipant.PublishBranchCaptureNoFail()
            {
            }

            void ICoCoStateGraphTemporalParticipant.CompletePreviewNoFail(
                CoCoContextRestoreApplyKind applyKind)
            {
            }

            void ICoCoStateGraphTemporalParticipant.DrainPublishedCleanupNoFail()
            {
                CleanupCount++;
            }

            void ICoCoStateGraphTemporalParticipant.DetachTemporalHostNoFail()
            {
                DetachCount++;
                _resetPrepared = false;
                _host = null;
            }

            private static CoCoDiagnostic ParticipantError(string message) =>
                CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Restore,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    message);
        }
    }
}
