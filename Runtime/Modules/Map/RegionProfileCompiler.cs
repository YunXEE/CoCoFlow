using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Map
{
    public sealed class RegionProfileCompiler
    {
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

            if (catalog == null || !catalog.IsSealed)
            {
                AddError(
                    diagnostics,
                    "catalog",
                    RegionErrors.CatalogConflict(
                        "Compilation requires an explicit sealed participant catalog."));
                return false;
            }

            var tiers = CompileTiers(profile, catalog, diagnostics);
            var participants = CompileParticipants(profile, catalog, diagnostics);
            if (HasErrors(diagnostics)) return false;

            blueprint = new RegionCompiledProfileBlueprint(tiers, participants);
            return true;
        }

        private static List<RegionCompiledTier> CompileTiers(
            CoCoRegionProfile profile,
            RegionParticipantCatalog catalog,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            var compiled = new List<RegionCompiledTier>(profile.Tiers.Count);
            var standardIntroduction = new int[4];
            for (int index = 0; index < standardIntroduction.Length; index++)
            {
                standardIntroduction[index] = -1;
            }

            if (profile.Tiers.Count < CoCoRegionProfile.DefaultTierCount)
            {
                AddError(
                    diagnostics,
                    "profile.tiers",
                    RegionErrors.InvalidProfile(
                        "A Region Profile needs tier 0 plus the four ordered standard capability tiers."));
            }

            RegionCapabilitySet previous = null;
            for (int tierIndex = 0; tierIndex < profile.Tiers.Count; tierIndex++)
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
                            path + ".capabilities[" + capabilityIndex + "]",
                            RegionErrors.UnsupportedCapability(capability));
                    }

                    int standardOrder =
                        RegionCapabilityId.StandardOrder(capability);
                    if (standardOrder >= 0 &&
                        standardIntroduction[standardOrder] < 0)
                    {
                        standardIntroduction[standardOrder] = tierIndex;
                    }
                }

                if (tierIndex == 0 && capabilities.Count != 0)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Tier 0 must contain no capabilities."));
                }
                else if (tierIndex > 0 &&
                         (previous == null ||
                          !capabilities.IsStrictSupersetOf(previous)))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidProfile(
                            "Every tier after tier 0 must be a strict superset of the preceding tier."));
                }

                compiled.Add(
                    new RegionCompiledTier(
                        tierIndex,
                        tier.Name,
                        capabilities));
                previous = capabilities;
            }

            for (int standardIndex = 0;
                 standardIndex < standardIntroduction.Length;
                 standardIndex++)
            {
                if (standardIntroduction[standardIndex] < 0)
                {
                    AddError(
                        diagnostics,
                        "profile.tiers",
                        RegionErrors.InvalidProfile(
                            "The final tier must include every standard Region capability."));
                    break;
                }

                if (standardIndex > 0 &&
                    standardIntroduction[standardIndex] <=
                    standardIntroduction[standardIndex - 1])
                {
                    AddError(
                        diagnostics,
                        "profile.tiers",
                        RegionErrors.InvalidProfile(
                            "Represented, Background, Enterable, and Full must be introduced in that order; custom capabilities may be inserted between them."));
                    break;
                }
            }

            return compiled;
        }

        private static List<RegionCompiledParticipantDefinition>
            CompileParticipants(
                CoCoRegionProfile profile,
                RegionParticipantCatalog catalog,
                IList<RegionCompileDiagnostic> diagnostics)
        {
            var compiled =
                new List<RegionCompiledParticipantDefinition>(
                    profile.Participants.Count);
            var definitionBySlot =
                new Dictionary<RegionParticipantSlotId, RegionParticipantDefinition>();

            for (int index = 0; index < profile.Participants.Count; index++)
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
                    !definition.ParticipantTypeId.IsValid ||
                    !definition.ModeId.IsValid)
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.InvalidIdentifier(
                            "Participant slot, type, and mode identifiers must be valid."));
                    continue;
                }

                if (!definitionBySlot.TryAdd(definition.SlotId, definition))
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
                    continue;
                }

                if (!catalog.TryGetRegistration(
                        definition.ParticipantTypeId,
                        definition.ModeId,
                        out RegionParticipantRegistration registration))
                {
                    AddError(
                        diagnostics,
                        path,
                        RegionErrors.CatalogConflict(
                            "No participant registration exists for type '" +
                            definition.ParticipantTypeId.Value +
                            "' and mode '" +
                            definition.ModeId.Value +
                            "'."));
                    continue;
                }

                if (definition.Configuration == null ||
                    definition.Configuration.GetType() !=
                    registration.ConfigurationType)
                {
                    AddError(
                        diagnostics,
                        path + ".configuration",
                        RegionErrors.InvalidProfile(
                            "The SerializeReference configuration is missing or does not match the registered freezer type."));
                    continue;
                }

                if (HasDuplicateCapabilities(
                        definition.RequiredCapabilities) ||
                    !RegionCapabilitySet.TryCreate(
                        definition.RequiredCapabilities,
                        out RegionCapabilitySet requiredCapabilities) ||
                    requiredCapabilities.Count == 0)
                {
                    AddError(
                        diagnostics,
                        path + ".requiredCapabilities",
                        RegionErrors.InvalidCapability(
                            "A participant needs at least one valid, unique activation capability."));
                    continue;
                }

                for (int capabilityIndex = 0;
                     capabilityIndex < requiredCapabilities.Count;
                     capabilityIndex++)
                {
                    RegionCapabilityId capability =
                        requiredCapabilities.Capabilities[capabilityIndex];
                    if (!catalog.SupportsCapability(capability))
                    {
                        AddError(
                            diagnostics,
                            path + ".requiredCapabilities[" +
                            capabilityIndex + "]",
                            RegionErrors.UnsupportedCapability(capability));
                    }
                    else if (!registration.SupportedCapabilities.Contains(capability))
                    {
                        AddError(
                            diagnostics,
                            path + ".requiredCapabilities[" +
                            capabilityIndex + "]",
                            RegionErrors.InvalidProfile(
                                "The participant registration does not support capability '" +
                                capability.Value +
                                "'."));
                    }
                }

                if (HasDuplicateOrInvalidDependencies(definition))
                {
                    AddError(
                        diagnostics,
                        path + ".dependencies",
                        RegionErrors.InvalidProfile(
                            "Dependencies must be valid, unique, and cannot reference the same slot."));
                    continue;
                }

                compiled.Add(
                    new RegionCompiledParticipantDefinition(
                        definition,
                        requiredCapabilities,
                        registration));
            }

            ValidateDependencyGraph(
                compiled,
                definitionBySlot,
                diagnostics);
            return compiled;
        }

        private static bool HasDuplicateOrInvalidDependencies(
            RegionParticipantDefinition definition)
        {
            var unique = new HashSet<RegionParticipantSlotId>();
            for (int index = 0; index < definition.Dependencies.Count; index++)
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

        private static void ValidateDependencyGraph(
            IReadOnlyList<RegionCompiledParticipantDefinition> compiled,
            IReadOnlyDictionary<RegionParticipantSlotId, RegionParticipantDefinition>
                definitionBySlot,
            IList<RegionCompileDiagnostic> diagnostics)
        {
            var state = new Dictionary<RegionParticipantSlotId, byte>();
            for (int index = 0; index < compiled.Count; index++)
            {
                RegionParticipantDefinition definition = compiled[index].Source;
                for (int dependencyIndex = 0;
                     dependencyIndex < definition.Dependencies.Count;
                     dependencyIndex++)
                {
                    RegionParticipantSlotId dependencyId =
                        definition.Dependencies[dependencyIndex];
                    if (!definitionBySlot.TryGetValue(
                            dependencyId,
                            out RegionParticipantDefinition dependency))
                    {
                        AddError(
                            diagnostics,
                            "profile.participants." +
                            definition.SlotId.Value +
                            ".dependencies",
                            RegionErrors.InvalidProfile(
                                "Dependency slot '" + dependencyId.Value +
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
                }

                if (HasCycle(definition.SlotId, definitionBySlot, state))
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

        private static bool HasCycle(
            RegionParticipantSlotId slotId,
            IReadOnlyDictionary<RegionParticipantSlotId, RegionParticipantDefinition>
                definitionBySlot,
            IDictionary<RegionParticipantSlotId, byte> state)
        {
            if (state.TryGetValue(slotId, out byte visitState))
            {
                return visitState == 1;
            }

            if (!definitionBySlot.TryGetValue(slotId, out var definition))
            {
                return false;
            }

            state[slotId] = 1;
            for (int index = 0; index < definition.Dependencies.Count; index++)
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
            diagnostics.Add(new RegionCompileDiagnostic(path, diagnostic));
    }
}
