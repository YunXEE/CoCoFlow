using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    public sealed class RegionProfileCompiler
    {
        private static readonly RegionCapabilityId[] StandardCapabilities =
        {
            RegionCapabilityId.Represented,
            RegionCapabilityId.Background,
            RegionCapabilityId.Enterable,
            RegionCapabilityId.Full
        };

        internal bool TryCompile(
            CoCoRegionProfile profile,
            RegionParticipantCatalog catalog,
            IList<RegionCompileDiagnostic> diagnostics,
            out RegionCompiledProfileBlueprint blueprint)
        {
            blueprint = null;
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            if (profile == null)
            {
                AddError(
                    diagnostics,
                    "profile",
                    RegionErrors.InvalidProfile(
                        "A Region Profile is required."));
                return false;
            }

            if (profile.SchemaVersion !=
                CoCoRegionProfile.CurrentSchemaVersion)
            {
                AddError(
                    diagnostics,
                    "profile.schemaVersion",
                    RegionErrors.InvalidProfile(
                        "Region Profile schema version '" +
                        profile.SchemaVersion +
                        "' is unsupported. Recreate the Profile from the current template."));
            }

            if (!profile.ProfileId.IsValid)
            {
                AddError(
                    diagnostics,
                    "profile.profileId",
                    RegionErrors.InvalidIdentifier(
                        "A Region Profile requires an Editor-assigned stable ProfileId."));
            }

            if (catalog == null || !catalog.IsSealed)
            {
                AddError(
                    diagnostics,
                    "catalog",
                    RegionErrors.CatalogConflict(
                        "Compilation requires an explicit sealed participant catalog."));
                return false;
            }

            List<RegionCompiledTier> tiers =
                CompileTiers(profile, catalog, diagnostics);
            List<RegionCompiledParticipantDefinition> participants =
                CompileParticipants(
                    profile,
                    tiers,
                    catalog,
                    diagnostics);
            if (HasErrors(diagnostics)) return false;

            blueprint = new RegionCompiledProfileBlueprint(
                profile.ProfileId,
                profile.SchemaVersion,
                tiers,
                participants);
            return true;
        }

        private static List<RegionCompiledTier> CompileTiers(
            CoCoRegionProfile profile,
            RegionParticipantCatalog catalog,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            var compiled =
                new List<RegionCompiledTier>(profile.Tiers.Count);
            var tierIds = new HashSet<RegionTierId>();

            if (profile.Tiers.Count < 2)
            {
                AddError(
                    diagnostics,
                    "profile.tiers",
                    RegionErrors.InvalidProfile(
                        "A Region Profile requires an empty first tier and at least one active tier."));
            }

            RegionCapabilitySet previous = null;
            for (int tierIndex = 0;
                 tierIndex < profile.Tiers.Count;
                 tierIndex++)
            {
                RegionTierDefinition tier = profile.Tiers[tierIndex];
                string path = "profile.tiers[" + tierIndex + "]";
                if (tier == null)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Tier definitions cannot be null."));
                    continue;
                }

                if (!tier.TierId.IsValid)
                {
                    AddError(
                        diagnostics,
                        path + ".tierId",
                        RegionErrors.InvalidIdentifier(
                            "TierId must be a valid stable identifier."));
                }
                else if (!tierIds.Add(tier.TierId))
                {
                    AddError(
                        diagnostics,
                        path + ".tierId",
                        RegionErrors.InvalidProfile(
                            "TierId values must be unique within a Profile."));
                }

                if (HasDuplicateCapabilities(tier.Capabilities) ||
                    !RegionCapabilitySet.TryCreate(
                        tier.Capabilities,
                        out RegionCapabilitySet capabilities))
                {
                    AddError(
                        diagnostics,
                        path + ".capabilities",
                        RegionErrors.InvalidCapability(
                            "Tier capabilities must be valid and unique."));
                    continue;
                }

                for (int capabilityIndex = 0;
                     capabilityIndex < capabilities.Count;
                     capabilityIndex++)
                {
                    RegionCapabilityId capability =
                        capabilities.Capabilities[capabilityIndex];
                    if (!catalog.SupportsCapability(capability))
                    {
                        AddError(
                            diagnostics,
                            path + ".capabilities[" +
                            capabilityIndex + "]",
                            RegionErrors.UnsupportedCapability(capability));
                    }
                }

                if (!HasStandardCapabilityPrefix(capabilities))
                {
                    AddError(
                        diagnostics,
                        path + ".capabilities",
                        RegionErrors.InvalidProfile(
                            "Standard capabilities must form the ordered Represented, Background, Enterable, Full prefix. Multiple consecutive standards may be introduced by one tier."));
                }

                if (tierIndex == 0 && capabilities.Count != 0)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "The first tier must contain no capabilities."));
                }
                else if (tierIndex > 0 &&
                         (previous == null ||
                          !capabilities.IsStrictSupersetOf(previous)))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Every tier after the first must be a strict superset of the preceding tier."));
                }

                compiled.Add(
                    new RegionCompiledTier(
                        tierIndex,
                        tier.TierId,
                        tier.Name,
                        capabilities));
                previous = capabilities;
            }

            if (compiled.Count == 0 ||
                !ContainsEveryStandardCapability(
                    compiled[compiled.Count - 1].Capabilities))
            {
                AddError(
                    diagnostics,
                    "profile.tiers",
                    RegionErrors.InvalidProfile(
                        "The final tier must include every standard Region capability."));
            }

            return compiled;
        }

        private static List<RegionCompiledParticipantDefinition>
            CompileParticipants(
                CoCoRegionProfile profile,
                IReadOnlyList<RegionCompiledTier> tiers,
                RegionParticipantCatalog catalog,
                IList<RegionCompileDiagnostic> diagnostics)
        {
            var compiled =
                new List<RegionCompiledParticipantDefinition>(
                    profile.Participants.Count);
            var definitionBySlot =
                new Dictionary<
                    RegionParticipantSlotId,
                    RegionParticipantDefinition>();
            var compiledBySlot =
                new Dictionary<
                    RegionParticipantSlotId,
                    RegionCompiledParticipantDefinition>();

            for (int index = 0;
                 index < profile.Participants.Count;
                 index++)
            {
                RegionParticipantDefinition definition =
                    profile.Participants[index];
                string path = "profile.participants[" + index + "]";
                if (definition == null)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Participant definitions cannot be null."));
                    continue;
                }

                if (!definition.SlotId.IsValid ||
                    !definition.ParticipantTypeId.IsValid)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidIdentifier(
                            "Participant slot and type identifiers must be valid."));
                    continue;
                }

                if (!definitionBySlot.TryAdd(
                        definition.SlotId,
                        definition))
                {
                    AddError(
                        diagnostics,
                        path + ".slotId",
                        RegionErrors.InvalidProfile(
                            "Participant SlotId values must be unique within a Profile."));
                    continue;
                }

                if (!Enum.IsDefined(
                        typeof(RegionParticipantPhase),
                        definition.Phase) ||
                    !Enum.IsDefined(
                        typeof(RegionParticipantRequirement),
                        definition.Requirement))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Participant phase and requirement must use defined values."));
                }

                if (HasDuplicateOrInvalidDependencies(definition))
                {
                    AddError(
                        diagnostics,
                        path + ".dependencies",
                        RegionErrors.InvalidProfile(
                            "Dependencies must be valid, unique, and cannot reference the same slot."));
                }

                List<RegionCompiledParticipantTierDefinition> settings =
                    CompileTierSettings(
                        definition,
                        tiers,
                        catalog,
                        path,
                        diagnostics);
                var compiledDefinition =
                    new RegionCompiledParticipantDefinition(
                        definition,
                        settings);
                compiled.Add(compiledDefinition);
                compiledBySlot.Add(
                    definition.SlotId,
                    compiledDefinition);
            }

            ValidateDependencyGraph(
                compiled,
                definitionBySlot,
                compiledBySlot,
                tiers,
                diagnostics);
            return compiled;
        }

        private static List<RegionCompiledParticipantTierDefinition>
            CompileTierSettings(
                RegionParticipantDefinition definition,
                IReadOnlyList<RegionCompiledTier> tiers,
                RegionParticipantCatalog catalog,
                string participantPath,
                IList<RegionCompileDiagnostic> diagnostics)
        {
            var compiled =
                new List<RegionCompiledParticipantTierDefinition>(
                    tiers.Count);
            var sourceByTier =
                new Dictionary<RegionTierId, RegionParticipantTierSetting>();
            var knownTierIds = new HashSet<RegionTierId>();
            for (int index = 0; index < tiers.Count; index++)
            {
                knownTierIds.Add(tiers[index].TierId);
            }

            for (int index = 0;
                 index < definition.TierSettings.Count;
                 index++)
            {
                RegionParticipantTierSetting setting =
                    definition.TierSettings[index];
                string path =
                    participantPath + ".tierSettings[" + index + "]";
                if (setting == null)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Participant tier settings cannot be null."));
                    continue;
                }

                if (!setting.TierId.IsValid)
                {
                    AddError(
                        diagnostics,
                        path + ".tierId",
                        RegionErrors.InvalidIdentifier(
                            "Participant tier settings require a valid TierId."));
                    continue;
                }

                if (!knownTierIds.Contains(setting.TierId))
                {
                    AddError(
                        diagnostics,
                        path + ".tierId",
                        RegionErrors.InvalidProfile(
                            "Participant tier settings cannot reference a TierId outside this Profile."));
                    continue;
                }

                if (!sourceByTier.TryAdd(setting.TierId, setting))
                {
                    AddError(
                        diagnostics,
                        path + ".tierId",
                        RegionErrors.InvalidProfile(
                            "Each Participant × Tier cell must be defined exactly once."));
                }
            }

            if (definition.TierSettings.Count != tiers.Count)
            {
                AddError(
                    diagnostics,
                    participantPath + ".tierSettings",
                    RegionErrors.InvalidProfile(
                        "Each participant requires exactly one setting for every Profile tier."));
            }

            for (int tierIndex = 0;
                 tierIndex < tiers.Count;
                 tierIndex++)
            {
                RegionCompiledTier tier = tiers[tierIndex];
                string path =
                    participantPath + ".tierSettings." +
                    tier.TierId.Value;
                if (!sourceByTier.TryGetValue(
                        tier.TierId,
                        out RegionParticipantTierSetting setting))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "The Participant × Tier cell is missing."));
                    continue;
                }

                if (!setting.Enabled)
                {
                    if (setting.ModeId.IsValid ||
                        setting.Configuration != null)
                    {
                        AddError(
                            diagnostics,
                            path,
                            RegionErrors.InvalidProfile(
                                "A disabled Participant × Tier cell cannot retain a Mode or configuration."));
                    }

                    compiled.Add(
                        new RegionCompiledParticipantTierDefinition(
                            tier.TierId,
                            tier.Capabilities));
                    continue;
                }

                if (tierIndex == 0)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Every participant must be disabled in the empty first tier."));
                }

                if (!setting.ModeId.IsValid)
                {
                    AddError(
                        diagnostics,
                        path + ".modeId",
                        RegionErrors.InvalidIdentifier(
                            "An enabled Participant × Tier cell requires a valid ModeId."));
                    continue;
                }

                if (!catalog.TryGetRegistration(
                        definition.ParticipantTypeId,
                        setting.ModeId,
                        out RegionParticipantRegistration registration))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.CatalogConflict(
                            "No participant registration exists for type '" +
                            definition.ParticipantTypeId.Value +
                            "' and mode '" +
                            setting.ModeId.Value +
                            "'."));
                    continue;
                }

                if (setting.Configuration == null ||
                    setting.Configuration.GetType() !=
                    registration.ConfigurationType)
                {
                    AddError(
                        diagnostics,
                        path + ".configuration",
                        RegionErrors.InvalidProfile(
                            "The SerializeReference configuration is missing or does not exactly match the registered freezer type."));
                    continue;
                }

                if (!Intersects(
                        tier.Capabilities,
                        registration.SupportedCapabilities))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "The selected participant Mode does not support any capability supplied by this tier."));
                }

                compiled.Add(
                    new RegionCompiledParticipantTierDefinition(
                        tier.TierId,
                        setting.ModeId,
                        setting.Configuration,
                        tier.Capabilities,
                        registration));
            }

            return compiled;
        }

        private static void ValidateDependencyGraph(
            IReadOnlyList<RegionCompiledParticipantDefinition> compiled,
            IReadOnlyDictionary<
                RegionParticipantSlotId,
                RegionParticipantDefinition> definitionBySlot,
            IReadOnlyDictionary<
                RegionParticipantSlotId,
                RegionCompiledParticipantDefinition> compiledBySlot,
            IReadOnlyList<RegionCompiledTier> tiers,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            var state = new Dictionary<RegionParticipantSlotId, byte>();
            for (int index = 0; index < compiled.Count; index++)
            {
                RegionCompiledParticipantDefinition compiledDefinition =
                    compiled[index];
                RegionParticipantDefinition definition =
                    compiledDefinition.Source;
                for (int dependencyIndex = 0;
                     dependencyIndex < definition.Dependencies.Count;
                     dependencyIndex++)
                {
                    RegionParticipantSlotId dependencyId =
                        definition.Dependencies[dependencyIndex];
                    if (!definitionBySlot.TryGetValue(
                            dependencyId,
                            out RegionParticipantDefinition dependency) ||
                        !compiledBySlot.TryGetValue(
                            dependencyId,
                            out RegionCompiledParticipantDefinition
                                compiledDependency))
                    {
                        AddError(
                            diagnostics,
                            "profile.participants." +
                            definition.SlotId.Value +
                            ".dependencies",
                            RegionErrors.InvalidProfile(
                                "Dependency slot '" +
                                dependencyId.Value +
                                "' is not defined by this Profile."));
                        continue;
                    }

                    if (definition.Requirement ==
                        RegionParticipantRequirement.Required &&
                        dependency.Requirement ==
                        RegionParticipantRequirement.Optional)
                    {
                        AddError(
                            diagnostics,
                            "profile.participants." +
                            definition.SlotId.Value +
                            ".dependencies",
                            RegionErrors.InvalidProfile(
                                "A Required participant cannot depend on an Optional participant."));
                    }

                    ValidateDependencyTierCells(
                        compiledDefinition,
                        compiledDependency,
                        tiers,
                        diagnostics);
                }

                if (HasCycle(
                        definition.SlotId,
                        definitionBySlot,
                        state))
                {
                    AddError(
                        diagnostics,
                        "profile.participants." +
                        definition.SlotId.Value +
                        ".dependencies",
                        RegionErrors.InvalidProfile(
                            "Participant dependencies must form a directed acyclic graph."));
                    break;
                }
            }
        }

        private static void ValidateDependencyTierCells(
            RegionCompiledParticipantDefinition dependant,
            RegionCompiledParticipantDefinition dependency,
            IReadOnlyList<RegionCompiledTier> tiers,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            for (int tierIndex = 0;
                 tierIndex < tiers.Count;
                 tierIndex++)
            {
                RegionTierId tierId = tiers[tierIndex].TierId;
                if (!dependant.TryGetTierSetting(
                        tierId,
                        out RegionCompiledParticipantTierDefinition
                            dependantSetting) ||
                    !dependantSetting.Enabled)
                {
                    continue;
                }

                if (!dependency.TryGetTierSetting(
                        tierId,
                        out RegionCompiledParticipantTierDefinition
                            dependencySetting) ||
                    !dependencySetting.Enabled)
                {
                    AddError(
                        diagnostics,
                        "profile.participants." +
                        dependant.Source.SlotId.Value +
                        ".tierSettings." +
                        tierId.Value,
                        RegionErrors.InvalidProfile(
                            "An enabled participant requires every dependency to be enabled in the same tier."));
                }
            }
        }

        private static bool HasDuplicateOrInvalidDependencies(
            RegionParticipantDefinition definition)
        {
            var unique = new HashSet<RegionParticipantSlotId>();
            for (int index = 0;
                 index < definition.Dependencies.Count;
                 index++)
            {
                RegionParticipantSlotId dependency =
                    definition.Dependencies[index];
                if (!dependency.IsValid ||
                    dependency == definition.SlotId ||
                    !unique.Add(dependency))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasDuplicateCapabilities(
            IReadOnlyList<RegionCapabilityId> capabilities)
        {
            if (capabilities == null) return true;

            var unique = new HashSet<RegionCapabilityId>();
            for (int index = 0; index < capabilities.Count; index++)
            {
                RegionCapabilityId capability = capabilities[index];
                if (!capability.IsValid || !unique.Add(capability))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasStandardCapabilityPrefix(
            RegionCapabilitySet capabilities)
        {
            bool precedingPresent = true;
            for (int index = 0;
                 index < StandardCapabilities.Length;
                 index++)
            {
                bool present =
                    capabilities.Contains(StandardCapabilities[index]);
                if (present && !precedingPresent) return false;
                precedingPresent &= present;
            }

            return true;
        }

        private static bool ContainsEveryStandardCapability(
            RegionCapabilitySet capabilities)
        {
            for (int index = 0;
                 index < StandardCapabilities.Length;
                 index++)
            {
                if (!capabilities.Contains(StandardCapabilities[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool Intersects(
            RegionCapabilitySet left,
            RegionCapabilitySet right)
        {
            if (left == null || right == null) return false;

            for (int index = 0; index < left.Count; index++)
            {
                if (right.Contains(left.Capabilities[index])) return true;
            }

            return false;
        }

        private static bool HasCycle(
            RegionParticipantSlotId slotId,
            IReadOnlyDictionary<
                RegionParticipantSlotId,
                RegionParticipantDefinition> definitionBySlot,
            IDictionary<RegionParticipantSlotId, byte> state)
        {
            if (state.TryGetValue(slotId, out byte visitState))
            {
                return visitState == 1;
            }

            if (!definitionBySlot.TryGetValue(
                    slotId,
                    out RegionParticipantDefinition definition))
            {
                return false;
            }

            state[slotId] = 1;
            for (int index = 0;
                 index < definition.Dependencies.Count;
                 index++)
            {
                if (HasCycle(
                        definition.Dependencies[index],
                        definitionBySlot,
                        state))
                {
                    return true;
                }
            }

            state[slotId] = 2;
            return false;
        }

        private static bool HasErrors(
            IList<RegionCompileDiagnostic> diagnostics)
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Diagnostic.IsError) return true;
            }

            return false;
        }

        private static void AddError(
            IList<RegionCompileDiagnostic> diagnostics,
            string path,
            CoCoDiagnostic diagnostic) =>
            diagnostics.Add(
                new RegionCompileDiagnostic(path, diagnostic));
    }
}
