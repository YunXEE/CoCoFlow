using System;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.StateGraphHost
{
    internal enum CoCoStateGraphHostTraceFilterMode
    {
        All = 0,
        StateId = 1,
        TransitionId = 2
    }

    internal sealed class CoCoStateGraphHostDebuggerView
    {
        private const int MaximumVisibleTraceEntries = 128;

        private static readonly string[] TraceFilterLabels =
        {
            "All",
            "State ID",
            "Transition ID"
        };

        private CoCoStateGraphHostDebugSnapshot _snapshot;
        private CoCoDiagnostic _diagnostic;
        private CoCoStateFlowTraceEntry[] _traceEntries = Array.Empty<CoCoStateFlowTraceEntry>();
        private CoCoStateGraphHostTraceFilterMode _traceFilterMode;
        private string _traceFilterText = string.Empty;
        private EntityId _observedHostId;
        private CoCoGraphInstanceId _observedGraphInstanceId;
        private double _deltaTime = 1d / 60d;
        private Vector2 _snapshotScroll;
        private Vector2 _traceScroll;

        internal void Draw(CoCoStateGraphHost host)
        {
            ObserveIdentity(host);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Runtime Debugger", EditorStyles.boldLabel);
            if (!Application.isPlaying || host == null || !host.HasLiveRuntime)
            {
                EditorGUILayout.HelpBox(
                    "Runtime debugging becomes available for a live Play Mode Host.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Committed Snapshot"))
                {
                    Refresh(host);
                }

                EditorGUILayout.LabelField(
                    $"Live Lifecycle: {host.Lifecycle}",
                    GUILayout.Width(180f));
            }

            if (_diagnostic.IsError)
            {
                EditorGUILayout.HelpBox(_diagnostic.Message, MessageType.Error);
            }

            DrawLifecycleControls(host);
            DrawSnapshot();
            DrawSuspendedStep(host);
            DrawTrace(host);
        }

        private void ObserveIdentity(CoCoStateGraphHost host)
        {
            EntityId hostId = host == null ? default : GetObjectEntityId(host);
            CoCoGraphInstanceId graphInstanceId = host == null
                ? default
                : host.GraphInstanceId;
            if (_observedHostId == hostId &&
                _observedGraphInstanceId == graphInstanceId)
            {
                return;
            }

            _observedHostId = hostId;
            _observedGraphInstanceId = graphInstanceId;
            _snapshot = null;
            _diagnostic = CoCoDiagnostic.None;
            _traceEntries = Array.Empty<CoCoStateFlowTraceEntry>();
            _traceFilterMode = CoCoStateGraphHostTraceFilterMode.All;
            _traceFilterText = string.Empty;
            _snapshotScroll = Vector2.zero;
            _traceScroll = Vector2.zero;
        }

        private void DrawLifecycleControls(CoCoStateGraphHost host)
        {
            if (host.Lifecycle != CoCoRuntimeLifecycleState.Running)
            {
                return;
            }

            using (new EditorGUI.DisabledScope(host.Fault.IsFaulted))
            {
                if (GUILayout.Button("Suspend Runtime"))
                {
                    host.TrySuspend(out _diagnostic);
                    Refresh(host);
                }
            }
        }

        private void Refresh(CoCoStateGraphHost host)
        {
            if (host.TryCaptureDebugSnapshot(
                    out CoCoStateGraphHostDebugSnapshot snapshot,
                    out _diagnostic))
            {
                _snapshot = snapshot;
            }
        }

        private void DrawSnapshot()
        {
            if (_snapshot == null)
            {
                return;
            }

            _snapshotScroll = EditorGUILayout.BeginScrollView(
                _snapshotScroll,
                GUILayout.MaxHeight(260f));
            EditorGUILayout.LabelField(
                $"Graph {_snapshot.GraphId}; Instance {_snapshot.GraphInstanceId}; Schema {_snapshot.SchemaVersion}");
            EditorGUILayout.LabelField(
                $"Content Fingerprint {_snapshot.ContentFingerprint}; Catalog Fingerprint {_snapshot.CatalogFingerprint}");
            EditorGUILayout.LabelField(
                $"Timeline {_snapshot.TimelineId}; Clock Domain {_snapshot.ClockDomainId}; Epoch {_snapshot.TimelineEpoch}");
            EditorGUILayout.LabelField(
                $"Tick {_snapshot.Tick}; Sequence {_snapshot.ExecutionSequence}; Seconds {_snapshot.Seconds:0.######}");
            EditorGUILayout.LabelField($"Snapshot Lifecycle {_snapshot.Lifecycle}");
            EditorGUILayout.LabelField(
                $"Context Revision {_snapshot.ContextRevision}; Origin {_snapshot.ContextOrigin.Kind}; Claims {_snapshot.ClaimCount}");
            CoCoStateFlowFrameHeader contextHeader = _snapshot.ContextHeader;
            EditorGUILayout.LabelField(
                contextHeader.IsValid
                    ? $"Context Header {contextHeader.Identity.GraphInstanceId}; Kind {contextHeader.Identity.Kind}; Tick {contextHeader.Identity.Tick}; Sequence {contextHeader.Identity.ExecutionSequence}; Layout {contextHeader.LayoutId}"
                    : "Context Header <none>");
            EditorGUILayout.LabelField(
                $"Fault {_snapshot.Fault.IsFaulted}; World Correction {_snapshot.RequiresWorldCorrection}");
            EditorGUILayout.LabelField(
                $"Last Diagnostic {_snapshot.LastDiagnostic.Domain}/{_snapshot.LastDiagnostic.Code}: {_snapshot.LastDiagnostic.Message}");
            for (int layerIndex = 0; layerIndex < _snapshot.LayerCount; layerIndex++)
            {
                CoCoStateGraphHostDebugLayer layer = _snapshot.GetLayer(layerIndex);
                EditorGUILayout.LabelField(
                    $"Layer {layer.LayerId}; Winner {layer.WinningTransitionId}",
                    EditorStyles.miniBoldLabel);
                for (int stateIndex = 0; stateIndex < layer.ActiveStateCount; stateIndex++)
                {
                    CoCoStateGraphHostDebugActiveState state = layer.GetActiveState(stateIndex);
                    EditorGUILayout.LabelField(
                        $"  {state.StateId}; Activation {state.ActivationId}; Local {state.LocalSeconds:0.######}; Progress {state.ActionProgress:0.######}");
                }
            }

            for (int claimIndex = 0; claimIndex < _snapshot.ClaimCount; claimIndex++)
            {
                CoCoOperatorClaimState claim = _snapshot.GetClaim(claimIndex);
                EditorGUILayout.LabelField(
                    $"Claim {claim.ClaimId}; Section {claim.SectionId}; Held {claim.IsHeld}; Owner {claim.OwnerOperatorId}");
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawSuspendedStep(CoCoStateGraphHost host)
        {
            if (host.Lifecycle != CoCoRuntimeLifecycleState.Suspended)
            {
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Suspended One-Tick Step", EditorStyles.miniBoldLabel);
            _deltaTime = EditorGUILayout.DoubleField("Delta Time", _deltaTime);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Resume Runtime"))
                {
                    host.TryResume(out _diagnostic);
                    Refresh(host);
                }

                using (new EditorGUI.DisabledScope(
                           _deltaTime <= 0d ||
                           double.IsNaN(_deltaTime) ||
                           double.IsInfinity(_deltaTime)))
                {
                    if (GUILayout.Button("Run One Normal Tick"))
                    {
                        host.TryDebugStepWhileSuspended(_deltaTime, out _diagnostic);
                        Refresh(host);
                    }
                }
            }
        }

        private void DrawTrace(CoCoStateGraphHost host)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Trace", EditorStyles.miniBoldLabel);
            ICoCoStateFlowTrace trace = host.Trace;
            if (trace == null)
            {
                EditorGUILayout.HelpBox(
                    "Trace Capacity is 0. Stop the Host, set a positive capacity, and restart to record history.",
                    MessageType.Info);
                return;
            }

            _traceFilterMode = (CoCoStateGraphHostTraceFilterMode)GUILayout.Toolbar(
                (int)_traceFilterMode,
                TraceFilterLabels);
            if (_traceFilterMode != CoCoStateGraphHostTraceFilterMode.All)
            {
                _traceFilterText = EditorGUILayout.TextField("ID", _traceFilterText);
            }

            int capacity = Math.Min(trace.Count, MaximumVisibleTraceEntries);
            if (_traceEntries.Length != capacity)
            {
                _traceEntries = capacity == 0
                    ? Array.Empty<CoCoStateFlowTraceEntry>()
                    : new CoCoStateFlowTraceEntry[capacity];
            }

            int count = 0;
            if (TryBuildTraceFilter(
                    _traceFilterMode,
                    _traceFilterText,
                    out CoCoStateFlowTraceFilter filter,
                    out string validationMessage))
            {
                count = trace.CopyLatestTo(_traceEntries, filter);
            }
            else
            {
                EditorGUILayout.HelpBox(validationMessage, MessageType.Warning);
            }

            EditorGUILayout.LabelField(
                $"Count {trace.Count}; Capacity {trace.Capacity}; Total Written {trace.TotalWritten}; Visible {count}");
            _traceScroll = EditorGUILayout.BeginScrollView(
                _traceScroll,
                GUILayout.MaxHeight(220f));
            DrawTraceGroup(
                "Transition",
                count,
                CoCoStateFlowTraceKind.Transition | CoCoStateFlowTraceKind.ActivePath);
            DrawTraceGroup(
                "Operation",
                count,
                CoCoStateFlowTraceKind.OperationSection | CoCoStateFlowTraceKind.OperatorOutcome);
            DrawTraceGroup(
                "Context Commit",
                count,
                CoCoStateFlowTraceKind.Tick |
                CoCoStateFlowTraceKind.ContextCommit |
                CoCoStateFlowTraceKind.Diagnostic |
                CoCoStateFlowTraceKind.Cancelled);
            DrawTraceGroup(
                "Event",
                count,
                CoCoStateFlowTraceKind.EventSequence | CoCoStateFlowTraceKind.EventPublished);

            EditorGUILayout.EndScrollView();
        }

        internal static bool TryBuildTraceFilter(
            CoCoStateGraphHostTraceFilterMode mode,
            string text,
            out CoCoStateFlowTraceFilter filter,
            out string validationMessage)
        {
            switch (mode)
            {
                case CoCoStateGraphHostTraceFilterMode.All:
                    filter = CoCoStateFlowTraceFilter.All;
                    validationMessage = string.Empty;
                    return true;
                case CoCoStateGraphHostTraceFilterMode.StateId:
                    if (CoCoStateId.TryParse(
                            string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim(),
                            out CoCoStateId stateId))
                    {
                        filter = new CoCoStateFlowTraceFilter(
                            CoCoStateFlowTraceKind.All,
                            stateId: stateId);
                        validationMessage = string.Empty;
                        return true;
                    }

                    filter = default;
                    validationMessage = "State ID must be one non-zero 32-digit hexadecimal identity.";
                    return false;
                case CoCoStateGraphHostTraceFilterMode.TransitionId:
                    if (CoCoTransitionId.TryParse(
                            string.IsNullOrWhiteSpace(text) ? string.Empty : text.Trim(),
                            out CoCoTransitionId transitionId))
                    {
                        filter = new CoCoStateFlowTraceFilter(
                            CoCoStateFlowTraceKind.All,
                            transitionId: transitionId);
                        validationMessage = string.Empty;
                        return true;
                    }

                    filter = default;
                    validationMessage = "Transition ID must be one non-zero 32-digit hexadecimal identity.";
                    return false;
                default:
                    filter = default;
                    validationMessage = "Trace filter mode is invalid.";
                    return false;
            }
        }

        private void DrawTraceGroup(
            string label,
            int count,
            CoCoStateFlowTraceKind kinds)
        {
            bool drewHeader = false;
            for (int index = 0; index < count; index++)
            {
                CoCoStateFlowTraceEntry entry = _traceEntries[index];
                if ((entry.Kind & kinds) == 0)
                {
                    continue;
                }

                if (!drewHeader)
                {
                    EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
                    drewHeader = true;
                }

                DrawTraceEntry(entry);
            }
        }

        private static void DrawTraceEntry(CoCoStateFlowTraceEntry entry)
        {
            string prefix = $"  {entry.Kind} | Tick {entry.TickFrame.Tick}";
            switch (entry.Kind)
            {
                case CoCoStateFlowTraceKind.Transition:
                    EditorGUILayout.LabelField(
                        $"{prefix} | Layer {entry.LayerId} | Transition {entry.TransitionId} | Role {entry.TransitionRole}");
                    break;
                case CoCoStateFlowTraceKind.ActivePath:
                    EditorGUILayout.LabelField(
                        $"{prefix} | Layer {entry.LayerId} | State {entry.StateId}");
                    break;
                case CoCoStateFlowTraceKind.OperationSection:
                    EditorGUILayout.LabelField(
                        $"{prefix} | Section {entry.SectionId}");
                    break;
                case CoCoStateFlowTraceKind.OperatorOutcome:
                    EditorGUILayout.LabelField(
                        $"{prefix} | Operator {entry.OperatorId} | Outcome {entry.OperatorOutcome}");
                    break;
                case CoCoStateFlowTraceKind.ContextCommit:
                    EditorGUILayout.LabelField(
                        $"{prefix} | Revision {entry.PreviousRevision} -> {entry.NewRevision}");
                    break;
                case CoCoStateFlowTraceKind.EventSequence:
                case CoCoStateFlowTraceKind.EventPublished:
                    EditorGUILayout.LabelField(
                        $"{prefix} | Event Sequence {entry.FirstEventSequence} -> {entry.LastEventSequence}");
                    break;
                case CoCoStateFlowTraceKind.Diagnostic:
                case CoCoStateFlowTraceKind.Cancelled:
                    EditorGUILayout.LabelField(
                        $"{prefix} | Diagnostic {entry.DiagnosticDomain}/{entry.DiagnosticCode}");
                    break;
                default:
                    EditorGUILayout.LabelField(prefix);
                    break;
            }
        }

        private static EntityId GetObjectEntityId(UnityEngine.Object value)
        {
#if UNITY_6000_5_OR_NEWER
            return value.GetEntityId();
#else
            return value.GetInstanceID();
#endif
        }
    }
}
