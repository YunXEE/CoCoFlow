using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoCoFlow.Fixtures.ExternalMapTa;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using NUnit.Framework;

namespace CoCoFlow.Tests.Runtime.Map.PublicSdk
{
    public sealed class MapPublicSdkPlayModeTests
    {
        [Test]
        public void ProductionTaRegistersUsingOnlyThePublicMapSdk()
        {
            var catalog = new RegionParticipantCatalog();

            Assert.That(
                ExternalWeatherParticipant.TryRegister(
                    catalog,
                    out CoCoDiagnostic diagnostic),
                Is.True,
                diagnostic.Message);
            catalog.Seal();

            Assert.That(
                catalog.SupportsCapability(
                    ExternalWeatherParticipant.CapabilityId),
                Is.True);
            Assert.That(
                catalog.TryGetRegistration(
                    ExternalWeatherParticipant.TypeId,
                    ExternalWeatherParticipant.ModeId,
                    out RegionParticipantRegistration registration),
                Is.True);
            Assert.That(
                registration.ConfigurationType,
                Is.EqualTo(
                    typeof(
                        ExternalWeatherParticipantConfig)));
            Assert.That(
                registration.PlanType,
                Is.EqualTo(
                    typeof(
                        ExternalWeatherParticipantPlan)));
            Assert.That(
                registration.CandidateType,
                Is.EqualTo(
                    typeof(
                        ExternalWeatherParticipantCandidate)));
            CollectionAssert.IsSubsetOf(
                new[]
                {
                    typeof(ExternalWeatherParticipantConfig),
                    typeof(ExternalWeatherParticipantPlan),
                    typeof(ExternalWeatherConfigFreezer),
                    typeof(ExternalWeatherParticipantFactory),
                    typeof(ExternalWeatherParticipantCandidate)
                },
                catalog.RegisteredTypes.ToArray());

            Assembly mapAssembly =
                typeof(CoCoMapHost).Assembly;
            Assembly fixtureAssembly =
                typeof(ExternalWeatherParticipant).Assembly;
            Assembly testAssembly =
                typeof(MapPublicSdkPlayModeTests).Assembly;
            Assert.That(
                HasFriendAccess(
                    mapAssembly,
                    fixtureAssembly.GetName().Name),
                Is.False,
                "The production-style TA fixture must not receive Map internals.");
            Assert.That(
                HasFriendAccess(
                    mapAssembly,
                    testAssembly.GetName().Name),
                Is.False,
                "The Public SDK test must not receive Map internals.");
            Assert.That(
                fixtureAssembly
                    .GetReferencedAssemblies()
                    .Select(reference => reference.Name)
                    .Any(name =>
                        name.StartsWith(
                            "CoCoFlow.Tests",
                            StringComparison.Ordinal) ||
                        name.Contains(
                            ".Editor",
                            StringComparison.Ordinal)),
                Is.False,
                "The production-style TA fixture must not depend on tests or Editor assemblies.");
        }

        [Test]
        public void DemandContractAndCustomIdentifiersArePubliclyComposable()
        {
            Assert.That(
                RegionId.TryCreate(
                    "world.wilderness",
                    out RegionId regionId),
                Is.True);
            Assert.That(
                RegionChunkId.TryCreate(
                    "wilderness/west",
                    out RegionChunkId chunkId),
                Is.True);
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    new[]
                    {
                        RegionCapabilityId.Represented,
                        ExternalWeatherParticipant.CapabilityId
                    },
                    out RegionCapabilitySet capabilities),
                Is.True);
            Assert.That(
                RegionCoverage.TryCreateChunks(
                    new[] { chunkId },
                    out RegionCoverage coverage),
                Is.True);
            Assert.That(regionId.IsValid, Is.True);
            Assert.That(
                capabilities.Contains(
                    ExternalWeatherParticipant.CapabilityId),
                Is.True);
            Assert.That(coverage.Contains(chunkId), Is.True);

            AssertPublicInstanceMethod(
                typeof(CoCoMapHost),
                nameof(CoCoMapHost.TryCreateDemandScope));
            AssertPublicInstanceMethod(
                typeof(CoCoMapHost),
                nameof(CoCoMapHost.TryRetryRegion));
            AssertPublicInstanceMethod(
                typeof(CoCoMapHost),
                nameof(CoCoMapHost.CaptureSnapshot));
            AssertPublicInstanceMethod(
                typeof(RegionDemandScope),
                nameof(RegionDemandScope.TryDemand));
            AssertPublicInstanceMethod(
                typeof(RegionDemandLease),
                nameof(RegionDemandLease.TryUpdate));
            AssertPublicInstanceMethod(
                typeof(RegionDemandLease),
                nameof(RegionDemandLease.WaitUntilReadyAsync));
            Assert.That(
                typeof(IDisposable).IsAssignableFrom(
                    typeof(RegionDemandScope)),
                Is.True);
            Assert.That(
                typeof(IDisposable).IsAssignableFrom(
                    typeof(RegionDemandLease)),
                Is.True);
        }

        private static void AssertPublicInstanceMethod(
            Type type,
            string name)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Instance |
                BindingFlags.Public);
            Assert.That(
                method,
                Is.Not.Null,
                type.FullName + "." + name +
                " must remain public.");
        }

        private static bool HasFriendAccess(
            Assembly assembly,
            string candidateAssemblyName) =>
            assembly
                .GetCustomAttributes<
                    InternalsVisibleToAttribute>()
                .Any(attribute =>
                    string.Equals(
                        attribute.AssemblyName,
                        candidateAssemblyName,
                        StringComparison.Ordinal));
    }
}
