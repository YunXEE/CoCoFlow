using System;
using System.Collections.Generic;
using System.Linq;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphEventAdapterDeclarationTests
    {
        private static readonly EventDomainId PrimaryDomain = EventDomain(11UL);
        private static readonly EventDomainId AlternateDomain = EventDomain(12UL);
        private static readonly EventTypeId PrimaryEventType = EventType(101UL);
        private static readonly EventTypeId AlternateEventType = EventType(102UL);
        private static readonly IntentId PrimaryIntentId = StateGraphTestFactory.IntentId;
        private static readonly IntentId AlternateIntentId =
            StateGraphTestFactory.CreateIntentId(2UL);

        [SetUp]
        public void SetUp()
        {
            StateGraphFixtureCounters.Reset();
        }

        [Test]
        public void ValidDeclarationAutoAddsIntentAndCarriesOnlyStaticBindingTypes()
        {
            var builder = new GraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            GraphDescriptorCatalog catalog = FreezeWithState(builder);

            StateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId));

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(1, result.Graph.IntentRequirements.Count);
            Assert.AreEqual(1, result.Graph.IntentRequirements.AdapterCount);
            Assert.AreEqual(
                PrimaryIntentId,
                result.Graph.IntentRequirements.Requirements[0].IntentId);

            CompiledEventToIntentDeclaration declaration =
                result.Graph.IntentRequirements.EventAdapterDeclarations[0];
            Assert.AreEqual(PrimaryDomain, declaration.EventDomainId);
            Assert.AreEqual(PrimaryEventType, declaration.EventTypeId);
            Assert.AreEqual(typeof(TestGraphEvent), declaration.EventPayloadType);
            Assert.AreEqual(PrimaryIntentId, declaration.ProvidedIntentId);
            Assert.AreEqual(typeof(TestIntent), declaration.ProvidedIntentType);

            Assert.AreEqual(
                0,
                typeof(CompiledEventToIntentDeclaration).GetConstructors().Length,
                "Pre3 declarations must not expose a public Adapter construction surface.");
            Assert.IsFalse(typeof(CompiledEventToIntentDeclaration)
                .GetProperties()
                .Any(property => property.Name.IndexOf("Adapter", StringComparison.Ordinal) >= 0));
        }

        [Test]
        public void RegistrationRejectsMissingAndWrongIntentRegistration()
        {
            var missingBuilder = new GraphDescriptorCatalogBuilder();
            Assert.IsFalse(missingBuilder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                PrimaryDomain,
                PrimaryEventType,
                PrimaryIntentId,
                out CoCoDiagnostic missingDiagnostic));
            Assert.IsTrue(missingDiagnostic.IsError);
            Assert.AreEqual(CoCoDiagnosticCode.MissingDescriptor, missingDiagnostic.Code);

            var wrongTypeBuilder = new GraphDescriptorCatalogBuilder();
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
            var builder = new GraphDescriptorCatalogBuilder();
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
            var builder = new GraphDescriptorCatalogBuilder();
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
            GraphDescriptorCatalog catalog = FreezeWithState(builder);

            StateGraphCompileResult result = Compile(
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
            var builder = new GraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            GraphDescriptorCatalog catalog = FreezeWithState(builder);

            StateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.MissingDescriptor &&
                diagnostic.Location.ElementKind == GraphElementKind.EventAdapterDeclaration &&
                diagnostic.Location.EventAdapterDeclarationIndex == 0));
        }

        [Test]
        public void CompilerRejectsDuplicateSourceDeclarationPair()
        {
            var builder = new GraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 4);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            GraphDescriptorCatalog catalog = FreezeWithState(builder);

            StateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId),
                Declaration(PrimaryEventType, PrimaryIntentId));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            Assert.IsTrue(result.Diagnostics.Any(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.DuplicateIdentifier &&
                diagnostic.Location.ElementKind == GraphElementKind.EventAdapterDeclaration &&
                diagnostic.Location.EventAdapterDeclarationIndex == 1));
        }

        [Test]
        public void CompilerRejectsDeclarationsFromDifferentEventDomains()
        {
            var builder = new GraphDescriptorCatalogBuilder();
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
            GraphDescriptorCatalog catalog = FreezeWithState(builder);

            StateGraphCompileResult result = Compile(
                catalog,
                Declaration(PrimaryEventType, PrimaryIntentId),
                Declaration(AlternateEventType, AlternateIntentId));

            Assert.IsFalse(result.Succeeded);
            Assert.IsNull(result.Graph);
            GraphDiagnostic[] diagnostics = result.Diagnostics.Where(diagnostic =>
                diagnostic.Diagnostic.Code == CoCoDiagnosticCode.EventDomainMismatch).ToArray();
            Assert.AreEqual(1, diagnostics.Length);
            Assert.AreEqual(
                GraphElementKind.EventAdapterDeclaration,
                diagnostics[0].Location.ElementKind);
            Assert.AreEqual(1, diagnostics[0].Location.EventAdapterDeclarationIndex);
        }

        [Test]
        public void CompilerRejectsDeclarationsBeyondIntentCapacityLowerBound()
        {
            var builder = new GraphDescriptorCatalogBuilder();
            RegisterPrimaryIntent(builder, PrimaryIntentId, 1);
            RegisterPrimaryDeclaration(builder, PrimaryEventType, PrimaryIntentId);
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<
                AlternateTestGraphEvent,
                TestIntent>(
                PrimaryDomain,
                AlternateEventType,
                PrimaryIntentId,
                out CoCoDiagnostic declarationDiagnostic), declarationDiagnostic.Message);
            GraphDescriptorCatalog catalog = FreezeWithState(builder);

            StateGraphCompileResult result = Compile(
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
            GraphDescriptorCatalog catalog = FreezeWithState(
                new GraphDescriptorCatalogBuilder());
            StateGraphSource source = TerminalSource(null, 6001UL);

            StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);

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
            GraphDescriptorCatalog firstCatalog = CreateTwoDeclarationCatalog(false);
            GraphDescriptorCatalog reversedCatalog = CreateTwoDeclarationCatalog(true);
            Assert.AreEqual(firstCatalog.Fingerprint, reversedCatalog.Fingerprint);

            StateGraphCompileResult first = Compile(
                firstCatalog,
                7001UL,
                Declaration(AlternateEventType, AlternateIntentId),
                Declaration(PrimaryEventType, PrimaryIntentId));
            StateGraphCompileResult reversed = Compile(
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
            GraphDescriptorCatalog catalog = CreateTwoDeclarationCatalog(false);
            var sourceDeclarations = new[]
            {
                Declaration(PrimaryEventType, PrimaryIntentId)
            };
            StateGraphSource source = TerminalSource(sourceDeclarations, 8001UL);
            sourceDeclarations[0] = Declaration(AlternateEventType, AlternateIntentId);

            StateGraphCompileResult result = new StateGraphCompiler().Compile(source, catalog);
            sourceDeclarations[0] = null;

            Assert.IsTrue(result.Succeeded);
            Assert.AreEqual(
                PrimaryEventType,
                result.Graph.IntentRequirements.EventAdapterDeclarations[0].EventTypeId);
            Assert.Throws<NotSupportedException>(() =>
                ((IList<EventToIntentDeclarationSource>)source.EventAdapterDeclarations)[0] =
                    Declaration(AlternateEventType, AlternateIntentId));
            Assert.Throws<NotSupportedException>(() =>
                ((IList<CompiledEventToIntentDeclaration>)result.Graph.IntentRequirements
                    .EventAdapterDeclarations)[0] = null);
        }

        private static GraphDescriptorCatalog CreateTwoDeclarationCatalog(bool reverseDeclarations)
        {
            var builder = new GraphDescriptorCatalogBuilder();
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

        private static StateGraphCompileResult Compile(
            GraphDescriptorCatalog catalog,
            params EventToIntentDeclarationSource[] declarations) =>
            Compile(catalog, 5001UL, declarations);

        private static StateGraphCompileResult Compile(
            GraphDescriptorCatalog catalog,
            ulong contentFingerprint,
            params EventToIntentDeclarationSource[] declarations) =>
            new StateGraphCompiler().Compile(
                TerminalSource(declarations, contentFingerprint),
                catalog);

        private static StateGraphSource TerminalSource(
            IReadOnlyList<EventToIntentDeclarationSource> declarations,
            ulong contentFingerprint)
        {
            var layer = new StateLayerSource(
                StateGraphTestFactory.LayerId,
                StateGraphTestFactory.RootStateId,
                new[]
                {
                    StateGraphTestFactory.State(
                        StateGraphTestFactory.RootStateId,
                        default,
                        default,
                        10)
                },
                Array.Empty<TransitionSource>());
            return new StateGraphSource(
                StateGraphCompiler.CurrentSchemaVersion,
                contentFingerprint,
                StateGraphTestFactory.GraphId,
                new[] { layer },
                declarations);
        }

        private static GraphDescriptorCatalog FreezeWithState(
            GraphDescriptorCatalogBuilder builder)
        {
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
                null,
                out CoCoDiagnostic stateDiagnostic), stateDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return catalog;
        }

        private static void RegisterPrimaryIntent(
            GraphDescriptorCatalogBuilder builder,
            IntentId intentId,
            int maxContributions)
        {
            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                maxContributions,
                new IntentReducerFactoryToken<
                    TestIntent,
                    TestIntentReducer,
                    TestIntentReducerFactory>(901UL),
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static void RegisterAlternateIntent(
            GraphDescriptorCatalogBuilder builder,
            IntentId intentId,
            int maxContributions)
        {
            Assert.IsTrue(builder.TryRegisterIntent(
                intentId,
                maxContributions,
                new IntentReducerFactoryToken<
                    AlternateTestIntent,
                    AlternateTestIntentReducer,
                    AlternateTestIntentReducerFactory>(902UL),
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static void RegisterPrimaryDeclaration(
            GraphDescriptorCatalogBuilder builder,
            EventTypeId eventTypeId,
            IntentId intentId)
        {
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<TestGraphEvent, TestIntent>(
                PrimaryDomain,
                eventTypeId,
                intentId,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static void RegisterAlternateDeclaration(
            GraphDescriptorCatalogBuilder builder,
            EventTypeId eventTypeId,
            IntentId intentId)
        {
            Assert.IsTrue(builder.TryRegisterEventToIntentDeclaration<
                AlternateTestGraphEvent,
                AlternateTestIntent>(
                PrimaryDomain,
                eventTypeId,
                intentId,
                out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static EventToIntentDeclarationSource Declaration(
            EventTypeId eventTypeId,
            IntentId intentId) =>
            new EventToIntentDeclarationSource(eventTypeId, intentId);

        private static EventDomainId EventDomain(ulong value)
        {
            Assert.IsTrue(EventDomainId.TryCreate(value, out EventDomainId id));
            return id;
        }

        private static EventTypeId EventType(ulong low)
        {
            Assert.IsTrue(EventTypeId.TryCreate(13UL, low, out EventTypeId id));
            return id;
        }
    }
}
