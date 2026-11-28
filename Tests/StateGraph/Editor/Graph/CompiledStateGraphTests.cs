using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.StateGraph;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class CompiledStateGraphTests
    {

        #region Public API

        [Test]
        public void SourceModelDefensivelyCopiesEveryPublicCollection()
        {
            var transition = new TransitionSource(
                StateGraphTestFactory.FirstTransitionId,
                StateGraphTestFactory.RootStateId,
                StateGraphTestFactory.RootStateId,
                0,
                TransitionWindow.Always);
            var originalState = StateGraphTestFactory.State(
                StateGraphTestFactory.RootStateId,
                default,
                default,
                1);
            var states = new[] { originalState };
            var transitions = new[] { transition };
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                originalState.StateId,
                states,
                transitions);
            var layers = new[] { layer };
            var source = new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion,
                5001UL,
                StateGraphTestFactory.GraphId,
                layers,
                Array.Empty<EventToIntentDeclarationSource>());

            states[0] = null;
            transitions[0] = null;
            layers[0] = null;

            Assert.AreSame(layer, source.Layers[0]);
            Assert.AreSame(originalState, source.Layers[0].States[0]);
            Assert.AreSame(transition, source.Layers[0].Transitions[0]);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<StateLayerSource>)source.Layers).Add(layer));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<StateSource>)source.Layers[0].States).Add(originalState));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<TransitionSource>)source.Layers[0].Transitions).Add(transition));
        }

        [Test]
        public void CompiledGraphDiagnosticsAndManifestsExposeReadOnlyCollections()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(true);
            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateHierarchicalSource(),
                catalog);
            CompiledStateGraph graph = result.Graph;
            CompiledStateLayer layer = graph.Layers[0];

            Assert.Throws<NotSupportedException>(() =>
                ((IList<CompiledStateLayer>)graph.Layers).Add(layer));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CompiledState>)layer.States).Add(layer.States[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CompiledTransition>)layer.Transitions).Add(layer.Transitions[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<int>)layer.States[0].ChildStateIndices).Add(99));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<int>)layer.States[1].RootPathStateIndices).Add(99));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<IntentRequirement>)graph.IntentRequirements.Requirements).Add(
                    graph.IntentRequirements.Requirements[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<GraphOperationProvision>)graph.OperationProvides.Provides).Add(
                    graph.OperationProvides.Provides[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<ContextStateBlockRequirement>)graph.ContextStateRequirements.Blocks).Add(
                    graph.ContextStateRequirements.Blocks[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<GraphDiagnostic>)result.Diagnostics).Add(default));
        }

        [Test]
        public void CompiledLookupsAreSafeForConcurrentReadOnlyAccess()
        {
            CompiledStateGraph graph = CompileGraph();
            int failures = 0;

            Parallel.For(0, 10000, _ =>
            {
                bool success = graph.TryGetLayer(
                    StateGraphTestFactory.LayerId,
                    out CompiledStateLayer layer) &&
                    layer.TryGetState(
                        StateGraphTestFactory.FirstChildStateId,
                        out CompiledState state) &&
                    layer.TryGetTransition(
                        StateGraphTestFactory.FirstTransitionId,
                        out CompiledTransition transition) &&
                    state.ParentStateIndex == 0 &&
                    transition.SourceStateIndex == state.DenseIndex;
                if (!success)
                {
                    Interlocked.Increment(ref failures);
                }
            });

            Assert.AreEqual(0, failures);
        }

        [Test]
        public void WarmedTenThousandLookupPassAllocatesZeroBytes()
        {
            CompiledStateGraph graph = CompileGraph();
            Assert.IsTrue(graph.TryGetLayer(
                StateGraphTestFactory.LayerId,
                out CompiledStateLayer layer));
            for (int index = 0; index < 100; index++)
            {
                graph.TryGetLayer(StateGraphTestFactory.LayerId, out _);
                layer.TryGetState(StateGraphTestFactory.FirstChildStateId, out _);
                layer.TryGetTransition(StateGraphTestFactory.FirstTransitionId, out _);
                _ = layer.States[0].ChildStateIndices[0];
                _ = layer.States[1].RootPathStateIndices[1];
                _ = layer.Transitions[layer.States[1].FirstOutgoingTransitionIndex].TargetStateIndex;
            }

            bool allFound = true;
            CompiledStateLayer foundLayer = null;
            CompiledState foundState = null;
            CompiledTransition foundTransition = null;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                allFound &= graph.TryGetLayer(
                    StateGraphTestFactory.LayerId,
                    out foundLayer);
                allFound &= layer.TryGetState(
                    StateGraphTestFactory.FirstChildStateId,
                    out foundState);
                allFound &= layer.TryGetTransition(
                    StateGraphTestFactory.FirstTransitionId,
                    out foundTransition);
                CompiledState root = foundLayer.States[0];
                allFound &= root.ChildStateIndices.Count == 2;
                allFound &= root.ChildStateIndices[0] == 1;
                allFound &= foundState.RootPathStateIndices.Count == 2;
                allFound &= foundState.RootPathStateIndices[0] == 0;
                allFound &= foundState.RootPathStateIndices[1] == foundState.DenseIndex;
                int firstOutgoing = foundState.FirstOutgoingTransitionIndex;
                allFound &= foundState.OutgoingTransitionCount == 1;
                allFound &= firstOutgoing >= 0;
                allFound &= foundLayer.Transitions[firstOutgoing].TransitionId ==
                            foundTransition.TransitionId;
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(allFound);
            Assert.IsNotNull(foundLayer);
            Assert.IsNotNull(foundState);
            Assert.IsNotNull(foundTransition);
            Assert.AreEqual(0L, allocated);
        }

        [TestCase(1)]
        [TestCase(10)]
        [TestCase(100)]
        public void ColdCompileBaselineRecordsGraphSizeWithoutSla(int stateCount)
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
            StateGraphSource source = CreateFlatSource(stateCount);
            var stopwatch = Stopwatch.StartNew();

            StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);

            stopwatch.Stop();
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(stateCount, result.Graph.Layers.Single().States.Count);
            TestContext.Progress.WriteLine(
                $"StateGraph cold compile baseline: states={stateCount}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");
        }

        #endregion

        #region Internal Logic

        private static CompiledStateGraph CompileGraph()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(true);
            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateHierarchicalSource(),
                catalog);
            Assert.IsTrue(result.Succeeded);
            return result.Graph;
        }

        private static StateGraphSource CreateFlatSource(int stateCount)
        {
            var states = new StateSource[stateCount];
            var transitions = new TransitionSource[Math.Max(0, stateCount - 1)];
            for (int index = 0; index < stateCount; index++)
            {
                StateId stateId = StateGraphTestFactory.CreateStateId((ulong)(100 + index));
                states[index] = StateGraphTestFactory.State(
                    stateId,
                    default,
                    default,
                    index + 1);
                if (index == 0)
                {
                    continue;
                }

                transitions[index - 1] = new TransitionSource(
                    StateGraphTestFactory.CreateTransitionId((ulong)(1000 + index)),
                    states[0].StateId,
                    stateId,
                    index,
                    TransitionWindow.Always);
            }

            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                states[0].StateId,
                states,
                transitions);
            return StateGraphTestFactory.Source(layer, unchecked(6000UL + (ulong)stateCount));
        }

        #endregion
    }
}
