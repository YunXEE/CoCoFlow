using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using NUnit.Framework;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map.Tests
{
    public sealed class RegionDependencyCompilerTests
    {
        private readonly List<UnityEngine.Object> owned =
            new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int index = owned.Count - 1; index >= 0; index--)
            {
                if (owned[index] != null)
                {
                    UnityEngine.Object.DestroyImmediate(
                        owned[index]);
                }
            }

            owned.Clear();
        }

        [Test]
        public void CompileAllAcceptsTransitiveDependencyDag()
        {
            CoCoRegionBinding wilderness =
                Binding("world.wilderness");
            CoCoRegionBinding castle =
                Binding(
                    "world.castle",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.wilderness",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Represented));
            CoCoRegionBinding mine =
                Binding(
                    "world.mine",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.castle",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Background));

            IReadOnlyList<RegionCompileResult> results =
                CompileAll(wilderness, castle, mine);

            Assert.IsTrue(results[0].Succeeded);
            Assert.IsTrue(results[1].Succeeded);
            Assert.IsTrue(results[2].Succeeded);
            Assert.AreEqual(
                1,
                results[1].Plan.DependencyRules.Count);
            Assert.AreEqual(
                "world.wilderness",
                results[1].Plan.DependencyRules[0]
                    .TargetRegionId.Value);
            Assert.IsNotEmpty(
                results[1].Plan.DependencyRules[0].Fingerprint);
        }

        [Test]
        public void CompileAllRejectsUnknownTarget()
        {
            CoCoRegionBinding castle =
                Binding(
                    "world.castle",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.missing",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Represented));

            IReadOnlyList<RegionCompileResult> results =
                CompileAll(castle);

            Assert.IsFalse(results[0].Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionProfile,
                FirstError(results[0]).Code);
        }

        [Test]
        public void CompileAllRejectsTargetCapabilityOutsideTargetProfile()
        {
            Assert.IsTrue(
                RegionCapabilityId.TryCreate(
                    "project.weather",
                    out RegionCapabilityId weather));
            CoCoRegionBinding wilderness =
                Binding("world.wilderness");
            CoCoRegionBinding castle =
                Binding(
                    "world.castle",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.wilderness",
                        RegionCoverageKind.All,
                        weather));
            RegionParticipantCatalog catalog = Catalog(weather);

            IReadOnlyList<RegionCompileResult> results =
                new RegionBindingCompiler().CompileAll(
                    new[] { wilderness, castle },
                    catalog);

            Assert.IsTrue(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionCapability,
                FirstError(results[1]).Code);
        }

        [Test]
        public void CompileAllRejectsTargetChunkOutsideTargetRegion()
        {
            Assert.IsTrue(
                RegionChunkId.TryCreate(
                    "world.wilderness.missing",
                    out RegionChunkId missingChunk));
            CoCoRegionBinding wilderness =
                Binding("world.wilderness");
            CoCoRegionBinding castle =
                Binding(
                    "world.castle",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.wilderness",
                        RegionCoverageKind.Chunks,
                        new[] { missingChunk },
                        RegionCapabilityId.Represented));

            IReadOnlyList<RegionCompileResult> results =
                CompileAll(wilderness, castle);

            Assert.IsTrue(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionCoverage,
                FirstError(results[1]).Code);
        }

        [Test]
        public void CompileRejectsDuplicateAndSelfDependencyRules()
        {
            RegionDependencyRule first =
                Rule(
                    RegionCapabilityId.Full,
                    "world.wilderness",
                    RegionCoverageKind.All,
                    RegionCapabilityId.Represented);
            CoCoRegionBinding duplicate =
                Binding(
                    "world.castle",
                    first,
                    Rule(
                        RegionCapabilityId.Full,
                        "world.wilderness",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Represented));
            CoCoRegionBinding self =
                Binding(
                    "world.wilderness",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.wilderness",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Represented));
            RegionParticipantCatalog catalog = Catalog();

            RegionCompileResult duplicateResult =
                new RegionBindingCompiler().Compile(
                    duplicate,
                    catalog);
            RegionCompileResult selfResult =
                new RegionBindingCompiler().Compile(
                    self,
                    catalog);

            Assert.IsFalse(duplicateResult.Succeeded);
            Assert.IsFalse(selfResult.Succeeded);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionProfile,
                FirstError(duplicateResult).Code);
            Assert.AreEqual(
                CoCoDiagnosticCode.InvalidRegionProfile,
                FirstError(selfResult).Code);
        }

        [Test]
        public void CompileAllRejectsGlobalRegionCycle()
        {
            CoCoRegionBinding wilderness =
                Binding(
                    "world.wilderness",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.castle",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Represented));
            CoCoRegionBinding castle =
                Binding(
                    "world.castle",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.wilderness",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Represented));

            IReadOnlyList<RegionCompileResult> results =
                CompileAll(wilderness, castle);

            Assert.IsFalse(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            StringAssert.Contains(
                "cycle",
                FirstError(results[0]).Message.ToLowerInvariant());
            StringAssert.Contains(
                "cycle",
                FirstError(results[1]).Message.ToLowerInvariant());
        }

        [Test]
        public void CompileAllPropagatesTransitiveTargetInvalidation()
        {
            Assert.IsTrue(
                RegionCapabilityId.TryCreate(
                    "project.weather",
                    out RegionCapabilityId weather));
            CoCoRegionBinding wilderness =
                Binding("world.wilderness");
            CoCoRegionBinding castle =
                Binding(
                    "world.castle",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.wilderness",
                        RegionCoverageKind.All,
                        weather));
            CoCoRegionBinding mine =
                Binding(
                    "world.mine",
                    Rule(
                        RegionCapabilityId.Full,
                        "world.castle",
                        RegionCoverageKind.All,
                        RegionCapabilityId.Represented));

            IReadOnlyList<RegionCompileResult> results =
                new RegionBindingCompiler().CompileAll(
                    new[] { wilderness, castle, mine },
                    Catalog(weather));

            Assert.IsTrue(results[0].Succeeded);
            Assert.IsFalse(results[1].Succeeded);
            Assert.IsFalse(
                results[2].Succeeded,
                "An upstream source cannot remain compiled when its target fails global dependency validation.");
            StringAssert.Contains(
                "failed global dependency validation",
                FirstError(results[2]).Message);
        }

        private IReadOnlyList<RegionCompileResult> CompileAll(
            params CoCoRegionBinding[] bindings) =>
            new RegionBindingCompiler().CompileAll(
                bindings,
                Catalog());

        private RegionParticipantCatalog Catalog(
            params RegionCapabilityId[] customCapabilities)
        {
            var catalog = new RegionParticipantCatalog();
            for (int index = 0;
                 index < customCapabilities.Length;
                 index++)
            {
                Assert.IsTrue(
                    catalog.TryRegisterCapability(
                        customCapabilities[index],
                        out CoCoDiagnostic diagnostic),
                    diagnostic.Message);
            }

            catalog.Seal();
            return catalog;
        }

        private CoCoRegionBinding Binding(
            string regionValue,
            params RegionDependencyRule[] rules)
        {
            Assert.IsTrue(
                RegionId.TryCreate(
                    regionValue,
                    out RegionId regionId));
            CoCoRegionProfile profile =
                ScriptableObject.CreateInstance<CoCoRegionProfile>();
            owned.Add(profile);
            string profileValue =
                "profile." +
                regionValue.Replace('/', '.');
            Assert.IsTrue(
                RegionProfileId.TryCreate(
                    profileValue,
                    out RegionProfileId profileId));
            SetField(
                profile,
                "profileId",
                profileId);

            CoCoRegionBinding binding =
                ScriptableObject.CreateInstance<CoCoRegionBinding>();
            owned.Add(binding);
            SetField(
                binding,
                "regionId",
                regionId);
            SetField(
                binding,
                "profile",
                profile);
            SetField(
                binding,
                "dependencyRules",
                new List<RegionDependencyRule>(
                    rules ?? Array.Empty<RegionDependencyRule>()));
            return binding;
        }

        private static RegionDependencyRule Rule(
            RegionCapabilityId sourceCapability,
            string targetRegionValue,
            RegionCoverageKind coverageKind,
            params RegionCapabilityId[] targetCapabilities) =>
            Rule(
                sourceCapability,
                targetRegionValue,
                coverageKind,
                Array.Empty<RegionChunkId>(),
                targetCapabilities);

        private static RegionDependencyRule Rule(
            RegionCapabilityId sourceCapability,
            string targetRegionValue,
            RegionCoverageKind coverageKind,
            IReadOnlyList<RegionChunkId> targetChunks,
            params RegionCapabilityId[] targetCapabilities)
        {
            Assert.IsTrue(
                RegionId.TryCreate(
                    targetRegionValue,
                    out RegionId targetRegionId));
            var rule = new RegionDependencyRule();
            SetField(
                rule,
                "sourceCapability",
                sourceCapability);
            SetField(
                rule,
                "targetRegionId",
                targetRegionId);
            SetField(
                rule,
                "targetCapabilities",
                new List<RegionCapabilityId>(
                    targetCapabilities));
            SetField(
                rule,
                "targetCoverageKind",
                coverageKind);
            SetField(
                rule,
                "targetChunks",
                new List<RegionChunkId>(
                    targetChunks));
            return rule;
        }

        private static CoCoDiagnostic FirstError(
            RegionCompileResult result)
        {
            for (int index = 0;
                 index < result.Diagnostics.Count;
                 index++)
            {
                if (result.Diagnostics[index].Diagnostic.IsError)
                {
                    return result.Diagnostics[index].Diagnostic;
                }
            }

            Assert.Fail("Expected an error diagnostic.");
            return default;
        }

        private static void SetField<TTarget, TValue>(
            TTarget target,
            string fieldName,
            TValue value)
        {
            FieldInfo field =
                typeof(TTarget).GetField(
                    fieldName,
                    BindingFlags.Instance |
                    BindingFlags.NonPublic);
            Assert.IsNotNull(
                field,
                typeof(TTarget).Name + "." + fieldName);
            field.SetValue(
                target,
                value);
        }
    }
}
