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
        private int selectedParticipantIndex;
        private int selectedTierIndex;

        private void OnEnable()
        {
            RefreshDiagnostics();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUI.BeginChangeCheck();
            DrawPropertiesExcluding(
                serializedObject,
                "m_Script",
                "schemaVersion",
                "profileId");
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                CoCoRegionProfile changedProfile =
                    (CoCoRegionProfile)target;
                changedProfile.SynchronizeParticipantTierSettings();
                EditorUtility.SetDirty(changedProfile);
                serializedObject.Update();
                RefreshDiagnostics();
            }

            CoCoRegionProfile profile =
                (CoCoRegionProfile)target;
            DrawIdentity(profile);
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
                        DrawMatrixCell(
                            participantIndex,
                            tierIndex,
                            profile.Tiers[tierIndex]);
                    }
                }
            }

            EditorGUILayout.EndScrollView();
            DrawSelectedTierSetting(profile);
            EditorGUILayout.HelpBox(
                "Each cell explicitly enables or disables the participant for that " +
                "tier. Select an enabled cell to edit its registered Mode and " +
                "SerializeReference configuration.",
                MessageType.Info);
        }

        private void DrawMatrixCell(
            int participantIndex,
            int tierIndex,
            RegionTierDefinition tier)
        {
            SerializedProperty setting =
                FindTierSettingProperty(
                    participantIndex,
                    tier == null ? default : tier.TierId);
            if (setting == null)
            {
                GUILayout.Label(
                    "missing",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(104f));
                return;
            }

            SerializedProperty enabled =
                setting.FindPropertyRelative("enabled");
            bool wasEnabled = enabled.boolValue;
            bool isSelected =
                participantIndex == selectedParticipantIndex &&
                tierIndex == selectedTierIndex;
            GUIStyle style = isSelected
                ? EditorStyles.miniButtonMid
                : EditorStyles.miniButton;
            bool next = GUILayout.Toggle(
                wasEnabled,
                wasEnabled ? "●" : "—",
                style,
                GUILayout.Width(104f));
            if (next != wasEnabled)
            {
                enabled.boolValue = next;
                if (!next)
                {
                    SerializedProperty mode =
                        setting.FindPropertyRelative("modeId")
                            .FindPropertyRelative("value");
                    mode.stringValue = string.Empty;
                    setting.FindPropertyRelative("configuration")
                        .managedReferenceValue = null;
                }

                serializedObject.ApplyModifiedProperties();
                RefreshDiagnostics();
            }

            if (Event.current.type == EventType.MouseDown &&
                GUILayoutUtility.GetLastRect().Contains(
                    Event.current.mousePosition))
            {
                selectedParticipantIndex = participantIndex;
                selectedTierIndex = tierIndex;
                Repaint();
            }
        }

        private void DrawSelectedTierSetting(
            CoCoRegionProfile profile)
        {
            if (profile.Participants.Count == 0 ||
                profile.Tiers.Count == 0)
            {
                return;
            }

            selectedParticipantIndex = Mathf.Clamp(
                selectedParticipantIndex,
                0,
                profile.Participants.Count - 1);
            selectedTierIndex = Mathf.Clamp(
                selectedTierIndex,
                0,
                profile.Tiers.Count - 1);
            RegionTierDefinition tier =
                profile.Tiers[selectedTierIndex];
            SerializedProperty setting =
                FindTierSettingProperty(
                    selectedParticipantIndex,
                    tier == null ? default : tier.TierId);
            if (setting == null) return;

            RegionParticipantDefinition participant =
                profile.Participants[selectedParticipantIndex];
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Selected Cell",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                ParticipantLabel(participant) +
                " / " +
                (tier == null ? "<null tier>" : tier.Name),
                EditorStyles.wordWrappedMiniLabel);

            SerializedProperty enabled =
                setting.FindPropertyRelative("enabled");
            using (new EditorGUI.DisabledScope(!enabled.boolValue))
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(
                    setting.FindPropertyRelative("modeId"),
                    new GUIContent("Mode"));
                EditorGUILayout.PropertyField(
                    setting.FindPropertyRelative("configuration"),
                    new GUIContent("Configuration"),
                    true);
                if (EditorGUI.EndChangeCheck())
                {
                    serializedObject.ApplyModifiedProperties();
                    RefreshDiagnostics();
                }
            }
        }

        private SerializedProperty FindTierSettingProperty(
            int participantIndex,
            RegionTierId tierId)
        {
            SerializedProperty participants =
                serializedObject.FindProperty("participants");
            if (participants == null ||
                participantIndex < 0 ||
                participantIndex >= participants.arraySize)
            {
                return null;
            }

            SerializedProperty settings =
                participants.GetArrayElementAtIndex(participantIndex)
                    .FindPropertyRelative("tierSettings");
            if (settings == null) return null;

            for (int index = 0; index < settings.arraySize; index++)
            {
                SerializedProperty setting =
                    settings.GetArrayElementAtIndex(index);
                SerializedProperty value =
                    setting.FindPropertyRelative("tierId")
                        .FindPropertyRelative("value");
                if (string.Equals(
                        value.stringValue,
                        tierId.Value,
                        StringComparison.Ordinal))
                {
                    return setting;
                }
            }

            return null;
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

        private static void DrawIdentity(CoCoRegionProfile profile)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Profile Contract",
                EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField(
                    "Schema Version",
                    profile.SchemaVersion);
                EditorGUILayout.TextField(
                    "Profile ID",
                    profile.ProfileId.Value);
            }

            if (!profile.ProfileId.IsValid)
            {
                EditorGUILayout.HelpBox(
                    "This asset does not have a valid GUID-derived ProfileId. " +
                    "Reimport it before compiling or building.",
                    MessageType.Error);
            }
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
