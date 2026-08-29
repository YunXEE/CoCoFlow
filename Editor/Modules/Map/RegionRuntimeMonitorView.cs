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

            RegionMapMonitorSnapshot monitor;
            try
            {
                monitor = host.CaptureMonitorSnapshot();
            }
            catch (Exception exception)
            {
                EditorGUILayout.HelpBox(
                    "The immutable Map snapshot could not be captured: " +
                    exception.Message,
                    MessageType.Error);
                return;
            }

            RegionRuntimeSnapshot snapshot = monitor.Runtime;
            DrawSummary(monitor);
            scroll = EditorGUILayout.BeginScrollView(
                scroll,
                GUILayout.MinHeight(180f));
            DrawDemands(snapshot);
            DrawTemporalRetention(monitor);
            DrawRegions(monitor);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawSummary(
            RegionMapMonitorSnapshot monitor)
        {
            RegionRuntimeSnapshot snapshot = monitor.Runtime;
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
                EditorGUILayout.LabelField(
                    "Temporal Barrier",
                    (monitor.TemporalDispatchDeferred
                        ? "deferred"
                        : "open") +
                    " · dirty Regions " +
                    monitor.DeferredTransitionCount +
                    " · retention Leases " +
                    monitor.TemporalRetentionDemands.Count);
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

        private static void DrawTemporalRetention(
            RegionMapMonitorSnapshot monitor)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Temporal Retention (" +
                monitor.TemporalRetentionDemands.Count +
                ")",
                EditorStyles.miniBoldLabel);
            if (monitor.TemporalRetentionDemands.Count == 0)
            {
                EditorGUILayout.LabelField(
                    "No live Map Temporal retention.",
                    EditorStyles.miniLabel);
                return;
            }

            for (int index = 0;
                 index <
                 monitor.TemporalRetentionDemands.Count;
                 index++)
            {
                RegionDemandRuntimeSnapshot demand =
                    monitor.TemporalRetentionDemands[index];
                EditorGUILayout.LabelField(
                    "  " + demand.RegionId.Value,
                    "lease " +
                    demand.LeaseSequence +
                    " · revision " +
                    demand.Revision.Value +
                    " · " +
                    FormatCapabilities(
                        demand.Capabilities) +
                    " · " +
                    FormatCoverage(demand.Coverage),
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawRegions(
            RegionMapMonitorSnapshot monitor)
        {
            RegionRuntimeSnapshot snapshot = monitor.Runtime;
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
                RegionRuntimeRegionState region =
                    snapshot.Regions[index];
                DrawRegion(
                    region,
                    FindTransitionRegion(
                        monitor,
                        region.RegionId));
            }
        }

        private void DrawRegion(
            RegionRuntimeRegionState region,
            RegionTransitionMonitorRegionSnapshot transition)
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
                    "Desired Requirement",
                    FormatCapabilities(
                        region.DesiredCapabilities) +
                    " · " +
                    FormatCoverage(
                        region.DesiredCoverage));
                EditorGUILayout.LabelField(
                    "Committed Requirement",
                    FormatCapabilities(
                        region.CommittedCapabilities) +
                    " · " +
                    FormatCoverage(
                        region.CommittedCoverage));
                EditorGUILayout.LabelField(
                    "Resolved Tier",
                    "desired " +
                    FormatTier(region.DesiredTierId) +
                    " · committed " +
                    FormatTier(region.CommittedTierId));
                EditorGUILayout.LabelField(
                    "Effective Capability",
                    "desired " +
                    FormatCapabilities(
                        region.DesiredEffectiveCapabilities) +
                    " · committed " +
                    FormatCapabilities(
                        region.CommittedEffectiveCapabilities));
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

                if (region.Chunks.Count != 0)
                {
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
                            "requirement desired " +
                            FormatCapabilities(
                                chunk.DesiredCapabilities) +
                            " · committed " +
                            FormatCapabilities(chunk.CommittedCapabilities) +
                            "\ntier desired " +
                            FormatTier(chunk.DesiredTierId) +
                            " · committed " +
                            FormatTier(chunk.CommittedTierId) +
                            "\neffective desired " +
                            FormatCapabilities(
                                chunk.DesiredEffectiveCapabilities) +
                            " · committed " +
                            FormatCapabilities(
                                chunk.CommittedEffectiveCapabilities),
                            EditorStyles.wordWrappedMiniLabel);
                    }
                }

                if (transition != null)
                {
                    EditorGUILayout.LabelField(
                        "Old + Candidate Peak",
                        "generation " +
                        transition.PeakGeneration +
                        " · old " +
                        transition.OldNodeCountAtAttemptStart +
                        " · peak " +
                        transition.OldPlusCandidatePeak);
                    DrawParticipants(transition);
                    DrawDependencies(transition);
                }
            }
        }

        private static void DrawParticipants(
            RegionTransitionMonitorRegionSnapshot region)
        {
            EditorGUILayout.LabelField(
                "Participants (" +
                region.Participants.Count +
                ")",
                EditorStyles.miniBoldLabel);
            for (int index = 0;
                 index < region.Participants.Count;
                 index++)
            {
                RegionParticipantMonitorSnapshot participant =
                    region.Participants[index];
                string content = participant.ContentId.IsValid
                    ? "\ncontent " +
                      participant.ContentId.Value +
                      " · scope/lease " +
                      participant.ContentScopeSequence +
                      "/" +
                      participant.ContentLeaseSequence
                    : string.Empty;
                string cleanup =
                    participant.CleanupReason.HasValue
                        ? " · " +
                          participant.CleanupReason.Value
                        : string.Empty;
                EditorGUILayout.LabelField(
                    "  " + FormatNode(participant.NodeId),
                    participant.Role +
                    cleanup +
                    " · ownership " +
                    participant.OwnershipSequence +
                    "\n" +
                    participant.ParticipantTypeId.Value +
                    " · " +
                    participant.Phase +
                    "/" +
                    participant.ExplicitOrder +
                    " · " +
                    participant.Requirement +
                    "\ntier " +
                    FormatTier(participant.TierId) +
                    " · mode " +
                    participant.ModeId.Value +
                    " · " +
                    FormatCapabilities(
                        participant.EffectiveCapabilities) +
                    content,
                    EditorStyles.wordWrappedMiniLabel);
            }
        }

        private static void DrawDependencies(
            RegionTransitionMonitorRegionSnapshot region)
        {
            EditorGUILayout.LabelField(
                "Cross-Region Dependencies (" +
                region.Dependencies.Count +
                ")",
                EditorStyles.miniBoldLabel);
            for (int index = 0;
                 index < region.Dependencies.Count;
                 index++)
            {
                RegionDependencyMonitorSnapshot dependency =
                    region.Dependencies[index];
                EditorGUILayout.LabelField(
                    "  " +
                    dependency.SourceCapability.Value +
                    " → " +
                    dependency.TargetRegionId.Value,
                    dependency.Role +
                    (dependency.IsBlocker
                        ? " · BLOCKER"
                        : string.Empty) +
                    " · rule " +
                    dependency.RuleFingerprint +
                    "\nlease/revision " +
                    dependency.LeaseSequence +
                    "/" +
                    dependency.Revision.Value +
                    " · " +
                    (dependency.Readiness.HasValue
                        ? dependency.Readiness.Value.ToString()
                        : "Pending") +
                    "\n" +
                    FormatCapabilities(
                        dependency.TargetCapabilities) +
                    " · " +
                    FormatCoverage(
                        dependency.TargetCoverage),
                    EditorStyles.wordWrappedMiniLabel);
                DrawDiagnostic(
                    "Dependency",
                    dependency.Diagnostic);
            }
        }

        private static RegionTransitionMonitorRegionSnapshot
            FindTransitionRegion(
                RegionMapMonitorSnapshot monitor,
                RegionId regionId)
        {
            for (int index = 0;
                 index < monitor.TransitionRegions.Count;
                 index++)
            {
                if (monitor.TransitionRegions[index].RegionId ==
                    regionId)
                {
                    return monitor.TransitionRegions[index];
                }
            }

            return null;
        }

        private static string FormatNode(RegionPlanNodeId nodeId) =>
            nodeId.HasChunkId
                ? nodeId.RegionId.Value +
                  " · chunk " +
                  nodeId.ChunkId.Value +
                  " · slot " +
                  nodeId.SlotId.Value
                : nodeId.RegionId.Value +
                  " · global · slot " +
                  nodeId.SlotId.Value;

        private static string RegionHeading(
            RegionRuntimeRegionState region)
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

        private static string FormatTier(RegionTierId tierId) =>
            tierId.IsValid ? tierId.Value : "<unresolved>";

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
