using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoCompiledStateGraphTests
    {
        [Test]
        public void SourceModelDefensivelyCopiesEveryPublicCollection()
        {
            var originalCondition = new CoCoConditionSource(
                CoCoStateGraphTestFactory.ConditionDescriptorId,
                CoCoStateGraphTestFactory.ConditionConfig(1));
            var conditions = new[] { originalCondition };
            var transition = new CoCoTransitionSource(
                CoCoStateGraphTestFactory.FirstTransitionId,
                CoCoStateGraphTestFactory.RootStateId,
                CoCoStateGraphTestFactory.RootStateId,
                0,
                CoCoTransitionWindow.Always,
                conditions);
            var originalState = CoCoStateGraphTestFactory.State(
                CoCoStateGraphTestFactory.RootStateId,
                default,
                default,
                1);
            var states = new[] { originalState };
            var transitions = new[] { transition };
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                originalState.StateId,
                states,
                transitions);
            var layers = new[] { layer };
            var source = new CoCoStateGraphSource(
                CoCoStateGraphCompiler.CurrentSchemaVersion,
                5001UL,
                CoCoStateGraphTestFactory.GraphId,
                layers,
                Array.Empty<CoCoEventToIntentDeclarationSource>());

            conditions[0] = null;
            states[0] = null;
            transitions[0] = null;
            layers[0] = null;

            Assert.AreSame(layer, source.Layers[0]);
            Assert.AreSame(originalState, source.Layers[0].States[0]);
            Assert.AreSame(transition, source.Layers[0].Transitions[0]);
            Assert.AreSame(originalCondition, source.Layers[0].Transitions[0].Conditions[0]);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoStateLayerSource>)source.Layers).Add(layer));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoStateSource>)source.Layers[0].States).Add(originalState));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoTransitionSource>)source.Layers[0].Transitions).Add(transition));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoConditionSource>)transition.Conditions).Add(originalCondition));
        }

        [Test]
        public void CompiledGraphDiagnosticsAndManifestsExposeReadOnlyCollections()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(true);
            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
                catalog);
            CoCoCompiledStateGraph graph = result.Graph;
            CoCoCompiledStateLayer layer = graph.Layers[0];

            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoCompiledStateLayer>)graph.Layers).Add(layer));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoCompiledState>)layer.States).Add(layer.States[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoCompiledTransition>)layer.Transitions).Add(layer.Transitions[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<int>)layer.States[0].ChildStateIndices).Add(99));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<int>)layer.States[1].RootPathStateIndices).Add(99));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoCompiledCondition>)layer.Transitions[0].Conditions).Add(
                    layer.Transitions[0].Conditions[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoIntentRequirement>)graph.IntentRequirements.Requirements).Add(
                    graph.IntentRequirements.Requirements[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoGraphOperationProvision>)graph.OperationProvides.Provides).Add(
                    graph.OperationProvides.Provides[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoContextStateBlockRequirement>)graph.ContextStateRequirements.Blocks).Add(
                    graph.ContextStateRequirements.Blocks[0]));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoGraphDiagnostic>)result.Diagnostics).Add(default));
        }

        [Test]
        public void CompiledLookupsAreSafeForConcurrentReadOnlyAccess()
        {
            CoCoCompiledStateGraph graph = CompileGraph();
            int failures = 0;

            Parallel.For(0, 10000, _ =>
            {
                bool success = graph.TryGetLayer(
                    CoCoStateGraphTestFactory.LayerId,
                    out CoCoCompiledStateLayer layer) &&
                    layer.TryGetState(
                        CoCoStateGraphTestFactory.FirstChildStateId,
                        out CoCoCompiledState state) &&
                    layer.TryGetTransition(
                        CoCoStateGraphTestFactory.FirstTransitionId,
                        out CoCoCompiledTransition transition) &&
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
            CoCoCompiledStateGraph graph = CompileGraph();
            Assert.IsTrue(graph.TryGetLayer(
                CoCoStateGraphTestFactory.LayerId,
                out CoCoCompiledStateLayer layer));
            for (int index = 0; index < 100; index++)
            {
                graph.TryGetLayer(CoCoStateGraphTestFactory.LayerId, out _);
                layer.TryGetState(CoCoStateGraphTestFactory.FirstChildStateId, out _);
                layer.TryGetTransition(CoCoStateGraphTestFactory.FirstTransitionId, out _);
                _ = layer.States[0].ChildStateIndices[0];
                _ = layer.States[1].RootPathStateIndices[1];
                _ = layer.Transitions[layer.States[1].FirstOutgoingTransitionIndex].TargetStateIndex;
            }

            bool allFound = true;
            CoCoCompiledStateLayer foundLayer = null;
            CoCoCompiledState foundState = null;
            CoCoCompiledTransition foundTransition = null;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 10000; index++)
            {
                allFound &= graph.TryGetLayer(
                    CoCoStateGraphTestFactory.LayerId,
                    out foundLayer);
                allFound &= layer.TryGetState(
                    CoCoStateGraphTestFactory.FirstChildStateId,
                    out foundState);
                allFound &= layer.TryGetTransition(
                    CoCoStateGraphTestFactory.FirstTransitionId,
                    out foundTransition);
                CoCoCompiledState root = foundLayer.States[0];
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
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
            CoCoStateGraphSource source = CreateFlatSource(stateCount);
            var stopwatch = Stopwatch.StartNew();

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(source, catalog);

            stopwatch.Stop();
            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(stateCount, result.Graph.Layers.Single().States.Count);
            TestContext.Progress.WriteLine(
                $"StateGraph cold compile baseline: states={stateCount}, elapsedMs={stopwatch.Elapsed.TotalMilliseconds:F3}");
        }

        private static CoCoCompiledStateGraph CompileGraph()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(true);
            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
                catalog);
            Assert.IsTrue(result.Succeeded);
            return result.Graph;
        }

        private static CoCoStateGraphSource CreateFlatSource(int stateCount)
        {
            var states = new CoCoStateSource[stateCount];
            var transitions = new CoCoTransitionSource[Math.Max(0, stateCount - 1)];
            for (int index = 0; index < stateCount; index++)
            {
                CoCoStateId stateId = CoCoStateGraphTestFactory.CreateStateId((ulong)(100 + index));
                states[index] = CoCoStateGraphTestFactory.State(
                    stateId,
                    default,
                    default,
                    index + 1);
                if (index == 0)
                {
                    continue;
                }

                transitions[index - 1] = new CoCoTransitionSource(
                    CoCoStateGraphTestFactory.CreateTransitionId((ulong)(1000 + index)),
                    states[0].StateId,
                    stateId,
                    index,
                    CoCoTransitionWindow.Always,
                    Array.Empty<CoCoConditionSource>());
            }

            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                states[0].StateId,
                states,
                transitions);
            return CoCoStateGraphTestFactory.Source(layer, unchecked(6000UL + (ulong)stateCount));
        }
    }
}
