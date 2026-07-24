using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map.Tests
{
    public sealed class RegionCompilerTests
    {
        private readonly List<UnityEngine.Object> assets =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < assets.Count; index++)
            {
                if (assets[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(assets[index]);
                }
            }

            assets.Clear();
        }

        [Test]
        public void DefaultFiveTiersAndDirectSceneCompileToPureImmutablePlan()
        {
            RegionParticipantCatalog catalog = CreateCatalog();
            CoCoRegionProfile profile = CreateProfile();
            CoCoRegionBinding binding = CreateBinding(
                "world.wilderness",
                profile,
                "terrain",
                CreateDirectScene("world.wilderness.terrain", "Assets/World/Terrain.unity"));

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(binding, catalog);

            Assert.IsTrue(
                result.Succeeded,
                result.Diagnostics.Count == 0
                    ? "No diagnostic."
                    : result.Diagnostics[0].Diagnostic.Message);
            Assert.AreEqual(5, result.Plan.Tiers.Count);
            Assert.AreEqual(1, result.Plan.Chunks.Count);
            Assert.AreEqual(1, result.Plan.Nodes.Count);
            Assert.AreEqual(
                "Assets/World/Terrain.unity",
                result.Plan.Chunks[0].CanonicalScenePath);
            Assert.IsFalse(string.IsNullOrEmpty(result.Plan.Fingerprint));
            AssertPlanFieldsArePureValues(result.Plan);
        }

        [Test]
        public void CustomCapabilityCanBeInsertedBetweenStandardTiers()
        {
            Assert.IsTrue(RegionCapabilityId.TryCreate(
                "project.weather.simulated",
                out RegionCapabilityId custom));
            RegionParticipantCatalog catalog = CreateCatalog(custom);
            CoCoRegionProfile profile = CreateProfile();
            SetField(
                profile,
                "tiers",
                new List<RegionTierDefinition>
                {
                    Tier("0", Array.Empty<RegionCapabilityId>()),
                    Tier("1", RegionCapabilityId.Represented),
                    Tier("custom", RegionCapabilityId.Represented, custom),
                    Tier(
                        "2",
                        RegionCapabilityId.Represented,
                        custom,
                        RegionCapabilityId.Background),
                    Tier(
                        "3",
                        RegionCapabilityId.Represented,
                        custom,
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable),
                    Tier(
                        "4",
                        RegionCapabilityId.Represented,
                        custom,
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable,
                        RegionCapabilityId.Full)
                });
            CoCoRegionBinding binding = CreateBinding(
                "world.castle",
                profile,
                "keep",
                CreateDirectScene("world.castle.keep", "Assets/World/Castle.unity"));

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(binding, catalog);

            Assert.IsTrue(result.Succeeded, FirstDiagnostic(result));
            Assert.AreEqual(6, result.Plan.Tiers.Count);
            Assert.IsTrue(result.Plan.Tiers[2].Capabilities.Contains(custom));
        }

        [Test]
        public void DirectSceneShortNameIsRejectedFailClosed()
        {
            CoCoRegionProfile profile = CreateProfile();
            CoCoRegionBinding binding = CreateBinding(
                "world.mine",
                profile,
                "entrance",
                CreateDirectScene("world.mine.entrance", "MineScene"));

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(binding, CreateCatalog());

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.RegionSceneContractViolation,
                FirstError(result).Code);
        }

        [Test]
        public void AddressableSceneRequiresExplicitUniqueResolver()
        {
            Assert.IsTrue(ContentId.TryCreate(
                "world.chapel.interior",
                out ContentId contentId));
            Assert.IsTrue(ContentReference.TryCreateAddressableAdditiveScene(
                contentId,
                "world/chapel/interior",
                out ContentReference scene));
            CoCoRegionBinding binding = CreateBinding(
                "world.chapel",
                CreateProfile(),
                "interior",
                scene);
            var compiler = new RegionBindingCompiler();

            Assert.IsFalse(
                compiler.Compile(binding, CreateCatalog()).Succeeded);
            RegionCompileResult resolved = compiler.Compile(
                binding,
                CreateCatalog(),
                new FixedAddressableResolver(
                    "Assets/World/ChapelInterior.unity"));
            Assert.IsTrue(resolved.Succeeded, FirstDiagnostic(resolved));
        }

        [Test]
        public void CompileAllRejectsOneSceneOwnedByTwoRegions()
        {
            CoCoRegionProfile profileA = CreateProfile();
            CoCoRegionProfile profileB = CreateProfile();
            CoCoRegionBinding bindingA = CreateBinding(
                "world.castle",
                profileA,
                "keep",
                CreateDirectScene(
                    "world.castle.keep",
                    "Assets/World/SharedInterior.unity"));
            CoCoRegionBinding bindingB = CreateBinding(
                "world.chapel",
                profileB,
                "interior",
                CreateDirectScene(
                    "world.chapel.interior",
                    "Assets/World/SharedInterior.unity"));

            IReadOnlyList<RegionCompileResult> results =
                new RegionBindingCompiler().CompileAll(
                    new[] { bindingA, bindingB },
                    CreateCatalog());

            Assert.AreEqual(2, results.Count);
            Assert.IsFalse(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.RegionSceneContractViolation,
                FirstError(results[0]).Code);
        }

        [Test]
        public void CompileAllRejectsDuplicateRegionIdAcrossBindings()
        {
            CoCoRegionBinding first = CreateBinding(
                "world.wilderness",
                CreateProfile(),
                "west",
                CreateDirectScene(
                    "world.wilderness.west",
                    "Assets/World/WildernessWest.unity"));
            CoCoRegionBinding second = CreateBinding(
                "world.wilderness",
                CreateProfile(),
                "east",
                CreateDirectScene(
                    "world.wilderness.east",
                    "Assets/World/WildernessEast.unity"));

            IReadOnlyList<RegionCompileResult> results =
                new RegionBindingCompiler().CompileAll(
                    new[] { first, second },
                    CreateCatalog());

            Assert.IsFalse(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionIdentifier,
                FirstError(results[0]).Code);
        }

        [Test]
        public void CompileAllRejectsDuplicateContentIdAcrossBindings()
        {
            CoCoRegionBinding first = CreateBinding(
                "world.castle",
                CreateProfile(),
                "keep",
                CreateDirectScene(
                    "world.shared.interior",
                    "Assets/World/CastleKeep.unity"));
            CoCoRegionBinding second = CreateBinding(
                "world.chapel",
                CreateProfile(),
                "nave",
                CreateDirectScene(
                    "world.shared.interior",
                    "Assets/World/ChapelNave.unity"));

            IReadOnlyList<RegionCompileResult> results =
                new RegionBindingCompiler().CompileAll(
                    new[] { first, second },
                    CreateCatalog());

            Assert.IsFalse(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.RegionSceneContractViolation,
                FirstError(results[0]).Code);
        }

        [Test]
        public void CompileAllRejectsDuplicateChunkIdAcrossRegions()
        {
            CoCoRegionBinding first = CreateBinding(
                "world.castle",
                CreateProfile(),
                "shared-interior",
                CreateDirectScene(
                    "world.castle.shared-interior",
                    "Assets/World/CastleSharedInterior.unity"));
            CoCoRegionBinding second = CreateBinding(
                "world.chapel",
                CreateProfile(),
                "shared-interior",
                CreateDirectScene(
                    "world.chapel.shared-interior",
                    "Assets/World/ChapelSharedInterior.unity"));

            IReadOnlyList<RegionCompileResult> results =
                new RegionBindingCompiler().CompileAll(
                    new[] { first, second },
                    CreateCatalog());

            Assert.IsFalse(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionIdentifier,
                FirstError(results[0]).Code);
            StringAssert.Contains(
                "world.castle/shared-interior/scene",
                FirstError(results[0]).Message);
            StringAssert.Contains(
                "world.chapel/shared-interior/scene",
                FirstError(results[0]).Message);
        }

        [Test]
        public void CanonicalNodeIdentitySeparatesAmbiguousDisplayPaths()
        {
            Assert.IsTrue(
                RegionId.TryCreate(
                    "world",
                    out RegionId regionId));
            Assert.IsTrue(
                RegionParticipantSlotId.TryCreate(
                    "a/b",
                    out RegionParticipantSlotId globalSlotId));
            Assert.IsTrue(
                RegionChunkId.TryCreate(
                    "global",
                    out RegionChunkId globalChunkId));
            Assert.IsTrue(
                RegionPlanNodeId.TryCreateGlobal(
                    regionId,
                    globalSlotId,
                    out RegionPlanNodeId globalNodeId));
            Assert.IsTrue(
                RegionPlanNodeId.TryCreateChunk(
                    regionId,
                    globalChunkId,
                    globalSlotId,
                    out RegionPlanNodeId chunkNodeId));

            Assert.AreEqual(
                globalNodeId.ToString(),
                chunkNodeId.ToString());
            Assert.AreNotEqual(
                RegionBindingCompiler.BuildCanonicalNodeIdentity(
                    globalNodeId),
                RegionBindingCompiler.BuildCanonicalNodeIdentity(
                    chunkNodeId));
            Assert.AreNotEqual(
                0,
                RegionBindingCompiler.CompareNodeIds(
                    globalNodeId,
                    chunkNodeId));

            Assert.IsTrue(
                RegionChunkId.TryCreate(
                    "a/b",
                    out RegionChunkId longChunkId));
            Assert.IsTrue(
                RegionParticipantSlotId.TryCreate(
                    "c",
                    out RegionParticipantSlotId shortSlotId));
            Assert.IsTrue(
                RegionChunkId.TryCreate(
                    "a",
                    out RegionChunkId shortChunkId));
            Assert.IsTrue(
                RegionParticipantSlotId.TryCreate(
                    "b/c",
                    out RegionParticipantSlotId longSlotId));
            Assert.IsTrue(
                RegionPlanNodeId.TryCreateChunk(
                    regionId,
                    longChunkId,
                    shortSlotId,
                    out RegionPlanNodeId left));
            Assert.IsTrue(
                RegionPlanNodeId.TryCreateChunk(
                    regionId,
                    shortChunkId,
                    longSlotId,
                    out RegionPlanNodeId right));

            Assert.AreEqual(left.ToString(), right.ToString());
            Assert.AreNotEqual(
                RegionBindingCompiler.BuildCanonicalNodeIdentity(left),
                RegionBindingCompiler.BuildCanonicalNodeIdentity(right));
            Assert.AreNotEqual(
                0,
                RegionBindingCompiler.CompareNodeIds(left, right));
            Assert.AreEqual(
                -Math.Sign(
                    RegionBindingCompiler.CompareNodeIds(left, right)),
                Math.Sign(
                    RegionBindingCompiler.CompareNodeIds(right, left)));
        }

        [Test]
        public void AmbiguousDisplayPathsKeepCompiledCacheEntriesSeparate()
        {
            CoCoRegionProfile profile = CreateProfile();
            ContentReference scene = CreateDirectScene(
                "world.shared",
                "Assets/World/Shared.unity");
            CoCoRegionBinding first = CreateBinding(
                "world",
                profile,
                "a/b",
                scene);
            CoCoRegionBinding second = CreateBinding(
                "world/a",
                profile,
                "b",
                scene);
            var cache = new RegionProfileCompilationCache();
            var compiler = new RegionBindingCompiler();
            RegionParticipantCatalog catalog = CreateCatalog();

            RegionCompileResult firstResult = cache.Compile(
                compiler,
                first,
                catalog);
            RegionCompileResult secondResult = cache.Compile(
                compiler,
                second,
                catalog);

            Assert.IsTrue(firstResult.Succeeded, FirstDiagnostic(firstResult));
            Assert.IsTrue(secondResult.Succeeded, FirstDiagnostic(secondResult));
            Assert.AreEqual(
                firstResult.Plan.Nodes[0].Id.ToString(),
                secondResult.Plan.Nodes[0].Id.ToString());
            Assert.AreNotEqual(
                firstResult.Plan.Nodes[0].Fingerprint,
                secondResult.Plan.Nodes[0].Fingerprint);
            Assert.AreNotEqual(
                firstResult.Plan.Fingerprint,
                secondResult.Plan.Fingerprint);
            Assert.AreNotSame(firstResult.Plan, secondResult.Plan);
            Assert.AreEqual(2, cache.Count);
        }

        [Test]
        public void PublicRegistrationCannotOwnChunkScene()
        {
            RegionParticipantCatalog catalog =
                CreateCatalogWithFreezer(
                    new TestFreezer(),
                    false);
            CoCoRegionBinding binding = CreateBinding(
                "world.mine",
                CreateProfile(),
                "entrance",
                CreateDirectScene(
                    "world.mine.entrance",
                    "Assets/World/MineEntrance.unity"));

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(binding, catalog);

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.RegionSceneContractViolation,
                FirstError(result).Code);
        }

        [Test]
        public void ChunkResourceParticipantRequiresDirectOwningContentDependency()
        {
            CoCoRegionProfile profile =
                CreateMarkerProfile(false);
            CoCoRegionBinding binding = CreateBinding(
                "world.castle",
                profile,
                "keep",
                CreateDirectScene(
                    "world.castle.keep",
                    "Assets/World/CastleKeep.unity"));
            SetField(
                binding.Chunks[0],
                "participants",
                new List<RegionParticipantSlotBinding>
                {
                    SlotBinding(SlotId()),
                    SlotBinding(MarkerSlotId())
                });

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(
                    binding,
                    CreateOwnerAndMarkerCatalog());

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionProfile,
                FirstError(result).Code);
        }

        [Test]
        public void GlobalResourceParticipantDoesNotRequireChunkOwner()
        {
            CoCoRegionProfile profile =
                CreateMarkerProfile(false);
            CoCoRegionBinding binding = CreateBinding(
                "world.wilderness",
                profile,
                "terrain",
                CreateDirectScene(
                    "world.wilderness.terrain",
                    "Assets/World/WildernessTerrain.unity"));
            SetField(
                binding,
                "regionParticipants",
                new List<RegionParticipantSlotBinding>
                {
                    SlotBinding(MarkerSlotId())
                });

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(
                    binding,
                    CreateOwnerAndMarkerCatalog());

            Assert.IsTrue(result.Succeeded, FirstDiagnostic(result));
        }

        [Test]
        public void ChunkResourceParticipantAcceptsDirectOwningContentDependency()
        {
            CoCoRegionProfile profile =
                CreateMarkerProfile(true);
            CoCoRegionBinding binding = CreateBinding(
                "world.mine",
                profile,
                "entrance",
                CreateDirectScene(
                    "world.mine.entrance",
                    "Assets/World/MineEntrance.unity"));
            SetField(
                binding.Chunks[0],
                "participants",
                new List<RegionParticipantSlotBinding>
                {
                    SlotBinding(SlotId()),
                    SlotBinding(MarkerSlotId())
                });

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(
                    binding,
                    CreateOwnerAndMarkerCatalog());

            Assert.IsTrue(result.Succeeded, FirstDiagnostic(result));
        }

        [Test]
        public void CompilerRejectsImpureFrozenPlan()
        {
            RegionParticipantCatalog catalog =
                CreateCatalogWithFreezer(
                    new FixedPlanFreezer(
                        typeof(MutableFieldPlan),
                        new MutableFieldPlan()),
                    true);
            CoCoRegionBinding binding = CreateBinding(
                "world.castle",
                CreateProfile(),
                "keep",
                CreateDirectScene(
                    "world.castle.keep",
                    "Assets/World/CastleKeep.unity"));

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(binding, catalog);

            Assert.IsFalse(result.Succeeded);
            StringAssert.Contains(
                "not immutable pure data",
                FirstError(result).Message);
        }

        [Test]
        public void PlanPurityRejectsNestedAuthoritiesTasksAndMutableCollections()
        {
            AssertPurityRejected(
                new ReadonlyUnityObjectPlan(),
                "UnityEngine.Object");
            AssertPurityRejected(
                new ReadonlyScenePlan(),
                "Unity runtime authority");
            AssertPurityRejected(
                new ReadonlyIntPtrPlan(),
                "native/backend handle");
            AssertPurityRejected(
                new ReadonlyDisposablePlan(),
                "disposable authority");
            AssertPurityRejected(
                new ReadonlyDelegatePlan(),
                "delegate");
            AssertPurityRejected(
                new ReadonlyTaskPlan(),
                "task");
            AssertPurityRejected(
                new ReadonlyMutableCollectionPlan(),
                "mutable collection");
            AssertPurityRejected(
                new ReadOnlyCollectionPlan(
                    new ReadOnlyCollection<int>(
                        new List<int> { 1 })),
                "mutable collection");
            AssertPurityRejected(
                new MutableFieldPlan(),
                "is mutable");
        }

        [Test]
        public void PackageImmutableArrayCopiesSourceAndPassesPurity()
        {
            var source = new List<int> { 1, 2, 3 };
            var values = new RegionImmutableArray<int>(source);
            var plan = new ImmutableArrayPlan(values);
            source[0] = 99;
            source.Add(4);

            Assert.AreEqual(3, values.Count);
            Assert.AreEqual(1, values[0]);
            Assert.IsTrue(
                RegionPlanPurityValidator.TryValidate(
                    plan,
                    out string failure),
                failure);
        }

        [Test]
        public void RegistrationRejectsNonExactPlanAndCandidateMetadata()
        {
            AssertRegistrationRejected(
                new DeclaredPlanTypeFreezer(
                    typeof(IRegionParticipantPlan)),
                new TestFactory());
            AssertRegistrationRejected(
                new DeclaredPlanTypeFreezer(
                    typeof(AbstractTestPlan)),
                new TestFactory());
            AssertRegistrationRejected(
                new DeclaredPlanTypeFreezer(
                    typeof(NonSealedTestPlan)),
                new TestFactory());
            AssertRegistrationRejected(
                new DeclaredPlanTypeFreezer(
                    typeof(ValueTestPlan)),
                new TestFactory());
            AssertRegistrationRejected(
                new TestFreezer(),
                new DeclaredCandidateTypeFactory(
                    typeof(IRegionParticipantCandidate)));
            AssertRegistrationRejected(
                new TestFreezer(),
                new DeclaredCandidateTypeFactory(
                    typeof(AbstractTestCandidate)));
        }

        [Test]
        public void RegistrationSnapshotsTypeMetadataExactlyOnce()
        {
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[] { RegionCapabilityId.Represented },
                out RegionCapabilitySet supported));
            var freezer = new MutableMetadataFreezer();
            var factory = new MutableMetadataFactory();
            Assert.IsTrue(
                RegionParticipantRegistration.TryCreateOwningContent(
                    TypeId(),
                    ModeId(),
                    supported,
                    freezer,
                    factory,
                    out RegionParticipantRegistration registration,
                    out CoCoDiagnostic diagnostic),
                diagnostic.Message);

            freezer.ConfigurationTypeValue = typeof(AlternateTestConfig);
            freezer.PlanTypeValue = typeof(AlternateTestPlan);
            factory.CandidateTypeValue = typeof(AlternateTestCandidate);

            Assert.AreEqual(typeof(TestConfig), registration.ConfigurationType);
            Assert.AreEqual(typeof(TestPlan), registration.PlanType);
            Assert.AreEqual(typeof(TestCandidate), registration.CandidateType);
            var catalog = new RegionParticipantCatalog();
            Assert.IsTrue(
                catalog.TryRegisterParticipant(
                    registration,
                    out diagnostic),
                diagnostic.Message);
            catalog.Seal();
            CollectionAssert.Contains(
                (System.Collections.ICollection)catalog.RegisteredTypes,
                typeof(TestPlan));
            CollectionAssert.DoesNotContain(
                (System.Collections.ICollection)catalog.RegisteredTypes,
                typeof(AlternateTestPlan));

            CoCoRegionBinding binding = CreateBinding(
                "world.wilderness",
                CreateProfile(),
                "terrain",
                CreateDirectScene(
                    "world.wilderness.terrain",
                    "Assets/World/WildernessTerrain.unity"));
            RegionCompileResult result =
                new RegionBindingCompiler().Compile(
                    binding,
                    catalog);

            Assert.IsTrue(result.Succeeded, FirstDiagnostic(result));
        }

        [Test]
        public void CompilerRejectsUnregisteredDerivedConfigurationType()
        {
            CoCoRegionProfile profile = CreateProfile();
            SetField(
                profile.Participants[0],
                "configuration",
                new DerivedTestConfig());
            CoCoRegionBinding binding = CreateBinding(
                "world.chapel",
                profile,
                "nave",
                CreateDirectScene(
                    "world.chapel.nave",
                    "Assets/World/ChapelNave.unity"));

            RegionCompileResult result =
                new RegionBindingCompiler().Compile(
                    binding,
                    CreateCatalogWithFreezer(
                        new BaseConfigFreezer(),
                        true));

            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionProfile,
                FirstError(result).Code);
        }

        private CoCoRegionProfile CreateProfile()
        {
            CoCoRegionProfile profile =
                ScriptableObject.CreateInstance<CoCoRegionProfile>();
            assets.Add(profile);
            SetField(
                profile,
                "participants",
                new List<RegionParticipantDefinition>
                {
                    new RegionParticipantDefinition(
                        SlotId(),
                        TypeId(),
                        ModeId(),
                        RegionParticipantPhase.Residency,
                        0,
                        RegionParticipantRequirement.Required,
                        new[] { RegionCapabilityId.Represented },
                        Array.Empty<RegionParticipantSlotId>(),
                        new TestConfig())
                });
            return profile;
        }

        private CoCoRegionProfile CreateMarkerProfile(
            bool markerDependsOnOwner)
        {
            CoCoRegionProfile profile =
                ScriptableObject.CreateInstance<CoCoRegionProfile>();
            assets.Add(profile);
            SetField(
                profile,
                "participants",
                new List<RegionParticipantDefinition>
                {
                    new RegionParticipantDefinition(
                        SlotId(),
                        TypeId(),
                        ModeId(),
                        RegionParticipantPhase.Residency,
                        0,
                        RegionParticipantRequirement.Required,
                        new[] { RegionCapabilityId.Represented },
                        Array.Empty<RegionParticipantSlotId>(),
                        new TestConfig()),
                    new RegionParticipantDefinition(
                        MarkerSlotId(),
                        MarkerTypeId(),
                        ModeId(),
                        RegionParticipantPhase.Services,
                        0,
                        RegionParticipantRequirement.Required,
                        new[] { RegionCapabilityId.Represented },
                        markerDependsOnOwner
                            ? new[] { SlotId() }
                            : Array.Empty<RegionParticipantSlotId>(),
                        new TestConfig())
                });
            return profile;
        }

        private CoCoRegionBinding CreateBinding(
            string regionValue,
            CoCoRegionProfile profile,
            string chunkValue,
            ContentReference scene)
        {
            Assert.IsTrue(RegionId.TryCreate(regionValue, out RegionId regionId));
            Assert.IsTrue(RegionChunkId.TryCreate(
                chunkValue,
                out RegionChunkId chunkId));
            var slotBinding = new RegionParticipantSlotBinding();
            SetField(slotBinding, "slotId", SlotId());
            SetField(slotBinding, "fragmentId", string.Empty);
            var chunk = new RegionChunkBinding();
            SetField(chunk, "chunkId", chunkId);
            SetField(chunk, "sceneSource", scene);
            SetField(chunk, "owningContentSlotId", SlotId());
            SetField(
                chunk,
                "participants",
                new List<RegionParticipantSlotBinding> { slotBinding });

            CoCoRegionBinding binding =
                ScriptableObject.CreateInstance<CoCoRegionBinding>();
            assets.Add(binding);
            SetField(binding, "regionId", regionId);
            SetField(binding, "profile", profile);
            SetField(
                binding,
                "regionParticipants",
                new List<RegionParticipantSlotBinding>());
            SetField(binding, "chunks", new List<RegionChunkBinding> { chunk });
            return binding;
        }

        private static ContentReference CreateDirectScene(
            string contentValue,
            string path)
        {
            Assert.IsTrue(ContentId.TryCreate(contentValue, out ContentId contentId));
            Assert.IsTrue(ContentReference.TryCreateDirectAdditiveScene(
                contentId,
                path,
                out ContentReference scene));
            return scene;
        }

        private static RegionParticipantSlotBinding SlotBinding(
            RegionParticipantSlotId slotId)
        {
            var binding = new RegionParticipantSlotBinding();
            SetField(binding, "slotId", slotId);
            SetField(binding, "fragmentId", string.Empty);
            return binding;
        }

        private static RegionParticipantCatalog CreateCatalog(
            params RegionCapabilityId[] customCapabilities)
        {
            return CreateCatalogWithFreezer(
                new TestFreezer(),
                true,
                customCapabilities);
        }

        private static RegionParticipantCatalog CreateCatalogWithFreezer(
            IRegionParticipantConfigFreezer freezer,
            bool authorizeChunkSceneOwnership,
            params RegionCapabilityId[] customCapabilities)
        {
            var catalog = new RegionParticipantCatalog();
            for (int index = 0; index < customCapabilities.Length; index++)
            {
                Assert.IsTrue(catalog.TryRegisterCapability(
                    customCapabilities[index],
                    out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
            }

            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[]
                {
                    RegionCapabilityId.Represented,
                    RegionCapabilityId.Background,
                    RegionCapabilityId.Enterable,
                    RegionCapabilityId.Full
                },
                out RegionCapabilitySet supported));
            RegionParticipantRegistration registration;
            CoCoDiagnostic registrationDiagnostic;
            bool created = authorizeChunkSceneOwnership
                ? RegionParticipantRegistration.TryCreateOwningContent(
                    TypeId(),
                    ModeId(),
                    supported,
                    freezer,
                    new TestFactory(),
                    out registration,
                    out registrationDiagnostic)
                : RegionParticipantRegistration.TryCreate(
                    TypeId(),
                    ModeId(),
                    supported,
                    freezer,
                    new TestFactory(),
                    out registration,
                    out registrationDiagnostic);
            Assert.IsTrue(
                created,
                registrationDiagnostic.Message);
            Assert.IsTrue(catalog.TryRegisterParticipant(
                registration,
                out CoCoDiagnostic catalogDiagnostic),
                catalogDiagnostic.Message);
            catalog.Seal();
            return catalog;
        }

        private static RegionParticipantCatalog
            CreateOwnerAndMarkerCatalog()
        {
            var catalog = new RegionParticipantCatalog();
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[]
                {
                    RegionCapabilityId.Represented,
                    RegionCapabilityId.Background,
                    RegionCapabilityId.Enterable,
                    RegionCapabilityId.Full
                },
                out RegionCapabilitySet supported));
            Assert.IsTrue(
                RegionParticipantRegistration.TryCreateOwningContent(
                    TypeId(),
                    ModeId(),
                    supported,
                    new TestFreezer(),
                    new TestFactory(),
                    out RegionParticipantRegistration owner,
                    out CoCoDiagnostic diagnostic),
                diagnostic.Message);
            Assert.IsTrue(
                catalog.TryRegisterParticipant(
                    owner,
                    out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(
                RegionParticipantRegistration.TryCreate(
                    MarkerTypeId(),
                    ModeId(),
                    supported,
                    new MarkerFreezer(),
                    new TestFactory(),
                    out RegionParticipantRegistration marker,
                    out diagnostic),
                diagnostic.Message);
            Assert.IsTrue(
                catalog.TryRegisterParticipant(
                    marker,
                    out diagnostic),
                diagnostic.Message);
            catalog.Seal();
            return catalog;
        }

        private static void AssertPurityRejected(
            IRegionParticipantPlan plan,
            string expectedMessage)
        {
            Assert.IsFalse(
                RegionPlanPurityValidator.TryValidate(
                    plan,
                    out string failure));
            StringAssert.Contains(expectedMessage, failure);
        }

        private static void AssertRegistrationRejected(
            IRegionParticipantConfigFreezer freezer,
            IRegionParticipantFactory factory)
        {
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[] { RegionCapabilityId.Represented },
                out RegionCapabilitySet supported));
            Assert.IsFalse(RegionParticipantRegistration.TryCreate(
                TypeId(),
                ModeId(),
                supported,
                freezer,
                factory,
                out RegionParticipantRegistration registration,
                out CoCoDiagnostic diagnostic));
            Assert.IsNull(registration);
            Assert.AreEqual(
                CoCoDiagnosticCode.RegionCatalogConflict,
                diagnostic.Code);
        }

        private static RegionTierDefinition Tier(
            string name,
            params RegionCapabilityId[] capabilities) =>
            new RegionTierDefinition(name, capabilities);

        private static RegionParticipantSlotId SlotId()
        {
            Assert.IsTrue(RegionParticipantSlotId.TryCreate(
                "scene",
                out RegionParticipantSlotId id));
            return id;
        }

        private static RegionParticipantTypeId TypeId()
        {
            Assert.IsTrue(RegionParticipantTypeId.TryCreate(
                "tests.scene",
                out RegionParticipantTypeId id));
            return id;
        }

        private static RegionParticipantSlotId MarkerSlotId()
        {
            Assert.IsTrue(RegionParticipantSlotId.TryCreate(
                "dependent-resource",
                out RegionParticipantSlotId id));
            return id;
        }

        private static RegionParticipantTypeId MarkerTypeId()
        {
            Assert.IsTrue(RegionParticipantTypeId.TryCreate(
                "tests.dependent-resource",
                out RegionParticipantTypeId id));
            return id;
        }

        private static RegionParticipantModeId ModeId()
        {
            Assert.IsTrue(RegionParticipantModeId.TryCreate(
                "tests.default",
                out RegionParticipantModeId id));
            return id;
        }

        private static void SetField<TTarget, TValue>(
            TTarget target,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(TTarget).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(target, value);
        }

        private static string FirstDiagnostic(RegionCompileResult result) =>
            result.Diagnostics.Count == 0
                ? "No diagnostic."
                : result.Diagnostics[0].Path + ": " +
                  result.Diagnostics[0].Diagnostic.Message;

        private static CoCoDiagnostic FirstError(RegionCompileResult result)
        {
            for (int index = 0; index < result.Diagnostics.Count; index++)
            {
                if (result.Diagnostics[index].Diagnostic.IsError)
                {
                    return result.Diagnostics[index].Diagnostic;
                }
            }

            Assert.Fail("Expected one compile error.");
            return default;
        }

        private static void AssertPlanFieldsArePureValues(RegionCompiledPlan plan)
        {
            var forbidden = new[]
            {
                typeof(UnityEngine.Object),
                typeof(ContentScope),
                typeof(ContentLease)
            };
            Type[] compiledTypes =
            {
                plan.GetType(),
                typeof(RegionCompiledTier),
                typeof(RegionCompiledChunk),
                typeof(RegionCompiledParticipantNode),
                typeof(RegionCompiledSceneReference),
                typeof(RegionPlanNodeId)
            };
            for (int typeIndex = 0; typeIndex < compiledTypes.Length; typeIndex++)
            {
                FieldInfo[] fields = compiledTypes[typeIndex].GetFields(
                    BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
                for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
                {
                    for (int forbiddenIndex = 0;
                         forbiddenIndex < forbidden.Length;
                         forbiddenIndex++)
                    {
                        Assert.IsFalse(
                            forbidden[forbiddenIndex].IsAssignableFrom(
                                fields[fieldIndex].FieldType),
                            compiledTypes[typeIndex].Name + "." +
                            fields[fieldIndex].Name);
                    }
                }
            }
        }

        [Serializable]
        private sealed class TestConfig : RegionParticipantConfig
        {
        }

        [Serializable]
        private sealed class AlternateTestConfig :
            RegionParticipantConfig
        {
        }

        [Serializable]
        private class BaseTestConfig :
            RegionParticipantConfig
        {
        }

        [Serializable]
        private sealed class DerivedTestConfig :
            BaseTestConfig
        {
        }

        private sealed class TestPlan : IRegionParticipantPlan
        {
            public string Fingerprint => "tests.scene.plan.v1";
        }

        private sealed class AlternateTestPlan :
            IRegionParticipantPlan
        {
            public string Fingerprint => "tests.alternate.plan.v1";
        }

        private sealed class MutableFieldPlan : IRegionParticipantPlan
        {
            private int value = 1;

            public string Fingerprint => "tests.mutable|" + value;
        }

        private sealed class ReadonlyUnityObjectPlan :
            IRegionParticipantPlan
        {
            private readonly NestedUnityObject payload =
                new NestedUnityObject();

            public string Fingerprint => "tests.unity-object";
        }

        private sealed class NestedUnityObject
        {
            internal readonly UnityEngine.Object Value;
        }

        private sealed class ReadonlyScenePlan :
            IRegionParticipantPlan
        {
            private readonly UnityEngine.SceneManagement.Scene scene;

            public string Fingerprint =>
                "tests.scene-authority|" + scene.handle;
        }

        private sealed class ReadonlyIntPtrPlan :
            IRegionParticipantPlan
        {
            private readonly IntPtr handle = new IntPtr(1);

            public string Fingerprint =>
                "tests.native-handle|" + handle;
        }

        private sealed class ReadonlyDisposablePlan :
            IRegionParticipantPlan
        {
            private readonly NestedDisposable payload =
                new NestedDisposable();

            public string Fingerprint => "tests.disposable";
        }

        private sealed class NestedDisposable
        {
            internal readonly IDisposable Value =
                new TestDisposable();
        }

        private sealed class TestDisposable : IDisposable
        {
            public void Dispose()
            {
            }
        }

        private sealed class ReadonlyDelegatePlan :
            IRegionParticipantPlan
        {
            private readonly Action callback = StaticCallback;

            public string Fingerprint => "tests.delegate";

            private static void StaticCallback()
            {
            }
        }

        private sealed class ReadonlyTaskPlan :
            IRegionParticipantPlan
        {
            private readonly Task task = Task.CompletedTask;

            public string Fingerprint => "tests.task";
        }

        private sealed class ReadonlyMutableCollectionPlan :
            IRegionParticipantPlan
        {
            private readonly NestedMutableCollection payload =
                new NestedMutableCollection();

            public string Fingerprint => "tests.mutable-collection";
        }

        private sealed class NestedMutableCollection
        {
            internal readonly List<int> Values =
                new List<int> { 1 };
        }

        private sealed class ReadOnlyCollectionPlan :
            IRegionParticipantPlan
        {
            private readonly ReadOnlyCollection<int> values;

            internal ReadOnlyCollectionPlan(
                ReadOnlyCollection<int> values)
            {
                this.values = values;
            }

            public string Fingerprint =>
                "tests.read-only|" + values.Count;
        }

        private sealed class ImmutableArrayPlan :
            IRegionParticipantPlan
        {
            private readonly RegionImmutableArray<int> values;

            internal ImmutableArrayPlan(
                RegionImmutableArray<int> values)
            {
                this.values = values;
            }

            public string Fingerprint =>
                "tests.immutable-array|" + values.Count;
        }

        private abstract class AbstractTestPlan :
            IRegionParticipantPlan
        {
            public abstract string Fingerprint { get; }
        }

        private class NonSealedTestPlan :
            IRegionParticipantPlan
        {
            public string Fingerprint => "tests.non-sealed";
        }

        private struct ValueTestPlan :
            IRegionParticipantPlan
        {
            public string Fingerprint => "tests.value-plan";
        }

        private sealed class FixedPlanFreezer :
            IRegionParticipantConfigFreezer
        {
            private readonly Type planType;
            private readonly IRegionParticipantPlan plan;

            internal FixedPlanFreezer(
                Type planType,
                IRegionParticipantPlan plan)
            {
                this.planType = planType;
                this.plan = plan;
            }

            public Type ConfigurationType => typeof(TestConfig);
            public Type PlanType => planType;

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan frozen,
                out CoCoDiagnostic diagnostic)
            {
                frozen = configuration is TestConfig ? plan : null;
                diagnostic = frozen == null
                    ? RegionErrors.InvalidProfile("Wrong test config.")
                    : CoCoDiagnostic.None;
                return frozen != null;
            }
        }

        private sealed class DeclaredPlanTypeFreezer :
            IRegionParticipantConfigFreezer
        {
            private readonly Type planType;

            internal DeclaredPlanTypeFreezer(Type planType)
            {
                this.planType = planType;
            }

            public Type ConfigurationType => typeof(TestConfig);
            public Type PlanType => planType;

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = null;
                diagnostic = RegionErrors.InvalidProfile(
                    "Registration-only freezer.");
                return false;
            }
        }

        private sealed class TestFreezer : IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType => typeof(TestConfig);
            public Type PlanType => typeof(TestPlan);

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = configuration is TestConfig ? new TestPlan() : null;
                diagnostic = plan == null
                    ? RegionErrors.InvalidProfile("Wrong test config.")
                    : CoCoDiagnostic.None;
                return plan != null;
            }
        }

        private sealed class MutableMetadataFreezer :
            IRegionParticipantConfigFreezer
        {
            internal Type ConfigurationTypeValue { get; set; } =
                typeof(TestConfig);
            internal Type PlanTypeValue { get; set; } =
                typeof(TestPlan);

            public Type ConfigurationType => ConfigurationTypeValue;
            public Type PlanType => PlanTypeValue;

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = configuration is TestConfig
                    ? new TestPlan()
                    : null;
                diagnostic = plan == null
                    ? RegionErrors.InvalidProfile("Wrong mutable metadata config.")
                    : CoCoDiagnostic.None;
                return plan != null;
            }
        }

        private sealed class BaseConfigFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType => typeof(BaseTestConfig);
            public Type PlanType => typeof(TestPlan);

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = configuration is BaseTestConfig
                    ? new TestPlan()
                    : null;
                diagnostic = plan == null
                    ? RegionErrors.InvalidProfile("Wrong base config.")
                    : CoCoDiagnostic.None;
                return plan != null;
            }
        }

        private sealed class MarkerPlan : IRegionParticipantPlan
        {
            public string Fingerprint => "tests.dependent-resource.v1";
        }

        private sealed class MarkerFreezer :
            IRegionParticipantConfigFreezer,
            IRegionRequiresOwningContentDependency
        {
            public Type ConfigurationType => typeof(TestConfig);
            public Type PlanType => typeof(MarkerPlan);

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = configuration is TestConfig
                    ? new MarkerPlan()
                    : null;
                diagnostic = plan == null
                    ? RegionErrors.InvalidProfile("Wrong marker config.")
                    : CoCoDiagnostic.None;
                return plan != null;
            }
        }

        private sealed class TestFactory : IRegionParticipantFactory
        {
            public Type CandidateType => typeof(TestCandidate);

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = new TestCandidate();
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class MutableMetadataFactory :
            IRegionParticipantFactory
        {
            internal Type CandidateTypeValue { get; set; } =
                typeof(TestCandidate);

            public Type CandidateType => CandidateTypeValue;

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = new TestCandidate();
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class TestCandidate : IRegionParticipantCandidate
        {
            public UniTask<RegionParticipantPrepareResult> PrepareAsync(
                in RegionParticipantPrepareContext context,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(RegionParticipantPrepareResult.Success());

            public bool TryCommit(
                in RegionParticipantCommitContext context,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public UniTask<RegionParticipantCleanupResult> CleanupAsync(
                RegionParticipantCleanupReason reason,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(RegionParticipantCleanupResult.Success());
        }

        private abstract class AbstractTestCandidate :
            IRegionParticipantCandidate
        {
            public abstract UniTask<RegionParticipantPrepareResult>
                PrepareAsync(
                    in RegionParticipantPrepareContext context,
                    CancellationToken cancellationToken);

            public abstract bool TryCommit(
                in RegionParticipantCommitContext context,
                out CoCoDiagnostic diagnostic);

            public abstract UniTask<RegionParticipantCleanupResult>
                CleanupAsync(
                RegionParticipantCleanupReason reason,
                CancellationToken cancellationToken);
        }

        private sealed class AlternateTestCandidate :
            IRegionParticipantCandidate
        {
            public UniTask<RegionParticipantPrepareResult> PrepareAsync(
                in RegionParticipantPrepareContext context,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(
                    RegionParticipantPrepareResult.Success());

            public bool TryCommit(
                in RegionParticipantCommitContext context,
                out CoCoDiagnostic diagnostic)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public UniTask<RegionParticipantCleanupResult> CleanupAsync(
                RegionParticipantCleanupReason reason,
                CancellationToken cancellationToken) =>
                UniTask.FromResult(
                    RegionParticipantCleanupResult.Success());
        }

        private sealed class DeclaredCandidateTypeFactory :
            IRegionParticipantFactory
        {
            private readonly Type candidateType;

            internal DeclaredCandidateTypeFactory(Type candidateType)
            {
                this.candidateType = candidateType;
            }

            public Type CandidateType => candidateType;

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = null;
                diagnostic = RegionErrors.TransitionFailed(
                    "Registration-only factory.");
                return false;
            }
        }

        private sealed class FixedAddressableResolver :
            IRegionAddressableSceneResolver
        {
            private readonly string path;

            internal FixedAddressableResolver(string path) => this.path = path;

            public bool TryResolveUniqueScene(
                string address,
                out string sceneAssetPath,
                out CoCoDiagnostic diagnostic)
            {
                sceneAssetPath = path;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }
    }
}
