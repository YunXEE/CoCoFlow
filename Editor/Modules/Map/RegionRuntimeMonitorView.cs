using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Map
{
    internal sealed class RegionRuntimeMonitorView
    {
        private readonly HashSet<string> expandedRegions =
            new HashSet<string>(StringComparer.Ordinal);
        private Vector2 scroll;

        internal void Draw(CoCoMapHost host)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Map Region Runtime Snapshot",
                EditorStyles.boldLabel);
            if (host == null ||
                !host.IsInitialized ||
                host.Runtime == null)
            {
                EditorGUILayout.HelpBox(
                    "No initialized Map Region Runtime is available on this Host.",
                    MessageType.Info);
                return;
            }

            RegionRuntimeSnapshot snapshot;
            try
            {
                snapshot = host.CaptureSnapshot();
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(
                    "The immutable Map snapshot could not be captured: " +
                    exception.Message,
                    MessageType.Error);
                return;
            }

            DrawSummary(snapshot);
            scroll = EditorGUILayout.BeginScrollView(
                scroll,
                GUILayout.MinHeight(180f));
            DrawDemands(snapshot);
            DrawRegions(snapshot);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawSummary(
            RegionRuntimeSnapshot snapshot)
        {
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                string lifecycle = snapshot.IsDisposed
                    ? "Disposed"
                    : snapshot.IsShuttingDown
                        ? "Shutting Down"
                        : "Live";
                EditorGUILayout.LabelField(
                    "Lifecycle",
                    lifecycle);
                EditorGUILayout.LabelField(
                    "Demands / Regions",
                    snapshot.Demands.Count + " / " +
                    snapshot.Regions.Count);
                DrawDiagnostic(
                    "Runtime",
                    snapshot.LastDiagnostic);
            }
        }

        private static void DrawDemands(
            RegionRuntimeSnapshot snapshot)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Demand Sources (" +
                snapshot.Demands.Count + ")",
                EditorStyles.miniBoldLabel);
            if (snapshot.Demands.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "There are no live Region Demand Leases.",
                    MessageType.Info);
                return;
            }

            for (int index = 0;
                 index < snapshot.Demands.Count;
                 index++)
            {
                RegionDemandRuntimeSnapshot demand =
                    snapshot.Demands[index];
                using (new EditorGUILayout.VerticalScope(
                           EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField(
                        demand.OwnerId.Value +
                        " → " + demand.RegionId.Value,
                        EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(
                        "Scope / Lease / Revision",
                        demand.ScopeSequence + " / " +
                        demand.LeaseSequence + " / " +
                        demand.Revision.Value);
                    EditorGUILayout.LabelField(
                        "Capability",
                        FormatCapabilities(
                            demand.Capabilities));
                    EditorGUILayout.LabelField(
                        "Coverage",
                        FormatCoverage(
                            demand.Coverage));
                    EditorGUILayout.LabelField(
                        "Readiness",
                        demand.Readiness.HasValue
                            ? demand.Readiness.Value.ToString()
                            : "Pending");
                    DrawDiagnostic(
                        "Demand",
                        demand.Diagnostic);
                }
            }
        }

        private void DrawRegions(
            RegionRuntimeSnapshot snapshot)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Resolved Regions (" +
                snapshot.Regions.Count + ")",
                EditorStyles.miniBoldLabel);
            if (snapshot.Regions.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No Region state is retained.",
                    MessageType.Info);
                return;
            }

            for (int index = 0;
                 index < snapshot.Regions.Count;
                 index++)
            {
                RegionRuntimeRegionSnapshot region =
                    snapshot.Regions[index];
                DrawRegion(region);
            }
        }

        private void DrawRegion(
            RegionRuntimeRegionSnapshot region)
        {
            string key = region.RegionId.Value;
            bool expanded =
                expandedRegions.Contains(key);
            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                bool nextExpanded =
                    EditorGUILayout.Foldout(
                        expanded,
                        RegionHeading(region),
                        true);
                if (nextExpanded)
                {
                    expandedRegions.Add(key);
                }
                else
                {
                    expandedRegions.Remove(key);
                }

                if (!nextExpanded) return;

                EditorGUILayout.LabelField(
                    "Generation",
                    "desired " +
                    region.DesiredGeneration +
                    " · committed " +
                    region.CommittedGeneration);
                EditorGUILayout.LabelField(
                    "Desired",
                    FormatCapabilities(
                        region.DesiredCapabilities) +
                    " · " +
                    FormatCoverage(
                        region.DesiredCoverage));
                EditorGUILayout.LabelField(
                    "Committed",
                    FormatCapabilities(
                        region.CommittedCapabilities) +
                    " · " +
                    FormatCoverage(
                        region.CommittedCoverage));
                EditorGUILayout.LabelField(
                    "Plan Diff",
                    "reused " +
                    region.ReusedNodeCount +
                    " · candidate " +
                    region.CandidateNodeCount);
                EditorGUILayout.LabelField(
                    "State Flags",
                    "degraded " +
                    YesNo(region.OptionalDegraded) +
                    " · fault " +
                    YesNo(region.Faulted) +
                    " · blocked cleanup " +
                    YesNo(region.BlockedCleanup));
                DrawDiagnostic(
                    "Region",
                    region.Diagnostic);

                if (region.Chunks.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        "Per-Chunk Capability",
                        "<none>");
                    return;
                }

                EditorGUILayout.LabelField(
                    "Per-Chunk Capability",
                    EditorStyles.miniBoldLabel);
                for (int index = 0;
                     index < region.Chunks.Count;
                     index++)
                {
                    RegionChunkRuntimeSnapshot chunk =
                        region.Chunks[index];
                    EditorGUILayout.LabelField(
                        "  " + chunk.ChunkId.Value,
                        "desired " +
                        FormatCapabilities(
                            chunk.DesiredCapabilities) +
                        " · committed " +
                        FormatCapabilities(
                            chunk.CommittedCapabilities),
                        EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        private static string RegionHeading(
            RegionRuntimeRegionSnapshot region)
        {
            string state = region.Faulted
                ? "Faulted"
                : region.BlockedCleanup
                    ? "BlockedCleanup"
                    : region.HasInFlightTransition
                        ? "Transitioning"
                        : region.OptionalDegraded
                            ? "OptionalDegraded"
                            : "Ready";
            return region.RegionId.Value +
                   "  ·  " + state;
        }

        private static string FormatCapabilities(
            RegionCapabilitySet capabilities)
        {
            if (capabilities == null ||
                capabilities.Count == 0)
            {
                return "∅";
            }

            var values =
                new string[capabilities.Count];
            for (int index = 0;
                 index < capabilities.Count;
                 index++)
            {
                values[index] =
                    capabilities.Capabilities[index].Value;
            }

            return string.Join(", ", values);
        }

        private static string FormatCoverage(
            RegionCoverage coverage)
        {
            if (!coverage.IsValid) return "<none>";
            if (coverage.CoversAll) return "All";

            var values =
                new string[coverage.Chunks.Count];
            for (int index = 0;
                 index < coverage.Chunks.Count;
                 index++)
            {
                values[index] =
                    coverage.Chunks[index].Value;
            }

            return "Chunks[" +
                   string.Join(", ", values) +
                   "]";
        }

        private static string YesNo(bool value) =>
            value ? "yes" : "no";

        private static void DrawDiagnostic(
            string label,
            CoCoDiagnostic diagnostic)
        {
            if (diagnostic.IsNone) return;
            MessageType type = diagnostic.IsError
                ? MessageType.Error
                : diagnostic.IsWarning
                    ? MessageType.Warning
                    : MessageType.Info;
            EditorGUILayout.HelpBox(
                label + ": " +
                diagnostic.Domain + "/" +
                diagnostic.Code + ": " +
                diagnostic.Message,
                type);
        }
    }
}
