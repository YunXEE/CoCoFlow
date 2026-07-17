using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphEventAdapterDeclarationTests
    {
        private static readonly CoCoEventDomainId PrimaryDomain = EventDomain(11UL);
        private static readonly CoCoEventDomainId AlternateDomain = EventDomain(12UL);
        private static readonly CoCoEventTypeId PrimaryEventType = EventType(101UL);
        private static readonly CoCoEventTypeId AlternateEventType = EventType(102UL);
        private static readonly CoCoIntentId PrimaryIntentId = CoCoStateGraphTestFactory.IntentId;
        private static readonly CoCoIntentId AlternateIntentId =
            CoCoStateGraphTestFactory.CreateIntentId(2UL);

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphFixtureCounters.Reset();
            CountingTestEventToIntentAdapter.Reset();
        }

        [Test]
        public void ValidDeclarationAutoAddsIntentAndCarriesOnlyStaticBindingTypes()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            CoCoGraphDescriptorCatalog catalog = FreezeWithState(builder);

            CoCoStateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, result.Graph.IntentRequirements.Count);
            Assert.AreEqual(1, result.Graph.IntentRequirements.AdapterCount);
            Assert.AreEqual(
                PrimaryIntentId,
                result.Graph.IntentRequirements.Requirements[0].IntentId);

            CoCoCompiledEventToIntentDeclaration declaration =
                result.Graph.IntentRequirements.EventAdapterDeclarations[0];
            Assert.AreEqual(PrimaryDomain, declaration.EventDomainId);
            Assert.AreEqual(PrimaryEventType, declaration.EventTypeId);
            Assert.AreEqual(typeof(TestGraphEvent), declaration.EventPayloadType);
            Assert.AreEqual(PrimaryIntentId, declaration.ProvidedIntentId);
            Assert.AreEqual(typeof(TestIntent), declaration.ProvidedIntentType);

            Assert.AreEqual(0, CountingTestEventToIntentAdapter.Constructed);
            Assert.AreEqual(0, CountingTestEventToIntentAdapter.Projected);
            Assert.AreEqual(
                0,
                typeof(CoCoCompiledEventToIntentDeclaration).GetConstructors().Length,
                "Pre3 declarations must not expose a public Adapter construction surface.");
            Assert.IsFalse(typeof(CoCoCompiledEventToIntentDeclaration)
                .GetProperties()
                .Any(property => property.Name.IndexOf("Adapter", StringComparison.Ordinal) >= 0));
        }

        [Test]
        public void RegistrationRejectsMissingAndWrongIntentRegistration()
        {
            var missingBuilder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsFalse(missingBuilder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                PrimaryDomain,
                PrimaryEventType,
                PrimaryIntentId,
                out CoCoDiagnostic missingDiagnostic));
            Assert.IsTrue(missingDiagnostic.IsError);
            Assert.AreEqual(CoCoDiagnosticCode.MissingDescriptor, missingDiagnostic.Code);

            var wrongTypeBuilder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterAlternateIntent(wrongTypeBuilder, PrimaryIntentId, 4);
            Assert.IsFalse(wrongTypeBuilder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                PrimaryDomain,
                PrimaryEventType,
                PrimaryIntentId,
                out CoCoDiagnostic wrongTypeDiagnostic));
            Assert.IsTrue(wrongTypeDiagnostic.IsError);
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, wrongTypeDiagnostic.Code);
        }

        [Test]
        public void RegistrationRejectsDuplicatePairAndConflictingEventDomainOrPayload()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterAlternateIntent(builder, AlternateIntentId, 4);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);

            Assert.IsFalse(builder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                PrimaryDomain,
                PrimaryEventType,
                PrimaryIntentId,
                out CoCoDiagnostic duplicateDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.DuplicateIdentifier, duplicateDiagnostic.Code);

            Assert.IsFalse(builder.TryRegisterEventToIntentDeclaration<TestGraphEvent, AlternateTestIntent>(
                AlternateDomain,
                PrimaryEventType,
                AlternateIntentId,
                out CoCoDiagnostic domainDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, domainDiagnostic.Code);

            Assert.IsFalse(builder.TryRegisterEventToIntentDeclaration<
                AlternateTestGraphEvent,
                AlternateTestIntent>(
                PrimaryDomain,
                PrimaryEventType,
                AlternateIntentId,
                out CoCoDiagnostic payloadDiagnostic));
            Assert.AreEqual(CoCoDiagnosticCode.ManifestConflict, payloadDiagnostic.Code);
        }

        [Test]
        public void SameEventMayProvideMultipleDistinctIntents()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterAlternateIntent(builder, AlternateIntentId, 4);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<
                TestGraphEvent,
                AlternateTestIntent>(
                PrimaryDomain,
                PrimaryEventType,
                AlternateIntentId,
                out CoCoDiagnostic declarationDiagnostic), declarationDiagnostic.Message);
            CoCoGraphDescriptorCatalog catalog = FreezeWithState(builder);

            CoCoStateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, AlternateIntentId),
                Declaration(PrimaryEventType, PrimaryIntentId));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(2, result.Graph.IntentRequirements.Count);
            Assert.AreEqual(2, result.Graph.IntentRequirements.AdapterCount);
            CollectionAssert.AreEqual(
                new[] { AlternateIntentId, PrimaryIntentId },
                result.Graph.IntentRequirements.EventAdapterDeclarations
                    .Select(declaration => declaration.ProvidedIntentId));
        }

        [Test]
        public void CompilerRejectsDeclarationMissingFromCatalog()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            CoCoGraphDescriptorCatalog catalog = FreezeWithState(builder);

            CoCoStateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.MissingDescriptor &&
                diagnostic.Location.ElementKind == CoCoGraphElementKind.EventAdapterDeclaration &&
                diagnostic.Location.EventAdapterDeclarationIndex == 0));
        }

        [Test]
        public void CompilerRejectsDuplicateSourceDeclarationPair()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            CoCoGraphDescriptorCatalog catalog = FreezeWithState(builder);

            CoCoStateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId),
                Declaration(PrimaryEventType, PrimaryIntentId));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DuplicateIdentifier &&
                diagnostic.Location.ElementKind == CoCoGraphElementKind.EventAdapterDeclaration &&
                diagnostic.Location.EventAdapterDeclarationIndex == 1));
        }

        [Test]
        public void CompilerRejectsDeclarationsFromDifferentEventDomains()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterAlternateIntent(builder, AlternateIntentId, 4);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<
                AlternateTestGraphEvent,
                AlternateTestIntent>(
                AlternateDomain,
                AlternateEventType,
                AlternateIntentId,
                out CoCoDiagnostic declarationDiagnostic), declarationDiagnostic.Message);
            CoCoGraphDescriptorCatalog catalog = FreezeWithState(builder);

            CoCoStateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId),
                Declaration(AlternateEventType, AlternateIntentId));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            CoCoGraphDiagnostic[] diagnostics = result.Diagnostics.Where(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.EventDomainMismatch).ToArray();
            Assert.AreEqual(1, diagnostics.Length);
            Assert.AreEqual(
                CoCoGraphElementKind.EventAdapterDeclaration,
                diagnostics[0].Location.ElementKind);
            Assert.AreEqual(1, diagnostics[0].Location.EventAdapterDeclarationIndex);
        }

        [Test]
        public void CompilerRejectsDeclarationsBeyondIntentCapacityLowerBound()
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 1);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<
                AlternateTestGraphEvent,
                TestIntent>(
                PrimaryDomain,
                AlternateEventType,
                PrimaryIntentId,
                out CoCoDiagnostic declarationDiagnostic), declarationDiagnostic.Message);
            CoCoGraphDescriptorCatalog catalog = FreezeWithState(builder);

            CoCoStateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId),
                Declaration(AlternateEventType, PrimaryIntentId));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.ManifestConflict &&
                diagnostic.Location.EventAdapterDeclarationIndex == 1));
        }

        [Test]
        public void EmptyDeclarationManifestIsAlwaysNonNull()
        {
            CoCoGraphDescriptorCatalog catalog = FreezeWithState(
                new CoCoGraphDescriptorCatalogBuilder());
            CoCoStateGraphSource source = TerminalSource(null, 6001UL);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(source, catalog);

            Assert.IsNotNull(source.EventAdapterDeclarations);
            Assert.AreEqual(0, source.EventAdapterDeclarations.Count);
            Assert.IsTrue(result.Succeeded);
            Assert.IsNotNull(result.Graph.IntentRequirements);
            Assert.IsNotNull(result.Graph.IntentRequirements.EventAdapterDeclarations);
            Assert.AreEqual(0, result.Graph.IntentRequirements.AdapterCount);
            Assert.AreEqual(0, result.Graph.IntentRequirements.EventAdapterDeclarations.Count);
        }

        [Test]
        public void CatalogIgnoresRegistrationOrderWhileManifestPreservesGraphAuthorOrder()
        {
            CoCoGraphDescriptorCatalog firstCatalog = CreateTwoDeclarationCatalog(false);
            CoCoGraphDescriptorCatalog reversedCatalog = CreateTwoDeclarationCatalog(true);
            Assert.AreEqual(firstCatalog.Fingerprint, reversedCatalog.Fingerprint);

            CoCoStateGraphCompileResult first = Compile(
                firstCatalog,
                7001UL,
                Declaration(AlternateEventType, AlternateIntentId),
                Declaration(PrimaryEventType, PrimaryIntentId));
            CoCoStateGraphCompileResult reversed = Compile(
                reversedCatalog,
                7001UL,
                Declaration(PrimaryEventType, PrimaryIntentId),
                Declaration(AlternateEventType, AlternateIntentId));

            Assert.IsTrue(first.Succeeded);
            Assert.IsTrue(reversed.Succeeded);
            Assert.AreNotEqual(
                first.Graph.IntentRequirements.LayoutId,
                reversed.Graph.IntentRequirements.LayoutId);
            CollectionAssert.AreEqual(
                new[] { AlternateEventType, PrimaryEventType },
                first.Graph.IntentRequirements.EventAdapterDeclarations
                    .Select(declaration => declaration.EventTypeId));
            CollectionAssert.AreEqual(
                new[] { PrimaryEventType, AlternateEventType },
                reversed.Graph.IntentRequirements.EventAdapterDeclarations
                    .Select(declaration => declaration.EventTypeId));
        }

        [Test]
        public void SourceAndCompiledManifestDefensivelyOwnDeclarationCollections()
        {
            CoCoGraphDescriptorCatalog catalog = CreateTwoDeclarationCatalog(false);
            var sourceDeclarations = new[]
            {
                Declaration(PrimaryEventType, PrimaryIntentId)
            };
            CoCoStateGraphSource source = TerminalSource(sourceDeclarations, 8001UL);
            sourceDeclarations[0] = Declaration(AlternateEventType, AlternateIntentId);

            CoCoStateGraphCompileResult result = new CoCoStateGraphCompiler().Compile(source, catalog);
            sourceDeclarations[0] = null;

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(
                PrimaryEventType,
                result.Graph.IntentRequirements.EventAdapterDeclarations[0].EventTypeId);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoEventToIntentDeclarationSource>)source.EventAdapterDeclarations)[0] =
                    Declaration(AlternateEventType, AlternateIntentId));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CoCoCompiledEventToIntentDeclaration>)result.Graph.IntentRequirements
                    .EventAdapterDeclarations)[0] = null);
        }

        private static CoCoGraphDescriptorCatalog CreateTwoDeclarationCatalog(bool reverseDeclarations)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterAlternateIntent(builder, AlternateIntentId, 4);
            if (reverseDeclarations)
            {
                RegisterAlternateDeclaration(builder, AlternateEventType, AlternateIntentId);
                RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            }
            else
            {
                RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
                RegisterAlternateDeclaration(builder, AlternateEventType, AlternateIntentId);
            }

            return FreezeWithState(builder);
        }

        private static CoCoStateGraphCompileResult Compile(
            CoCoGraphDescriptorCatalog catalog,
            params CoCoEventToIntentDeclarationSource[] declarations) =>
            Compile(catalog, 5001UL, declarations);

        private static CoCoStateGraphCompileResult Compile(
            CoCoGraphDescriptorCatalog catalog,
            ulong contentFingerprint,
            params CoCoEventToIntentDeclarationSource[] declarations) =>
            new CoCoStateGraphCompiler().Compile(
                TerminalSource(declarations, contentFingerprint),
                catalog);

        private static CoCoStateGraphSource TerminalSource(
            IReadOnlyList<CoCoEventToIntentDeclarationSource> declarations,
            ulong contentFingerprint)
        {
            var layer = new CoCoStateLayerSource(
                CoCoStateGraphTestFactory.LayerId,
                CoCoStateGraphTestFactory.RootStateId,
                new[]
                {
                    CoCoStateGraphTestFactory.State(
                        CoCoStateGraphTestFactory.RootStateId,
                        default,
                        default,
                        10)
                },
                Array.Empty<CoCoTransitionSource>());
            return new CoCoStateGraphSource(
                CoCoStateGraphCompiler.CurrentSchemaVersion,
                contentFingerprint,
                CoCoStateGraphTestFactory.GraphId,
                new[] { layer },
                declarations);
        }

        private static CoCoGraphDescriptorCatalog FreezeWithState(
            CoCoGraphDescriptorCatalogBuilder builder)
        {
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
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return catalog;
        }

        private static void RegisterPrimaryIntent(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoIntentId intentId,
            int maxContributions)
        {
            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                maxContributions,
                new CoCoIntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(901UL),
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static void RegisterAlternateIntent(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoIntentId intentId,
            int maxContributions)
        {
            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                maxContributions,
                new CoCoIntentReducerFactoryToken<
                    AlternateTestIntent,
                    AlternateTestIntentReducer,
                    AlternateTestIntentReducerFactory>(902UL),
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static void RegisterPrimaryDeclaration(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoEventTypeId eventTypeId,
            CoCoIntentId intentId)
        {
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                PrimaryDomain,
                eventTypeId,
                intentId,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static void RegisterAlternateDeclaration(
            CoCoGraphDescriptorCatalogBuilder builder,
            CoCoEventTypeId eventTypeId,
            CoCoIntentId intentId)
        {
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<
                AlternateTestGraphEvent,
                AlternateTestIntent>(
                PrimaryDomain,
                eventTypeId,
                intentId,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static CoCoEventToIntentDeclarationSource Declaration(
            CoCoEventTypeId eventTypeId,
            CoCoIntentId intentId) =>
            new CoCoEventToIntentDeclarationSource(eventTypeId, intentId);

        private static CoCoEventDomainId EventDomain(ulong value)
        {
            Assert.IsTrue(CoCoEventDomainId.TryCreate(value, out CoCoEventDomainId id));
            return id;
        }

        private static CoCoEventTypeId EventType(ulong low)
        {
            Assert.IsTrue(CoCoEventTypeId.TryCreate(13UL, low, out CoCoEventTypeId id));
            return id;
        }
    }
}
