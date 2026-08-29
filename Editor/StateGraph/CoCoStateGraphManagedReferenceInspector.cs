using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEditor;
using UnityEngine.Serialization;

namespace CoCoFlow.Editor.StateGraph
{
    [InitializeOnLoad]
    internal static class CoCoStateGraphManagedReferenceInspector
    {
        private sealed class ConfigCandidate
        {
            internal ConfigCandidate(
                SerializedProperty property,
                CoCoGraphDiagnosticLocation location)
            {
                Property = property.Copy();
                Location = location;
            }

            internal SerializedProperty Property { get; }
            internal CoCoGraphDiagnosticLocation Location { get; }
        }

        static CoCoStateGraphManagedReferenceInspector()
        {
            CoCoStateGraphMainThreadGuard.CaptureCurrentThread();
            CoCoStateGraphManagedReferenceInspectionBridge.Install(Inspect);
        }

        private static CoCoStateGraphManagedReferenceInspection Inspect(CoCoStateGraphAsset asset)
        {
            if (!SerializationUtility.HasManagedReferencesWithMissingTypes(asset))
            {
                return CoCoStateGraphManagedReferenceInspection.Empty;
            }

            ManagedReferenceMissingType[] missingTypes =
                SerializationUtility.GetManagedReferencesWithMissingTypes(asset);
            Array.Sort(missingTypes, CompareMissingTypes);

            var serializedAsset = new SerializedObject(asset);
            serializedAsset.UpdateIfRequiredOrScript();
            List<ConfigCandidate> candidates = CollectConfigCandidates(serializedAsset, asset.GraphId);
            bool hasUniqueUnresolvedConfig = TryFindUniqueUnresolvedConfig(
                candidates,
                out CoCoGraphDiagnosticLocation unresolvedConfigLocation);
            var diagnostics = new List<CoCoGraphDiagnostic>(missingTypes.Length);
            for (int index = 0; index < missingTypes.Length; index++)
            {
                ManagedReferenceMissingType missing = missingTypes[index];
                CoCoGraphDiagnosticLocation location = FindLocation(
                    candidates,
                    missing.referenceId,
                    hasUniqueUnresolvedConfig,
                    unresolvedConfigLocation);
                string qualifiedType = string.IsNullOrEmpty(missing.namespaceName)
                    ? missing.className
                    : missing.namespaceName + "." + missing.className;
                diagnostics.Add(new CoCoGraphDiagnostic(
                    CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.State,
                        CoCoDiagnosticCode.InvalidAuthoringDependency,
                        $"Managed-reference Config type '{qualifiedType}' from " +
                        $"assembly '{missing.assemblyName}' is missing."),
                    location));
            }

            return new CoCoStateGraphManagedReferenceInspection(
                ComputeFingerprint(missingTypes),
                diagnostics);
        }

        private static List<ConfigCandidate> CollectConfigCandidates(
            SerializedObject serializedAsset,
            CoCoGraphId graphId)
        {
            var candidates = new List<ConfigCandidate>();
            SerializedProperty layers = serializedAsset.FindProperty("layers");
            if (layers == null || !layers.isArray)
            {
                return candidates;
            }

            for (int layerIndex = 0; layerIndex < layers.arraySize; layerIndex++)
            {
                SerializedProperty layer = layers.GetArrayElementAtIndex(layerIndex);
                TryReadId(layer.FindPropertyRelative("layerId"), out ulong layerHigh, out ulong layerLow);
                CoCoLayerId.TryCreate(layerHigh, layerLow, out CoCoLayerId layerId);

                SerializedProperty states = layer.FindPropertyRelative("states");
                for (int stateIndex = 0;
                     states != null && stateIndex < states.arraySize;
                     stateIndex++)
                {
                    SerializedProperty state = states.GetArrayElementAtIndex(stateIndex);
                    TryReadId(state.FindPropertyRelative("stateId"), out ulong stateHigh, out ulong stateLow);
                    CoCoStateId.TryCreate(stateHigh, stateLow, out CoCoStateId stateId);
                    SerializedProperty config = state.FindPropertyRelative("config");
                    if (config != null)
                    {
                        candidates.Add(new ConfigCandidate(
                            config,
                            new CoCoGraphDiagnosticLocation(
                                CoCoGraphElementKind.State,
                                CoCoGraphField.Config,
                                graphId,
                                layerId,
                                stateId,
                                default,
                                layerIndex,
                                stateIndex,
                                -1,
                                -1)));
                    }
                }

                SerializedProperty transitions = layer.FindPropertyRelative("transitions");
                for (int transitionIndex = 0;
                     transitions != null && transitionIndex < transitions.arraySize;
                     transitionIndex++)
                {
                    SerializedProperty transition = transitions.GetArrayElementAtIndex(transitionIndex);
                    TryReadId(
                        transition.FindPropertyRelative("transitionId"),
                        out ulong transitionHigh,
                        out ulong transitionLow);
                    CoCoTransitionId.TryCreate(
                        transitionHigh,
                        transitionLow,
                        out CoCoTransitionId transitionId);
                    TryReadId(
                        transition.FindPropertyRelative("sourceStateId"),
                        out ulong sourceHigh,
                        out ulong sourceLow);
                    CoCoStateId.TryCreate(sourceHigh, sourceLow, out CoCoStateId sourceStateId);
                    SerializedProperty conditions = transition.FindPropertyRelative("conditions");
                    for (int conditionIndex = 0;
                         conditions != null && conditionIndex < conditions.arraySize;
                         conditionIndex++)
                    {
                        SerializedProperty condition = conditions.GetArrayElementAtIndex(conditionIndex);
                        SerializedProperty config = condition.FindPropertyRelative("config");
                        if (config == null)
                        {
                            continue;
                        }

                        candidates.Add(new ConfigCandidate(
                            config,
                            new CoCoGraphDiagnosticLocation(
                                CoCoGraphElementKind.Condition,
                                CoCoGraphField.Config,
                                graphId,
                                layerId,
                                sourceStateId,
                                transitionId,
                                layerIndex,
                                -1,
                                transitionIndex,
                                conditionIndex)));
                    }
                }
            }

            return candidates;
        }

        private static CoCoGraphDiagnosticLocation FindLocation(
            IReadOnlyList<ConfigCandidate> candidates,
            long referenceId,
            bool hasUniqueUnresolvedConfig,
            CoCoGraphDiagnosticLocation unresolvedConfigLocation)
        {
            bool foundExactLocation = false;
            CoCoGraphDiagnosticLocation exactLocation = default;
            for (int index = 0; index < candidates.Count; index++)
            {
                if (ContainsManagedReference(candidates[index].Property, referenceId))
                {
                    if (foundExactLocation)
                    {
                        return GraphConfigLocation(candidates);
                    }

                    foundExactLocation = true;
                    exactLocation = candidates[index].Location;
                }
            }

            if (foundExactLocation)
            {
                return exactLocation;
            }

            // Unity preserves the original id only in
            // SerializationUtility.GetManagedReferencesWithMissingTypes. SerializedProperty
            // commonly exposes the missing field as RefIdNull (and some versions use
            // RefIdUnknown). Only use that sentinel when exactly one Config subtree can own it;
            // otherwise a precise-looking location would be misleading.
            if (hasUniqueUnresolvedConfig)
            {
                return unresolvedConfigLocation;
            }

            return GraphConfigLocation(candidates);
        }

        private static CoCoGraphDiagnosticLocation GraphConfigLocation(
            IReadOnlyList<ConfigCandidate> candidates)
        {
            CoCoGraphId graphId = candidates.Count > 0 ? candidates[0].Location.GraphId : default;
            return new CoCoGraphDiagnosticLocation(
                CoCoGraphElementKind.Graph,
                CoCoGraphField.Config,
                graphId,
                default,
                default,
                default,
                -1,
                -1,
                -1,
                -1);
        }

        private static bool TryFindUniqueUnresolvedConfig(
            IReadOnlyList<ConfigCandidate> candidates,
            out CoCoGraphDiagnosticLocation location)
        {
            location = default;
            bool found = false;
            for (int index = 0; index < candidates.Count; index++)
            {
                SerializedProperty property = candidates[index].Property;
                bool containsSentinel =
                    ContainsManagedReference(property, ManagedReferenceUtility.RefIdNull) ||
                    ContainsManagedReference(property, ManagedReferenceUtility.RefIdUnknown);
                if (!containsSentinel)
                {
                    continue;
                }

                if (found)
                {
                    location = default;
                    return false;
                }

                found = true;
                location = candidates[index].Location;
            }

            return found;
        }

        private static bool ContainsManagedReference(SerializedProperty root, long referenceId)
        {
            if (MatchesManagedReference(root, referenceId))
            {
                return true;
            }

            SerializedProperty iterator = root.Copy();
            SerializedProperty end = root.GetEndProperty();
            while (iterator.Next(true) && !SerializedProperty.EqualContents(iterator, end))
            {
                if (MatchesManagedReference(iterator, referenceId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool MatchesManagedReference(SerializedProperty property, long referenceId)
        {
            return property.propertyType == SerializedPropertyType.ManagedReference &&
                   property.managedReferenceId == referenceId;
        }

        private static bool TryReadId(
            SerializedProperty serializedId,
            out ulong high,
            out ulong low)
        {
            high = 0UL;
            low = 0UL;
            if (serializedId == null)
            {
                return false;
            }

            SerializedProperty highProperty = serializedId.FindPropertyRelative("high");
            SerializedProperty lowProperty = serializedId.FindPropertyRelative("low");
            if (highProperty == null || lowProperty == null)
            {
                return false;
            }

            high = highProperty.ulongValue;
            low = lowProperty.ulongValue;
            return true;
        }

        private static int CompareMissingTypes(
            ManagedReferenceMissingType left,
            ManagedReferenceMissingType right)
        {
            int referenceComparison = left.referenceId.CompareTo(right.referenceId);
            if (referenceComparison != 0)
            {
                return referenceComparison;
            }

            int assemblyComparison = StringComparer.Ordinal.Compare(
                left.assemblyName,
                right.assemblyName);
            if (assemblyComparison != 0)
            {
                return assemblyComparison;
            }

            int namespaceComparison = StringComparer.Ordinal.Compare(
                left.namespaceName,
                right.namespaceName);
            return namespaceComparison != 0
                ? namespaceComparison
                : StringComparer.Ordinal.Compare(left.className, right.className);
        }

        private static ulong ComputeFingerprint(IReadOnlyList<ManagedReferenceMissingType> missingTypes)
        {
            ulong hash = 14695981039346656037UL;
            Add(ref hash, missingTypes.Count);
            for (int index = 0; index < missingTypes.Count; index++)
            {
                ManagedReferenceMissingType missing = missingTypes[index];
                Add(ref hash, unchecked((ulong)missing.referenceId));
                Add(ref hash, missing.assemblyName);
                Add(ref hash, missing.namespaceName);
                Add(ref hash, missing.className);
                Add(ref hash, missing.serializedData);
            }

            return hash == 0UL ? 14695981039346656037UL : hash;
        }

        private static void Add(ref ulong hash, int value) =>
            Add(ref hash, unchecked((ulong)value));

        private static void Add(ref ulong hash, string value)
        {
            if (value == null)
            {
                Add(ref hash, ulong.MaxValue);
                return;
            }

            Add(ref hash, value.Length);
            for (int index = 0; index < value.Length; index++)
            {
                Add(ref hash, value[index]);
            }
        }

        private static void Add(ref ulong hash, ulong value)
        {
            const ulong prime = 1099511628211UL;
            for (int byteIndex = 0; byteIndex < sizeof(ulong); byteIndex++)
            {
                hash ^= (byte)(value >> (byteIndex * 8));
                hash *= prime;
            }
        }
    }
}
