using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Map
{
    [CustomEditor(typeof(CoCoRegionBinding))]
    internal sealed class CoCoRegionBindingEditor : UnityEditor.Editor
    {
        private RegionCompileResult compileResult;
        private string compileFailure = string.Empty;

        private void OnEnable()
        {
            RefreshCompilation();
        }

        public override void OnInspectorGUI()
        {
            EditorGUI.BeginChangeCheck();
            DrawDefaultInspector();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                RefreshCompilation();
            }

            var binding = (CoCoRegionBinding)target;
            DrawManagedReferenceStatus(binding);
            DrawCoverageAndDependencies(binding);

            EditorGUILayout.Space();
            if (GUILayout.Button("Refresh Compile Diagnostics"))
            {
                RefreshCompilation();
            }

            DrawCompilation();
        }

        private void RefreshCompilation()
        {
            compileResult = null;
            compileFailure = string.Empty;
            var binding = target as CoCoRegionBinding;
            if (binding == null) return;

            if (!CoCoMapAuthoringContext.TryCompile(
                    binding,
                    out compileResult,
                    out compileFailure))
            {
                compileResult = null;
            }

            Repaint();
        }

        private static void DrawCoverageAndDependencies(
            CoCoRegionBinding binding)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Coverage & Dependency Projection",
                EditorStyles.boldLabel);
            if (binding == null)
            {
                return;
            }

            var definitions =
                new Dictionary<
                    RegionParticipantSlotId,
                    RegionParticipantDefinition>();
            if (binding.Profile != null)
            {
                for (int index = 0;
                     index < binding.Profile.Participants.Count;
                     index++)
                {
                    RegionParticipantDefinition definition =
                        binding.Profile.Participants[index];
                    if (definition != null &&
                        definition.SlotId.IsValid &&
                        !definitions.ContainsKey(definition.SlotId))
                    {
                        definitions.Add(
                            definition.SlotId,
                            definition);
                    }
                }
            }

            using (new EditorGUILayout.VerticalScope(
                       EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(
                    "Region-global",
                    EditorStyles.miniBoldLabel);
                if (binding.RegionParticipants.Count == 0)
                {
                    EditorGUILayout.LabelField(
                        "<no Region-global participant bindings>");
                }

                for (int index = 0;
                     index < binding.RegionParticipants.Count;
                     index++)
                {
                    DrawSlotBinding(
                        binding.RegionParticipants[index],
                        definitions);
                }
            }

            for (int chunkIndex = 0;
                 chunkIndex < binding.Chunks.Count;
                 chunkIndex++)
            {
                RegionChunkBinding chunk =
                    binding.Chunks[chunkIndex];
                using (new EditorGUILayout.VerticalScope(
                           EditorStyles.helpBox))
                {
                    if (chunk == null)
                    {
                        EditorGUILayout.LabelField(
                            "Chunk [" + chunkIndex + "]: <null>",
                            EditorStyles.miniBoldLabel);
                        continue;
                    }

                    EditorGUILayout.LabelField(
                        "Chunk: " + chunk.ChunkId.Value,
                        EditorStyles.miniBoldLabel);
                    EditorGUILayout.LabelField(
                        "Coverage",
                        "Explicit chunk '" +
                        chunk.ChunkId.Value + "'");
                    EditorGUILayout.LabelField(
                        "Scene",
                        chunk.SceneSource.SourceKind + " · " +
                        chunk.SceneSource.Location);
                    EditorGUILayout.LabelField(
                        "Owning Content Slot",
                        chunk.OwningContentSlotId.Value);
                    for (int bindingIndex = 0;
                         bindingIndex < chunk.Participants.Count;
                         bindingIndex++)
                    {
                        DrawSlotBinding(
                            chunk.Participants[bindingIndex],
                            definitions);
                    }
                }
            }
        }

        private void DrawCompilation()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Compilation Diagnostics",
                EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(compileFailure))
            {
                EditorGUILayout.HelpBox(
                    compileFailure,
                    MessageType.Warning);
                return;
            }

            if (compileResult == null)
            {
                EditorGUILayout.HelpBox(
                    "No compilation result is available.",
                    MessageType.Info);
                return;
            }

            if (compileResult.Diagnostics.Count == 0)
            {
                RegionCompiledPlan plan = compileResult.Plan;
                EditorGUILayout.HelpBox(
                    "Compiled successfully. " +
                    (plan == null
                        ? string.Empty
                        : plan.Nodes.Count + " nodes, " +
                          plan.Chunks.Count + " chunks, fingerprint " +
                          plan.Fingerprint + "."),
                    MessageType.Info);
                return;
            }

            for (int index = 0;
                 index < compileResult.Diagnostics.Count;
                 index++)
            {
                RegionCompileDiagnostic item =
                    compileResult.Diagnostics[index];
                EditorGUILayout.HelpBox(
                    item.Path + ": " +
                    item.Diagnostic.Message,
                    DiagnosticMessageType(item.Diagnostic));
            }
        }

        private static void DrawSlotBinding(
            RegionParticipantSlotBinding slotBinding,
            IReadOnlyDictionary<
                RegionParticipantSlotId,
                RegionParticipantDefinition> definitions)
        {
            if (slotBinding == null)
            {
                EditorGUILayout.LabelField("• <null binding>");
                return;
            }

            string detail =
                "fragment " +
                (string.IsNullOrEmpty(slotBinding.FragmentId)
                    ? "<root>"
                    : slotBinding.FragmentId);
            if (definitions.TryGetValue(
                    slotBinding.SlotId,
                    out RegionParticipantDefinition definition))
            {
                detail +=
                    " · " + definition.Requirement +
                    " · " + definition.Phase +
                    " · depends " +
                    JoinSlots(definition.Dependencies);
            }
            else
            {
                detail += " · undefined Profile slot";
            }

            EditorGUILayout.LabelField(
                "• " + slotBinding.SlotId.Value,
                detail,
                EditorStyles.wordWrappedMiniLabel);
        }

        private static void DrawManagedReferenceStatus(
            CoCoRegionBinding binding)
        {
            bool bindingMissing =
                CoCoMapAuthoringContext
                    .HasMissingManagedReferences(binding);
            bool profileMissing =
                binding != null &&
                CoCoMapAuthoringContext.HasMissingManagedReferences(
                    binding.Profile);
            if (!bindingMissing && !profileMissing)
            {
                return;
            }

            EditorGUILayout.HelpBox(
                "This Binding or its Profile contains a missing managed reference. " +
                "Player build validation will fail closed.",
                MessageType.Error);
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
            CoCoDiagnostic diagnostic)
        {
            return diagnostic.IsError
                ? MessageType.Error
                : diagnostic.IsWarning
                    ? MessageType.Warning
                    : MessageType.Info;
        }
    }
}
