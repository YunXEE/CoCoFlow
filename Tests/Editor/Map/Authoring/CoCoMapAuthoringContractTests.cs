using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CoCoFlow.Fixtures.ExternalMapTa;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Tests.Editor.Map.Authoring
{
    public sealed class CoCoMapAuthoringContractTests
    {
        private const string TemplateGuid =
            "7a04045d8302471a8dd3bb4b57041104";

        [Test]
        public void DefaultTemplateCopiesTheFrozenFiveTierBaseline()
        {
            string path = AssetDatabase.GUIDToAssetPath(TemplateGuid);
            TextAsset template =
                AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(template, Is.Not.Null);

            CoCoRegionProfile profile =
                ScriptableObject.CreateInstance<CoCoRegionProfile>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(
                    template.text,
                    profile);

                Assert.That(profile.Tiers.Count, Is.EqualTo(5));
                Assert.That(
                    profile.Tiers[0].Capabilities,
                    Is.Empty);
                Assert.That(
                    profile.Tiers[1].Capabilities,
                    Is.EqualTo(new[]
                    {
                        RegionCapabilityId.Represented
                    }));
                Assert.That(
                    profile.Tiers[4].Capabilities,
                    Is.EqualTo(new[]
                    {
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable,
                        RegionCapabilityId.Full
                    }));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void ExternalTaRegistersThroughPublicMapSdkOnly()
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
                registration.ConfigFreezer.ConfigurationType,
                Is.EqualTo(typeof(ExternalWeatherParticipantConfig)));
            Assert.That(
                catalog.RegisteredTypes.All(
                    type =>
                        type != null &&
                        !type.IsAbstract &&
                        !type.IsInterface &&
                        !type.ContainsGenericParameters),
                Is.True);
        }

        [Test]
        public void AbstractAotMetadataFailsClosedBeforeLinker()
        {
            var catalog = new RegionParticipantCatalog();
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    new[] { RegionCapabilityId.Represented },
                    out RegionCapabilitySet capabilities),
                Is.True);
            Assert.That(
                RegionParticipantTypeId.TryCreate(
                    "tests.map.abstract-aot",
                    out RegionParticipantTypeId typeId),
                Is.True);
            Assert.That(
                RegionParticipantModeId.TryCreate(
                    "tests.map.abstract-aot.default",
                    out RegionParticipantModeId modeId),
                Is.True);
            Assert.That(
                RegionParticipantRegistration.TryCreate(
                    typeId,
                    modeId,
                    capabilities,
                    new AbstractPlanFreezer(),
                    new ConcreteCandidateFactory(),
                    out RegionParticipantRegistration registration,
                    out CoCoDiagnostic registrationDiagnostic),
                Is.True,
                registrationDiagnostic.Message);
            Assert.That(
                catalog.TryRegisterParticipant(
                    registration,
                    out CoCoDiagnostic catalogDiagnostic),
                Is.True,
                catalogDiagnostic.Message);
            catalog.Seal();

            Type validationType = Type.GetType(
                "CoCoFlow.Editor.Modules.Map.CoCoMapBuildValidation, " +
                "CoCoFlow.Editor.Modules.Map",
                true);
            MethodInfo validate = validationType.GetMethod(
                "ValidatePlayerAssemblyClosure",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(validate, Is.Not.Null);

            var messages =
                new SortedSet<string>(StringComparer.Ordinal);
            validate.Invoke(
                null,
                new object[] { catalog, messages });

            Assert.That(
                messages.Any(
                    message =>
                        message.Contains(
                            typeof(AbstractPlan).FullName) &&
                        message.Contains(
                            "closed concrete metadata type")),
                Is.True,
                string.Join(Environment.NewLine, messages));
        }

        [Serializable]
        private sealed class ConcreteConfig :
            RegionParticipantConfig
        {
        }

        private abstract class AbstractPlan :
            IRegionParticipantPlan
        {
            public abstract string Fingerprint { get; }
        }

        private sealed class AbstractPlanFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(ConcreteConfig);

            public Type PlanType =>
                typeof(AbstractPlan);

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Map,
                    CoCoDiagnosticCode.InvalidRegionProfile,
                    "The abstract AOT fixture is validation-only.");
                return false;
            }
        }

        private sealed class ConcreteCandidateFactory :
            IRegionParticipantFactory
        {
            public Type CandidateType =>
                typeof(ConcreteCandidate);

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = new ConcreteCandidate();
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ConcreteCandidate :
            IRegionParticipantCandidate
        {
            public UniTask<RegionParticipantPrepareResult>
                PrepareAsync(
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

            public UniTask<RegionParticipantCleanupResult>
                CleanupAsync(
                    RegionParticipantCleanupReason reason,
                    CancellationToken cancellationToken) =>
                UniTask.FromResult(
                    RegionParticipantCleanupResult.Success());
        }
    }
}
