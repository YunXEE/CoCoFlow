using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
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

        private static RegionParticipantCatalog CreateCatalog(
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
            Assert.IsTrue(RegionParticipantRegistration.TryCreate(
                TypeId(),
                ModeId(),
                supported,
                new TestFreezer(),
                new TestFactory(),
                out RegionParticipantRegistration registration,
                out CoCoDiagnostic registrationDiagnostic),
                registrationDiagnostic.Message);
            Assert.IsTrue(catalog.TryRegisterParticipant(
                registration,
                out CoCoDiagnostic catalogDiagnostic),
                catalogDiagnostic.Message);
            catalog.Seal();
            return catalog;
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

        private sealed class TestPlan : IRegionParticipantPlan
        {
            public string Fingerprint => "tests.scene.plan.v1";
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
