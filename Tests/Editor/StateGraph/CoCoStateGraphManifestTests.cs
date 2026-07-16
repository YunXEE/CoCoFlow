using System;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphManifestTests
    {
        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphFixtureCounters.Reset();
        }

        [Test]
        public void CompilerBuildsExactDeduplicatedManifestsWithoutInvokingFactories()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(true);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
                catalog);

            Assert.IsTrue(result.Succeeded);
            CoCoIntentRequirementManifest intents = result.Graph.IntentRequirements;
            CoCoGraphOperationProvidesManifest operations = result.Graph.OperationProvides;
            CoCoContextFrameStateRequirementManifest context = result.Graph.ContextStateRequirements;

            Assert.IsNotNull(intents);
            Assert.AreEqual(1, intents.Count);
            CoCoIntentRequirement intent = intents.Requirements[0];
            Assert.AreEqual(CoCoStateGraphTestFactory.IntentId, intent.IntentId);
            Assert.AreEqual(typeof(TestIntent), intent.ValueType);
            Assert.AreEqual(typeof(TestIntentReducer), intent.ReducerType);
            Assert.AreEqual(typeof(TestIntentReducerFactory), intent.ReducerFactoryType);
            Assert.AreEqual(101UL, intent.ReducerFactorySemanticFingerprint);
            Assert.AreEqual(4, intent.MaxContributions);
            Assert.AreEqual(0, intent.DenseIndex);

            Assert.IsNotNull(operations);
            Assert.AreEqual(1, operations.Count);
            Assert.AreEqual(
                CoCoStateGraphTestFactory.OperationSectionId,
                operations.Provides[0].SectionId);
            Assert.AreEqual(CoCoOperationSectionMode.Continuous, operations.Provides[0].Mode);
            Assert.AreEqual(typeof(ITestOperationSection), operations.Provides[0].SectionType);
            Assert.AreEqual(
                typeof(TestOperationSectionViewFactory),
                operations.Provides[0].ViewFactoryType);
            Assert.AreEqual(102UL, operations.Provides[0].ViewFactorySemanticFingerprint);

            Assert.IsNotNull(context);
            Assert.AreEqual(1, context.BlockCount);
            Assert.AreEqual(1, context.SlotCount);
            Assert.AreEqual(CoCoStateGraphCompiler.CurrentSchemaVersion, context.LayoutVersion);
            CoCoContextStateBlockRequirement block = context.Blocks[0];
            Assert.AreEqual(CoCoStateGraphTestFactory.StateBlockId, block.BlockId);
            Assert.AreEqual(CoCoStateBlockOwner.Graph, block.Owner);
            Assert.AreEqual(1, block.Slots.Count);
            CoCoContextStateSlotRequirement slot = block.Slots[0];
            Assert.AreEqual(CoCoStateGraphTestFactory.StateSlotId, slot.SlotId);
            Assert.AreEqual(CoCoStateGraphTestFactory.StateBlockId, slot.WriterBlockId);
            Assert.AreEqual(typeof(int), slot.ValueType);
            Assert.AreEqual(CoCoContextProjection.Temporal, slot.Projection);
            Assert.AreEqual(CoCoContextRestorePolicy.Stored, slot.RestorePolicy);
            Assert.AreEqual(7UL, slot.DefaultValueFingerprint);
            Assert.IsNull(slot.RebuilderType);
            Assert.AreEqual(0UL, slot.RebuilderSemanticFingerprint);
            Assert.AreEqual(0, slot.DerivedDependencies.Count);

            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.MemoryConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ConditionConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ReducerCreated);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.OperationViewCreated);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.RebuilderCalled);
        }

        [Test]
        public void EmptyManifestsRemainNonNullStaticContracts()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateTerminalSource(),
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
            Assert.IsTrue(CoCoFrameLayoutId.TryCreate(
                1UL,
                1UL,
                out CoCoFrameLayoutId layoutId));
            var block = new CoCoGraphStateBlockRegistration(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateBlockOwner.Graph);
            var slot = new CoCoGraphStateSlotRegistration<IntPtr>(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateGraphTestFactory.StateSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                IntPtr.Zero,
                1UL,
                default,
                Array.Empty<CoCoStateSlotId>(),
                null,
                0UL);
            var manifest = new CoCoContextFrameStateRequirementManifest(
                layoutId,
                CoCoStateGraphCompiler.CurrentSchemaVersion,
                new[] { block },
                new ICoCoGraphStateSlotRegistration[][]
                {
                    new ICoCoGraphStateSlotRegistration[] { slot }
                });

            Assert.IsFalse(manifest.TryValidate(out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateSlot, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void LayoutIdentityIsStableAcrossRepeatedCompile()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(true);
            var compiler = new CoCoStateGraphCompiler();
            CoCoStateGraphSource source = CoCoStateGraphTestFactory.CreateHierarchicalSource();

            CoCoCompiledStateGraph first = compiler.Compile(source, catalog).Graph;
            CoCoCompiledStateGraph second = compiler.Compile(source, catalog).Graph;

            Assert.AreEqual(first.IntentRequirements.LayoutId, second.IntentRequirements.LayoutId);
            Assert.AreEqual(first.OperationProvides.LayoutId, second.OperationProvides.LayoutId);
            Assert.AreEqual(
                first.ContextStateRequirements.LayoutId,
                second.ContextStateRequirements.LayoutId);
        }

        [Test]
        public void CompiledManifestTokensAreStableUnderConcurrentReads()
        {
            CoCoCompiledStateGraph graph = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
                CoCoStateGraphTestFactory.CreateCatalog(true)).Graph;

            Assert.DoesNotThrow(() => Parallel.For(0, 100, _ =>
            {
                CoCoIntentRequirement intent = graph.IntentRequirements.Requirements[0];
                CoCoGraphOperationProvideRequirement operation = graph.OperationProvides.Provides[0];
                CoCoContextStateSlotRequirement slot = graph.ContextStateRequirements.Blocks[0].Slots[0];
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

            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ReducerCreated);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.OperationViewCreated);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.RebuilderCalled);
        }

        [Test]
        public void ConditionAloneContributesAllThreeManifestKinds()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterIntent(
                CoCoStateGraphTestFactory.IntentId,
                4,
                new CoCoIntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(700UL),
                out CoCoDiagnostic intentDiagnostic), intentDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterOperationSection(
                CoCoStateGraphTestFactory.OperationSectionId,
                CoCoOperationSectionMode.Continuous,
                new CoCoOperationSectionViewFactoryToken<
                    ITestOperationSection,
                    TestOperationSectionViewFactory>(701UL),
                out CoCoDiagnostic operationDiagnostic), operationDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterStateBlock(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateBlockOwner.Graph,
                out CoCoDiagnostic blockDiagnostic), blockDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterStateSlot(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateGraphTestFactory.StateSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                0,
                702UL,
                default,
                null,
                out CoCoDiagnostic slotDiagnostic), slotDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                CoCoStateGraphTestFactory.StateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterCondition(
                CoCoStateGraphTestFactory.ConditionDescriptorId,
                1U,
                new TestConditionConfigFreezer(),
                new CoCoConditionRuntimeRegistration<
                    TestStateCondition,
                    TestConditionConfigSchema>(TestFrozenConfigSchemas.ConditionSchema),
                new[] { CoCoStateGraphTestFactory.IntentId },
                new[] { CoCoStateGraphTestFactory.OperationSectionId },
                new[] { CoCoStateGraphTestFactory.StateBlockId },
                out CoCoDiagnostic conditionDiagnostic), conditionDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
                catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, result.Graph.IntentRequirements.Count);
            Assert.AreEqual(1, result.Graph.OperationProvides.Count);
            Assert.AreEqual(1, result.Graph.ContextStateRequirements.BlockCount);
            Assert.AreEqual(1, result.Graph.ContextStateRequirements.SlotCount);
            Assert.AreEqual(
                CoCoStateGraphTestFactory.OperationSectionId,
                result.Graph.OperationProvides.Provides[0].SectionId);
            Assert.AreEqual(701UL, result.Graph.OperationProvides.Provides[0].ViewFactorySemanticFingerprint);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.OperationViewCreated);
        }

        [Test]
        public void DerivedSlotManifestCarriesOnlyRebuilderTokenMetadata()
        {
            Assert.IsTrue(CoCoStateSlotId.TryCreate(7UL, 2UL, out CoCoStateSlotId derivedSlotId));
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterStateBlock(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateBlockOwner.Graph,
                out CoCoDiagnostic blockDiagnostic), blockDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterStateSlot(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateGraphTestFactory.StateSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                3,
                801UL,
                default,
                null,
                out CoCoDiagnostic storedDiagnostic), storedDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterDerivedStateSlot(
                CoCoStateGraphTestFactory.StateBlockId,
                derivedSlotId,
                CoCoContextProjection.Temporal,
                4,
                802UL,
                default,
                new[] { CoCoStateGraphTestFactory.StateSlotId },
                new CoCoDerivedStateRebuilderToken<int, TestDerivedStateRebuilder>(803UL),
                out CoCoDiagnostic derivedDiagnostic), derivedDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                CoCoStateGraphTestFactory.StateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                new[] { CoCoStateGraphTestFactory.StateBlockId },
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateTerminalSource(),
                catalog);

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, result.Graph.ContextStateRequirements.SlotCount);
            CoCoContextStateSlotRequirement first =
                result.Graph.ContextStateRequirements.Blocks[0].Slots[0];
            CoCoContextStateSlotRequirement second =
                result.Graph.ContextStateRequirements.Blocks[0].Slots[1];
            CoCoContextStateSlotRequirement derived = first.SlotId == derivedSlotId ? first : second;
            Assert.AreEqual(derivedSlotId, derived.SlotId);
            Assert.AreEqual(typeof(TestDerivedStateRebuilder), derived.RebuilderType);
            Assert.AreEqual(803UL, derived.RebuilderSemanticFingerprint);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.RebuilderCalled);
        }
    }
}
