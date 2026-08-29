using CoCoFlow.Editor.StateGraph;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphEditorDiagnosticTests
    {
        private CoCoStateGraphAsset asset;

        [SetUp]
        public void SetUp()
        {
            asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity("test-asset-guid"));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(asset);
        }

        [Test]
        public void NavigatorLocatesGraphLayerStateAndTransitionFieldsByStableId()
        {
            CoCoStateDescriptorId descriptorId = StateDescriptorId(10UL);
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId firstStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Idle");
            CoCoStateId secondStateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Run");
            CoCoTransitionId transitionId = CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                firstStateId,
                secondStateId,
                priority: 5);
            var serialized = new SerializedObject(asset);

            AssertPath(serialized, Location(
                CoCoGraphElementKind.Graph,
                CoCoGraphField.Identifier,
                default,
                default,
                default), "graphId");
            AssertPath(serialized, Location(
                CoCoGraphElementKind.Layer,
                CoCoGraphField.Identifier,
                layerId,
                default,
                default), "layers.Array.data[0].layerId");
            AssertPath(serialized, Location(
                CoCoGraphElementKind.State,
                CoCoGraphField.Descriptor,
                layerId,
                firstStateId,
                default), "layers.Array.data[0].states.Array.data[0].stateDescriptorId");
            AssertPath(serialized, Location(
                CoCoGraphElementKind.Transition,
                CoCoGraphField.Priority,
                layerId,
                default,
                transitionId), "layers.Array.data[0].transitions.Array.data[0].priority");
        }

        [Test]
        public void NavigatorRejectsALocationThatDoesNotBelongToTheAsset()
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(99UL, 1UL, out CoCoLayerId missingLayer));
            var serialized = new SerializedObject(asset);

            Assert.IsFalse(CoCoStateGraphDiagnosticNavigator.TryFindProperty(
                serialized,
                Location(
                    CoCoGraphElementKind.Layer,
                    CoCoGraphField.Identifier,
                    missingLayer,
                    default,
                    default),
                out SerializedProperty property));
            Assert.IsNull(property);
        }

        [Test]
        public void NavigatorRejectsForeignGraphIdentityBeforeResolvingAField()
        {
            Assert.IsTrue(CoCoGraphId.TryCreate(99UL, 1UL, out CoCoGraphId foreignGraphId));
            var serialized = new SerializedObject(asset);
            var location = new CoCoGraphDiagnosticLocation(
                CoCoGraphElementKind.Graph,
                CoCoGraphField.Identifier,
                foreignGraphId,
                default,
                default,
                default,
                -1,
                -1,
                -1,
                -1);

            Assert.IsFalse(CoCoStateGraphDiagnosticNavigator.TryFindProperty(
                serialized,
                location,
                out SerializedProperty property));
            Assert.IsNull(property);
        }

        [Test]
        public void NavigatorUsesTheDiagnosticIndexForADuplicateLayerId()
        {
            CoCoLayerId duplicateId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "First");
            CoCoStateGraphAuthoringOperations.AddLayer(asset, "Second");
            asset.Layers[1].LayerId = asset.Layers[0].LayerId;
            var serialized = new SerializedObject(asset);

            AssertPath(
                serialized,
                Location(
                    CoCoGraphElementKind.Layer,
                    CoCoGraphField.Identifier,
                    duplicateId,
                    default,
                    default,
                    layerIndex: 1),
                "layers.Array.data[1].layerId");
        }

        [Test]
        public void NavigatorUsesTheDiagnosticIndexForADuplicateStateId()
        {
            CoCoStateDescriptorId descriptorId = StateDescriptorId(20UL);
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId duplicateId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "First");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Second");
            asset.Layers[0].States[1].StateId = asset.Layers[0].States[0].StateId;
            var serialized = new SerializedObject(asset);

            AssertPath(
                serialized,
                Location(
                    CoCoGraphElementKind.State,
                    CoCoGraphField.Identifier,
                    layerId,
                    duplicateId,
                    default,
                    layerIndex: 0,
                    stateIndex: 1),
                "layers.Array.data[0].states.Array.data[1].stateId");
        }

        [Test]
        public void NavigatorUsesTheDiagnosticIndexForADuplicateTransitionId()
        {
            CoCoStateDescriptorId descriptorId = StateDescriptorId(30UL);
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateId sourceId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Source");
            CoCoStateId targetId = CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Target");
            CoCoTransitionId duplicateId = CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                sourceId,
                targetId,
                priority: 1);
            CoCoStateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                targetId,
                sourceId,
                priority: 2);
            asset.Layers[0].Transitions[1].TransitionId = asset.Layers[0].Transitions[0].TransitionId;
            var serialized = new SerializedObject(asset);

            AssertPath(
                serialized,
                Location(
                    CoCoGraphElementKind.Transition,
                    CoCoGraphField.Identifier,
                    layerId,
                    default,
                    duplicateId,
                    layerIndex: 0,
                    transitionIndex: 1),
                "layers.Array.data[0].transitions.Array.data[1].transitionId");
        }

        [Test]
        public void NavigatorMapsManifestDiagnosticsToTheAssetRoot()
        {
            var serialized = new SerializedObject(asset);

            AssertPath(
                serialized,
                Location(
                    CoCoGraphElementKind.Manifest,
                    CoCoGraphField.Manifest,
                    default,
                    default,
                    default),
                "layers");
        }

        private CoCoGraphDiagnosticLocation Location(
            CoCoGraphElementKind kind,
            CoCoGraphField field,
            CoCoLayerId layerId,
            CoCoStateId stateId,
            CoCoTransitionId transitionId,
            int layerIndex = -1,
            int stateIndex = -1,
            int transitionIndex = -1,
            int conditionIndex = -1)
        {
            return new CoCoGraphDiagnosticLocation(
                kind,
                field,
                asset.GraphId,
                layerId,
                stateId,
                transitionId,
                layerIndex,
                stateIndex,
                transitionIndex,
                conditionIndex);
        }

        private static void AssertPath(
            SerializedObject serialized,
            CoCoGraphDiagnosticLocation location,
            string expectedPath)
        {
            Assert.IsTrue(CoCoStateGraphDiagnosticNavigator.TryFindProperty(
                serialized,
                location,
                out SerializedProperty property));
            Assert.AreEqual(expectedPath, property.propertyPath);
        }

        private static CoCoStateDescriptorId StateDescriptorId(ulong low)
        {
            Assert.IsTrue(CoCoStateDescriptorId.TryCreate(1UL, low, out CoCoStateDescriptorId id));
            return id;
        }
    }
}
