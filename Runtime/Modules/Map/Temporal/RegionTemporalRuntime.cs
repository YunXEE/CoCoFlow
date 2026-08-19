using System;
using System.Collections.Generic;
using System.Globalization;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map.Temporal
{
    internal sealed class RegionTemporalRuntime : IDisposable
    {
        private readonly CoCoStateGraphHost host;
        private readonly RegionRuntime regionRuntime;
        private readonly RegionDemandScope retentionScope;
        private readonly RegionTemporalFrame[] frames;
        private readonly Dictionary<RetentionKey, RegionDemandLease> retentionLeases =
            new Dictionary<RetentionKey, RegionDemandLease>();
        private RegionTemporalFrame stagingFrame;
        private Dictionary<RetentionKey, RegionCapabilitySet> stagingTarget;
        private Dictionary<RetentionKey, RegionCapabilitySet> pendingTarget;
        private PreparedCaptureKind preparedCaptureKind;
        private int preparedBranchDepth;
        private int preparedBranchSourceIndex = -1;
        private int headIndex = -1;
        private int count;
        private bool isPreviewing;
        private bool projectionPrepared;
        private CoCoContextRestoreApplyKind preparedApplyKind;
        private RegionTemporalFrame preparedProjectionFrame;
        private RegionRuntime.RegionTemporalBarrier previewBarrier;
        private RegionRuntime.RegionTemporalBarrier correctionBarrier;
        private bool isDisposed;

        private RegionTemporalRuntime(
            CoCoStateGraphHost host,
            RegionRuntime regionRuntime,
            RegionDemandScope retentionScope,
            int historyCapacity)
        {
            this.host = host;
            this.regionRuntime = regionRuntime;
            this.retentionScope = retentionScope;
            frames = new RegionTemporalFrame[historyCapacity];
        }

        internal bool IsDisposed => isDisposed;
        internal bool IsPreviewing => isPreviewing;
        internal int HistoryCount => isDisposed ? 0 : count;
        internal CoCoDiagnostic LastDiagnostic { get; private set; }

        internal static bool TryCreate(
            CoCoStateGraphHost host,
            RegionRuntime regionRuntime,
            int historyCapacity,
            out RegionTemporalRuntime runtime,
            out CoCoDiagnostic diagnostic)
        {
            runtime = null;
            diagnostic = CoCoDiagnostic.None;
            if (host == null ||
                regionRuntime == null ||
                regionRuntime.IsShuttingDown ||
                regionRuntime.IsDisposed ||
                historyCapacity < 2)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal requires exact live StateGraph and Map runtimes plus at least two history entries.");
                return false;
            }

#if UNITY_6000_5_OR_NEWER
            string hostIdentity = host.GetEntityId().ToString();
#else
            string hostIdentity = host.GetInstanceID().ToString(CultureInfo.InvariantCulture);
#endif
            string ownerValue = "cocoflow.map.temporal." + hostIdentity;
            if (!RegionDemandOwnerId.TryCreate(
                    ownerValue,
                    out RegionDemandOwnerId ownerId) ||
                !regionRuntime.TryCreateDemandScope(
                    ownerId,
                    out RegionDemandScope scope,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "Map Temporal could not reserve its explicit retention Demand Scope.");
                }

                return false;
            }

            runtime = new RegionTemporalRuntime(
                host,
                regionRuntime,
                scope,
                historyCapacity);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryPrepareForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireIdle(out diagnostic) ||
                regionRuntime.IsTemporalDispatchDeferred ||
                isPreviewing ||
                pendingTarget != null ||
                !candidate.IsValid)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "Map Temporal forward capture is not available in the current lifecycle state.");
                }

                return RecordFailure(diagnostic);
            }

            if (!TryCaptureCommittedFrame(
                    candidate,
                    out RegionTemporalFrame frame,
                    out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            Dictionary<RetentionKey, RegionCapabilitySet> futureTarget =
                BuildForwardTarget(frame);
            if (!TryApplyRetention(
                    futureTarget,
                    allowDecrease: false,
                    out diagnostic) ||
                !TryValidateFrameAvailable(frame, out diagnostic))
            {
                RestorePublishedRetentionNoFail();
                return RecordFailure(diagnostic);
            }

            stagingFrame = frame;
            stagingTarget = futureTarget;
            preparedCaptureKind = PreparedCaptureKind.Forward;
            diagnostic = CoCoDiagnostic.None;
            LastDiagnostic = diagnostic;
            return true;
        }

        internal void PublishForwardCaptureNoFail()
        {
            if (isDisposed ||
                preparedCaptureKind != PreparedCaptureKind.Forward ||
                stagingFrame == null ||
                stagingTarget == null)
            {
                return;
            }

            int publishIndex =
                count == 0 ? 0 : (headIndex + 1) % frames.Length;
            frames[publishIndex] = stagingFrame;
            headIndex = publishIndex;
            if (count < frames.Length) count++;

            pendingTarget = stagingTarget;
            ClearPreparedCapture();
        }

        internal void CancelPreparedCaptureNoFail()
        {
            if (isDisposed) return;

            if (preparedCaptureKind == PreparedCaptureKind.Forward)
            {
                RestorePublishedRetentionNoFail();
            }

            ClearPreparedCapture();
        }

        internal bool TryPrepareAuthorityReset(
            in CoCoTemporalFrameInfo targetAuthority,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireIdle(out diagnostic) ||
                regionRuntime.IsTemporalDispatchDeferred ||
                isPreviewing ||
                pendingTarget != null ||
                !targetAuthority.IsValid)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "Map Temporal authority reset is not available in the current lifecycle state.");
                }

                return RecordFailure(diagnostic);
            }

            stagingFrame = new RegionTemporalFrame(
                targetAuthority,
                Array.Empty<RegionTemporalRegionState>());
            stagingTarget =
                new Dictionary<RetentionKey, RegionCapabilitySet>();
            preparedCaptureKind = PreparedCaptureKind.AuthorityReset;
            diagnostic = CoCoDiagnostic.None;
            LastDiagnostic = diagnostic;
            return true;
        }

        internal void CommitPreparedAuthorityResetNoFail()
        {
            if (isDisposed ||
                preparedCaptureKind != PreparedCaptureKind.AuthorityReset ||
                stagingFrame == null ||
                stagingTarget == null)
            {
                return;
            }

            for (int index = 0; index < frames.Length; index++)
            {
                frames[index] = null;
            }

            frames[0] = stagingFrame;
            headIndex = 0;
            count = 1;
            pendingTarget = stagingTarget;
            ClearPreparedCapture();
        }

        internal void CancelPreparedAuthorityResetNoFail()
        {
            if (isDisposed ||
                preparedCaptureKind != PreparedCaptureKind.AuthorityReset)
            {
                return;
            }

            ClearPreparedCapture();
        }

        internal bool TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireIdle(out diagnostic) ||
                isPreviewing ||
                pendingTarget != null ||
                historyCount != count)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "Map Temporal history is not aligned or retained for Preview.");
                }

                return RecordFailure(diagnostic);
            }

            if (!regionRuntime.TryEnterTemporalBarrier(
                    out RegionRuntime.RegionTemporalBarrier barrier,
                    out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            if (!TryValidateTargetAvailable(
                    BuildPublishedTarget(),
                    out diagnostic))
            {
                barrier.Dispose();
                return RecordFailure(diagnostic);
            }

            previewBarrier = barrier;
            isPreviewing = true;
            diagnostic = CoCoDiagnostic.None;
            LastDiagnostic = diagnostic;
            return true;
        }

        internal void CancelPreviewStartNoFail()
        {
            if (!isDisposed && !projectionPrepared)
            {
                isPreviewing = false;
                ReleasePreviewBarrierNoFail();
            }
        }

        internal bool TryPrepareProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            bool standaloneCorrection =
                applyKind == CoCoContextRestoreApplyKind.Correction &&
                !isPreviewing;
            if (isDisposed ||
                projectionPrepared ||
                !source.IsValid ||
                !targetTickFrame.IsValid ||
                (applyKind != CoCoContextRestoreApplyKind.Correction &&
                 !isPreviewing) ||
                (isPreviewing && previewBarrier == null) ||
                (standaloneCorrection &&
                 !TryRequireIdle(out diagnostic)))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Map Temporal projection metadata is invalid for the current lifecycle state.");
                }

                return RecordFailure(diagnostic);
            }

            RegionRuntime.RegionTemporalBarrier acquiredCorrection = null;
            if (standaloneCorrection &&
                !regionRuntime.TryEnterTemporalBarrier(
                    out acquiredCorrection,
                    out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            RegionTemporalFrame frame = null;
            if (applyKind == CoCoContextRestoreApplyKind.Correction)
            {
                if (!TryCaptureCommittedFrame(
                        source,
                        out frame,
                        out diagnostic))
                {
                    acquiredCorrection?.Dispose();
                    return RecordFailure(diagnostic);
                }
            }
            else if (!TryResolveDepth(historyDepth, out int frameIndex))
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "Map Temporal projection depth is outside retained history.");
                return RecordFailure(diagnostic);
            }
            else
            {
                frame = frames[frameIndex];
            }

            if (!TryValidateFrameAvailable(frame, out diagnostic))
            {
                acquiredCorrection?.Dispose();
                return RecordFailure(diagnostic);
            }

            correctionBarrier = acquiredCorrection;
            preparedApplyKind = applyKind;
            preparedProjectionFrame = frame;
            projectionPrepared = true;
            diagnostic = CoCoDiagnostic.None;
            LastDiagnostic = diagnostic;
            return true;
        }

        internal bool TryApplyPreparedAvailabilityBarrier(
            CoCoContextRestoreApplyKind applyKind,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (isDisposed ||
                !projectionPrepared ||
                applyKind != preparedApplyKind ||
                preparedProjectionFrame == null ||
                !TryValidateFrameAvailable(
                    preparedProjectionFrame,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Map Temporal lost retained Region availability before Restore projection.");
                }

                return RecordFailure(diagnostic);
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void FinishProjectionNoFail(bool succeeded)
        {
            if (isDisposed) return;
            if (!succeeded && LastDiagnostic.IsNone)
            {
                LastDiagnostic = RegionErrors.TemporalProjection(
                    "Map Temporal projection failed after the availability barrier.");
            }

            ClearPreparedProjection();
            ReleaseCorrectionBarrierNoFail();
        }

        internal bool CanConfirmPreview(int historyDepth)
        {
            return !isDisposed &&
                   isPreviewing &&
                   historyDepth > 0 &&
                   TryResolveDepth(historyDepth, out int frameIndex) &&
                   TryValidateFrameAvailable(frames[frameIndex], out _);
        }

        internal bool TryPrepareBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryRequireIdle(out diagnostic) ||
                !isPreviewing ||
                historyDepth <= 0 ||
                !branchHead.IsValid ||
                !TryResolveDepth(historyDepth, out int sourceIndex))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Map Temporal could not stage the restored branch head.");
                }

                return RecordFailure(diagnostic);
            }

            RegionTemporalFrame source = frames[sourceIndex];
            if (!TryValidateFrameAvailable(source, out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            stagingFrame = source.WithFrameInfo(branchHead);
            stagingTarget = BuildBranchTarget(
                historyDepth,
                stagingFrame);
            preparedBranchDepth = historyDepth;
            preparedBranchSourceIndex = sourceIndex;
            preparedCaptureKind = PreparedCaptureKind.Branch;
            diagnostic = CoCoDiagnostic.None;
            LastDiagnostic = diagnostic;
            return true;
        }

        internal void PublishBranchCaptureNoFail()
        {
            if (isDisposed ||
                preparedCaptureKind != PreparedCaptureKind.Branch ||
                preparedBranchSourceIndex < 0 ||
                stagingFrame == null ||
                stagingTarget == null)
            {
                return;
            }

            int publishIndex =
                (preparedBranchSourceIndex + 1) % frames.Length;
            frames[publishIndex] = stagingFrame;
            headIndex = publishIndex;
            count = count - preparedBranchDepth + 1;
            pendingTarget = stagingTarget;
            ClearPreparedCapture();
        }

        internal void CompletePreviewNoFail(
            CoCoContextRestoreApplyKind applyKind)
        {
            if (isDisposed) return;

            isPreviewing = false;
            ClearPreparedProjection();
            ReleaseCorrectionBarrierNoFail();
            ReleasePreviewBarrierNoFail();
        }

        internal void DrainPublishedCleanupNoFail()
        {
            if (isDisposed ||
                isPreviewing ||
                preparedCaptureKind != PreparedCaptureKind.None ||
                pendingTarget == null)
            {
                return;
            }

            if (TryApplyRetention(
                    pendingTarget,
                    allowDecrease: true,
                    out CoCoDiagnostic diagnostic))
            {
                pendingTarget = null;
                LastDiagnostic = CoCoDiagnostic.None;
                return;
            }

            LastDiagnostic = diagnostic.IsNone
                ? RegionErrors.TemporalCleanup(
                    "Map Temporal could not publish its deferred retention decrease.")
                : diagnostic;
        }

        public void Dispose()
        {
            if (isDisposed) return;

            isDisposed = true;
            ReleaseCorrectionBarrierNoFail();
            ReleasePreviewBarrierNoFail();
            try
            {
                retentionScope.Dispose();
            }
            catch (Exception)
            {
                LastDiagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal retention Scope required terminal teardown.");
            }

            retentionLeases.Clear();
            for (int index = 0; index < frames.Length; index++)
            {
                frames[index] = null;
            }

            count = 0;
            headIndex = -1;
            stagingFrame = null;
            stagingTarget = null;
            pendingTarget = null;
            ClearPreparedCapture();
            ClearPreparedProjection();
        }

        private bool TryCaptureCommittedFrame(
            in CoCoTemporalFrameInfo frameInfo,
            out RegionTemporalFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            frame = null;
            RegionRuntimeSnapshot snapshot;
            try
            {
                snapshot = regionRuntime.CaptureSnapshot();
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "Map committed snapshot capture failed: " +
                    exception.Message);
                return false;
            }

            if (snapshot.IsDisposed || snapshot.IsShuttingDown)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Runtime is not live for Temporal capture.");
                return false;
            }

            var regions = new List<RegionTemporalRegionState>();
            for (int regionIndex = 0;
                 regionIndex < snapshot.Regions.Count;
                 regionIndex++)
            {
                RegionRuntimeRegionState source =
                    snapshot.Regions[regionIndex];
                if (source.Faulted ||
                    source.BlockedCleanup ||
                    source.HasInFlightTransition)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Region '" + source.RegionId.Value +
                        "' is not at one stable committed availability barrier.");
                    return false;
                }

                if (source.CommittedEffectiveCapabilities.Count == 0)
                {
                    continue;
                }

                if (!source.CommittedCoverage.IsValid)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Region '" + source.RegionId.Value +
                        "' has capabilities without valid committed Coverage.");
                    return false;
                }

                var chunks = new List<RegionTemporalChunkState>();
                RegionCapabilitySet chunkUnion = RegionCapabilitySet.Empty;
                for (int chunkIndex = 0;
                     chunkIndex < source.Chunks.Count;
                     chunkIndex++)
                {
                    RegionChunkRuntimeSnapshot chunk =
                        source.Chunks[chunkIndex];
                    if (chunk.CommittedEffectiveCapabilities.Count == 0)
                    {
                        continue;
                    }

                    chunks.Add(
                        new RegionTemporalChunkState(
                            chunk.ChunkId,
                            chunk.CommittedEffectiveCapabilities));
                    chunkUnion =
                        chunkUnion.Union(
                            chunk.CommittedEffectiveCapabilities);
                }

                if (chunks.Count == 0)
                {
                    if (!source.CommittedCoverage.CoversAll)
                    {
                        diagnostic = RegionErrors.TemporalProjection(
                            "Region '" + source.RegionId.Value +
                            "' has no Chunk availability for explicit committed Coverage.");
                        return false;
                    }
                }
                else if (!chunkUnion.Equals(
                             source.CommittedEffectiveCapabilities))
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Region '" + source.RegionId.Value +
                        "' committed global and per-Chunk capabilities are inconsistent.");
                    return false;
                }

                chunks.Sort(
                    (left, right) => string.CompareOrdinal(
                        left.ChunkId.Value,
                        right.ChunkId.Value));
                RegionCapabilitySet allCoverageCapabilities =
                    RegionCapabilitySet.Empty;
                if (source.CommittedCoverage.CoversAll)
                {
                    if (!TryResolveAllCoverageCapabilities(
                            source.CommittedEffectiveCapabilities,
                            chunks,
                            out allCoverageCapabilities))
                    {
                        diagnostic = RegionErrors.TemporalProjection(
                            "Region '" + source.RegionId.Value +
                            "' cannot preserve All Coverage without diffusing a Chunk-specific capability.");
                        return false;
                    }
                }

                regions.Add(
                    new RegionTemporalRegionState(
                        source.RegionId,
                        source.CommittedEffectiveCapabilities,
                        source.CommittedCoverage,
                        allCoverageCapabilities,
                        chunks));
            }

            regions.Sort(
                (left, right) => string.CompareOrdinal(
                    left.RegionId.Value,
                    right.RegionId.Value));
            frame = new RegionTemporalFrame(frameInfo, regions);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateFrameAvailable(
            RegionTemporalFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (frame == null)
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "Map Temporal has no captured Region availability frame.");
                return false;
            }

            RegionRuntimeSnapshot snapshot;
            try
            {
                snapshot = regionRuntime.CaptureSnapshot();
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "Map availability validation failed: " +
                    exception.Message);
                return false;
            }

            var current =
                new Dictionary<RegionId, RegionRuntimeRegionState>();
            for (int index = 0; index < snapshot.Regions.Count; index++)
            {
                current[snapshot.Regions[index].RegionId] =
                    snapshot.Regions[index];
            }

            for (int regionIndex = 0;
                 regionIndex < frame.Regions.Count;
                 regionIndex++)
            {
                RegionTemporalRegionState required =
                    frame.Regions[regionIndex];
                if (!current.TryGetValue(
                        required.RegionId,
                        out RegionRuntimeRegionState available) ||
                    available.Faulted ||
                    available.BlockedCleanup ||
                    !available.CommittedEffectiveCapabilities.IsSupersetOf(
                        required.Capabilities))
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Historical Region '" + required.RegionId.Value +
                        "' is no longer committed at the required availability barrier.");
                    return false;
                }

                if (!IsCoverageAvailable(
                        required.Coverage,
                        available.CommittedCoverage))
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Historical Region '" + required.RegionId.Value +
                        "' is no longer committed with the required Coverage.");
                    return false;
                }

                var chunks =
                    new Dictionary<RegionChunkId, RegionCapabilitySet>();
                for (int chunkIndex = 0;
                     chunkIndex < available.Chunks.Count;
                     chunkIndex++)
                {
                    chunks[available.Chunks[chunkIndex].ChunkId] =
                        available.Chunks[chunkIndex]
                            .CommittedEffectiveCapabilities;
                }

                for (int chunkIndex = 0;
                     chunkIndex < required.Chunks.Count;
                     chunkIndex++)
                {
                    RegionTemporalChunkState requiredChunk =
                        required.Chunks[chunkIndex];
                    if (!chunks.TryGetValue(
                            requiredChunk.ChunkId,
                            out RegionCapabilitySet availableCapabilities) ||
                        !availableCapabilities.IsSupersetOf(
                            requiredChunk.Capabilities))
                    {
                        diagnostic = RegionErrors.TemporalProjection(
                            "Historical Chunk '" +
                            required.RegionId.Value + "/" +
                            requiredChunk.ChunkId.Value +
                            "' is no longer retained at the required capability.");
                        return false;
                    }
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateTargetAvailable(
            IReadOnlyDictionary<RetentionKey, RegionCapabilitySet> target,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            foreach (
                KeyValuePair<RetentionKey, RegionCapabilitySet> pair
                in target)
            {
                if (!retentionLeases.TryGetValue(
                        pair.Key,
                        out RegionDemandLease lease) ||
                    lease.IsDisposed ||
                    !lease.Capabilities.IsSupersetOf(pair.Value))
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Map Temporal retention Demand is no longer live at the historical availability barrier.");
                    return false;
                }
            }

            for (int depth = 0; depth < count; depth++)
            {
                if (!TryResolveDepth(depth, out int frameIndex))
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "Map Temporal history contains an invalid retained depth.");
                    return false;
                }

                if (!TryValidateFrameAvailable(
                        frames[frameIndex],
                        out diagnostic))
                {
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private Dictionary<RetentionKey, RegionCapabilitySet>
            BuildForwardTarget(RegionTemporalFrame candidate)
        {
            var target =
                new Dictionary<RetentionKey, RegionCapabilitySet>();
            AddFrameToTarget(candidate, target);
            int retainedExisting = Math.Min(count, frames.Length - 1);
            for (int depth = 0; depth < retainedExisting; depth++)
            {
                if (TryResolveDepth(depth, out int frameIndex))
                {
                    AddFrameToTarget(frames[frameIndex], target);
                }
            }

            return target;
        }

        private Dictionary<RetentionKey, RegionCapabilitySet>
            BuildBranchTarget(
                int historyDepth,
                RegionTemporalFrame branchHead)
        {
            var target =
                new Dictionary<RetentionKey, RegionCapabilitySet>();
            AddFrameToTarget(branchHead, target);
            for (int depth = historyDepth; depth < count; depth++)
            {
                if (TryResolveDepth(depth, out int frameIndex))
                {
                    AddFrameToTarget(frames[frameIndex], target);
                }
            }

            return target;
        }

        private Dictionary<RetentionKey, RegionCapabilitySet>
            BuildPublishedTarget()
        {
            var target =
                new Dictionary<RetentionKey, RegionCapabilitySet>();
            for (int depth = 0; depth < count; depth++)
            {
                if (TryResolveDepth(depth, out int frameIndex))
                {
                    AddFrameToTarget(frames[frameIndex], target);
                }
            }

            return target;
        }

        private static void AddFrameToTarget(
            RegionTemporalFrame frame,
            IDictionary<RetentionKey, RegionCapabilitySet> target)
        {
            if (frame == null) return;

            for (int regionIndex = 0;
                 regionIndex < frame.Regions.Count;
                 regionIndex++)
            {
                RegionTemporalRegionState region =
                    frame.Regions[regionIndex];
                if (region.Coverage.CoversAll)
                {
                    AddTarget(
                        new RetentionKey(region.RegionId),
                        region.AllCoverageCapabilities,
                        target);
                }

                for (int chunkIndex = 0;
                     chunkIndex < region.Chunks.Count;
                     chunkIndex++)
                {
                    RegionTemporalChunkState chunk =
                        region.Chunks[chunkIndex];
                    AddTarget(
                        new RetentionKey(
                            region.RegionId,
                            chunk.ChunkId),
                        chunk.Capabilities,
                        target);
                }
            }
        }

        private static bool TryResolveAllCoverageCapabilities(
            RegionCapabilitySet regionCapabilities,
            IReadOnlyList<RegionTemporalChunkState> chunks,
            out RegionCapabilitySet capabilities)
        {
            if (chunks == null || chunks.Count == 0)
            {
                capabilities =
                    regionCapabilities ?? RegionCapabilitySet.Empty;
                return capabilities.Count > 0;
            }

            var intersection = new List<RegionCapabilityId>(
                chunks[0].Capabilities.Capabilities);
            for (int chunkIndex = 1;
                 chunkIndex < chunks.Count;
                 chunkIndex++)
            {
                RegionCapabilitySet chunkCapabilities =
                    chunks[chunkIndex].Capabilities;
                for (int capabilityIndex = intersection.Count - 1;
                     capabilityIndex >= 0;
                     capabilityIndex--)
                {
                    if (!chunkCapabilities.Contains(
                            intersection[capabilityIndex]))
                    {
                        intersection.RemoveAt(capabilityIndex);
                    }
                }
            }

            return RegionCapabilitySet.TryCreate(
                       intersection,
                       out capabilities) &&
                   capabilities.Count > 0;
        }

        private static bool IsCoverageAvailable(
            RegionCoverage required,
            RegionCoverage available)
        {
            if (!required.IsValid || !available.IsValid)
            {
                return false;
            }

            if (required.CoversAll)
            {
                return available.CoversAll;
            }

            for (int index = 0; index < required.Chunks.Count; index++)
            {
                if (!available.Contains(required.Chunks[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static void AddTarget(
            RetentionKey key,
            RegionCapabilitySet capabilities,
            IDictionary<RetentionKey, RegionCapabilitySet> target)
        {
            if (capabilities == null || capabilities.Count == 0) return;

            target.TryGetValue(
                key,
                out RegionCapabilitySet existing);
            target[key] =
                (existing ?? RegionCapabilitySet.Empty).Union(capabilities);
        }

        private bool TryApplyRetention(
            IReadOnlyDictionary<RetentionKey, RegionCapabilitySet> target,
            bool allowDecrease,
            out CoCoDiagnostic diagnostic)
        {
            var orderedKeys = new List<RetentionKey>(target.Keys);
            orderedKeys.Sort(RetentionKey.Compare);
            for (int index = 0; index < orderedKeys.Count; index++)
            {
                RetentionKey key = orderedKeys[index];
                RegionCapabilitySet targetCapabilities = target[key];
                if (retentionLeases.TryGetValue(
                        key,
                        out RegionDemandLease lease))
                {
                    RegionCapabilitySet capabilities = allowDecrease
                        ? targetCapabilities
                        : lease.Capabilities.Union(targetCapabilities);
                    if (lease.Capabilities.Equals(capabilities))
                    {
                        continue;
                    }

                    if (!lease.TryUpdate(
                            capabilities,
                            CreateCoverage(key),
                            out _,
                            out diagnostic))
                    {
                        return false;
                    }

                    continue;
                }

                if (!retentionScope.TryDemand(
                        key.RegionId,
                        targetCapabilities,
                        CreateCoverage(key),
                        out lease,
                        out _,
                        out diagnostic))
                {
                    return false;
                }

                retentionLeases.Add(key, lease);
            }

            if (allowDecrease)
            {
                var removals = new List<RetentionKey>();
                foreach (
                    KeyValuePair<RetentionKey, RegionDemandLease> pair
                    in retentionLeases)
                {
                    if (!target.ContainsKey(pair.Key))
                    {
                        removals.Add(pair.Key);
                    }
                }

                removals.Sort(RetentionKey.Compare);
                for (int index = 0; index < removals.Count; index++)
                {
                    RegionDemandLease lease = retentionLeases[removals[index]];
                    retentionLeases.Remove(removals[index]);
                    lease.Dispose();
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static RegionCoverage CreateCoverage(RetentionKey key)
        {
            if (key.CoversAll) return RegionCoverage.All;
            if (RegionCoverage.TryCreateChunks(
                    new[] { key.ChunkId },
                    out RegionCoverage coverage))
            {
                return coverage;
            }

            throw new InvalidOperationException(
                "A valid temporal retention key must produce valid Coverage.");
        }

        private void RestorePublishedRetentionNoFail()
        {
            try
            {
                if (!TryApplyRetention(
                        BuildPublishedTarget(),
                        allowDecrease: true,
                        out CoCoDiagnostic diagnostic))
                {
                    LastDiagnostic = diagnostic.IsNone
                        ? RegionErrors.TemporalCleanup(
                            "Map Temporal could not roll back unpublished retention.")
                        : diagnostic;
                }
            }
            catch (Exception exception)
            {
                LastDiagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal retention rollback threw: " +
                    exception.Message);
            }
        }

        private bool TryRequireIdle(out CoCoDiagnostic diagnostic)
        {
            if (!isDisposed &&
                !regionRuntime.IsShuttingDown &&
                !regionRuntime.IsDisposed &&
                host != null &&
                preparedCaptureKind == PreparedCaptureKind.None &&
                !projectionPrepared)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            diagnostic = RegionErrors.TemporalConflict(
                "Map Temporal runtime is disposed, re-entered, or has an active prepared operation.");
            return false;
        }

        private bool TryResolveDepth(
            int historyDepth,
            out int frameIndex)
        {
            if (historyDepth < 0 ||
                historyDepth >= count ||
                headIndex < 0)
            {
                frameIndex = -1;
                return false;
            }

            frameIndex = headIndex - historyDepth;
            if (frameIndex < 0) frameIndex += frames.Length;
            return frames[frameIndex] != null;
        }

        private void ClearPreparedCapture()
        {
            stagingFrame = null;
            stagingTarget = null;
            preparedCaptureKind = PreparedCaptureKind.None;
            preparedBranchDepth = 0;
            preparedBranchSourceIndex = -1;
        }

        private void ClearPreparedProjection()
        {
            projectionPrepared = false;
            preparedApplyKind = default;
            preparedProjectionFrame = null;
        }

        private void ReleasePreviewBarrierNoFail()
        {
            try
            {
                previewBarrier?.Dispose();
            }
            catch (Exception exception)
            {
                LastDiagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal Preview barrier release threw: " +
                    exception.Message);
            }
            finally
            {
                previewBarrier = null;
            }
        }

        private void ReleaseCorrectionBarrierNoFail()
        {
            try
            {
                correctionBarrier?.Dispose();
            }
            catch (Exception exception)
            {
                LastDiagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal Correction barrier release threw: " +
                    exception.Message);
            }
            finally
            {
                correctionBarrier = null;
            }
        }

        private bool RecordFailure(CoCoDiagnostic diagnostic)
        {
            LastDiagnostic = diagnostic;
            return false;
        }

        private enum PreparedCaptureKind
        {
            None = 0,
            Forward = 1,
            Branch = 2,
            AuthorityReset = 3
        }

        private readonly struct RetentionKey :
            IEquatable<RetentionKey>
        {
            internal RetentionKey(RegionId regionId)
            {
                RegionId = regionId;
                ChunkId = default;
                CoversAll = true;
            }

            internal RetentionKey(
                RegionId regionId,
                RegionChunkId chunkId)
            {
                RegionId = regionId;
                ChunkId = chunkId;
                CoversAll = false;
            }

            internal RegionId RegionId { get; }
            internal RegionChunkId ChunkId { get; }
            internal bool CoversAll { get; }

            public bool Equals(RetentionKey other) =>
                RegionId == other.RegionId &&
                CoversAll == other.CoversAll &&
                (CoversAll || ChunkId == other.ChunkId);

            public override bool Equals(object obj) =>
                obj is RetentionKey other && Equals(other);

            public override int GetHashCode()
            {
                unchecked
                {
                    return RegionId.GetHashCode() * 397 ^
                           (CoversAll ? 1 : ChunkId.GetHashCode());
                }
            }

            internal static int Compare(
                RetentionKey left,
                RetentionKey right)
            {
                int region = string.CompareOrdinal(
                    left.RegionId.Value,
                    right.RegionId.Value);
                if (region != 0) return region;
                if (left.CoversAll != right.CoversAll)
                {
                    return left.CoversAll ? -1 : 1;
                }

                return string.CompareOrdinal(
                    left.ChunkId.Value,
                    right.ChunkId.Value);
            }
        }

        private sealed class RegionTemporalFrame
        {
            internal RegionTemporalFrame(
                CoCoTemporalFrameInfo info,
                IList<RegionTemporalRegionState> regions)
            {
                Info = info;
                Regions = Array.AsReadOnly(
                    regions == null
                        ? Array.Empty<RegionTemporalRegionState>()
                        : new List<RegionTemporalRegionState>(
                            regions).ToArray());
            }

            internal CoCoTemporalFrameInfo Info { get; }
            internal IReadOnlyList<RegionTemporalRegionState> Regions { get; }

            internal RegionTemporalFrame WithFrameInfo(
                CoCoTemporalFrameInfo info) =>
                new RegionTemporalFrame(
                    info,
                    new List<RegionTemporalRegionState>(Regions));
        }

        private sealed class RegionTemporalRegionState
        {
            internal RegionTemporalRegionState(
                RegionId regionId,
                RegionCapabilitySet capabilities,
                RegionCoverage coverage,
                RegionCapabilitySet allCoverageCapabilities,
                IList<RegionTemporalChunkState> chunks)
            {
                RegionId = regionId;
                Capabilities =
                    capabilities ?? RegionCapabilitySet.Empty;
                Coverage = coverage;
                AllCoverageCapabilities =
                    allCoverageCapabilities ??
                    RegionCapabilitySet.Empty;
                Chunks = Array.AsReadOnly(
                    chunks == null
                        ? Array.Empty<RegionTemporalChunkState>()
                        : new List<RegionTemporalChunkState>(
                            chunks).ToArray());
            }

            internal RegionId RegionId { get; }
            internal RegionCapabilitySet Capabilities { get; }
            internal RegionCoverage Coverage { get; }
            internal RegionCapabilitySet AllCoverageCapabilities { get; }
            internal IReadOnlyList<RegionTemporalChunkState> Chunks { get; }
        }

        private readonly struct RegionTemporalChunkState
        {
            internal RegionTemporalChunkState(
                RegionChunkId chunkId,
                RegionCapabilitySet capabilities)
            {
                ChunkId = chunkId;
                Capabilities =
                    capabilities ?? RegionCapabilitySet.Empty;
            }

            internal RegionChunkId ChunkId { get; }
            internal RegionCapabilitySet Capabilities { get; }
        }
    }
}
