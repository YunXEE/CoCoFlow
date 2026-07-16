using System.Linq;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphCompilerTests
    {
        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphFixtureCounters.Reset();
        }

        [Test]
        public void CompileBuildsHierarchyPathsAdjacencyAndStableLookups()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(true);
            CoCoStateGraphSource source = CoCoStateGraphTestFactory.CreateHierarchicalSource();

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(source, catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.IsFalse(result.HasErrors);
            Assert.IsNotNull(result.Graph);
            Assert.AreEqual(0, result.Diagnostics.Count);
            Assert.AreEqual(source.ContentFingerprint, result.ContentFingerprint);
            Assert.AreEqual(source.ContentFingerprint, result.Graph.ContentFingerprint);
            Assert.AreEqual(source.SchemaVersion, result.Graph.SchemaVersion);
            Assert.AreEqual(source.GraphId, result.Graph.GraphId);
            Assert.AreEqual(catalog.Fingerprint, result.Graph.CatalogFingerprint);

            Assert.IsTrue(result.Graph.TryGetLayer(
                CoCoStateGraphTestFactory.LayerId,
                out CoCoCompiledStateLayer layer));
            Assert.AreSame(result.Graph.Layers[0], layer);
            Assert.AreEqual(0, layer.DenseIndex);
            Assert.AreEqual(0, layer.InitialStateIndex);
            CollectionAssert.AreEqual(
                new[]
                {
                    CoCoStateGraphTestFactory.RootStateId,
                    CoCoStateGraphTestFactory.FirstChildStateId,
                    CoCoStateGraphTestFactory.SecondChildStateId
                },
                layer.States.Select(state => state.StateId));

            CoCoCompiledState root = layer.States[0];
            CoCoCompiledState firstChild = layer.States[1];
            CoCoCompiledState secondChild = layer.States[2];
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
                    CoCoStateGraphTestFactory.FirstTransitionId,
                    CoCoStateGraphTestFactory.SecondTransitionId
                },
                layer.Transitions.Select(transition => transition.TransitionId));
            CoCoCompiledTransition firstTransition = layer.Transitions[0];
            Assert.AreEqual(1, firstTransition.SourceStateIndex);
            Assert.AreEqual(2, firstTransition.TargetStateIndex);
            Assert.AreEqual(1, firstTransition.Conditions.Count);
            Assert.AreEqual(
                CoCoStateGraphTestFactory.ConditionDescriptorId,
                firstTransition.Conditions[0].Descriptor.DescriptorId);
            Assert.AreEqual(0, firstTransition.Conditions[0].AuthoringIndex);
            Assert.IsTrue(layer.TryGetState(firstChild.StateId, out CoCoCompiledState stateLookup));
            Assert.AreSame(firstChild, stateLookup);
            Assert.IsTrue(layer.TryGetTransition(
                firstTransition.TransitionId,
                out CoCoCompiledTransition transitionLookup));
            Assert.AreSame(firstTransition, transitionLookup);

            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.MemoryConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ConditionConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ReducerCreated);
        }

        [Test]
        public void CompileNormalizesAuthoringOrderDeterministically()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(true);
            var compiler = new CoCoStateGraphCompiler();

            CoCoStateGraphCompileResult reversed = compiler.Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(1001UL, true),
                catalog);
            CoCoStateGraphCompileResult ordered = compiler.Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(1002UL, false),
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
        public void TerminalStateWithoutTransitionsCompilesSuccessfully()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateTerminalSource(),
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
        public void OutgoingTransitionsSortByPriorityDescendingThenIdentityAscending()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateId stateId = CoCoStateGraphTestFactory.RootStateId;
            CoCoTransitionId higherId = CoCoStateGraphTestFactory.CreateTransitionId(70UL);
            CoCoTransitionId lowerId = CoCoStateGraphTestFactory.CreateTransitionId(40UL);
            CoCoTransitionId lowerPriorityId = CoCoStateGraphTestFactory.CreateTransitionId(80UL);
            var transitions = new[]
            {
                Transition(higherId, stateId, 5),
                Transition(lowerPriorityId, stateId, 1),
                Transition(lowerId, stateId, 5)
            };
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                stateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(stateId, default, default, 1)
                },
                transitions);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.Source(layer, 2100UL),
                catalog);

            Assert.IsTrue(result.Succeeded);
            CollectionAssert.AreEqual(
                new[] { lowerId, higherId, lowerPriorityId },
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
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateId stateId = CoCoStateGraphTestFactory.RootStateId;
            var transition = new CoCoTransitionSource(
                CoCoStateGraphTestFactory.FirstTransitionId,
                stateId,
                stateId,
                0,
                CoCoTransitionWindow.Always,
                CoCoTransitionInterruptPolicy.RequireSourceCompletion,
                new[]
                {
                    new CoCoConditionSource(
                        CoCoStateGraphTestFactory.ConditionDescriptorId,
                        CoCoStateGraphTestFactory.ConditionConfig(7)),
                    new CoCoConditionSource(
                        CoCoStateGraphTestFactory.ConditionDescriptorId,
                        CoCoStateGraphTestFactory.ConditionConfig(3))
                });
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                stateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(stateId, default, default, 1)
                },
                new[] { transition });

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.Source(layer, 2200UL),
                catalog);

            Assert.IsTrue(result.Succeeded);
            CoCoCompiledTransition compiled = result.Graph.Layers[0].Transitions[0];
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

        private static CoCoTransitionSource Transition(
            CoCoTransitionId transitionId,
            CoCoStateId stateId,
            int priority)
        {
            return new CoCoTransitionSource(
                transitionId,
                stateId,
                stateId,
                priority,
                CoCoTransitionWindow.Always,
                CoCoTransitionInterruptPolicy.RequireSourceCompletion,
                System.Array.Empty<CoCoConditionSource>());
        }
    }
}
