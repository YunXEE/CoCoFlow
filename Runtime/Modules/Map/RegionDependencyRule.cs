using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    /// <summary>
    /// Declares a make-before-break demand from one Region to another.
    /// The rule is activated when the source Region's resolved effective
    /// capabilities contain <see cref="SourceCapability"/>.
    /// </summary>
    [Serializable]
    public sealed class RegionDependencyRule
    {
        [SerializeField] private RegionCapabilityId sourceCapability;
        [SerializeField] private RegionId targetRegionId;
        [SerializeField] private List<RegionCapabilityId> targetCapabilities =
            new List<RegionCapabilityId>();
        [SerializeField] private RegionCoverageKind targetCoverageKind =
            RegionCoverageKind.All;
        [SerializeField] private List<RegionChunkId> targetChunks =
            new List<RegionChunkId>();

        public RegionCapabilityId SourceCapability => sourceCapability;
        public RegionId TargetRegionId => targetRegionId;
        public IReadOnlyList<RegionCapabilityId> TargetCapabilities =>
            targetCapabilities ??
            (IReadOnlyList<RegionCapabilityId>)
            Array.Empty<RegionCapabilityId>();
        public RegionCoverageKind TargetCoverageKind => targetCoverageKind;
        public IReadOnlyList<RegionChunkId> TargetChunks =>
            targetChunks ??
            (IReadOnlyList<RegionChunkId>)Array.Empty<RegionChunkId>();

        public bool TryGetTargetCoverage(out RegionCoverage coverage)
        {
            if (targetCoverageKind == RegionCoverageKind.All)
            {
                coverage = RegionCoverage.All;
                return true;
            }

            if (targetCoverageKind == RegionCoverageKind.Chunks)
            {
                return RegionCoverage.TryCreateChunks(
                    TargetChunks,
                    out coverage);
            }

            coverage = default;
            return false;
        }
    }
}
