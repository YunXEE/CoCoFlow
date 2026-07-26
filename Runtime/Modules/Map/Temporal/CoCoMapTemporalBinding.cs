using System;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map.Temporal
{
    [DisallowMultipleComponent]
    public sealed class CoCoMapTemporalBinding :
        MonoBehaviour,
        ICoCoContextRestoreBinding,
        ICoCoStateGraphTemporalParticipant,
        ICoCoTemporalDecoratorBinding
    {
        [SerializeField] private CoCoStateGraphHost stateGraphHost;
        [SerializeField] private CoCoMapHost mapHost;
        [SerializeField] private MonoBehaviour downstreamRestoreBinding;

        private RegionTemporalRuntime runtime;
        private CoCoStateGraphHost attachedStateGraphHost;
        private CoCoMapHost attachedMapHost;
        private RegionRuntime attachedRegionRuntime;
        private bool downstreamWasConfigured;
        private MonoBehaviour attachedDownstreamComponent;
        private ICoCoContextRestoreBinding attachedDownstreamBinding;
        private ICoCoStateGraphTemporalParticipant
            attachedDownstreamParticipant;
        private bool downstreamParticipantAttached;
        private bool lifecycleCallbackActive;
        private bool restoreCallbackActive;
        private bool authorityResetPrepared;
        private CoCoDiagnostic lastDiagnostic;

        public CoCoStateGraphHost StateGraphHost => stateGraphHost;
        public CoCoMapHost MapHost => mapHost;
        MonoBehaviour ICoCoTemporalDecoratorBinding
            .DownstreamRestoreBinding => downstreamRestoreBinding;

        public CoCoDiagnostic LastDiagnostic
        {
            get
            {
                CoCoDiagnostic runtimeDiagnostic = runtime == null
                    ? CoCoDiagnostic.None
                    : runtime.LastDiagnostic;
                return !runtimeDiagnostic.IsNone
                    ? runtimeDiagnostic
                    : lastDiagnostic;
            }
        }

        public bool TryApply(
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterRestoreCallback(out diagnostic))
            {
                return false;
            }

            try
            {
                if (!context.IsValid ||
                    (authorityResetPrepared
                        ? context.ApplyKind !=
                          CoCoContextRestoreApplyKind.Confirm
                        : !runtime.TryApplyPreparedAvailabilityBarrier(
                            context.ApplyKind,
                            out diagnostic)))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = RegionErrors.TemporalProjection(
                            "Map Temporal Restore Binding received an invalid projection context.");
                    }

                    return RecordFailure(diagnostic);
                }

                if (downstreamWasConfigured)
                {
                    bool applied;
                    try
                    {
                        applied = attachedDownstreamBinding.TryApply(
                            context,
                            out diagnostic);
                    }
                    catch (Exception exception)
                    {
                        applied = false;
                        diagnostic = RegionErrors.TemporalProjection(
                            "The downstream Restore Binding threw during Map Temporal projection: " +
                            exception.Message);
                    }

                    if (!TryValidateFrozenDownstream(
                            attachedStateGraphHost,
                            requireParticipantLive: true,
                            out CoCoDiagnostic livenessDiagnostic))
                    {
                        diagnostic = livenessDiagnostic;
                        return RecordFailure(diagnostic);
                    }

                    if (!applied || diagnostic.IsError)
                    {
                        if (!diagnostic.IsError)
                        {
                            diagnostic = RegionErrors.TemporalProjection(
                                "The downstream Restore Binding rejected Map Temporal projection.");
                        }

                        return RecordFailure(diagnostic);
                    }
                }

                diagnostic = CoCoDiagnostic.None;
                lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "Map Temporal projection threw inside its synchronous Restore boundary: " +
                    exception.Message);
                return RecordFailure(diagnostic);
            }
            finally
            {
                restoreCallbackActive = false;
            }
        }

        private void OnDestroy()
        {
            DetachTemporalHostNoFail();
        }

        bool ICoCoStateGraphTemporalParticipant.TryAttachTemporalHost(
            CoCoStateGraphHost host,
            int historyCapacity,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (runtime != null ||
                attachedStateGraphHost != null ||
                attachedMapHost != null ||
                attachedRegionRuntime != null ||
                lifecycleCallbackActive ||
                restoreCallbackActive ||
                host == null ||
                !ReferenceEquals(host, stateGraphHost) ||
                historyCapacity < 2 ||
                mapHost == null ||
                !mapHost.IsInitialized ||
                mapHost.Runtime == null ||
                mapHost.Runtime.IsShuttingDown ||
                mapHost.Runtime.IsDisposed ||
                !CoCoStateGraphHostBoundary.Contains(host, this) ||
                !CoCoTemporalDecoratorChain.TryValidate(
                    host,
                    this,
                    out diagnostic) ||
                !TryFreezeDownstream(host, out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "Map Temporal Binding requires exact live StateGraph and Map Hosts plus one valid optional downstream Restore Binding.");
                }

                return RecordFailure(diagnostic);
            }

            attachedStateGraphHost = host;
            attachedMapHost = mapHost;
            attachedRegionRuntime = mapHost.Runtime;
            try
            {
                if (!RegionTemporalRuntime.TryCreate(
                        host,
                        attachedRegionRuntime,
                        historyCapacity,
                        out runtime,
                        out diagnostic))
                {
                    RollBackAttachmentNoFail();
                    return RecordFailure(diagnostic);
                }

                if (attachedDownstreamParticipant != null)
                {
                    bool downstreamAttached;
                    try
                    {
                        downstreamAttached =
                            attachedDownstreamParticipant
                                .TryAttachTemporalHost(
                                    host,
                                    historyCapacity,
                                    out diagnostic);
                    }
                    catch (Exception exception)
                    {
                        downstreamAttached = false;
                        diagnostic = RegionErrors.TemporalConflict(
                            "The downstream Temporal participant threw during Host attachment: " +
                            exception.Message);
                    }

                    if (!downstreamAttached || diagnostic.IsError)
                    {
                        RollBackAttachmentNoFail();
                        if (!diagnostic.IsError)
                        {
                            diagnostic = RegionErrors.TemporalConflict(
                                "The downstream Temporal participant rejected Map decorator attachment.");
                        }

                        return RecordFailure(diagnostic);
                    }

                    downstreamParticipantAttached = true;
                }

                if (!TryValidateFrozenDownstream(
                        host,
                        requireParticipantLive: true,
                        out diagnostic))
                {
                    RollBackAttachmentNoFail();
                    return RecordFailure(diagnostic);
                }

                diagnostic = CoCoDiagnostic.None;
                lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                RollBackAttachmentNoFail();
                diagnostic = RegionErrors.TemporalProjection(
                    "Map Temporal sidecar allocation failed during Host startup: " +
                    exception.Message);
                return RecordFailure(diagnostic);
            }
        }

        bool ICoCoStateGraphTemporalParticipant.IsTemporalParticipantLive(
            CoCoStateGraphHost host)
        {
            try
            {
                CoCoDiagnostic diagnostic = CoCoDiagnostic.None;
                bool live = IsAttachedRuntimeLive(host);
                if (live)
                {
                    live = TryValidateFrozenDownstream(
                        host,
                        requireParticipantLive: true,
                        out diagnostic);
                }
                else
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "Map Temporal Binding is no longer attached to its exact live Host pair.");
                }

                if (!live && !diagnostic.IsNone)
                {
                    lastDiagnostic = diagnostic;
                }

                return live;
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal liveness validation threw: " +
                    exception.Message);
                return false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterLifecycleCallback(
                    "forward capture preparation",
                    out diagnostic))
            {
                return false;
            }

            try
            {
                if (!runtime.TryPrepareForwardCapture(
                        candidate,
                        out diagnostic))
                {
                    return RecordFailure(diagnostic);
                }

                if (!TryPrepareDownstreamForwardCapture(
                        candidate,
                        out diagnostic))
                {
                    CancelDownstreamCaptureNoFail();
                    runtime.CancelPreparedCaptureNoFail();
                    return RecordFailure(diagnostic);
                }

                diagnostic = CoCoDiagnostic.None;
                lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                CancelDownstreamCaptureNoFail();
                runtime?.CancelPreparedCaptureNoFail();
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal forward capture preparation threw: " +
                    exception.Message);
                return RecordFailure(diagnostic);
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant
            .PublishForwardCaptureNoFail()
        {
            if (!TryEnterNoFailLifecycleCallback(
                    "forward capture publication",
                    out CoCoDiagnostic diagnostic))
            {
                return;
            }

            try
            {
                try
                {
                    runtime.PublishForwardCaptureNoFail();
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.TemporalCleanup(
                        "Map Temporal forward capture publication threw: " +
                        exception.Message);
                }

                try
                {
                    if (downstreamParticipantAttached)
                    {
                        attachedDownstreamParticipant
                            .PublishForwardCaptureNoFail();
                    }
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.TemporalCleanup(
                        "The downstream Temporal participant threw while publishing forward capture: " +
                        exception.Message);
                }

                lastDiagnostic = diagnostic;
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant
            .CancelPreparedCaptureNoFail()
        {
            if (!TryEnterNoFailLifecycleCallback(
                    "capture cancellation",
                    out CoCoDiagnostic diagnostic))
            {
                return;
            }

            try
            {
                CancelDownstreamCaptureNoFail();
                try
                {
                    runtime.CancelPreparedCaptureNoFail();
                }
                catch (Exception exception)
                {
                    lastDiagnostic = RegionErrors.TemporalCleanup(
                        "Map Temporal capture cancellation threw: " +
                        exception.Message);
                }
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareAuthorityReset(
            in CoCoTemporalFrameInfo targetAuthority,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterLifecycleCallback(
                    "authority reset preparation",
                    out diagnostic))
            {
                return false;
            }

            try
            {
                if (authorityResetPrepared ||
                    !runtime.TryPrepareAuthorityReset(
                        targetAuthority,
                        out diagnostic))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = RegionErrors.TemporalConflict(
                            "Map Temporal authority reset is already prepared.");
                    }

                    return RecordFailure(diagnostic);
                }

                if (!TryPrepareDownstreamAuthorityReset(
                        targetAuthority,
                        out diagnostic))
                {
                    CancelDownstreamAuthorityResetNoFail();
                    runtime.CancelPreparedAuthorityResetNoFail();
                    return RecordFailure(diagnostic);
                }

                if (!TryValidateFrozenDownstream(
                        attachedStateGraphHost,
                        requireParticipantLive: true,
                        out diagnostic))
                {
                    CancelDownstreamAuthorityResetNoFail();
                    runtime.CancelPreparedAuthorityResetNoFail();
                    return RecordFailure(diagnostic);
                }

                authorityResetPrepared = true;
                diagnostic = CoCoDiagnostic.None;
                lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                CancelDownstreamAuthorityResetNoFail();
                runtime?.CancelPreparedAuthorityResetNoFail();
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal authority reset preparation threw: " +
                    exception.Message);
                return RecordFailure(diagnostic);
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant
            .CommitPreparedAuthorityResetNoFail()
        {
            if (!authorityResetPrepared ||
                !TryEnterNoFailLifecycleCallback(
                    "authority reset publication",
                    out CoCoDiagnostic diagnostic))
            {
                return;
            }

            try
            {
                try
                {
                    runtime.CommitPreparedAuthorityResetNoFail();
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.TemporalCleanup(
                        "Map Temporal authority reset publication threw: " +
                        exception.Message);
                }

                lastDiagnostic = diagnostic;
                CommitDownstreamAuthorityResetNoFail();
                authorityResetPrepared = false;
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant
            .CancelPreparedAuthorityResetNoFail()
        {
            if (!authorityResetPrepared ||
                !TryEnterNoFailLifecycleCallback(
                    "authority reset cancellation",
                    out _))
            {
                return;
            }

            try
            {
                CancelDownstreamAuthorityResetNoFail();
                runtime.CancelPreparedAuthorityResetNoFail();
                authorityResetPrepared = false;
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal authority reset cancellation threw: " +
                    exception.Message);
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.TryBeginPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterLifecycleCallback(
                    "Preview start",
                    out diagnostic))
            {
                return false;
            }

            try
            {
                if (!runtime.TryBeginPreview(
                        historyCount,
                        out diagnostic))
                {
                    return RecordFailure(diagnostic);
                }

                if (!TryBeginDownstreamPreview(
                        historyCount,
                        out diagnostic))
                {
                    CompleteDownstreamPreviewNoFail(
                        CoCoContextRestoreApplyKind.Cancel);
                    runtime.CancelPreviewStartNoFail();
                    return RecordFailure(diagnostic);
                }

                diagnostic = CoCoDiagnostic.None;
                lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                CompleteDownstreamPreviewNoFail(
                    CoCoContextRestoreApplyKind.Cancel);
                runtime?.CancelPreviewStartNoFail();
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal Preview start threw: " +
                    exception.Message);
                return RecordFailure(diagnostic);
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterLifecycleCallback(
                    "projection preparation",
                    out diagnostic))
            {
                return false;
            }

            try
            {
                if (!runtime.TryPrepareProjection(
                        applyKind,
                        historyDepth,
                        source,
                        targetTickFrame,
                        out diagnostic))
                {
                    return RecordFailure(diagnostic);
                }

                if (!TryPrepareDownstreamProjection(
                        applyKind,
                        historyDepth,
                        source,
                        targetTickFrame,
                        out diagnostic))
                {
                    FinishDownstreamProjectionNoFail(false);
                    runtime.FinishProjectionNoFail(false);
                    return RecordFailure(diagnostic);
                }

                diagnostic = CoCoDiagnostic.None;
                lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                FinishDownstreamProjectionNoFail(false);
                runtime?.FinishProjectionNoFail(false);
                diagnostic = RegionErrors.TemporalProjection(
                    "Map Temporal projection preparation threw: " +
                    exception.Message);
                return RecordFailure(diagnostic);
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant.FinishProjectionNoFail(
            bool succeeded)
        {
            if (!TryEnterNoFailLifecycleCallback(
                    "projection completion",
                    out CoCoDiagnostic diagnostic))
            {
                return;
            }

            try
            {
                FinishDownstreamProjectionNoFail(succeeded);
                try
                {
                    runtime.FinishProjectionNoFail(succeeded);
                }
                catch (Exception exception)
                {
                    lastDiagnostic = RegionErrors.TemporalCleanup(
                        "Map Temporal projection completion threw: " +
                        exception.Message);
                }
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.CanConfirmPreview(
            int historyDepth)
        {
            if (!TryEnterLifecycleCallback(
                    "Preview confirmation validation",
                    out _))
            {
                return false;
            }

            try
            {
                if (!runtime.CanConfirmPreview(historyDepth))
                {
                    return false;
                }

                if (!downstreamParticipantAttached)
                {
                    return true;
                }

                try
                {
                    return attachedDownstreamParticipant
                        .CanConfirmPreview(historyDepth);
                }
                catch (Exception exception)
                {
                    lastDiagnostic = RegionErrors.TemporalProjection(
                        "The downstream Temporal participant threw while validating Preview confirmation: " +
                        exception.Message);
                    return false;
                }
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        bool ICoCoStateGraphTemporalParticipant.TryPrepareBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic)
        {
            if (!TryEnterLifecycleCallback(
                    "branch capture preparation",
                    out diagnostic))
            {
                return false;
            }

            try
            {
                if (!runtime.TryPrepareBranchCapture(
                        historyDepth,
                        branchHead,
                        out diagnostic))
                {
                    return RecordFailure(diagnostic);
                }

                if (!TryPrepareDownstreamBranchCapture(
                        historyDepth,
                        branchHead,
                        out diagnostic))
                {
                    CancelDownstreamCaptureNoFail();
                    runtime.CancelPreparedCaptureNoFail();
                    return RecordFailure(diagnostic);
                }

                diagnostic = CoCoDiagnostic.None;
                lastDiagnostic = diagnostic;
                return true;
            }
            catch (Exception exception)
            {
                CancelDownstreamCaptureNoFail();
                runtime?.CancelPreparedCaptureNoFail();
                diagnostic = RegionErrors.TemporalProjection(
                    "Map Temporal branch capture preparation threw: " +
                    exception.Message);
                return RecordFailure(diagnostic);
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant
            .PublishBranchCaptureNoFail()
        {
            if (!TryEnterNoFailLifecycleCallback(
                    "branch capture publication",
                    out CoCoDiagnostic diagnostic))
            {
                return;
            }

            try
            {
                try
                {
                    runtime.PublishBranchCaptureNoFail();
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.TemporalCleanup(
                        "Map Temporal branch capture publication threw: " +
                        exception.Message);
                }

                try
                {
                    if (downstreamParticipantAttached)
                    {
                        attachedDownstreamParticipant
                            .PublishBranchCaptureNoFail();
                    }
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.TemporalCleanup(
                        "The downstream Temporal participant threw while publishing branch capture: " +
                        exception.Message);
                }

                lastDiagnostic = diagnostic;
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant.CompletePreviewNoFail(
            CoCoContextRestoreApplyKind applyKind)
        {
            if (!TryEnterNoFailLifecycleCallback(
                    "Preview completion",
                    out CoCoDiagnostic diagnostic))
            {
                return;
            }

            try
            {
                CompleteDownstreamPreviewNoFail(applyKind);
                try
                {
                    runtime.CompletePreviewNoFail(applyKind);
                }
                catch (Exception exception)
                {
                    lastDiagnostic = RegionErrors.TemporalCleanup(
                        "Map Temporal Preview completion threw: " +
                        exception.Message);
                }
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant
            .DrainPublishedCleanupNoFail()
        {
            if (!TryEnterNoFailLifecycleCallback(
                    "published cleanup drain",
                    out CoCoDiagnostic diagnostic))
            {
                return;
            }

            try
            {
                DrainDownstreamCleanupNoFail();
                try
                {
                    runtime.DrainPublishedCleanupNoFail();
                }
                catch (Exception exception)
                {
                    lastDiagnostic = RegionErrors.TemporalCleanup(
                        "Map Temporal deferred retention cleanup threw: " +
                        exception.Message);
                }
            }
            finally
            {
                lifecycleCallbackActive = false;
            }
        }

        void ICoCoStateGraphTemporalParticipant
            .DetachTemporalHostNoFail()
        {
            DetachTemporalHostNoFail();
        }

        private bool TryEnterRestoreCallback(
            out CoCoDiagnostic diagnostic)
        {
            if (restoreCallbackActive || lifecycleCallbackActive)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal Restore Binding rejected synchronous callback re-entry.");
                return RecordFailure(diagnostic);
            }

            if (!TryRequireRuntime(out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            restoreCallbackActive = true;
            return true;
        }

        private bool TryEnterLifecycleCallback(
            string operation,
            out CoCoDiagnostic diagnostic)
        {
            if (lifecycleCallbackActive || restoreCallbackActive)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal rejected re-entry during " +
                    operation + ".");
                return RecordFailure(diagnostic);
            }

            if (!TryRequireRuntime(out diagnostic))
            {
                return RecordFailure(diagnostic);
            }

            lifecycleCallbackActive = true;
            return true;
        }

        private bool TryEnterNoFailLifecycleCallback(
            string operation,
            out CoCoDiagnostic diagnostic)
        {
            if (lifecycleCallbackActive || restoreCallbackActive)
            {
                diagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal rejected cleanup re-entry during " +
                    operation + ".");
                return RecordFailure(diagnostic);
            }

            if (runtime == null || runtime.IsDisposed)
            {
                diagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal cannot complete " + operation +
                    " because its retention sidecar is no longer live.");
                return RecordFailure(diagnostic);
            }

            lifecycleCallbackActive = true;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryRequireRuntime(
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = CoCoDiagnostic.None;
            if (IsAttachedRuntimeLive(attachedStateGraphHost) &&
                TryValidateFrozenDownstream(
                    attachedStateGraphHost,
                    requireParticipantLive: true,
                    out diagnostic))
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (diagnostic.IsNone)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "Map Temporal Binding is not attached to live StateGraph and Map Hosts.");
            }

            return false;
        }

        private bool IsAttachedRuntimeLive(CoCoStateGraphHost host)
        {
            return runtime != null &&
                   !runtime.IsDisposed &&
                   attachedStateGraphHost != null &&
                   ReferenceEquals(host, attachedStateGraphHost) &&
                   ReferenceEquals(stateGraphHost, attachedStateGraphHost) &&
                   attachedMapHost != null &&
                   ReferenceEquals(mapHost, attachedMapHost) &&
                   attachedRegionRuntime != null &&
                   !attachedRegionRuntime.IsShuttingDown &&
                   !attachedRegionRuntime.IsDisposed &&
                   ReferenceEquals(
                       attachedMapHost.Runtime,
                       attachedRegionRuntime) &&
                   CoCoStateGraphHostBoundary.Contains(
                       attachedStateGraphHost,
                       this);
        }

        private bool TryFreezeDownstream(
            CoCoStateGraphHost host,
            out CoCoDiagnostic diagnostic)
        {
            if (ReferenceEquals(downstreamRestoreBinding, null))
            {
                ClearFrozenDownstream();
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (ReferenceEquals(downstreamRestoreBinding, this) ||
                downstreamRestoreBinding == null ||
                !(downstreamRestoreBinding is
                    ICoCoContextRestoreBinding downstream) ||
                !CoCoStateGraphHostBoundary.Contains(
                    host,
                    downstreamRestoreBinding))
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "Downstream Restore Binding must be a different live component inside the same StateGraph Host boundary.");
                return false;
            }

            downstreamWasConfigured = true;
            attachedDownstreamComponent = downstreamRestoreBinding;
            attachedDownstreamBinding = downstream;
            attachedDownstreamParticipant =
                downstreamRestoreBinding as
                    ICoCoStateGraphTemporalParticipant;
            downstreamParticipantAttached = false;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryValidateFrozenDownstream(
            CoCoStateGraphHost host,
            bool requireParticipantLive,
            out CoCoDiagnostic diagnostic)
        {
            bool configuredNow =
                !ReferenceEquals(downstreamRestoreBinding, null);
            if (configuredNow != downstreamWasConfigured)
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "The downstream Restore Binding assignment changed after Map Host attachment.");
                return false;
            }

            if (!downstreamWasConfigured)
            {
                bool remainsEmpty =
                    ReferenceEquals(attachedDownstreamComponent, null) &&
                    ReferenceEquals(attachedDownstreamBinding, null) &&
                    ReferenceEquals(
                        attachedDownstreamParticipant,
                        null) &&
                    !downstreamParticipantAttached;
                diagnostic = remainsEmpty
                    ? CoCoDiagnostic.None
                    : RegionErrors.TemporalProjection(
                        "The optional downstream Restore Binding lost its frozen attachment state.");
                return remainsEmpty;
            }

            if (!ReferenceEquals(
                    downstreamRestoreBinding,
                    attachedDownstreamComponent) ||
                ReferenceEquals(attachedDownstreamComponent, null) ||
                attachedDownstreamComponent == null ||
                ReferenceEquals(attachedDownstreamComponent, this) ||
                ReferenceEquals(attachedDownstreamBinding, null) ||
                !(attachedDownstreamComponent is
                    ICoCoContextRestoreBinding currentBinding) ||
                !ReferenceEquals(
                    currentBinding,
                    attachedDownstreamBinding) ||
                !CoCoStateGraphHostBoundary.Contains(
                    host,
                    attachedDownstreamComponent))
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "The original downstream Restore Binding is no longer live inside the StateGraph Host boundary.");
                return false;
            }

            ICoCoStateGraphTemporalParticipant currentParticipant =
                attachedDownstreamComponent as
                    ICoCoStateGraphTemporalParticipant;
            if (!ReferenceEquals(
                    currentParticipant,
                    attachedDownstreamParticipant) ||
                (attachedDownstreamParticipant == null &&
                 downstreamParticipantAttached) ||
                (attachedDownstreamParticipant != null &&
                 requireParticipantLive &&
                 !downstreamParticipantAttached))
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "The downstream Temporal participant identity changed after attachment.");
                return false;
            }

            if (attachedDownstreamParticipant != null &&
                requireParticipantLive)
            {
                bool participantLive;
                try
                {
                    participantLive =
                        attachedDownstreamParticipant
                            .IsTemporalParticipantLive(host);
                }
                catch (Exception exception)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "The downstream Temporal participant threw during liveness validation: " +
                        exception.Message);
                    return false;
                }

                if (!participantLive)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "The original downstream Temporal participant is no longer live.");
                    return false;
                }
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private bool TryPrepareDownstreamForwardCapture(
            in CoCoTemporalFrameInfo candidate,
            out CoCoDiagnostic diagnostic)
        {
            if (!downstreamParticipantAttached)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                bool prepared =
                    attachedDownstreamParticipant
                        .TryPrepareForwardCapture(
                            candidate,
                            out diagnostic);
                if (!prepared && !diagnostic.IsError)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "The downstream Temporal participant rejected forward capture.");
                }

                return prepared && !diagnostic.IsError;
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "The downstream Temporal participant threw during forward capture preparation: " +
                    exception.Message);
                return false;
            }
        }

        private bool TryPrepareDownstreamAuthorityReset(
            in CoCoTemporalFrameInfo targetAuthority,
            out CoCoDiagnostic diagnostic)
        {
            if (!downstreamParticipantAttached)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                bool prepared =
                    attachedDownstreamParticipant
                        .TryPrepareAuthorityReset(
                            targetAuthority,
                            out diagnostic);
                if (!prepared && !diagnostic.IsError)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "The downstream Temporal participant rejected authority reset.");
                }

                return prepared && !diagnostic.IsError;
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "The downstream Temporal participant threw during authority reset preparation: " +
                    exception.Message);
                return false;
            }
        }

        private void CommitDownstreamAuthorityResetNoFail()
        {
            if (!downstreamParticipantAttached) return;

            try
            {
                attachedDownstreamParticipant
                    .CommitPreparedAuthorityResetNoFail();
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "The downstream Temporal participant threw while publishing authority reset: " +
                    exception.Message);
            }
        }

        private void CancelDownstreamAuthorityResetNoFail()
        {
            if (!downstreamParticipantAttached) return;

            try
            {
                attachedDownstreamParticipant
                    .CancelPreparedAuthorityResetNoFail();
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "The downstream Temporal participant threw while cancelling authority reset: " +
                    exception.Message);
            }
        }

        private bool TryBeginDownstreamPreview(
            int historyCount,
            out CoCoDiagnostic diagnostic)
        {
            if (!downstreamParticipantAttached)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                bool began =
                    attachedDownstreamParticipant.TryBeginPreview(
                        historyCount,
                        out diagnostic);
                if (!began && !diagnostic.IsError)
                {
                    diagnostic = RegionErrors.TemporalConflict(
                        "The downstream Temporal participant rejected Preview start.");
                }

                return began && !diagnostic.IsError;
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalConflict(
                    "The downstream Temporal participant threw during Preview start: " +
                    exception.Message);
                return false;
            }
        }

        private bool TryPrepareDownstreamProjection(
            CoCoContextRestoreApplyKind applyKind,
            int historyDepth,
            in CoCoTemporalFrameInfo source,
            in CoCoTickFrame targetTickFrame,
            out CoCoDiagnostic diagnostic)
        {
            if (!downstreamParticipantAttached)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                bool prepared =
                    attachedDownstreamParticipant.TryPrepareProjection(
                        applyKind,
                        historyDepth,
                        source,
                        targetTickFrame,
                        out diagnostic);
                if (!prepared && !diagnostic.IsError)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "The downstream Temporal participant rejected projection preparation.");
                }

                return prepared && !diagnostic.IsError;
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "The downstream Temporal participant threw during projection preparation: " +
                    exception.Message);
                return false;
            }
        }

        private bool TryPrepareDownstreamBranchCapture(
            int historyDepth,
            in CoCoTemporalFrameInfo branchHead,
            out CoCoDiagnostic diagnostic)
        {
            if (!downstreamParticipantAttached)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            try
            {
                bool prepared =
                    attachedDownstreamParticipant
                        .TryPrepareBranchCapture(
                            historyDepth,
                            branchHead,
                            out diagnostic);
                if (!prepared && !diagnostic.IsError)
                {
                    diagnostic = RegionErrors.TemporalProjection(
                        "The downstream Temporal participant rejected branch capture.");
                }

                return prepared && !diagnostic.IsError;
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.TemporalProjection(
                    "The downstream Temporal participant threw during branch capture preparation: " +
                    exception.Message);
                return false;
            }
        }

        private void FinishDownstreamProjectionNoFail(
            bool succeeded)
        {
            if (!downstreamParticipantAttached) return;

            try
            {
                attachedDownstreamParticipant
                    .FinishProjectionNoFail(succeeded);
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "The downstream Temporal participant threw during projection completion: " +
                    exception.Message);
            }
        }

        private void CancelDownstreamCaptureNoFail()
        {
            if (!downstreamParticipantAttached) return;

            try
            {
                attachedDownstreamParticipant
                    .CancelPreparedCaptureNoFail();
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "The downstream Temporal participant threw during capture cancellation: " +
                    exception.Message);
            }
        }

        private void CompleteDownstreamPreviewNoFail(
            CoCoContextRestoreApplyKind applyKind)
        {
            if (!downstreamParticipantAttached) return;

            try
            {
                attachedDownstreamParticipant
                    .CompletePreviewNoFail(applyKind);
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "The downstream Temporal participant threw during Preview completion: " +
                    exception.Message);
            }
        }

        private void DrainDownstreamCleanupNoFail()
        {
            if (!downstreamParticipantAttached) return;

            try
            {
                attachedDownstreamParticipant
                    .DrainPublishedCleanupNoFail();
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "The downstream Temporal participant threw while draining published cleanup: " +
                    exception.Message);
            }
        }

        private void TryDetachDownstreamNoFail()
        {
            if (attachedDownstreamParticipant == null) return;

            try
            {
                attachedDownstreamParticipant
                    .DetachTemporalHostNoFail();
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "The downstream Temporal participant threw during Host detachment: " +
                    exception.Message);
            }
            finally
            {
                downstreamParticipantAttached = false;
            }
        }

        private void DetachTemporalHostNoFail()
        {
            TryDetachDownstreamNoFail();
            try
            {
                runtime?.Dispose();
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal Binding required a terminal retention teardown fallback: " +
                    exception.Message);
            }
            finally
            {
                runtime = null;
                attachedStateGraphHost = null;
                attachedMapHost = null;
                attachedRegionRuntime = null;
                lifecycleCallbackActive = false;
                restoreCallbackActive = false;
                authorityResetPrepared = false;
                ClearFrozenDownstream();
            }
        }

        private void RollBackAttachmentNoFail()
        {
            TryDetachDownstreamNoFail();
            try
            {
                runtime?.Dispose();
            }
            catch (Exception exception)
            {
                lastDiagnostic = RegionErrors.TemporalCleanup(
                    "Map Temporal attachment rollback required terminal cleanup: " +
                    exception.Message);
            }
            finally
            {
                runtime = null;
                attachedStateGraphHost = null;
                attachedMapHost = null;
                attachedRegionRuntime = null;
                authorityResetPrepared = false;
                ClearFrozenDownstream();
            }
        }

        private void ClearFrozenDownstream()
        {
            downstreamWasConfigured = false;
            attachedDownstreamComponent = null;
            attachedDownstreamBinding = null;
            attachedDownstreamParticipant = null;
            downstreamParticipantAttached = false;
        }

        private bool RecordFailure(CoCoDiagnostic diagnostic)
        {
            lastDiagnostic = diagnostic;
            return false;
        }
    }
}
