using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

// 编辑器用实际诊断打开对应资产元素，检查定位、选择和反馈展示。
namespace CoCoFlow.Tests.Editor.StateGraph
{
    /// <summary>编辑器用实际诊断打开对应资产元素，检查定位、选择和反馈展示。</summary>
    public sealed class StateGraphEditorDiagnosticTests
    {

        #region Public API

        /// <summary>创建临时图资产并分配固定图身份，供导航诊断查找字段。</summary>
        [SetUp]
        public void SetUp()
        {
            asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity("test-asset-guid"));
        }

        /// <summary>释放为诊断导航创建的临时 Unity 图对象。</summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(asset);
        }

        /// <summary>逐一按稳定 ID 定位图、层、节点和转换，核对选中的序列化字段。</summary>
        [Test]
        public void NavigatorLocatesGraphLayerStateAndTransitionFieldsByStableId()
        {
            StateDescriptorId descriptorId = CreateStateDescriptorId(10UL);
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId firstStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Idle");
            StateId secondStateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Run");
            TransitionId transitionId = StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                firstStateId,
                secondStateId,
                priority: 5);
            var serialized = new SerializedObject(asset);

            AssertPath(serialized, Location(
                GraphElementKind.Graph,
                GraphField.Identifier,
                default,
                default,
                default), "graphId");
            AssertPath(serialized, Location(
                GraphElementKind.Layer,
                GraphField.Identifier,
                layerId,
                default,
                default), "layers.Array.data[0].layerId");
            AssertPath(serialized, Location(
                GraphElementKind.State,
                GraphField.Descriptor,
                layerId,
                firstStateId,
                default), "layers.Array.data[0].states.Array.data[0].stateDescriptorId");
            AssertPath(serialized, Location(
                GraphElementKind.Transition,
                GraphField.Priority,
                layerId,
                default,
                transitionId), "layers.Array.data[0].transitions.Array.data[0].priority");
        }

        /// <summary>传入不属于当前资产的位置，确认导航器拒绝选择。</summary>
        [Test]
        public void NavigatorRejectsALocationThatDoesNotBelongToTheAsset()
        {
            Assert.IsTrue(LayerId.TryCreate(99UL, 1UL, out LayerId missingLayer));
            var serialized = new SerializedObject(asset);

            Assert.IsFalse(StateGraphDiagnosticNavigator.TryFindProperty(
                serialized,
                Location(
                    GraphElementKind.Layer,
                    GraphField.Identifier,
                    missingLayer,
                    default,
                    default),
                out SerializedProperty property));
            Assert.IsNull(property);
        }

        /// <summary>传入另一张图的标识，确认在解析局部字段前就拒绝导航。</summary>
        [Test]
        public void NavigatorRejectsForeignGraphIdentityBeforeResolvingAField()
        {
            Assert.IsTrue(GraphId.TryCreate(99UL, 1UL, out GraphId foreignGraphId));
            var serialized = new SerializedObject(asset);
            var location = new GraphDiagnosticLocation(
                GraphElementKind.Graph,
                GraphField.Identifier,
                foreignGraphId,
                default,
                default,
                default,
                -1,
                -1,
                -1);

            Assert.IsFalse(StateGraphDiagnosticNavigator.TryFindProperty(
                serialized,
                location,
                out SerializedProperty property));
            Assert.IsNull(property);
        }

        /// <summary>构造重复层 ID，确认使用诊断中的源索引定位实际错误行。</summary>
        [Test]
        public void NavigatorUsesTheDiagnosticIndexForADuplicateLayerId()
        {
            LayerId duplicateId = StateGraphAuthoringOperations.AddLayer(asset, "First");
            StateGraphAuthoringOperations.AddLayer(asset, "Second");
            asset.Layers[1].LayerId = asset.Layers[0].LayerId;
            var serialized = new SerializedObject(asset);

            AssertPath(
                serialized,
                Location(
                    GraphElementKind.Layer,
                    GraphField.Identifier,
                    duplicateId,
                    default,
                    default,
                    layerIndex: 1),
                "layers.Array.data[1].layerId");
        }

        /// <summary>构造重复节点 ID，确认使用诊断记录的节点索引定位错误字段。</summary>
        [Test]
        public void NavigatorUsesTheDiagnosticIndexForADuplicateStateId()
        {
            StateDescriptorId descriptorId = CreateStateDescriptorId(20UL);
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId duplicateId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "First");
            StateGraphAuthoringOperations.AddState(
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
                    GraphElementKind.State,
                    GraphField.Identifier,
                    layerId,
                    duplicateId,
                    default,
                    layerIndex: 0,
                    stateIndex: 1),
                "layers.Array.data[0].states.Array.data[1].stateId");
        }

        /// <summary>构造重复转换 ID，确认诊断仍指向实际发生冲突的边。</summary>
        [Test]
        public void NavigatorUsesTheDiagnosticIndexForADuplicateTransitionId()
        {
            StateDescriptorId descriptorId = CreateStateDescriptorId(30UL);
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateId sourceId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Source");
            StateId targetId = StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                descriptorId,
                displayName: "Target");
            TransitionId duplicateId = StateGraphAuthoringOperations.AddTransition(
                asset,
                layerId,
                sourceId,
                targetId,
                priority: 1);
            StateGraphAuthoringOperations.AddTransition(
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
                    GraphElementKind.Transition,
                    GraphField.Identifier,
                    layerId,
                    default,
                    duplicateId,
                    layerIndex: 0,
                    transitionIndex: 1),
                "layers.Array.data[0].transitions.Array.data[1].transitionId");
        }

        /// <summary>选中清单级诊断，检查导航目标是当前图资产根。</summary>
        [Test]
        public void NavigatorMapsManifestDiagnosticsToTheAssetRoot()
        {
            var serialized = new SerializedObject(asset);

            AssertPath(
                serialized,
                Location(
                    GraphElementKind.Manifest,
                    GraphField.Manifest,
                    default,
                    default,
                    default),
                "layers");
        }

        #endregion

        #region Internal Logic

        private StateGraphAsset asset;

        private GraphDiagnosticLocation Location(
            GraphElementKind kind,
            GraphField field,
            LayerId layerId,
            StateId stateId,
            TransitionId transitionId,
            int layerIndex = -1,
            int stateIndex = -1,
            int transitionIndex = -1)
        {
            return new GraphDiagnosticLocation(
                kind,
                field,
                asset.GraphId,
                layerId,
                stateId,
                transitionId,
                layerIndex,
                stateIndex,
                transitionIndex);
        }

        private static void AssertPath(
            SerializedObject serialized,
            GraphDiagnosticLocation location,
            string expectedPath)
        {
            Assert.IsTrue(StateGraphDiagnosticNavigator.TryFindProperty(
                serialized,
                location,
                out SerializedProperty property));
            Assert.AreEqual(expectedPath, property.propertyPath);
        }

        private static StateDescriptorId CreateStateDescriptorId(ulong low)
        {
            Assert.IsTrue(StateDescriptorId.TryCreate(1UL, low, out StateDescriptorId id));
            return id;
        }

        #endregion
    }
}
