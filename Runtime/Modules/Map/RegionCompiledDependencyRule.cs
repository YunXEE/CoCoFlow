using System;

namespace CoCoFlow.Runtime.Modules.Map
{
    /// <summary>
    /// Immutable cross-Region dependency data produced by
    /// <see cref="RegionBindingCompiler"/>.
    /// </summary>
    public sealed class RegionCompiledDependencyRule
    {
        internal RegionCompiledDependencyRule(
            RegionCapabilityId sourceCapability,
            RegionId targetRegionId,
            RegionCapabilitySet targetCapabilities,
            RegionCoverage targetCoverage,
            string fingerprint)
        {
            SourceCapability = sourceCapability;
            TargetRegionId = targetRegionId;
            TargetCapabilities =
                targetCapabilities ?? RegionCapabilitySet.Empty;
            TargetCoverage = targetCoverage;
            Fingerprint = fingerprint ?? string.Empty;
        }

        public RegionCapabilityId SourceCapability { get; }
        public RegionId TargetRegionId { get; }
        public RegionCapabilitySet TargetCapabilities { get; }
        public RegionCoverage TargetCoverage { get; }
        public string Fingerprint { get; }

        public bool IsActiveFor(RegionCapabilitySet sourceCapabilities) =>
            sourceCapabilities != null &&
            sourceCapabilities.Contains(SourceCapability);

        public bool EqualsDefinition(RegionCompiledDependencyRule other) =>
            other != null &&
            SourceCapability == other.SourceCapability &&
            TargetRegionId == other.TargetRegionId &&
            TargetCapabilities.Equals(other.TargetCapabilities) &&
            TargetCoverage == other.TargetCoverage &&
            string.Equals(
                Fingerprint,
                other.Fingerprint,
                StringComparison.Ordinal);
    }
}
