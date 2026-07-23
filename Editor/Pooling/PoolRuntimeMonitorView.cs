using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Pooling;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Pooling
{
    /// <summary>
    /// Read-only projection of one immutable PoolRuntimeSnapshot plus an explicit
    /// manual idle-clear command.
    /// </summary>
    internal sealed class PoolRuntimeMonitorView
    {
        private Vector2 scopeScroll;
        private Vector2 diagnosticScroll;
        private long expandedDiagnosticSequence;
        private CoCoDiagnostic lastActionDiagnostic;

        internal void Draw(PoolRuntime runtime)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pool Runtime Snapshot", EditorStyles.boldLabel);
            if (runtime == null)
            {
                EditorGUILayout.HelpBox(
                    "No initialized Pool Runtime is available on this Host.",
                    MessageType.Info);
                return;
            }

            PoolRuntimeSnapshot snapshot;
            try
            {
                snapshot = runtime.CaptureSnapshot();
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(
                    "The read-only Pool snapshot could not be captured: " +
                    exception.Message,
                    MessageType.Error);
                return;
            }

            DrawSummary(runtime, snapshot);
            DrawDiagnostic(lastActionDiagnostic);
            DrawScopes(runtime, snapshot);
            DrawDiagnostics(snapshot);
        }

        private static void DrawSummary(
            PoolRuntime runtime,
            PoolRuntimeSnapshot snapshot)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    snapshot.IsDisposed
                        ? "Lifecycle: Disposed"
                        : snapshot.IsShuttingDown
                            ? "Lifecycle: Shutting Down"
                            : "Lifecycle: Live");
                EditorGUILayout.LabelField(
                    "Scopes: " + snapshot.Scopes.Count +
                    "    Ledger Records: " + snapshot.Diagnostics.Count);
                EditorGUILayout.LabelField(
                    "Rental Stack Capture: " +
                    (runtime.CaptureRentalStacks ? "Enabled" : "Disabled"));
                EditorGUILayout.LabelField(
                    "Capacity Policy",
                    "Max Retained limits idle instances only; no active cap or auto-trim.");
            }
        }

        private void DrawScopes(
            PoolRuntime runtime,
            PoolRuntimeSnapshot snapshot)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Owned Scopes (" + snapshot.Scopes.Count + ")",
                EditorStyles.miniBoldLabel);
            if (snapshot.Scopes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "The runtime currently owns no Pool Scopes.",
                    MessageType.Info);
                return;
            }

            scopeScroll = EditorGUILayout.BeginScrollView(
                scopeScroll,
                GUILayout.MinHeight(130f),
                GUILayout.MaxHeight(360f));
            for (int scopeIndex = 0; scopeIndex < snapshot.Scopes.Count; scopeIndex++)
            {
                DrawScope(runtime, snapshot.Scopes[scopeIndex]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawScope(PoolRuntime runtime, PoolScopeSnapshot scope)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Scope " + scope.ScopeSequence + "  ·  " +
                    EmptyAsPlaceholder(scope.OwnerId.Value) + "  ·  " + scope.State,
                    EditorStyles.miniBoldLabel);
                if (scope.Entries.Count == 0)
                {
                    EditorGUILayout.LabelField("No prepared Pool entries.");
                    return;
                }

                for (int entryIndex = 0; entryIndex < scope.Entries.Count; entryIndex++)
                {
                    DrawEntry(runtime, scope, scope.Entries[entryIndex]);
                }
            }
        }

        private void DrawEntry(
            PoolRuntime runtime,
            PoolScopeSnapshot scope,
            PoolEntrySnapshot entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(
                        entry.PoolId.Value + "  ·  " + entry.State,
                        EditorStyles.miniBoldLabel);
                    using (new EditorGUI.DisabledScope(
                               entry.InactiveCount == 0 ||
                               scope.State != PoolScopeState.Open ||
                               (entry.State != PoolEntryState.Ready &&
                                entry.State != PoolEntryState.Prewarming)))
                    {
                        if (GUILayout.Button(
                                "Clear Inactive",
                                GUILayout.Width(105f)))
                        {
                            TryClearInactive(runtime, scope, entry);
                        }
                    }
                }

                EditorGUILayout.LabelField(
                    "Source",
                    EmptyAsPlaceholder(entry.ContentId.Value) +
                    (entry.HoldsSourceLease ? "  ·  lease held" : "  ·  no lease"));
                EditorGUILayout.LabelField(
                    "Instances",
                    "active " + entry.ActiveCount +
                    "    idle " + entry.InactiveCount +
                    "    temporal retained " + entry.TemporalRetainedCount +
                    "    quarantine " + entry.QuarantineCount +
                    "    destroy pending " + entry.PendingDestroyCount);
                EditorGUILayout.LabelField(
                    "Retention",
                    "prewarm " + entry.PrewarmCount +
                    "    max idle " + entry.MaxRetained);
                EditorGUILayout.LabelField(
                    "Traffic",
                    "rents " + entry.RentCount +
                    "    idle hits " + entry.IdleHitCount +
                    "    create misses " + entry.CreateMissCount +
                    "    hit rate " + (entry.HitRate * 100f).ToString("0.0") + "%");
                EditorGUILayout.LabelField(
                    "Lifecycle",
                    "created " + entry.CreatedCount +
                    "    destroyed " + entry.DestroyedCount +
                    "    reset failures " + entry.ResetFailureCount +
                    "    external destroys " + entry.ExternalDestroyCount);
                DrawDiagnostic(entry.Diagnostic);
            }
        }

        private void TryClearInactive(
            PoolRuntime runtime,
            PoolScopeSnapshot scope,
            PoolEntrySnapshot entry)
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Clear inactive pooled instances?",
                "Destroy " + entry.InactiveCount + " inactive instance(s) from Pool '" +
                entry.PoolId.Value + "' in Scope " + scope.ScopeSequence +
                ". Active and Temporal-retained instances are not touched. " +
                "The pool remains Ready and does not auto-prewarm.",
                "Clear Inactive",
                "Cancel");
            if (!confirmed)
            {
                return;
            }

            if (runtime.TryClearInactive(
                    scope.ScopeSequence,
                    entry.PoolId,
                    out CoCoDiagnostic diagnostic))
            {
                lastActionDiagnostic = CoCoDiagnostic.None;
            }
            else
            {
                lastActionDiagnostic = diagnostic;
            }
        }

        private void DrawDiagnostics(PoolRuntimeSnapshot snapshot)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Bounded Diagnostic Ledger (" + snapshot.Diagnostics.Count + ")",
                EditorStyles.miniBoldLabel);
            if (snapshot.Diagnostics.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Pool lifecycle events have been recorded.",
                    MessageType.Info);
                return;
            }

            diagnosticScroll = EditorGUILayout.BeginScrollView(
                diagnosticScroll,
                GUILayout.MinHeight(130f),
                GUILayout.MaxHeight(360f));
            for (int index = snapshot.Diagnostics.Count - 1; index >= 0; index--)
            {
                DrawDiagnosticRecord(snapshot.Diagnostics[index]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawDiagnosticRecord(PoolDiagnosticRecord record)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool hasDetails = !record.Diagnostic.IsNone ||
                                  !string.IsNullOrEmpty(record.AllocationStack) ||
                                  !string.IsNullOrEmpty(record.ReleaseStack);
                string heading = "#" + record.Sequence + "  " +
                                 record.TimestampUtc.ToString("HH:mm:ss.fff") +
                                 " UTC  ·  " + record.EventKind;
                if (hasDetails)
                {
                    bool expanded = expandedDiagnosticSequence == record.Sequence;
                    bool nextExpanded = EditorGUILayout.Foldout(expanded, heading, true);
                    expandedDiagnosticSequence = nextExpanded
                        ? record.Sequence
                        : expanded
                            ? 0
                            : expandedDiagnosticSequence;
                }
                else
                {
                    EditorGUILayout.LabelField(heading, EditorStyles.miniBoldLabel);
                }

                EditorGUILayout.LabelField(
                    "Authority",
                    "scope " + record.ScopeSequence +
                    "    pool " + EmptyAsPlaceholder(record.PoolId.Value) +
                    "    instance " + record.InstanceSequence +
                    "    generation " + record.Generation);
                EditorGUILayout.LabelField(
                    "State",
                    record.InstanceState +
                    "    active " + record.ActiveCount +
                    "    idle " + record.InactiveCount +
                    "    quarantine " + record.QuarantineCount);

                if (expandedDiagnosticSequence != record.Sequence)
                {
                    return;
                }

                DrawDiagnostic(record.Diagnostic);
                DrawStack("Allocation Stack", record.AllocationStack);
                DrawStack("Release Stack", record.ReleaseStack);
            }
        }

        private static void DrawDiagnostic(CoCoDiagnostic diagnostic)
        {
            if (diagnostic.IsNone)
            {
                return;
            }

            MessageType messageType = diagnostic.IsError
                ? MessageType.Error
                : diagnostic.IsWarning
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(
                diagnostic.Domain + "/" + diagnostic.Code + ": " + diagnostic.Message,
                messageType);
        }

        private static void DrawStack(string label, string stack)
        {
            if (string.IsNullOrEmpty(stack))
            {
                return;
            }

            EditorGUILayout.LabelField(label, EditorStyles.miniBoldLabel);
            float height = Mathf.Clamp(
                EditorStyles.textArea.CalcHeight(new GUIContent(stack), 440f),
                42f,
                140f);
            EditorGUILayout.SelectableLabel(
                stack,
                EditorStyles.textArea,
                GUILayout.MinHeight(height),
                GUILayout.MaxHeight(height));
        }

        private static string EmptyAsPlaceholder(string value) =>
            string.IsNullOrEmpty(value) ? "<none>" : value;
    }
}
