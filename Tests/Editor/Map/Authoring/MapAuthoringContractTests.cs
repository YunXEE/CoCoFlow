using System;
using System.IO;
using System.Linq;
using System.Threading;
using CoCoFlow.Editor.Modules.Map;
using CoCoFlow.Fixtures.ExternalMapTa;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Tests.Editor.Map.Authoring
{
    public sealed class MapAuthoringContractTests
    {
        private const string TemplateGuid =
            "7a04045d8302471a8dd3bb4b57041104";
        private const string IdentityTestFolder =
            "Assets/__CoCoFlowRegionProfileIdentityTests";

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(IdentityTestFolder))
            {
                AssetDatabase.DeleteAsset(IdentityTestFolder);
            }
        }

        [Test]
        public void DefaultTemplateCopiesTheFrozenFiveTierBaseline()
        {
            string path = AssetDatabase.GUIDToAssetPath(TemplateGuid);
            TextAsset template =
                AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            Assert.That(template, Is.Not.Null);

            RegionProfile profile =
                ScriptableObject.CreateInstance<RegionProfile>();
            try
            {
                EditorJsonUtility.FromJsonOverwrite(
                    template.text,
                    profile);

                Assert.That(
                    profile.SchemaVersion,
                    Is.EqualTo(
                        RegionProfile.CurrentSchemaVersion));
                Assert.That(profile.Tiers.Count, Is.EqualTo(5));
                Assert.That(
                    profile.Tiers.Select(tier => tier.TierId),
                    Is.EqualTo(new[]
                    {
                        RegionTierId.Off,
                        RegionTierId.Represented,
                        RegionTierId.Background,
                        RegionTierId.Enterable,
                        RegionTierId.Full
                    }));
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
        public void ProfileIdentityFollowsAssetGuidAcrossCopyAndMove()
        {
            AssetDatabase.CreateFolder(
                "Assets",
                "__CoCoFlowRegionProfileIdentityTests");
            const string originalPath =
                IdentityTestFolder + "/Original.asset";
            const string movedPath =
                IdentityTestFolder + "/Moved.asset";
            const string copiedPath =
                IdentityTestFolder + "/Copied.asset";
            var original =
                ScriptableObject.CreateInstance<RegionProfile>();
            AssetDatabase.CreateAsset(original, originalPath);

            Assert.That(
                RegionProfileIdentity.Synchronize(original),
                Is.True);
            string originalGuid =
                AssetDatabase.AssetPathToGUID(originalPath)
                    .ToLowerInvariant();
            Assert.That(
                original.ProfileId.Value,
                Is.EqualTo(originalGuid));

            Assert.That(
                AssetDatabase.MoveAsset(
                    originalPath,
                    movedPath),
                Is.Empty);
            var moved =
                AssetDatabase.LoadAssetAtPath<RegionProfile>(
                    movedPath);
            Assert.That(
                RegionProfileIdentity.Synchronize(moved),
                Is.True);
            Assert.That(
                moved.ProfileId.Value,
                Is.EqualTo(originalGuid));

            Assert.That(
                AssetDatabase.CopyAsset(
                    movedPath,
                    copiedPath),
                Is.True);
            var copied =
                AssetDatabase.LoadAssetAtPath<RegionProfile>(
                    copiedPath);
            Assert.That(
                RegionProfileIdentity.Synchronize(copied),
                Is.True);
            Assert.That(
                copied.ProfileId.Value,
                Is.EqualTo(
                    AssetDatabase.AssetPathToGUID(copiedPath)
                        .ToLowerInvariant()));
            Assert.That(
                copied.ProfileId,
                Is.Not.EqualTo(moved.ProfileId));
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
                registration.ConfigurationType,
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
        public void AbstractPlanMetadataFailsClosedAtRegistration()
        {
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
                Is.False);
            Assert.That(
                registration,
                Is.Null);
            Assert.That(
                registrationDiagnostic.Code,
                Is.EqualTo(
                    CoCoDiagnosticCode.RegionCatalogConflict));
        }

        [Test]
        public void LinkXmlUsesUnityLinkerNameForNestedTypes()
        {
            string path = MapBuildValidation.WriteLinkXml(
                new[] { typeof(NestedLinkerFixture) });
            try
            {
                string xml = File.ReadAllText(path);
                string expectedName =
                    typeof(NestedLinkerFixture).FullName
                        .Replace('+', '/');

                StringAssert.Contains(
                    "fullname=\"" + expectedName + "\"",
                    xml);
                StringAssert.DoesNotContain(
                    typeof(NestedLinkerFixture).FullName,
                    xml);
            }
            finally
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        [Test]
        public void DirectSceneMissingFromPlayerListFailsBuild()
        {
            Assert.That(
                () => MapBuildValidation
                    .ValidateDirectScenePathsForBuild(
                        new[] { "Assets/World/Direct.unity" },
                        Array.Empty<string>()),
                Throws.TypeOf<UnityEditor.Build.BuildFailedException>()
                    .With.Message.Contains(
                        "Assets/World/Direct.unity"));
        }

        [Test]
        public void DirectSceneIncludedInPlayerListPassesBuild()
        {
            Assert.DoesNotThrow(
                () => MapBuildValidation
                    .ValidateDirectScenePathsForBuild(
                        new[] { "Assets/World/Direct.unity" },
                        new[] { "Assets/World/Direct.unity" }));
        }

        [Test]
        public void AddressablesOnlyPlanDoesNotRequirePlayerScene()
        {
            Assert.DoesNotThrow(
                () => MapBuildValidation
                    .ValidateDirectScenePathsForBuild(
                        Array.Empty<string>(),
                        Array.Empty<string>()));
        }

        [Test]
        public void BuildCatalogResolutionNeverBorrowsALoadedHost()
        {
            Func<RegionParticipantCatalog> previousCatalogProvider =
                MapEditorCatalogProvider.CatalogProvider;
            Func<IRegionAddressableSceneResolver> previousResolverProvider =
                MapEditorCatalogProvider
                    .AddressableSceneResolverProvider;
            var hostObject =
                new GameObject("Build Catalog Fallback Trap");
            hostObject.SetActive(false);
            try
            {
                DefaultRegionCatalogProvider hostProvider =
                    hostObject.AddComponent<
                        DefaultRegionCatalogProvider>();
                MapHost host =
                    hostObject.AddComponent<MapHost>();
                var serializedHost = new SerializedObject(host);
                serializedHost.FindProperty(
                        "catalogProviderComponent")
                    .objectReferenceValue = hostProvider;
                serializedHost.ApplyModifiedPropertiesWithoutUndo();

                MapEditorCatalogProvider.CatalogProvider = null;
                MapEditorCatalogProvider
                    .AddressableSceneResolverProvider = null;
                Assert.That(
                    MapAuthoringContext.TryResolveGlobal(
                        out _,
                        out _,
                        out string failure),
                    Is.False);
                StringAssert.Contains(
                    "requires an explicit",
                    failure);

                Assert.That(
                    hostProvider.TryGetCatalog(
                        out RegionParticipantCatalog expected,
                        out CoCoDiagnostic diagnostic),
                    Is.True,
                    diagnostic.Message);
                MapEditorCatalogProvider.CatalogProvider =
                    () => expected;
                Assert.That(
                    MapAuthoringContext.TryResolveGlobal(
                        out RegionParticipantCatalog resolved,
                        out IRegionAddressableSceneResolver resolver,
                        out failure),
                    Is.True,
                    failure);
                Assert.That(resolved, Is.SameAs(expected));
                Assert.That(resolver, Is.Null);
            }
            finally
            {
                MapEditorCatalogProvider.CatalogProvider =
                    previousCatalogProvider;
                MapEditorCatalogProvider
                    .AddressableSceneResolverProvider =
                    previousResolverProvider;
                UnityEngine.Object.DestroyImmediate(hostObject);
            }
        }

        [Serializable]
        private sealed class ConcreteConfig :
            RegionParticipantConfig
        {
        }

        private sealed class NestedLinkerFixture
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
