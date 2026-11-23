using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphCompilerTests
    {
        [SetUp]
        public void SetUp()
        {
            StateGraphFixtureCounters.Reset();
        }

        [Test]
        public void CompileBuildsHierarchyPathsAdjacencyAndStableLookups()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(true);
            StateGraphSource source = StateGraphTestFactory.CreateHierarchicalSource();

            StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.HasErrors);
            Assert.IsNotNull(result.Graph);
            Assert.AreEqual(
                2,
                result.Diagnostics.Count(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.ActivePathOperationOverlap));
            Assert.AreEqual(source.ContentFingerprint, result.ContentFingerprint);
            Assert.AreEqual(source.ContentFingerprint, result.Graph.ContentFingerprint);
            Assert.AreEqual(source.SchemaVersion, result.Graph.SchemaVersion);
            Assert.AreEqual(source.GraphId, result.Graph.GraphId);
            Assert.AreEqual(catalog.Fingerprint, result.Graph.CatalogFingerprint);

            Assert.IsTrue(result.Graph.TryGetLayer(
                StateGraphTestFactory.LayerId,
                out CompiledStateLayer layer));
            Assert.AreSame(result.Graph.Layers[0], layer);
            Assert.AreEqual(0, layer.DenseIndex);
            Assert.AreEqual(0, layer.InitialStateIndex);
            CollectionAssert.AreEqual(
                new[]
                {
                    StateGraphTestFactory.RootStateId,
                    StateGraphTestFactory.FirstChildStateId,
                    StateGraphTestFactory.SecondChildStateId
                },
                layer.States.Select(state => state.StateId));

            CompiledState root = layer.States[0];
            CompiledState firstChild = layer.States[1];
            CompiledState secondChild = layer.States[2];
            Assert.IsTrue(root.IsRoot);
            Assert.IsFalse(root.IsLeaf);
            Assert.AreEqual(1, root.InitialChildStateIndex);
            CollectionAssert.AreEqual(new[] { 1, 2 }, root.ChildStateIndices);
            CollectionAssert.AreEqual(new[] { 0 }, root.RootPathStateIndices);
            Assert.AreEqual(-1, root.FirstOutgoingTransitionIndex);
            Assert.AreEqual(0, root.OutgoingTransitionCount);

            Assert.AreEqual(0, firstChild.ParentStateIndex);
            Assert.IsTrue(firstChild.IsLeaf);
            CollectionAssert.AreEqual(new[] { 0, 1 }, firstChild.RootPathStateIndices);
            Assert.AreEqual(0, firstChild.FirstOutgoingTransitionIndex);
            Assert.AreEqual(1, firstChild.OutgoingTransitionCount);
            Assert.AreEqual(0, secondChild.ParentStateIndex);
            CollectionAssert.AreEqual(new[] { 0, 2 }, secondChild.RootPathStateIndices);
            Assert.AreEqual(1, secondChild.FirstOutgoingTransitionIndex);
            Assert.AreEqual(1, secondChild.OutgoingTransitionCount);

            CollectionAssert.AreEqual(
                new[]
                {
                    StateGraphTestFactory.FirstTransitionId,
                    StateGraphTestFactory.SecondTransitionId
                },
                layer.Transitions.Select(transition => transition.TransitionId));
            CompiledTransition firstTransition = layer.Transitions[0];
            Assert.AreEqual(1, firstTransition.SourceStateIndex);
            Assert.AreEqual(2, firstTransition.TargetStateIndex);
            Assert.AreEqual(1, firstTransition.Conditions.Count);
            Assert.AreEqual(
                StateGraphTestFactory.ConditionDescriptorId,
                firstTransition.Conditions[0].Descriptor.DescriptorId);
            Assert.AreEqual(0, firstTransition.Conditions[0].AuthoringIndex);
            Assert.IsTrue(layer.TryGetState(firstChild.StateId, out CompiledState stateLookup));
            Assert.AreSame(firstChild, stateLookup);
            Assert.IsTrue(layer.TryGetTransition(
                firstTransition.TransitionId,
                out CompiledTransition transitionLookup));
            Assert.AreSame(firstTransition, transitionLookup);

            Assert.AreEqual(0, StateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.MemoryConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.ConditionConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.ReducerCreated);
        }

        [Test]
        public void CompileNormalizesStateAndTransitionAuthoringOrderDeterministically()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(true);
            var compiler = new StateGraphCompiler();

            StateGraphCompileResult reversed = compiler.Compile(
                StateGraphTestFactory.CreateHierarchicalSource(1001UL, true),
                catalog);
            StateGraphCompileResult ordered = compiler.Compile(
                StateGraphTestFactory.CreateHierarchicalSource(1002UL, false),
                catalog);

            Assert.IsTrue(reversed.Succeeded);
            Assert.IsTrue(ordered.Succeeded);
            CollectionAssert.AreEqual(
                reversed.Graph.Layers[0].States.Select(state => state.StateId),
                ordered.Graph.Layers[0].States.Select(state => state.StateId));
            CollectionAssert.AreEqual(
                reversed.Graph.Layers[0].Transitions.Select(transition => transition.TransitionId),
                ordered.Graph.Layers[0].Transitions.Select(transition => transition.TransitionId));
            Assert.AreEqual(
                reversed.Graph.IntentRequirements.LayoutId,
                ordered.Graph.IntentRequirements.LayoutId);
            Assert.AreEqual(
                reversed.Graph.OperationProvides.LayoutId,
                ordered.Graph.OperationProvides.LayoutId);
            Assert.AreEqual(
                reversed.Graph.ContextStateRequirements.LayoutId,
                ordered.Graph.ContextStateRequirements.LayoutId);
        }

        [Test]
        public void LayerDenseIndicesPreserveSourceOrderInsteadOfLayerIdentityOrder()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            LayerId higherIdFirst = StateGraphTestFactory.CreateLayerId(20UL);
            LayerId lowerIdSecond = StateGraphTestFactory.CreateLayerId(10UL);
            StateId firstStateId = StateGraphTestFactory.CreateStateId(200UL);
            StateId secondStateId = StateGraphTestFactory.CreateStateId(100UL);
            var firstLayer = new StateLayerSource(
                higherIdFirst,
                firstStateId,
                new[]
                {
                    StateGraphTestFactory.State(firstStateId, default, default, 1)
                },
                System.Array.Empty<TransitionSource>());
            var secondLayer = new StateLayerSource(
                lowerIdSecond,
                secondStateId,
                new[]
                {
                    StateGraphTestFactory.State(secondStateId, default, default, 2)
                },
                System.Array.Empty<TransitionSource>());
            var source = new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion,
                2050UL,
                StateGraphTestFactory.GraphId,
                new[] { firstLayer, secondLayer },
                System.Array.Empty<EventToIntentDeclarationSource>());

            StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(higherIdFirst, result.Graph.Layers[0].LayerId);
            Assert.AreEqual(0, result.Graph.Layers[0].DenseIndex);
            Assert.AreEqual(lowerIdSecond, result.Graph.Layers[1].LayerId);
            Assert.AreEqual(1, result.Graph.Layers[1].DenseIndex);
            Assert.IsTrue(result.Graph.TryGetLayer(higherIdFirst, out CompiledStateLayer first));
            Assert.AreSame(result.Graph.Layers[0], first);
            Assert.IsTrue(result.Graph.TryGetLayer(lowerIdSecond, out CompiledStateLayer second));
            Assert.AreSame(result.Graph.Layers[1], second);
        }

        [Test]
        public void TerminalStateWithoutTransitionsCompilesSuccessfully()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);

            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateTerminalSource(),
                catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(0, result.Diagnostics.Count);
            Assert.AreEqual(1, result.Graph.Layers[0].States.Count);
            Assert.AreEqual(0, result.Graph.Layers[0].Transitions.Count);
            Assert.IsTrue(result.Graph.Layers[0].States[0].IsLeaf);
            Assert.AreEqual(-1, result.Graph.Layers[0].States[0].FirstOutgoingTransitionIndex);
            Assert.AreEqual(0, result.Graph.Layers[0].States[0].OutgoingTransitionCount);
        }

        [Test]
        public void OutgoingTransitionsSortByUniquePriorityDescending()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateId stateId = StateGraphTestFactory.RootStateId;
            TransitionId higherId = StateGraphTestFactory.CreateTransitionId(70UL);
            TransitionId lowerId = StateGraphTestFactory.CreateTransitionId(40UL);
            TransitionId lowerPriorityId = StateGraphTestFactory.CreateTransitionId(80UL);
            var transitions = new[]
            {
                Transition(higherId, stateId, 5),
                Transition(lowerPriorityId, stateId, 1),
                Transition(lowerId, stateId, 3)
            };
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                stateId,
                new[]
                {
                    StateGraphTestFactory.State(stateId, default, default, 1)
                },
                transitions);

            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.Source(layer, 2100UL),
                catalog);

            Assert.IsTrue(result.Succeeded);
            CollectionAssert.AreEqual(
                new[] { higherId, lowerId, lowerPriorityId },
                result.Graph.Layers[0].Transitions.Select(transition => transition.TransitionId));
            Assert.IsTrue(result.Graph.Layers[0].Transitions.All(
                transition => transition.Conditions.Count == 0));
            Assert.IsTrue(result.Graph.Layers[0].Transitions.All(
                transition => transition.SourceStateIndex == transition.TargetStateIndex));
            Assert.AreEqual(0, result.Graph.Layers[0].States[0].FirstOutgoingTransitionIndex);
            Assert.AreEqual(3, result.Graph.Layers[0].States[0].OutgoingTransitionCount);
        }

        [Test]
        public void MultipleConditionsPreserveAuthorOrder()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateId stateId = StateGraphTestFactory.RootStateId;
            var transition = new TransitionSource(
                StateGraphTestFactory.FirstTransitionId,
                stateId,
                stateId,
                0,
                TransitionWindow.Always,
                new[]
                {
                    new ConditionSource(
                        StateGraphTestFactory.ConditionDescriptorId,
                        StateGraphTestFactory.ConditionConfig(7)),
                    new ConditionSource(
                        StateGraphTestFactory.ConditionDescriptorId,
                        StateGraphTestFactory.ConditionConfig(3))
                });
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                stateId,
                new[]
                {
                    StateGraphTestFactory.State(stateId, default, default, 1)
                },
                new[] { transition });

            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.Source(layer, 2200UL),
                catalog);

            Assert.IsTrue(result.Succeeded);
            CompiledTransition compiled = result.Graph.Layers[0].Transitions[0];
            Assert.AreEqual(2, compiled.Conditions.Count);
            Assert.AreEqual(0, compiled.Conditions[0].AuthoringIndex);
            Assert.AreEqual(1, compiled.Conditions[1].AuthoringIndex);
            Assert.IsTrue(compiled.Conditions[0].Config.TryRead(
                TestFrozenConfigSchemas.ConditionThreshold,
                out int firstThreshold));
            Assert.AreEqual(7, firstThreshold);
            Assert.IsTrue(compiled.Conditions[1].Config.TryRead(
                TestFrozenConfigSchemas.ConditionThreshold,
                out int secondThreshold));
            Assert.AreEqual(3, secondThreshold);
        }

        private static TransitionSource Transition(
            TransitionId transitionId,
            StateId stateId,
            int priority)
        {
            return new TransitionSource(
                transitionId,
                stateId,
                stateId,
                priority,
                TransitionWindow.Always,
                System.Array.Empty<ConditionSource>());
        }
    }
}
