using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.StateGraphAuthoringDependencyFixtures;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using CoCoFlow.Tests.StateGraphLegacyCoreDependencyFixtures;
using NUnit.Framework;

namespace CoCoFlow.Tests.StateGraph
{
    public sealed class GraphDescriptorCatalogTests
    {

        #region Public API

        [SetUp]
        public void SetUp()
        {
            StateGraphFixtureCounters.Reset();
        }

        [Test]
        public void CatalogFreezesPureDescriptorsWithoutConstructingRuntimeObjects()
        {
            StateDescriptorId stateDescriptorId = CreateStateDescriptorId(1UL);
            IntentId intentId = CreateIntentId(3UL);
            OperationId operationId = CreateOperationId(4UL);
            StateBlockId blockId = BlockId(5UL);
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                4,
                new IntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(101UL),
                out CoCoDiagnostic intentDiagnostic),
                intentDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterOperation(
                operationId,
                Mode.Continuous,
                new OperationViewFactoryToken<
                    ITestOperationSection,
                    TestOperationSectionViewFactory>(102UL),
                out CoCoDiagnostic operationDiagnostic),
                operationDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterStateBlock(
                blockId,
                StateBlockOwner.Graph,
                out CoCoDiagnostic blockDiagnostic),
                blockDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                stateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                new[] { intentId },
                new[] { operationId },
                new[] { blockId },
                out CoCoDiagnostic stateDiagnostic),
                stateDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);

            Assert.IsTrue(builder.IsFrozen);
            Assert.IsTrue(catalog.IsFrozen);
            Assert.AreNotEqual(0UL, catalog.Fingerprint);
            Assert.AreEqual(1, catalog.StateDescriptorCount);
            Assert.IsTrue(catalog.TryGetStateDescriptor(
                stateDescriptorId,
                out StateDescriptor stateDescriptor));
            Assert.AreEqual(typeof(TestStateLogic), stateDescriptor.LogicType);
            Assert.AreEqual(typeof(TestStateAuthoringConfig), stateDescriptor.AuthoringConfigType);
            Assert.AreEqual(typeof(TestStateConfigSchema), stateDescriptor.ConfigSchemaType);
            Assert.AreEqual(
                TestFrozenConfigSchemas.StateSchema.Fingerprint,
                stateDescriptor.ConfigSchemaFingerprint);
            Assert.AreEqual(typeof(TestActivationMemory), stateDescriptor.ActivationMemoryType);
            Assert.IsFalse(stateDescriptor.ProvidesActionProgress);
            CollectionAssert.AreEqual(new[] { intentId }, stateDescriptor.IntentRequirements);
            CollectionAssert.AreEqual(new[] { operationId }, stateDescriptor.OperationProvides);
            CollectionAssert.AreEqual(new[] { blockId }, stateDescriptor.ContextStateRequirements);

            Assert.AreEqual(0, StateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.MemoryConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.ReducerCreated);
        }

        [Test]
        public void DescriptorEnumerationIsImmutableAndSortedByStableId()
        {
            StateDescriptorId laterStateId = CreateStateDescriptorId(90UL);
            StateDescriptorId earlierStateId = CreateStateDescriptorId(10UL);
            var builder = new GraphDescriptorCatalogBuilder();

            foreach (StateDescriptorId descriptorId in new[] { laterStateId, earlierStateId })
            {
                Assert.IsTrue(builder.TryRegisterState(
                    descriptorId,
                    1U,
                    new TestStateConfigFreezer(),
                    new StateRuntimeRegistration<
                        TestStateLogic,
                        TestStateConfigSchema,
                        TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                    null,
                    null,
                    null,
                    out CoCoDiagnostic diagnostic), diagnostic.Message);
            }

            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);

            CollectionAssert.AreEqual(
                new[] { earlierStateId, laterStateId },
                catalog.StateDescriptors.Select(descriptor => descriptor.DescriptorId));
            Assert.IsFalse(catalog.StateDescriptors is StateDescriptor[]);
        }

        [Test]
        public void FrozenConfigSnapshotIsDetachedFromItsAuthoringConfig()
        {
            StateDescriptorId descriptorId = CreateStateDescriptorId(10UL);
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic),
                registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);
            var authoring = new TestStateAuthoringConfig { Value = 17 };

            Assert.IsTrue(catalog.TryFreezeStateConfig(
                descriptorId,
                authoring,
                out FrozenConfigSnapshot snapshot,
                out CoCoDiagnostic snapshotDiagnostic),
                snapshotDiagnostic.Message);
            authoring.Value = 99;

            Assert.IsTrue(snapshot.IsValid);
            Assert.AreNotEqual(0UL, snapshot.Fingerprint);
            Assert.IsTrue(snapshot.TryRead(TestFrozenConfigSchemas.StateValue, out int frozenValue));
            Assert.AreEqual(17, frozenValue);
            Assert.AreEqual(1, StateGraphFixtureCounters.StateFreezeCalls);
            Assert.AreEqual(0, StateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, StateGraphFixtureCounters.MemoryConstructed);
        }

        [Test]
        public void CatalogRejectsAuthoringTypesFromTheUnityEditorTestAssembly()
        {
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterState(
                CreateStateDescriptorId(20UL),
                1U,
                new EditorAssemblyConfigFreezer(),
                new StateRuntimeRegistration<
                    EditorAssemblyStateLogic,
                    EditorAssemblyConfigSchema,
                    EditorAssemblyMemory>(EditorAssemblySchema),
                null,
                null,
                null,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidAuthoringDependency, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void OrdinaryStateSlotRejectsUnityValueTypes()
        {
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterStateSlot(
                StateGraphTestFactory.StateBlockId,
                StateGraphTestFactory.StateSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                UnityEngine.Vector3.zero,
                1UL,
                default,
                null,
                out CoCoDiagnostic diagnostic));

            Assert.AreEqual(CoCoDiagnosticCode.InvalidAuthoringDependency, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void OrdinaryStateSlotsRejectValuesStateFlowCannotMaterialize()
        {
            AssertOrdinaryStateSlotRejected(IntPtr.Zero);
            AssertOrdinaryStateSlotRejected(UIntPtr.Zero);
            AssertOrdinaryStateSlotRejected(default(TestPointerSizedStateValue));
        }

        [Test]
        public void DerivedStateSlotsRejectValuesStateFlowCannotMaterialize()
        {
            AssertDerivedStateSlotRejected(
                IntPtr.Zero,
                new DerivedStateRebuilderToken<
                    IntPtr,
                    TestRejectedValueStateRebuilder<IntPtr>>(1UL));
            AssertDerivedStateSlotRejected(
                UIntPtr.Zero,
                new DerivedStateRebuilderToken<
                    UIntPtr,
                    TestRejectedValueStateRebuilder<UIntPtr>>(2UL));
            AssertDerivedStateSlotRejected(
                default(TestPointerSizedStateValue),
                new DerivedStateRebuilderToken<
                    TestPointerSizedStateValue,
                    TestRejectedValueStateRebuilder<TestPointerSizedStateValue>>(3UL));
        }

        [Test]
        public void CatalogAcceptsDescriptorAssembliesThatUseCore()
        {
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsTrue(builder.TryRegisterState(
                CreateStateDescriptorId(21UL),
                1U,
                new LegacyCoreDependencyFreezer(),
                new StateRuntimeRegistration<
                    LegacyCoreDependencyLogic,
                    LegacyCoreDependencyConfigSchema,
                    LegacyCoreDependencyMemory>(LegacyCoreDependencySchemas.Schema),
                null,
                null,
                null,
                out CoCoDiagnostic diagnostic));

            Assert.IsTrue(diagnostic.IsNone, diagnostic.Message);
        }

        [Test]
        public void CatalogRejectsDescriptorAssembliesThatReferenceUnityAuthoring()
        {
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterState(
                CreateStateDescriptorId(22UL),
                1U,
                new AuthoringDependencyFreezer(),
                new StateRuntimeRegistration<
                    AuthoringDependencyLogic,
                    AuthoringDependencyConfigSchema,
                    AuthoringDependencyMemory>(AuthoringDependencySchemas.Schema),
                null,
                null,
                null,
                out CoCoDiagnostic diagnostic));

            Assert.AreEqual(CoCoDiagnosticCode.InvalidAuthoringDependency, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void DescriptorCanonicalizesDuplicateManifestRequirements()
        {
            IntentId intentId = CreateIntentId(30UL);
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                2,
                new IntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(201UL),
                out CoCoDiagnostic intentDiagnostic), intentDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                CreateStateDescriptorId(31UL),
                1U,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                new[] { intentId, intentId },
                null,
                null,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            Assert.IsTrue(catalog.TryGetStateDescriptor(
                CreateStateDescriptorId(31UL),
                out StateDescriptor descriptor));

            CollectionAssert.AreEqual(new[] { intentId }, descriptor.IntentRequirements);
        }

        [Test]
        public void SameManifestIdWithDifferentMetadataIsAConflict()
        {
            IntentId intentId = CreateIntentId(32UL);
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                2,
                new IntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(202UL),
                out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);

            Assert.IsFalse(builder.TryRegisterIntent(
                intentId,
                3,
                new IntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(203UL),
                out CoCoDiagnostic conflict));

            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, conflict.Code);
            Assert.IsTrue(conflict.IsError);
        }

        [Test]
        public void OperationAndContextIdsRejectConflictingMetadata()
        {
            OperationId operationId = CreateOperationId(33UL);
            StateBlockId blockId = BlockId(34UL);
            Assert.IsTrue(StateSlotId.TryCreate(6UL, 35UL, out StateSlotId slotId));
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterOperation(
                operationId,
                Mode.Continuous,
                new OperationViewFactoryToken<
                    ITestOperationSection,
                    TestOperationSectionViewFactory>(204UL),
                out CoCoDiagnostic operationDiagnostic), operationDiagnostic.Message);
            Assert.IsFalse(builder.TryRegisterOperation(
                operationId,
                Mode.Discrete,
                new OperationViewFactoryToken<
                    ITestOperationSection,
                    TestOperationSectionViewFactory>(205UL),
                out CoCoDiagnostic operationConflict));
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, operationConflict.Code);

            Assert.IsTrue(builder.TryRegisterStateBlock(
                blockId,
                StateBlockOwner.Graph,
                out CoCoDiagnostic blockDiagnostic), blockDiagnostic.Message);
            Assert.IsFalse(builder.TryRegisterStateBlock(
                blockId,
                StateBlockOwner.Host,
                out CoCoDiagnostic blockConflict));
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, blockConflict.Code);

            Assert.IsTrue(builder.TryRegisterStateSlot(
                blockId,
                slotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                206UL,
                default,
                null,
                out CoCoDiagnostic slotDiagnostic), slotDiagnostic.Message);
            Assert.IsFalse(builder.TryRegisterStateSlot(
                blockId,
                slotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                1,
                207UL,
                default,
                null,
                out CoCoDiagnostic slotConflict));
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, slotConflict.Code);
        }

        [Test]
        public void DescriptorRevisionContributesToCatalogFingerprint()
        {
            GraphDescriptorCatalog first = StateGraphTestFactory.CreateCatalog(false, 1U);
            GraphDescriptorCatalog second = StateGraphTestFactory.CreateCatalog(false, 2U);

            Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
        }

        [Test]
        public void StateActionProgressCapabilityContributesToCatalogFingerprint()
        {
            GraphDescriptorCatalog withoutProvider =
                StateGraphTestFactory.CreateCatalog(false);
            GraphDescriptorCatalog withProvider =
                StateGraphTestFactory.CreateCatalog(
                    false,
                    providesActionProgress: true);

            Assert.AreNotEqual(withoutProvider.Fingerprint, withProvider.Fingerprint);
            Assert.IsTrue(withProvider.TryGetStateDescriptor(
                StateGraphTestFactory.StateDescriptorId,
                out StateDescriptor descriptor));
            Assert.IsTrue(descriptor.ProvidesActionProgress);
            Assert.IsTrue(descriptor.RuntimeRegistration.ProvidesActionProgress);
        }

        [Test]
        public void SameDescriptorIdWithDifferentRevisionIsRejected()
        {
            StateDescriptorId descriptorId = CreateStateDescriptorId(36UL);
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);

            Assert.IsFalse(builder.TryRegisterState(
                descriptorId,
                2U,
                new TestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic conflict));

            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, conflict.Code);
            Assert.IsTrue(conflict.IsError);
        }

        [Test]
        public void SameFactoryTypesWithDifferentSemanticsHaveDifferentCatalogAndManifestIdentity()
        {
            GraphDescriptorCatalog first = StateGraphTestFactory.CreateCatalog(
                true,
                1U,
                301UL,
                401UL);
            GraphDescriptorCatalog second = StateGraphTestFactory.CreateCatalog(
                true,
                1U,
                302UL,
                402UL);

            CompiledStateGraph firstGraph = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateHierarchicalSource(),
                first).Graph;
            CompiledStateGraph secondGraph = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateHierarchicalSource(),
                second).Graph;

            Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
            Assert.AreNotEqual(
                firstGraph.IntentRequirements.LayoutId,
                secondGraph.IntentRequirements.LayoutId);
            Assert.AreNotEqual(
                firstGraph.OperationProvides.LayoutId,
                secondGraph.OperationProvides.LayoutId);
            Assert.AreEqual(
                typeof(TestIntentReducerFactory),
                firstGraph.IntentRequirements.Requirements[0].ReducerFactoryType);
            Assert.AreEqual(
                typeof(TestIntentReducerFactory),
                secondGraph.IntentRequirements.Requirements[0].ReducerFactoryType);
            Assert.AreEqual(
                301UL,
                firstGraph.IntentRequirements.Requirements[0].ReducerFactorySemanticFingerprint);
            Assert.AreEqual(
                302UL,
                secondGraph.IntentRequirements.Requirements[0].ReducerFactorySemanticFingerprint);
        }

        [Test]
        public void TokenReassignmentAfterCatalogFreezeCannotChangeCompiledManifest()
        {
            var token = new IntentReducerFactoryToken<
                TestIntent,
                TestIntentReducer,
                TestIntentReducerFactory>(501UL);
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(
                true,
                1U,
                token.SemanticFingerprint,
                502UL);

            token = new IntentReducerFactoryToken<
                TestIntent,
                TestIntentReducer,
                TestIntentReducerFactory>(999UL);
            CompiledStateGraph graph = new StateGraphCompiler().Compile(
                StateGraphTestFactory.CreateHierarchicalSource(),
                catalog).Graph;

            Assert.AreEqual(999UL, token.SemanticFingerprint);
            Assert.AreEqual(
                501UL,
                graph.IntentRequirements.Requirements[0].ReducerFactorySemanticFingerprint);
            Assert.AreEqual(0, StateGraphFixtureCounters.ReducerCreated);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StateFreezerFailureAlwaysReturnsInvalidFrozenConfig(bool throws)
        {
            StateDescriptorId descriptorId = CreateStateDescriptorId(40UL);
            IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema> freezer = throws
                ? new ThrowingStateConfigFreezer()
                : new FalseWithoutDiagnosticStateConfigFreezer();
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                freezer,
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic), registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);

            Assert.IsFalse(catalog.TryFreezeStateConfig(
                descriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                out FrozenConfigSnapshot snapshot,
                out CoCoDiagnostic diagnostic));

            Assert.IsNull(snapshot);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void SchemaRejectsMutableOrNestedValueShapes()
        {
            var builder = new FrozenConfigSchemaBuilder<LocalConfigSchema>();

            Assert.IsFalse(builder.TryAddField(
                FieldId(50UL),
                out FrozenConfigField<LocalConfigSchema, int[]> array,
                out CoCoDiagnostic arrayDiagnostic));
            Assert.IsFalse(array.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, arrayDiagnostic.Code);
            Assert.IsFalse(builder.TryAddField(
                FieldId(51UL),
                out FrozenConfigField<LocalConfigSchema, System.Collections.Generic.List<int>> list,
                out CoCoDiagnostic listDiagnostic));
            Assert.IsFalse(list.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, listDiagnostic.Code);
            Assert.IsFalse(builder.TryAddArrayField(
                FieldId(52UL),
                out FrozenConfigArrayField<LocalConfigSchema, int[]> nestedArray,
                out CoCoDiagnostic nestedDiagnostic));
            Assert.IsFalse(nestedArray.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, nestedDiagnostic.Code);
            Assert.IsFalse(builder.TryAddField(
                FieldId(53UL),
                out FrozenConfigField<LocalConfigSchema, int?> nullable,
                out CoCoDiagnostic nullableDiagnostic));
            Assert.IsFalse(nullable.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, nullableDiagnostic.Code);
        }

        [Test]
        public void FrozenArrayInputIsDefensivelyCopied()
        {
            StateDescriptorId descriptorId = CreateStateDescriptorId(55UL);
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TestArrayConfigFreezer(),
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestArrayConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.ArraySchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic), registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            var source = new[] { 1, 2, 3 };
            var authoring = new TestStateAuthoringConfig { Values = source };

            Assert.IsTrue(catalog.TryFreezeStateConfig(
                descriptorId,
                authoring,
                out FrozenConfigSnapshot snapshot,
                out CoCoDiagnostic snapshotDiagnostic), snapshotDiagnostic.Message);
            source[0] = 99;

            Assert.IsTrue(snapshot.TryReadArray(
                TestFrozenConfigSchemas.ArrayValues,
                out FrozenArray<int> frozenValues));
            Assert.AreEqual(1, frozenValues[0]);
            Assert.AreEqual(3, frozenValues.Count);
        }

        [Test]
        public void WriterFailureIsPermanentAndSealingRequiresEveryField()
        {
            FrozenConfigWriter<TestStateConfigSchema> missingWriter =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Assert.IsFalse(missingWriter.TrySeal(
                out FrozenConfigSnapshot missingSnapshot,
                out CoCoDiagnostic missingDiagnostic));
            Assert.IsNull(missingSnapshot);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, missingDiagnostic.Code);

            FrozenConfigWriter<TestStateConfigSchema> duplicateWriter =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Assert.IsTrue(duplicateWriter.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                1,
                out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);
            Assert.IsFalse(duplicateWriter.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                2,
                out CoCoDiagnostic duplicateDiagnostic));
            Assert.IsFalse(duplicateWriter.TrySeal(
                out FrozenConfigSnapshot duplicateSnapshot,
                out CoCoDiagnostic sealDiagnostic));
            Assert.IsNull(duplicateSnapshot);
            Assert.AreEqual(duplicateDiagnostic.Message, sealDiagnostic.Message);
        }

        [Test]
        public void SealedWriterCannotMutateAnExistingSnapshot()
        {
            FrozenConfigWriter<TestStateConfigSchema> writer =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Assert.IsTrue(writer.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                7,
                out CoCoDiagnostic writeDiagnostic), writeDiagnostic.Message);
            Assert.IsTrue(writer.TrySeal(
                out FrozenConfigSnapshot snapshot,
                out CoCoDiagnostic sealDiagnostic), sealDiagnostic.Message);

            Assert.IsFalse(writer.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                99,
                out CoCoDiagnostic sealedDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, sealedDiagnostic.Code);
            Assert.IsTrue(snapshot.TryRead(TestFrozenConfigSchemas.StateValue, out int value));
            Assert.AreEqual(7, value);
        }

        [Test]
        public void UnknownAndNullWritesPermanentlyFailTheirWriter()
        {
            FrozenConfigWriter<TestStateConfigSchema> unknownWriter =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            var unknownField = new FrozenConfigField<TestStateConfigSchema, int>(FieldId(59UL));
            Assert.IsFalse(unknownWriter.TryWrite(
                unknownField,
                1,
                out CoCoDiagnostic unknownDiagnostic));
            Assert.IsFalse(unknownWriter.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                2,
                out CoCoDiagnostic afterFailureDiagnostic));
            Assert.AreEqual(unknownDiagnostic.Message, afterFailureDiagnostic.Message);

            var builder = new FrozenConfigSchemaBuilder<LocalConfigSchema>();
            Assert.IsTrue(builder.TryAddField(
                FieldId(58UL),
                out FrozenConfigField<LocalConfigSchema, string> textField,
                out CoCoDiagnostic fieldDiagnostic), fieldDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out FrozenConfigSchema<LocalConfigSchema> schema,
                out CoCoDiagnostic schemaDiagnostic), schemaDiagnostic.Message);
            FrozenConfigWriter<LocalConfigSchema> nullWriter = schema.CreateWriter();
            Assert.IsFalse(nullWriter.TryWrite(
                textField,
                null,
                out CoCoDiagnostic nullDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, nullDiagnostic.Code);
        }

        [Test]
        public void WrongTypedOrArrayKindFieldPermanentlyFailsItsWriter()
        {
            var wrongTypeField = new FrozenConfigField<TestStateConfigSchema, uint>(
                TestFrozenConfigSchemas.StateValue.FieldId);
            FrozenConfigWriter<TestStateConfigSchema> wrongTypeWriter =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();

            Assert.IsFalse(wrongTypeWriter.TryWrite(
                wrongTypeField,
                7U,
                out CoCoDiagnostic wrongTypeDiagnostic));
            Assert.IsFalse(wrongTypeWriter.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                7,
                out CoCoDiagnostic afterWrongTypeDiagnostic));
            Assert.AreEqual(wrongTypeDiagnostic.Message, afterWrongTypeDiagnostic.Message);

            var wrongKindField = new FrozenConfigArrayField<TestStateConfigSchema, int>(
                TestFrozenConfigSchemas.StateValue.FieldId);
            FrozenConfigWriter<TestStateConfigSchema> wrongKindWriter =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Assert.IsFalse(wrongKindWriter.TryWriteArray(
                wrongKindField,
                new[] { 7 },
                out CoCoDiagnostic wrongKindDiagnostic));
            Assert.IsTrue(wrongKindWriter.HasFailed);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, wrongKindDiagnostic.Code);
        }

        [Test]
        public void EmptySchemaProducesAValidImmutableSnapshot()
        {
            var builder = new FrozenConfigSchemaBuilder<LocalConfigSchema>();
            Assert.IsTrue(builder.TryFreeze(
                out FrozenConfigSchema<LocalConfigSchema> schema,
                out CoCoDiagnostic schemaDiagnostic), schemaDiagnostic.Message);
            FrozenConfigWriter<LocalConfigSchema> writer = schema.CreateWriter();

            Assert.IsTrue(writer.TrySeal(
                out FrozenConfigSnapshot snapshot,
                out CoCoDiagnostic snapshotDiagnostic), snapshotDiagnostic.Message);
            Assert.IsTrue(snapshot.IsValid);
            Assert.AreEqual(typeof(LocalConfigSchema), snapshot.SchemaType);
            Assert.AreEqual(schema.Fingerprint, snapshot.SchemaFingerprint);
        }

        [Test]
        public void WarmedTypedReadsAllocateNothingAndAreSafeForParallelReaders()
        {
            FrozenConfigSnapshot scalar = StateGraphTestFactory.StateConfig(42);
            FrozenConfigWriter<TestArrayConfigSchema> arrayWriter =
                TestFrozenConfigSchemas.ArraySchema.CreateWriter();
            Assert.IsTrue(arrayWriter.TryWriteArray(
                TestFrozenConfigSchemas.ArrayValues,
                new[] { 1, 2, 3 },
                out CoCoDiagnostic writeDiagnostic), writeDiagnostic.Message);
            Assert.IsTrue(arrayWriter.TrySeal(
                out FrozenConfigSnapshot array,
                out CoCoDiagnostic sealDiagnostic), sealDiagnostic.Message);

            Assert.IsTrue(scalar.TryRead(TestFrozenConfigSchemas.StateValue, out int warmScalar));
            Assert.IsTrue(array.TryReadArray(
                TestFrozenConfigSchemas.ArrayValues,
                out FrozenArray<int> warmArray));
            Assert.AreEqual(42, warmScalar);
            Assert.AreEqual(3, warmArray.Count);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int observed = 0;
            for (int index = 0; index < 10000; index++)
            {
                scalar.TryRead(TestFrozenConfigSchemas.StateValue, out int scalarValue);
                array.TryReadArray(
                    TestFrozenConfigSchemas.ArrayValues,
                    out FrozenArray<int> arrayValue);
                observed ^= scalarValue ^ arrayValue[1];
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated);
            Assert.AreEqual(0, observed);

            int failures = 0;
            Parallel.For(0, 10000, _ =>
            {
                if (!scalar.TryRead(TestFrozenConfigSchemas.StateValue, out int scalarValue) ||
                    scalarValue != 42 ||
                    !array.TryReadArray(
                        TestFrozenConfigSchemas.ArrayValues,
                        out FrozenArray<int> arrayValue) ||
                    arrayValue.Count != 3 ||
                    arrayValue[2] != 3)
                {
                    Interlocked.Increment(ref failures);
                }
            });
            Assert.AreEqual(0, failures);
        }

        [Test]
        public void SchemaAndSnapshotFingerprintsAreCanonicalAndValueSensitive()
        {
            FrozenConfigSchema<LocalConfigSchema> first = CreateLocalSchema(false);
            FrozenConfigSchema<LocalConfigSchema> second = CreateLocalSchema(true);
            Assert.AreEqual(first.Fingerprint, second.Fingerprint);

            FrozenConfigSnapshot firstValue = StateGraphTestFactory.StateConfig(10);
            FrozenConfigSnapshot sameValue = StateGraphTestFactory.StateConfig(10);
            FrozenConfigSnapshot differentValue = StateGraphTestFactory.StateConfig(11);
            Assert.AreEqual(firstValue.Fingerprint, sameValue.Fingerprint);
            Assert.AreNotEqual(firstValue.Fingerprint, differentValue.Fingerprint);

            var alternateBuilder = new FrozenConfigSchemaBuilder<TestStateConfigSchema>();
            Assert.IsTrue(alternateBuilder.TryAddField(
                FieldId(62UL),
                out FrozenConfigField<TestStateConfigSchema, int> alternateField,
                out CoCoDiagnostic fieldDiagnostic), fieldDiagnostic.Message);
            Assert.IsTrue(alternateBuilder.TryFreeze(
                out FrozenConfigSchema<TestStateConfigSchema> alternateSchema,
                out CoCoDiagnostic schemaDiagnostic), schemaDiagnostic.Message);
            Assert.IsFalse(firstValue.MatchesSchema(alternateSchema));
            Assert.IsFalse(firstValue.TryRead(
                new FrozenConfigField<LocalConfigSchema, int>(
                    TestFrozenConfigSchemas.StateValue.FieldId),
                out int wrongSchemaValue));
        }

        [Test]
        public void CatalogKeepsSortedImmutableAuthorAssemblyRoots()
        {
            GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(true);
            var actual = new string[catalog.AuthorAssemblyRootNames.Count];
            for (int index = 0; index < actual.Length; index++)
            {
                actual[index] = catalog.AuthorAssemblyRootNames[index];
            }

            var sorted = (string[])actual.Clone();
            Array.Sort(sorted, StringComparer.Ordinal);
            CollectionAssert.AreEqual(sorted, actual);
            CollectionAssert.Contains(
                actual,
                typeof(TestStateLogic).Assembly.GetName().Name);
        }

        #endregion

        #region Internal Logic

        private static readonly FrozenConfigSchema<EditorAssemblyConfigSchema> EditorAssemblySchema =
            CreateEditorAssemblySchema();

        private static StateDescriptorId CreateStateDescriptorId(ulong low)
        {
            Assert.IsTrue(StateDescriptorId.TryCreate(1UL, low, out StateDescriptorId id));
            return id;
        }

        private static void AssertOrdinaryStateSlotRejected<TValue>(TValue value)
            where TValue : unmanaged
        {
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterStateSlot(
                StateGraphTestFactory.StateBlockId,
                StateGraphTestFactory.StateSlotId,
                ContextProjection.Temporal,
                ContextRestorePolicy.Stored,
                value,
                1UL,
                default,
                null,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateSlot, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        private static void AssertDerivedStateSlotRejected<TValue, TRebuilder>(
            TValue value,
            DerivedStateRebuilderToken<TValue, TRebuilder> token)
            where TValue : unmanaged
            where TRebuilder : IDerivedStateRebuilder<TValue>
        {
            var builder = new GraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterDerivedStateSlot(
                StateGraphTestFactory.StateBlockId,
                StateGraphTestFactory.StateSlotId,
                ContextProjection.Temporal,
                value,
                1UL,
                default,
                new[] { StateGraphTestFactory.StateSlotId },
                token,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateSlot, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        private static IntentId CreateIntentId(ulong low)
        {
            Assert.IsTrue(IntentId.TryCreate(3UL, low, out IntentId id));
            return id;
        }

        private static OperationId CreateOperationId(ulong low)
        {
            Assert.IsTrue(OperationId.TryCreate(4UL, low, out OperationId id));
            return id;
        }

        private static StateBlockId BlockId(ulong low)
        {
            Assert.IsTrue(StateBlockId.TryCreate(5UL, low, out StateBlockId id));
            return id;
        }

        private static FrozenConfigFieldId FieldId(ulong low)
        {
            Assert.IsTrue(FrozenConfigFieldId.TryCreate(6UL, low, out FrozenConfigFieldId id));
            return id;
        }

        private static FrozenConfigSchema<LocalConfigSchema> CreateLocalSchema(bool reverse)
        {
            var builder = new FrozenConfigSchemaBuilder<LocalConfigSchema>();
            FrozenConfigFieldId firstId = FieldId(60UL);
            FrozenConfigFieldId secondId = FieldId(61UL);
            if (reverse)
            {
                Assert.IsTrue(builder.TryAddArrayField(
                    secondId,
                    out FrozenConfigArrayField<LocalConfigSchema, string> second,
                    out CoCoDiagnostic secondDiagnostic), secondDiagnostic.Message);
                Assert.IsTrue(builder.TryAddField(
                    firstId,
                    out FrozenConfigField<LocalConfigSchema, double> first,
                    out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);
            }
            else
            {
                Assert.IsTrue(builder.TryAddField(
                    firstId,
                    out FrozenConfigField<LocalConfigSchema, double> first,
                    out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);
                Assert.IsTrue(builder.TryAddArrayField(
                    secondId,
                    out FrozenConfigArrayField<LocalConfigSchema, string> second,
                    out CoCoDiagnostic secondDiagnostic), secondDiagnostic.Message);
            }

            Assert.IsTrue(builder.TryFreeze(
                out FrozenConfigSchema<LocalConfigSchema> schema,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return schema;
        }

        private static FrozenConfigSchema<EditorAssemblyConfigSchema> CreateEditorAssemblySchema()
        {
            var builder = new FrozenConfigSchemaBuilder<EditorAssemblyConfigSchema>();
            Assert.IsTrue(builder.TryFreeze(
                out FrozenConfigSchema<EditorAssemblyConfigSchema> schema,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return schema;
        }

        private readonly struct LocalConfigSchema : IFrozenConfigSchema
        {
        }

        private readonly struct EditorAssemblyConfigSchema : IFrozenConfigSchema
        {
        }

        [Serializable]
        private sealed class EditorAssemblyAuthoringConfig : StateConfig
        {
        }

        private sealed class EditorAssemblyStateLogic : StateLogic
        {
        }

        private sealed class EditorAssemblyMemory : StateActivationMemory
        {
        }

        private sealed class EditorAssemblyConfigFreezer :
            IConfigFreezer<EditorAssemblyAuthoringConfig, EditorAssemblyConfigSchema>
        {
            public bool TryFreeze(
                EditorAssemblyAuthoringConfig source,
                FrozenConfigWriter<EditorAssemblyConfigSchema> writer,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        #endregion
    }
}
