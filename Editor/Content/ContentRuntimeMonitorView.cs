using System;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Content
{
    /// <summary>
    /// Read-only projection of one immutable ContentRuntimeSnapshot.
    /// </summary>
    internal sealed class ContentRuntimeMonitorView
    {
        private Vector2 entryScroll;
        private Vector2 diagnosticScroll;
        private long expandedDiagnosticSequence;

        internal void Draw(ContentRuntime runtime)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Content Runtime Snapshot", EditorStyles.boldLabel);
            if (runtime == null)
            {
                EditorGUILayout.HelpBox(
                    "No initialized Content Runtime is available on this Host.",
                    MessageType.Info);
                return;
            }

            ContentRuntimeSnapshot snapshot;
            try
            {
                snapshot = runtime.CaptureSnapshot();
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(
                    "The read-only Content snapshot could not be captured: " +
                    exception.Message,
                    MessageType.Error);
                return;
            }

            DrawSummary(runtime, snapshot);
            DrawEntries(snapshot);
            DrawDiagnostics(snapshot);
        }

        private static void DrawSummary(
            ContentRuntime runtime,
            ContentRuntimeSnapshot snapshot)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    snapshot.IsShuttingDown ? "Lifecycle: Shutting Down" : "Lifecycle: Live");
                EditorGUILayout.LabelField(
                    "Entries: " + snapshot.Entries.Count +
                    "    Ledger Records: " + snapshot.Diagnostics.Count);
                EditorGUILayout.LabelField(
                    "Lease Stack Capture: " +
                    (runtime.CaptureLeaseStacks ? "Enabled" : "Disabled"));
            }
        }

        private void DrawEntries(ContentRuntimeSnapshot snapshot)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Owned Entries (" + snapshot.Entries.Count + ")",
                EditorStyles.miniBoldLabel);
            if (snapshot.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "The runtime currently owns no loading, loaded, releasing, or tombstoned entries.",
                    MessageType.Info);
                return;
            }

            entryScroll = EditorGUILayout.BeginScrollView(
                entryScroll,
                GUILayout.MinHeight(90f),
                GUILayout.MaxHeight(260f));
            for (int index = 0; index < snapshot.Entries.Count; index++)
            {
                DrawEntry(snapshot.Entries[index]);
            }

            EditorGUILayout.EndScrollView();
        }

        private static void DrawEntry(ContentEntrySnapshot entry)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    entry.ContentId.Value + "  ·  " + entry.Kind + "  ·  " + entry.State,
                    EditorStyles.miniBoldLabel);
                string expectedType = entry.ExpectedType == null
                    ? "<unknown>"
                    : entry.ExpectedType.FullName;
                EditorGUILayout.LabelField("Value Type", expectedType);
                EditorGUILayout.LabelField(
                    "Backend",
                    entry.BackendId.Value + "  generation " + entry.BackendGeneration);
                EditorGUILayout.LabelField(
                    "Resource",
                    "generation " + entry.ResourceGeneration +
                    "    waiters " + entry.WaiterCount +
                    "    leases " + entry.LeaseCount);
                DrawDiagnostic(entry.Diagnostic);
            }
        }

        private void DrawDiagnostics(ContentRuntimeSnapshot snapshot)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Bounded Diagnostic Ledger (" + snapshot.Diagnostics.Count + ")",
                EditorStyles.miniBoldLabel);
            if (snapshot.Diagnostics.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Content lifecycle events have been recorded.",
                    MessageType.Info);
                return;
            }

            diagnosticScroll = EditorGUILayout.BeginScrollView(
                diagnosticScroll,
                GUILayout.MinHeight(120f),
                GUILayout.MaxHeight(340f));
            for (int index = snapshot.Diagnostics.Count - 1; index >= 0; index--)
            {
                DrawDiagnosticRecord(snapshot.Diagnostics[index]);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawDiagnosticRecord(ContentDiagnosticRecord record)
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool hasDetails = !record.Diagnostic.IsNone ||
                                  !string.IsNullOrEmpty(record.AllocationStack) ||
                                  !string.IsNullOrEmpty(record.ReleaseStack);
                string heading = "#" + record.Sequence + "  " +
                                 record.TimestampUtc.ToString("HH:mm:ss.fff") + " UTC  ·  " +
                                 record.EventKind;
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
                    "Content",
                    EmptyAsPlaceholder(record.ContentId.Value) +
                    "    owner " + EmptyAsPlaceholder(record.OwnerId.Value));
                EditorGUILayout.LabelField(
                    "Backend",
                    EmptyAsPlaceholder(record.BackendId.Value) +
                    "  b" + record.BackendGeneration +
                    "  resource " + record.ResourceGeneration);
                EditorGUILayout.LabelField(
                    "Sequences",
                    "scope " + record.ScopeSequence +
                    "    request " + record.RequestSequence +
                    "    lease " + record.LeaseSequence);

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
                EditorStyles.textArea.CalcHeight(new GUIContent(stack), 400f),
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
