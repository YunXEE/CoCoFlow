using System;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core.StateGraph.Tests.AuthoringDependencyFixtures;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using CoCoFlow.Runtime.Core.StateGraph.Tests.LegacyCoreDependencyFixtures;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoGraphDescriptorCatalogTests
    {
        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphFixtureCounters.Reset();
        }

        [Test]
        public void CatalogFreezesPureDescriptorsWithoutConstructingRuntimeObjects()
        {
            CoCoStateDescriptorId stateDescriptorId = StateDescriptorId(1UL);
            CoCoConditionDescriptorId conditionDescriptorId = ConditionDescriptorId(2UL);
            CoCoIntentId intentId = IntentId(3UL);
            CoCoOperationSectionId sectionId = SectionId(4UL);
            CoCoStateBlockId blockId = BlockId(5UL);
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                4,
                new CoCoIntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(101UL),
                out CoCoDiagnostic intentDiagnostic),
                intentDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterOperationSection(
                sectionId,
                CoCoOperationSectionMode.Continuous,
                new CoCoOperationSectionViewFactoryToken<
                    ITestOperationSection,
                    TestOperationSectionViewFactory>(102UL),
                out CoCoDiagnostic operationDiagnostic),
                operationDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterStateBlock(
                blockId,
                CoCoStateBlockOwner.Graph,
                out CoCoDiagnostic blockDiagnostic),
                blockDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                stateDescriptorId,
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                new[] { intentId },
                new[] { sectionId },
                new[] { blockId },
                out CoCoDiagnostic stateDiagnostic),
                stateDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterCondition(
                conditionDescriptorId,
                1U,
                new TestConditionConfigFreezer(),
                new CoCoConditionRuntimeRegistration<
                    TestStateCondition,
                    TestConditionConfigSchema>(TestFrozenConfigSchemas.ConditionSchema),
                new[] { intentId },
                new[] { sectionId },
                new[] { blockId },
                out CoCoDiagnostic conditionDiagnostic),
                conditionDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);

            Assert.IsTrue(builder.IsFrozen);
            Assert.IsTrue(catalog.IsFrozen);
            Assert.AreNotEqual(0UL, catalog.Fingerprint);
            Assert.AreEqual(1, catalog.StateDescriptorCount);
            Assert.AreEqual(1, catalog.ConditionDescriptorCount);
            Assert.IsTrue(catalog.TryGetStateDescriptor(
                stateDescriptorId,
                out CoCoStateDescriptor stateDescriptor));
            Assert.AreEqual(typeof(TestStateLogic), stateDescriptor.LogicType);
            Assert.AreEqual(typeof(TestStateAuthoringConfig), stateDescriptor.AuthoringConfigType);
            Assert.AreEqual(typeof(TestStateConfigSchema), stateDescriptor.ConfigSchemaType);
            Assert.AreEqual(
                TestFrozenConfigSchemas.StateSchema.Fingerprint,
                stateDescriptor.ConfigSchemaFingerprint);
            Assert.AreEqual(typeof(TestActivationMemory), stateDescriptor.ActivationMemoryType);
            CollectionAssert.AreEqual(new[] { intentId }, stateDescriptor.IntentRequirements);
            CollectionAssert.AreEqual(new[] { sectionId }, stateDescriptor.OperationProvides);
            CollectionAssert.AreEqual(new[] { blockId }, stateDescriptor.ContextStateRequirements);
            Assert.IsTrue(catalog.TryGetConditionDescriptor(
                conditionDescriptorId,
                out CoCoConditionDescriptor conditionDescriptor));
            Assert.AreEqual(typeof(TestStateCondition), conditionDescriptor.ConditionType);
            CollectionAssert.AreEqual(new[] { intentId }, conditionDescriptor.IntentRequirements);
            CollectionAssert.AreEqual(new[] { sectionId }, conditionDescriptor.OperationProvides);
            CollectionAssert.AreEqual(new[] { blockId }, conditionDescriptor.ContextStateRequirements);

            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.MemoryConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ConditionConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ReducerCreated);
        }

        [Test]
        public void FrozenConfigSnapshotIsDetachedFromItsAuthoringConfig()
        {
            CoCoStateDescriptorId descriptorId = StateDescriptorId(10UL);
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic),
                registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic),
                freezeDiagnostic.Message);
            var authoring = new TestStateAuthoringConfig { Value = 17 };

            Assert.IsTrue(catalog.TryFreezeStateConfig(
                descriptorId,
                authoring,
                out CoCoFrozenConfigSnapshot snapshot,
                out CoCoDiagnostic snapshotDiagnostic),
                snapshotDiagnostic.Message);
            authoring.Value = 99;

            Assert.IsTrue(snapshot.IsValid);
            Assert.AreNotEqual(0UL, snapshot.Fingerprint);
            Assert.IsTrue(snapshot.TryRead(TestFrozenConfigSchemas.StateValue, out int frozenValue));
            Assert.AreEqual(17, frozenValue);
            Assert.AreEqual(1, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.LogicConstructed);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.MemoryConstructed);
        }

        [Test]
        public void CatalogRejectsAuthoringTypesFromTheUnityEditorTestAssembly()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterState(
                StateDescriptorId(20UL),
                1U,
                new EditorAssemblyConfigFreezer(),
                new CoCoStateRuntimeRegistration<
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
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterStateSlot(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateGraphTestFactory.StateSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
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
                new CoCoDerivedStateRebuilderToken<
                    IntPtr,
                    TestRejectedValueStateRebuilder<IntPtr>>(1UL));
            AssertDerivedStateSlotRejected(
                UIntPtr.Zero,
                new CoCoDerivedStateRebuilderToken<
                    UIntPtr,
                    TestRejectedValueStateRebuilder<UIntPtr>>(2UL));
            AssertDerivedStateSlotRejected(
                default(TestPointerSizedStateValue),
                new CoCoDerivedStateRebuilderToken<
                    TestPointerSizedStateValue,
                    TestRejectedValueStateRebuilder<TestPointerSizedStateValue>>(3UL));
        }

        [Test]
        public void CatalogRejectsDescriptorAssembliesThatReferenceLegacyCore()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterState(
                StateDescriptorId(21UL),
                1U,
                new LegacyCoreDependencyFreezer(),
                new CoCoStateRuntimeRegistration<
                    LegacyCoreDependencyLogic,
                    LegacyCoreDependencyConfigSchema,
                    LegacyCoreDependencyMemory>(LegacyCoreDependencySchemas.Schema),
                null,
                null,
                null,
                out CoCoDiagnostic diagnostic));

            Assert.AreEqual(CoCoDiagnosticCode.InvalidAuthoringDependency, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void CatalogRejectsDescriptorAssembliesThatReferenceUnityAuthoring()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterState(
                StateDescriptorId(22UL),
                1U,
                new AuthoringDependencyFreezer(),
                new CoCoStateRuntimeRegistration<
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
            CoCoIntentId intentId = IntentId(30UL);
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                2,
                new CoCoIntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(201UL),
                out CoCoDiagnostic intentDiagnostic), intentDiagnostic.Message);
            Assert.IsTrue(builder.TryRegisterState(
                StateDescriptorId(31UL),
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                new[] { intentId, intentId },
                null,
                null,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            Assert.IsTrue(catalog.TryGetStateDescriptor(
                StateDescriptorId(31UL),
                out CoCoStateDescriptor descriptor));

            CollectionAssert.AreEqual(new[] { intentId }, descriptor.IntentRequirements);
        }

        [Test]
        public void SameManifestIdWithDifferentMetadataIsAConflict()
        {
            CoCoIntentId intentId = IntentId(32UL);
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                2,
                new CoCoIntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(202UL),
                out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);

            Assert.IsFalse(builder.TryRegisterIntent(
                intentId,
                3,
                new CoCoIntentReducerFactoryToken<
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
            CoCoOperationSectionId sectionId = SectionId(33UL);
            CoCoStateBlockId blockId = BlockId(34UL);
            Assert.IsTrue(CoCoStateSlotId.TryCreate(6UL, 35UL, out CoCoStateSlotId slotId));
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterOperationSection(
                sectionId,
                CoCoOperationSectionMode.Continuous,
                new CoCoOperationSectionViewFactoryToken<
                    ITestOperationSection,
                    TestOperationSectionViewFactory>(204UL),
                out CoCoDiagnostic operationDiagnostic), operationDiagnostic.Message);
            Assert.IsFalse(builder.TryRegisterOperationSection(
                sectionId,
                CoCoOperationSectionMode.Discrete,
                new CoCoOperationSectionViewFactoryToken<
                    ITestOperationSection,
                    TestOperationSectionViewFactory>(205UL),
                out CoCoDiagnostic operationConflict));
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, operationConflict.Code);

            Assert.IsTrue(builder.TryRegisterStateBlock(
                blockId,
                CoCoStateBlockOwner.Graph,
                out CoCoDiagnostic blockDiagnostic), blockDiagnostic.Message);
            Assert.IsFalse(builder.TryRegisterStateBlock(
                blockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnostic blockConflict));
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, blockConflict.Code);

            Assert.IsTrue(builder.TryRegisterStateSlot(
                blockId,
                slotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
                1,
                206UL,
                default,
                null,
                out CoCoDiagnostic slotDiagnostic), slotDiagnostic.Message);
            Assert.IsFalse(builder.TryRegisterStateSlot(
                blockId,
                slotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
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
            CoCoGraphDescriptorCatalog first = CoCoStateGraphTestFactory.CreateCatalog(false, 1U);
            CoCoGraphDescriptorCatalog second = CoCoStateGraphTestFactory.CreateCatalog(false, 2U);

            Assert.AreNotEqual(first.Fingerprint, second.Fingerprint);
        }

        [Test]
        public void SameDescriptorIdWithDifferentRevisionIsRejected()
        {
            CoCoStateDescriptorId descriptorId = StateDescriptorId(36UL);
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
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
                new CoCoStateRuntimeRegistration<
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
            CoCoGraphDescriptorCatalog first = CoCoStateGraphTestFactory.CreateCatalog(
                true,
                1U,
                301UL,
                401UL);
            CoCoGraphDescriptorCatalog second = CoCoStateGraphTestFactory.CreateCatalog(
                true,
                1U,
                302UL,
                402UL);

            CoCoCompiledStateGraph firstGraph = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
                first).Graph;
            CoCoCompiledStateGraph secondGraph = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
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
            var token = new CoCoIntentReducerFactoryToken<
                TestIntent,
                TestIntentReducer,
                TestIntentReducerFactory>(501UL);
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(
                true,
                1U,
                token.SemanticFingerprint,
                502UL);

            token = new CoCoIntentReducerFactoryToken<
                TestIntent,
                TestIntentReducer,
                TestIntentReducerFactory>(999UL);
            CoCoCompiledStateGraph graph = new CoCoStateGraphCompiler().Compile(
                CoCoStateGraphTestFactory.CreateHierarchicalSource(),
                catalog).Graph;

            Assert.AreEqual(999UL, token.SemanticFingerprint);
            Assert.AreEqual(
                501UL,
                graph.IntentRequirements.Requirements[0].ReducerFactorySemanticFingerprint);
            Assert.AreEqual(0, CoCoStateGraphFixtureCounters.ReducerCreated);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void StateFreezerFailureAlwaysReturnsInvalidFrozenConfig(bool throws)
        {
            CoCoStateDescriptorId descriptorId = StateDescriptorId(40UL);
            ICoCoConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema> freezer = throws
                ? new ThrowingStateConfigFreezer()
                : new FalseWithoutDiagnosticStateConfigFreezer();
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                freezer,
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic), registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);

            Assert.IsFalse(catalog.TryFreezeStateConfig(
                descriptorId,
                new TestStateAuthoringConfig { Value = 1 },
                out CoCoFrozenConfigSnapshot snapshot,
                out CoCoDiagnostic diagnostic));

            Assert.IsNull(snapshot);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ConditionFreezerFailureAlwaysReturnsInvalidFrozenConfig(bool throws)
        {
            CoCoConditionDescriptorId descriptorId = ConditionDescriptorId(41UL);
            ICoCoConfigFreezer<TestConditionAuthoringConfig, TestConditionConfigSchema> freezer = throws
                ? new ThrowingConditionConfigFreezer()
                : new FalseWithoutDiagnosticConditionConfigFreezer();
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterCondition(
                descriptorId,
                1U,
                freezer,
                new CoCoConditionRuntimeRegistration<
                    TestStateCondition,
                    TestConditionConfigSchema>(TestFrozenConfigSchemas.ConditionSchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic), registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);

            Assert.IsFalse(catalog.TryFreezeConditionConfig(
                descriptorId,
                new TestConditionAuthoringConfig { Threshold = 1 },
                out CoCoFrozenConfigSnapshot snapshot,
                out CoCoDiagnostic diagnostic));

            Assert.IsNull(snapshot);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        [Test]
        public void SchemaRejectsMutableOrNestedValueShapes()
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<LocalConfigSchema>();

            Assert.IsFalse(builder.TryAddField(
                FieldId(50UL),
                out CoCoFrozenConfigField<LocalConfigSchema, int[]> array,
                out CoCoDiagnostic arrayDiagnostic));
            Assert.IsFalse(array.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, arrayDiagnostic.Code);
            Assert.IsFalse(builder.TryAddField(
                FieldId(51UL),
                out CoCoFrozenConfigField<LocalConfigSchema, System.Collections.Generic.List<int>> list,
                out CoCoDiagnostic listDiagnostic));
            Assert.IsFalse(list.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, listDiagnostic.Code);
            Assert.IsFalse(builder.TryAddArrayField(
                FieldId(52UL),
                out CoCoFrozenConfigArrayField<LocalConfigSchema, int[]> nestedArray,
                out CoCoDiagnostic nestedDiagnostic));
            Assert.IsFalse(nestedArray.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, nestedDiagnostic.Code);
            Assert.IsFalse(builder.TryAddField(
                FieldId(53UL),
                out CoCoFrozenConfigField<LocalConfigSchema, int?> nullable,
                out CoCoDiagnostic nullableDiagnostic));
            Assert.IsFalse(nullable.IsValid);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, nullableDiagnostic.Code);
        }

        [Test]
        public void FrozenArrayInputIsDefensivelyCopied()
        {
            CoCoStateDescriptorId descriptorId = StateDescriptorId(55UL);
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                descriptorId,
                1U,
                new TestArrayConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestArrayConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.ArraySchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic), registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            var source = new[] { 1, 2, 3 };
            var authoring = new TestStateAuthoringConfig { Values = source };

            Assert.IsTrue(catalog.TryFreezeStateConfig(
                descriptorId,
                authoring,
                out CoCoFrozenConfigSnapshot snapshot,
                out CoCoDiagnostic snapshotDiagnostic), snapshotDiagnostic.Message);
            source[0] = 99;

            Assert.IsTrue(snapshot.TryReadArray(
                TestFrozenConfigSchemas.ArrayValues,
                out CoCoFrozenArray<int> frozenValues));
            Assert.AreEqual(1, frozenValues[0]);
            Assert.AreEqual(3, frozenValues.Count);
        }

        [Test]
        public void WriterFailureIsPermanentAndSealingRequiresEveryField()
        {
            CoCoFrozenConfigWriter<TestStateConfigSchema> missingWriter =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Assert.IsFalse(missingWriter.TrySeal(
                out CoCoFrozenConfigSnapshot missingSnapshot,
                out CoCoDiagnostic missingDiagnostic));
            Assert.IsNull(missingSnapshot);
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, missingDiagnostic.Code);

            CoCoFrozenConfigWriter<TestStateConfigSchema> duplicateWriter =
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
                out CoCoFrozenConfigSnapshot duplicateSnapshot,
                out CoCoDiagnostic sealDiagnostic));
            Assert.IsNull(duplicateSnapshot);
            Assert.AreEqual(duplicateDiagnostic.Message, sealDiagnostic.Message);
        }

        [Test]
        public void SealedWriterCannotMutateAnExistingSnapshot()
        {
            CoCoFrozenConfigWriter<TestStateConfigSchema> writer =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            Assert.IsTrue(writer.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                7,
                out CoCoDiagnostic writeDiagnostic), writeDiagnostic.Message);
            Assert.IsTrue(writer.TrySeal(
                out CoCoFrozenConfigSnapshot snapshot,
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
            CoCoFrozenConfigWriter<TestStateConfigSchema> unknownWriter =
                TestFrozenConfigSchemas.StateSchema.CreateWriter();
            var unknownField = new CoCoFrozenConfigField<TestStateConfigSchema, int>(FieldId(59UL));
            Assert.IsFalse(unknownWriter.TryWrite(
                unknownField,
                1,
                out CoCoDiagnostic unknownDiagnostic));
            Assert.IsFalse(unknownWriter.TryWrite(
                TestFrozenConfigSchemas.StateValue,
                2,
                out CoCoDiagnostic afterFailureDiagnostic));
            Assert.AreEqual(unknownDiagnostic.Message, afterFailureDiagnostic.Message);

            var builder = new CoCoFrozenConfigSchemaBuilder<LocalConfigSchema>();
            Assert.IsTrue(builder.TryAddField(
                FieldId(58UL),
                out CoCoFrozenConfigField<LocalConfigSchema, string> textField,
                out CoCoDiagnostic fieldDiagnostic), fieldDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoFrozenConfigSchema<LocalConfigSchema> schema,
                out CoCoDiagnostic schemaDiagnostic), schemaDiagnostic.Message);
            CoCoFrozenConfigWriter<LocalConfigSchema> nullWriter = schema.CreateWriter();
            Assert.IsFalse(nullWriter.TryWrite(
                textField,
                null,
                out CoCoDiagnostic nullDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidFrozenConfig, nullDiagnostic.Code);
        }

        [Test]
        public void WrongTypedOrArrayKindFieldPermanentlyFailsItsWriter()
        {
            var wrongTypeField = new CoCoFrozenConfigField<TestStateConfigSchema, uint>(
                TestFrozenConfigSchemas.StateValue.FieldId);
            CoCoFrozenConfigWriter<TestStateConfigSchema> wrongTypeWriter =
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

            var wrongKindField = new CoCoFrozenConfigArrayField<TestStateConfigSchema, int>(
                TestFrozenConfigSchemas.StateValue.FieldId);
            CoCoFrozenConfigWriter<TestStateConfigSchema> wrongKindWriter =
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
            var builder = new CoCoFrozenConfigSchemaBuilder<LocalConfigSchema>();
            Assert.IsTrue(builder.TryFreeze(
                out CoCoFrozenConfigSchema<LocalConfigSchema> schema,
                out CoCoDiagnostic schemaDiagnostic), schemaDiagnostic.Message);
            CoCoFrozenConfigWriter<LocalConfigSchema> writer = schema.CreateWriter();

            Assert.IsTrue(writer.TrySeal(
                out CoCoFrozenConfigSnapshot snapshot,
                out CoCoDiagnostic snapshotDiagnostic), snapshotDiagnostic.Message);
            Assert.IsTrue(snapshot.IsValid);
            Assert.AreEqual(typeof(LocalConfigSchema), snapshot.SchemaType);
            Assert.AreEqual(schema.Fingerprint, snapshot.SchemaFingerprint);
        }

        [Test]
        public void WarmedTypedReadsAllocateNothingAndAreSafeForParallelReaders()
        {
            CoCoFrozenConfigSnapshot scalar = CoCoStateGraphTestFactory.StateConfig(42);
            CoCoFrozenConfigWriter<TestArrayConfigSchema> arrayWriter =
                TestFrozenConfigSchemas.ArraySchema.CreateWriter();
            Assert.IsTrue(arrayWriter.TryWriteArray(
                TestFrozenConfigSchemas.ArrayValues,
                new[] { 1, 2, 3 },
                out CoCoDiagnostic writeDiagnostic), writeDiagnostic.Message);
            Assert.IsTrue(arrayWriter.TrySeal(
                out CoCoFrozenConfigSnapshot array,
                out CoCoDiagnostic sealDiagnostic), sealDiagnostic.Message);

            Assert.IsTrue(scalar.TryRead(TestFrozenConfigSchemas.StateValue, out int warmScalar));
            Assert.IsTrue(array.TryReadArray(
                TestFrozenConfigSchemas.ArrayValues,
                out CoCoFrozenArray<int> warmArray));
            Assert.AreEqual(42, warmScalar);
            Assert.AreEqual(3, warmArray.Count);

            long before = GC.GetAllocatedBytesForCurrentThread();
            int observed = 0;
            for (int index = 0; index < 10000; index++)
            {
                scalar.TryRead(TestFrozenConfigSchemas.StateValue, out int scalarValue);
                array.TryReadArray(
                    TestFrozenConfigSchemas.ArrayValues,
                    out CoCoFrozenArray<int> arrayValue);
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
                        out CoCoFrozenArray<int> arrayValue) ||
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
            CoCoFrozenConfigSchema<LocalConfigSchema> first = CreateLocalSchema(false);
            CoCoFrozenConfigSchema<LocalConfigSchema> second = CreateLocalSchema(true);
            Assert.AreEqual(first.Fingerprint, second.Fingerprint);

            CoCoFrozenConfigSnapshot firstValue = CoCoStateGraphTestFactory.StateConfig(10);
            CoCoFrozenConfigSnapshot sameValue = CoCoStateGraphTestFactory.StateConfig(10);
            CoCoFrozenConfigSnapshot differentValue = CoCoStateGraphTestFactory.StateConfig(11);
            Assert.AreEqual(firstValue.Fingerprint, sameValue.Fingerprint);
            Assert.AreNotEqual(firstValue.Fingerprint, differentValue.Fingerprint);

            var alternateBuilder = new CoCoFrozenConfigSchemaBuilder<TestStateConfigSchema>();
            Assert.IsTrue(alternateBuilder.TryAddField(
                FieldId(62UL),
                out CoCoFrozenConfigField<TestStateConfigSchema, int> alternateField,
                out CoCoDiagnostic fieldDiagnostic), fieldDiagnostic.Message);
            Assert.IsTrue(alternateBuilder.TryFreeze(
                out CoCoFrozenConfigSchema<TestStateConfigSchema> alternateSchema,
                out CoCoDiagnostic schemaDiagnostic), schemaDiagnostic.Message);
            Assert.IsFalse(firstValue.MatchesSchema(alternateSchema));
            Assert.IsFalse(firstValue.TryRead(
                new CoCoFrozenConfigField<LocalConfigSchema, int>(
                    TestFrozenConfigSchemas.StateValue.FieldId),
                out int wrongSchemaValue));
        }

        [Test]
        public void CatalogKeepsSortedImmutableAuthorAssemblyRoots()
        {
            CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(true);
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

        private static CoCoStateDescriptorId StateDescriptorId(ulong low)
        {
            Assert.IsTrue(CoCoStateDescriptorId.TryCreate(1UL, low, out CoCoStateDescriptorId id));
            return id;
        }

        private static void AssertOrdinaryStateSlotRejected<TValue>(TValue value)
            where TValue : unmanaged
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterStateSlot(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateGraphTestFactory.StateSlotId,
                CoCoContextProjection.Temporal,
                CoCoContextRestorePolicy.Stored,
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
            CoCoDerivedStateRebuilderToken<TValue, TRebuilder> token)
            where TValue : unmanaged
            where TRebuilder : ICoCoDerivedStateRebuilder<TValue>
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();

            Assert.IsFalse(builder.TryRegisterDerivedStateSlot(
                CoCoStateGraphTestFactory.StateBlockId,
                CoCoStateGraphTestFactory.StateSlotId,
                CoCoContextProjection.Temporal,
                value,
                1UL,
                default,
                new[] { CoCoStateGraphTestFactory.StateSlotId },
                token,
                out CoCoDiagnostic diagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.InvalidStateSlot, diagnostic.Code);
            Assert.IsTrue(diagnostic.IsError);
        }

        private static CoCoConditionDescriptorId ConditionDescriptorId(ulong low)
        {
            Assert.IsTrue(CoCoConditionDescriptorId.TryCreate(2UL, low, out CoCoConditionDescriptorId id));
            return id;
        }

        private static CoCoIntentId IntentId(ulong low)
        {
            Assert.IsTrue(CoCoIntentId.TryCreate(3UL, low, out CoCoIntentId id));
            return id;
        }

        private static CoCoOperationSectionId SectionId(ulong low)
        {
            Assert.IsTrue(CoCoOperationSectionId.TryCreate(4UL, low, out CoCoOperationSectionId id));
            return id;
        }

        private static CoCoStateBlockId BlockId(ulong low)
        {
            Assert.IsTrue(CoCoStateBlockId.TryCreate(5UL, low, out CoCoStateBlockId id));
            return id;
        }

        private static CoCoFrozenConfigFieldId FieldId(ulong low)
        {
            Assert.IsTrue(CoCoFrozenConfigFieldId.TryCreate(6UL, low, out CoCoFrozenConfigFieldId id));
            return id;
        }

        private static CoCoFrozenConfigSchema<LocalConfigSchema> CreateLocalSchema(bool reverse)
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<LocalConfigSchema>();
            CoCoFrozenConfigFieldId firstId = FieldId(60UL);
            CoCoFrozenConfigFieldId secondId = FieldId(61UL);
            if (reverse)
            {
                Assert.IsTrue(builder.TryAddArrayField(
                    secondId,
                    out CoCoFrozenConfigArrayField<LocalConfigSchema, string> second,
                    out CoCoDiagnostic secondDiagnostic), secondDiagnostic.Message);
                Assert.IsTrue(builder.TryAddField(
                    firstId,
                    out CoCoFrozenConfigField<LocalConfigSchema, double> first,
                    out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);
            }
            else
            {
                Assert.IsTrue(builder.TryAddField(
                    firstId,
                    out CoCoFrozenConfigField<LocalConfigSchema, double> first,
                    out CoCoDiagnostic firstDiagnostic), firstDiagnostic.Message);
                Assert.IsTrue(builder.TryAddArrayField(
                    secondId,
                    out CoCoFrozenConfigArrayField<LocalConfigSchema, string> second,
                    out CoCoDiagnostic secondDiagnostic), secondDiagnostic.Message);
            }

            Assert.IsTrue(builder.TryFreeze(
                out CoCoFrozenConfigSchema<LocalConfigSchema> schema,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return schema;
        }

        private static CoCoFrozenConfigSchema<EditorAssemblyConfigSchema> CreateEditorAssemblySchema()
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<EditorAssemblyConfigSchema>();
            Assert.IsTrue(builder.TryFreeze(
                out CoCoFrozenConfigSchema<EditorAssemblyConfigSchema> schema,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
            return schema;
        }

        private readonly struct LocalConfigSchema : ICoCoFrozenConfigSchema
        {
        }

        private readonly struct EditorAssemblyConfigSchema : ICoCoFrozenConfigSchema
        {
        }

        private static readonly CoCoFrozenConfigSchema<EditorAssemblyConfigSchema> EditorAssemblySchema =
            CreateEditorAssemblySchema();

        [Serializable]
        private sealed class EditorAssemblyAuthoringConfig : CoCoStateConfig
        {
        }

        private sealed class EditorAssemblyStateLogic : CoCoStateLogic
        {
        }

        private sealed class EditorAssemblyMemory : CoCoActivationMemory
        {
        }

        private sealed class EditorAssemblyConfigFreezer :
            ICoCoConfigFreezer<EditorAssemblyAuthoringConfig, EditorAssemblyConfigSchema>
        {
            public bool TryFreeze(
                EditorAssemblyAuthoringConfig source,
                CoCoFrozenConfigWriter<EditorAssemblyConfigSchema> writer,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }
    }
}
