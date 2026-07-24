using NUnit.Framework;

namespace CoCoFlow.Runtime.Modules.Map.Tests
{
    public sealed class RegionContractTests
    {
        [Test]
        public void CapabilityIdAllowsNamespacedExtensionsAndReservesCoCoFlow()
        {
            Assert.IsTrue(RegionCapabilityId.TryCreate(
                "project.weather.simulated",
                out RegionCapabilityId custom));
            Assert.AreEqual("project.weather.simulated", custom.Value);
            Assert.IsFalse(custom.IsStandard);

            Assert.IsTrue(RegionCapabilityId.TryCreate(
                RegionCapabilityId.Full.Value,
                out RegionCapabilityId full));
            Assert.AreEqual(RegionCapabilityId.Full, full);
            Assert.IsTrue(full.IsStandard);

            Assert.IsFalse(RegionCapabilityId.TryCreate(
                "cocoflow.project-private",
                out _));
            Assert.IsFalse(RegionCapabilityId.TryCreate(
                "not-namespaced",
                out _));
        }

        [Test]
        public void CapabilitySetSupportsCustomInsertionAndStrictSuperset()
        {
            Assert.IsTrue(RegionCapabilityId.TryCreate(
                "project.weather.visible",
                out RegionCapabilityId custom));
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[] { RegionCapabilityId.Represented },
                out RegionCapabilitySet represented));
            Assert.IsTrue(RegionCapabilitySet.TryCreate(
                new[]
                {
                    RegionCapabilityId.Represented,
                    custom
                },
                out RegionCapabilitySet extended));
            Assert.IsTrue(extended.IsStrictSupersetOf(represented));
            Assert.IsTrue(extended.Contains(custom));
            Assert.AreEqual(2, represented.Union(extended).Count);
        }

        [Test]
        public void CoverageIsExactlyAllOrANonEmptyUniqueChunkSet()
        {
            Assert.IsTrue(RegionCoverage.All.IsValid);
            Assert.IsTrue(RegionCoverage.All.CoversAll);

            Assert.IsTrue(RegionChunkId.TryCreate(
                "wilderness/north-west",
                out RegionChunkId northWest));
            Assert.IsTrue(RegionCoverage.TryCreateChunks(
                new[] { northWest },
                out RegionCoverage explicitCoverage));
            Assert.AreEqual(RegionCoverageKind.Chunks, explicitCoverage.Kind);
            Assert.IsTrue(explicitCoverage.Contains(northWest));

            Assert.IsFalse(RegionCoverage.TryCreateChunks(
                System.Array.Empty<RegionChunkId>(),
                out _));
            Assert.IsFalse(RegionCoverage.TryCreateChunks(
                new[] { northWest, northWest },
                out _));
        }

        [Test]
        public void StableIdsNormalizeOnlyAtExplicitConstructionBoundary()
        {
            Assert.IsTrue(RegionId.TryCreate(
                " Wilderness.Main ",
                out RegionId regionId));
            Assert.AreEqual("wilderness.main", regionId.Value);
            Assert.IsTrue(regionId.IsValid);

            Assert.IsTrue(RegionParticipantModeId.TryCreate(
                "project.mode.low",
                out RegionParticipantModeId modeId));
            Assert.IsTrue(modeId.IsValid);
            Assert.IsFalse(RegionParticipantModeId.TryCreate(
                "mode",
                out _));
        }
    }
}
