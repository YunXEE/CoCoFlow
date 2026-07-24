using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Map
{
    [CustomEditor(typeof(CoCoRegionProfile))]
    internal sealed class CoCoRegionProfileEditor : UnityEditor.Editor
    {
        private readonly List<ProfileDiagnostic> diagnostics =
            new List<ProfileDiagnostic>();
        private Vector2 matrixScroll;
        private string diagnosticContext = string.Empty;
        private int matchingBindingCount;

        private void OnEnable()
        {
            RefreshDiagnostics();
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                RefreshDiagnostics();
            }

            CoCoRegionProfile profile =
                (CoCoRegionProfile)target;
            DrawManagedReferenceStatus(profile);
            DrawParticipantTierMatrix(profile);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Compile Diagnostics"))
                {
                    RefreshDiagnostics();
                }

                if (GUILayout.Button("Copy Default Template…"))
                {
                    CoCoRegionProfileTemplateWizard.Open();
                }
            }

            DrawDiagnostics();
        }

        private void DrawParticipantTierMatrix(
            CoCoRegionProfile profile)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Participant × Tier Matrix",
                EditorStyles.boldLabel);
            if (profile == null || profile.Tiers.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No fidelity tiers are defined.",
                    MessageType.Warning);
                return;
            }

            if (profile.Participants.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No participants are defined. Tier capabilities remain valid, " +
                    "but the Profile currently drives no Region behavior.",
                    MessageType.Info);
                return;
            }

            matrixScroll = EditorGUILayout.BeginScrollView(
                matrixScroll,
                true,
                false,
                GUILayout.MinHeight(90f),
                GUILayout.MaxHeight(260f));
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label(
                    "Participant",
                    EditorStyles.miniBoldLabel,
                    GUILayout.Width(210f));
                for (int tierIndex = 0;
                     tierIndex < profile.Tiers.Count;
                     tierIndex++)
                {
                    RegionTierDefinition tier =
                        profile.Tiers[tierIndex];
                    GUILayout.Label(
                        tier == null
                            ? "<null>"
                            : tier.Name,
                        EditorStyles.miniBoldLabel,
                        GUILayout.Width(104f));
                }
            }

            for (int participantIndex = 0;
                 participantIndex < profile.Participants.Count;
                 participantIndex++)
            {
                RegionParticipantDefinition participant =
                    profile.Participants[participantIndex];
                using (new EditorGUILayout.HorizontalScope(
                           EditorStyles.helpBox))
                {
                    GUILayout.Label(
                        ParticipantLabel(participant),
                        EditorStyles.miniLabel,
                        GUILayout.Width(210f));
                    for (int tierIndex = 0;
                         tierIndex < profile.Tiers.Count;
                         tierIndex++)
                    {
                        GUILayout.Label(
                            MatrixCell(
                                participant,
                                profile.Tiers[tierIndex]),
                            EditorStyles.centeredGreyMiniLabel,
                            GUILayout.Width(104f));
                    }
                }

                if (participant == null) continue;
                EditorGUILayout.LabelField(
                    "    Required capabilities",
                    JoinCapabilities(
                        participant.RequiredCapabilities),
                    EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(
                    "    Dependencies",
                    JoinSlots(participant.Dependencies),
                    EditorStyles.wordWrappedMiniLabel);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox(
                "● means the tier contains every capability required by the " +
                "participant. Capability presence does not bypass Required/Optional " +
                "or dependency validation.",
                MessageType.Info);
        }

        private void RefreshDiagnostics()
        {
            diagnostics.Clear();
            diagnosticContext = string.Empty;
            matchingBindingCount = 0;
            var profile = target as CoCoRegionProfile;
            if (profile == null) return;

            string[] bindingGuids =
                AssetDatabase.FindAssets(
                    "t:" + nameof(CoCoRegionBinding));
            Array.Sort(bindingGuids, StringComparer.Ordinal);
            for (int index = 0;
                 index < bindingGuids.Length;
                 index++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(
                        bindingGuids[index]);
                CoCoRegionBinding binding =
                    AssetDatabase.LoadAssetAtPath<CoCoRegionBinding>(
                        path);
                if (binding == null ||
                    binding.Profile != profile)
                {
                    continue;
                }

                matchingBindingCount++;
                if (!CoCoMapAuthoringContext.TryCompile(
                        binding,
                        out RegionCompileResult result,
                        out string failure))
                {
                    if (string.IsNullOrEmpty(diagnosticContext))
                    {
                        diagnosticContext = failure;
                    }

                    continue;
                }

                for (int diagnosticIndex = 0;
                     diagnosticIndex < result.Diagnostics.Count;
                     diagnosticIndex++)
                {
                    diagnostics.Add(
                        new ProfileDiagnostic(
                            path,
                            result.Diagnostics[diagnosticIndex]));
                }
            }

            Repaint();
        }

        private void DrawDiagnostics()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Compilation Diagnostics",
                EditorStyles.boldLabel);
            if (matchingBindingCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "This Profile is not referenced by a project Region Binding. " +
                    "Binding-level compilation is therefore not available yet.",
                    MessageType.Info);
                return;
            }

            if (!string.IsNullOrEmpty(diagnosticContext))
            {
                EditorGUILayout.HelpBox(
                    diagnosticContext,
                    MessageType.Warning);
            }

            if (diagnostics.Count == 0 &&
                string.IsNullOrEmpty(diagnosticContext))
            {
                EditorGUILayout.HelpBox(
                    matchingBindingCount +
                    " matching Region Binding(s) compile without diagnostics.",
                    MessageType.Info);
                return;
            }

            for (int index = 0;
                 index < diagnostics.Count;
                 index++)
            {
                ProfileDiagnostic item = diagnostics[index];
                EditorGUILayout.HelpBox(
                    item.AssetPath + "\n" +
                    item.Diagnostic.Path + ": " +
                    item.Diagnostic.Diagnostic.Message,
                    DiagnosticMessageType(
                        item.Diagnostic.Diagnostic));
            }
        }

        private static void DrawManagedReferenceStatus(
            CoCoRegionProfile profile)
        {
            if (!CoCoMapAuthoringContext
                    .HasMissingManagedReferences(profile))
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "This Profile contains a missing managed-reference participant " +
                "configuration. Player build validation will fail closed.",
                MessageType.Error);
        }

        private static string ParticipantLabel(
            RegionParticipantDefinition participant)
        {
            if (participant == null) return "<null participant>";
            return participant.SlotId.Value +
                   "  [" + participant.Requirement + "]\n" +
                   participant.Phase + " / " +
                   participant.ExplicitOrder;
        }

        private static string MatrixCell(
            RegionParticipantDefinition participant,
            RegionTierDefinition tier)
        {
            if (participant == null || tier == null)
            {
                return "invalid";
            }

            if (!RegionCapabilitySet.TryCreate(
                    participant.RequiredCapabilities,
                    out RegionCapabilitySet required) ||
                !RegionCapabilitySet.TryCreate(
                    tier.Capabilities,
                    out RegionCapabilitySet available))
            {
                return "invalid";
            }

            return available.IsSupersetOf(required) ? "●" : "—";
        }

        private static string JoinCapabilities(
            IReadOnlyList<RegionCapabilityId> capabilities)
        {
            if (capabilities == null || capabilities.Count == 0)
            {
                return "<none>";
            }

            var values = new string[capabilities.Count];
            for (int index = 0;
                 index < capabilities.Count;
                 index++)
            {
                values[index] = capabilities[index].Value;
            }

            return string.Join(", ", values);
        }

        private static string JoinSlots(
            IReadOnlyList<RegionParticipantSlotId> slots)
        {
            if (slots == null || slots.Count == 0)
            {
                return "<none>";
            }

            var values = new string[slots.Count];
            for (int index = 0;
                 index < slots.Count;
                 index++)
            {
                values[index] = slots[index].Value;
            }

            return string.Join(", ", values);
        }

        private static MessageType DiagnosticMessageType(
            CoCoFlow.Runtime.Core.CoCoDiagnostic diagnostic)
        {
            return diagnostic.IsError
                ? MessageType.Error
                : diagnostic.IsWarning
                    ? MessageType.Warning
                    : MessageType.Info;
        }

        private readonly struct ProfileDiagnostic
        {
            internal ProfileDiagnostic(
                string assetPath,
                RegionCompileDiagnostic diagnostic)
            {
                AssetPath = assetPath ?? string.Empty;
                Diagnostic = diagnostic;
            }

            internal string AssetPath { get; }
            internal RegionCompileDiagnostic Diagnostic { get; }
        }
    }
}
