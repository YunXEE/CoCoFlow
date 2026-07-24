using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

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

        [TestCase(true)]
        [TestCase(false)]
        public void ColdStartRejectsActiveAndInactiveChildrenUnderAnchorRoot(
            bool childActive)
        {
            Scene scene = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
            var anchorRoot = new GameObject("Region Anchor");
            SceneManager.MoveGameObjectToScene(anchorRoot, scene);
            var child = new GameObject("Escaped Managed Content");
            child.transform.SetParent(anchorRoot.transform, false);
            child.SetActive(childActive);

            CoCoRegionChunkAnchor anchor =
                anchorRoot.AddComponent<CoCoRegionChunkAnchor>();
            Assert.IsTrue(RegionId.TryCreate(
                "world.wilderness",
                out RegionId regionId));
            Assert.IsTrue(RegionChunkId.TryCreate(
                "north-west",
                out RegionChunkId chunkId));
            SetField(anchor, "regionId", regionId);
            SetField(anchor, "chunkId", chunkId);

            try
            {
                Assert.IsFalse(anchor.TryValidateColdStart(
                    regionId,
                    chunkId,
                    out CoCoFlow.Runtime.Core.CoCoDiagnostic diagnostic));
                Assert.AreEqual(
                    CoCoFlow.Runtime.Core.CoCoDiagnosticCode
                        .RegionSceneContractViolation,
                    diagnostic.Code);
                StringAssert.Contains(
                    "cannot contain child GameObjects",
                    diagnostic.Message);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void SetField<TValue>(
            CoCoRegionChunkAnchor anchor,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(CoCoRegionChunkAnchor).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(anchor, value);
        }
    }
}
