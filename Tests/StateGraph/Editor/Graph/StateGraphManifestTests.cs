using System;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class StateGraphManifestTests
    {
        [SetUp]
        public void SetUp()
        {
            StateGraphFixtureCounters.Reset();
        }

        [Test]
        public void CompilerBuildsExactDeduplicatedManifestsWithoutInvokingFactories()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(true);

            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateHierarchicalSource(),
                catalog);

            Assert.IsTrue(result.Succeeded);
            IntentRequirementManifest intents = result.Graph.IntentRequirements;
            GraphOperationProvisionManifest operations = result.Graph.OperationProvides;
            ContextFrameStateRequirementManifest context = result.Graph.ContextStateRequirements;

            Assert.IsNotNull(intents);
            Assert.AreEqual(1, intents.Count);
            IntentRequirement intent = intents.Requirements[0];
            Assert.AreEqual(StateGraphTestFactory.IntentId, intent.IntentId);
            Assert.AreEqual(typeof(TestIntent), intent.ValueType);
            Assert.AreEqual(typeof(TestIntentReducer), intent.ReducerType);
            Assert.AreEqual(typeof(TestIntentReducerFactory), intent.ReducerFactoryType);
            Assert.AreEqual(101UL, intent.ReducerFactorySemanticFingerprint);
            Assert.AreEqual(4, intent.MaxContributions);
            Assert.AreEqual(0, intent.DenseIndex);

            Assert.IsNotNull(operations);
            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(
                StateGraphTestFactory.OperationId,
                operations.Provides[0].OperationId);
            Assert.AreEqual(Mode.Continuous, operations.Provides[0].Mode);
            Assert.AreEqual(typeof(ITestOperationSection), operations.Provides[0].OperationType);
            Assert.AreEqual(
                typeof(TestOperationSectionViewFactory),
                operations.Provides[0].ViewFactoryType);
            Assert.AreEqual(102UL, operations.Provides[0].ViewFactorySemanticFingerprint);

            Assert.IsNotNull(context);
            Assert.AreEqual(1, context.BlockCount);
            Assert.AreEqual(1, context.SlotCount);
            Assert.AreEqual(StateGraphCompiler.CurrentSchemaVersion, context.LayoutVersion);
            ContextStateBlockRequirement block = context.Blocks[0];
            Assert.AreEqual(StateGraphTestFactory.StateBlockId, block.BlockId);
            Assert.AreEqual(StateBlockOwner.Graph, block.Owner);
            Assert.AreEqual(1, block.Slots.Count);
            ContextStateSlotRequirement slot = block.Slots[0];
            Assert.AreEqual(StateGraphTestFactory.StateSlotId, slot.SlotId);
            Assert.AreEqual(StateGraphTestFactory.StateBlockId, slot.WriterBlockId);
            Assert.AreEqual(typeof(int), slot.ValueType);
            Assert.AreEqual(ContextProjection.Temporal, slot.Projection);
            Assert.AreEqual(ContextRestorePolicy.Stored, slot.RestorePolicy);
            Assert.AreEqual(7UL, slot.DefaultValueFingerprint);
            Assert.IsNull(slot.RebuilderType);
            Assert.AreEqual(0UL, slot.RebuilderSemanticFingerprint);
            Assert.AreEqual(0, slot.DerivedDependencies.Count);

            Assert.AreEqual(0, StateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.MemoryConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.ReducerCreated);
            Assert.AreEqual(0, StateGraphFixtureCounters.OperationViewCreated);
            Assert.AreEqual(0, StateGraphFixtureCounters.RebuilderCalled);
        }

        [Test]
        public void EmptyManifestsRemainNonNullStaticContracts()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);

            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateTerminalSource(),
                catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.IsNotNull(result.Graph.IntentRequirements);
            Assert.AreEqual(0, result.Graph.IntentRequirements.Count);
            Assert.IsNotNull(result.Graph.OperationProvides);
            Assert.AreEqual(0, result.Graph.OperationProvides.Count);
            Assert.IsNotNull(result.Graph.ContextStateRequirements);
            Assert.AreEqual(0, result.Graph.ContextStateRequirements.BlockCount);
            Assert.AreEqual(0, result.Graph.ContextStateRequirements.SlotCount);
        }

        [Test]
        public void ContextManifestRejectsValuesStateFlowCannotMaterialize()
        {
            Assert.IsTrue(FrameLayoutId.TryCreate(
                1UL,
                1UL,
                out FrameLayoutId layoutId));
            var block = new GraphStateBlockRegistration(
                StateGraphTestFactory.StateBlockId,
                StateBlockOwner.Graph);
            var slot = new GraphStateSlotRegistration<IntPtr>(
                StateGraphTestFactory.StateBlockId,
                StateGraphTestFactory.StateSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                IntPtr.Zero,
                1UL,
                default,
                Array.Empty<StateSlotId>(),
                null,
                0UL);
            var manifest = new ContextFrameStateRequirementManifest(
                layoutId,
                StateGraphCompiler.CurrentSchemaVersion,
                new[] { block },
                new IGraphStateSlotRegistration[][]
                {
                    new IGraphStateSlotRegistration[] { slot }
                });

            Assert.IsFalse(manifest.TryValidate(out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateSlot, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void LayoutIdentityIsStableAcrossRepeatedCompile()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(true);
            var compiler = new StateGraphCompiler();
            StateGraphSource source = StateGraphTestFactory.CreateHierarchicalSource();

            CompiledStateGraph first = compiler.Compile(source, catalog).Graph;
            CompiledStateGraph second = compiler.Compile(source, catalog).Graph;

            Assert.AreEqual(first.IntentRequirements.LayoutId, second.IntentRequirements.LayoutId);
            Assert.AreEqual(first.OperationProvides.LayoutId, second.OperationProvides.LayoutId);
            Assert.AreEqual(
                first.ContextStateRequirements.LayoutId,
                second.ContextStateRequirements.LayoutId);
        }

        [Test]
        public void CompiledManifestTokensAreStableUnderConcurrentReads()
        {
            CompiledStateGraph graph = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateHierarchicalSource(),
                StateGraphTestFactory.CreateCatalog(true)).Graph;

            Assert.DoesNotThrow(() => Parallel.For(0, 100, _ =>
            {
                IntentRequirement intent = graph.IntentRequirements.Requirements[0];
                GraphOperationProvision operation = graph.OperationProvides.Provides[0];
                ContextStateSlotRequirement slot = graph.ContextStateRequirements.Blocks[0].Slots[0];
                Assert.AreEqual(typeof(TestIntentReducerFactory), intent.ReducerFactoryType);
                Assert.AreEqual(101UL, intent.ReducerFactorySemanticFingerprint);
                Assert.AreEqual(typeof(TestOperationSectionViewFactory), operation.ViewFactoryType);
                Assert.AreEqual(102UL, operation.ViewFactorySemanticFingerprint);
                Assert.IsNotNull(operation.Shape);
                Assert.AreEqual(4, operation.Shape.ByteSize);
                Assert.AreEqual(1, operation.Shape.FieldCount);
                Assert.AreNotEqual(0UL, operation.Shape.ShapeFingerprint);
                Assert.AreEqual(0, operation.Shape.Fields[0].DenseIndex);
                Assert.AreEqual("Value", operation.Shape.Fields[0].Name);
                Assert.AreEqual(typeof(int), operation.Shape.Fields[0].ValueType);
                Assert.AreEqual(0, operation.Shape.Fields[0].ByteOffset);
                Assert.AreEqual(4, operation.Shape.Fields[0].ByteSize);
                Assert.AreEqual(7UL, slot.DefaultValueFingerprint);
            }));

            Assert.AreEqual(0, StateGraphFixtureCounters.ReducerCreated);
            Assert.AreEqual(0, StateGraphFixtureCounters.OperationViewCreated);
            Assert.AreEqual(0, StateGraphFixtureCounters.RebuilderCalled);
        }

        [Test]
        public void DerivedSlotManifestCarriesOnlyRebuilderTokenMetadata()
        {
            Assert.IsTrue(StateSlotId.TryCreate(7UL, 2UL, out StateSlotId derivedSlotId));
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterStateBlock(
                StateGraphTestFactory.StateBlockId,
                StateBlockOwner.Graph,
                out CoCoDiagnostic blockDiagnostic), blockDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterStateSlot(
                StateGraphTestFactory.StateBlockId,
                StateGraphTestFactory.StateSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                3,
                801UL,
                default,
                null,
                out CoCoDiagnostic storedDiagnostic), storedDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterDerivedStateSlot(
                StateGraphTestFactory.StateBlockId,
                derivedSlotId,
                ContextProjection.Temporal,
                4,
                802UL,
                default,
                new[] { StateGraphTestFactory.StateSlotId },
                new DerivedStateRebuilderToken<int, TestDerivedStateRebuilder>(803UL),
                out CoCoDiagnostic derivedDiagnostic), derivedDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                StateGraphTestFactory.StateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                new[] { StateGraphTestFactory.StateBlockId },
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);

            StateGraphCompileResult result = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateTerminalSource(),
                catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, result.Graph.ContextStateRequirements.SlotCount);
            ContextStateSlotRequirement first =
                result.Graph.ContextStateRequirements.Blocks[0].Slots[0];
            ContextStateSlotRequirement second =
                result.Graph.ContextStateRequirements.Blocks[0].Slots[1];
            ContextStateSlotRequirement derived = first.SlotId == derivedSlotId ? first : second;
            Assert.AreEqual(derivedSlotId, derived.SlotId);
            Assert.AreEqual(typeof(TestDerivedStateRebuilder), derived.RebuilderType);
            Assert.AreEqual(803UL, derived.RebuilderSemanticFingerprint);
            Assert.AreEqual(0, StateGraphFixtureCounters.RebuilderCalled);
        }
    }
}
