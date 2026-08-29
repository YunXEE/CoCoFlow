using System.Collections;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Map;
using CoCoFlow.Runtime.Modules.Map.Pooling;
using CoCoFlow.Runtime.Pooling;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.Map.Pooling
{
    public sealed class MapPoolingPublicIntegrationPlayModeTests
    {
        [UnityTest]
        public IEnumerator PoolAdapterRegistersThroughPublicRuntimeBinding() =>
            UniTask.ToCoroutine(async () =>
            {
                GameObject owner =
                    new GameObject("Map Pooling Public Owner");
                GameObject prefab =
                    new GameObject("Map Pooling Public Prefab");
                ContentRuntime contentRuntime = null;
                PoolRuntime poolRuntime = null;
                try
                {
                    Assert.That(
                        ContentRuntime.TryCreate(
                            out contentRuntime,
                            out CoCoDiagnostic diagnostic),
                        Is.True,
                        diagnostic.Message);
                    Assert.That(
                        PoolRuntime.TryCreate(
                            contentRuntime,
                            owner.transform,
                            out poolRuntime,
                            out diagnostic),
                        Is.True,
                        diagnostic.Message);
                    PoolProfile profile =
                        CreateProfile(prefab);
                    var binding =
                        new PublicPoolBinding(
                            poolRuntime,
                            profile);
                    var catalog =
                        new RegionParticipantCatalog();

                    Assert.That(
                        RegionBuiltInPoolCatalog.TryRegister(
                            catalog,
                            binding,
                            out diagnostic),
                        Is.True,
                        diagnostic.Message);
                    catalog.Seal();
                    Assert.That(
                        RegionParticipantTypeId.TryCreate(
                            RegionBuiltInPoolCatalog
                                .ParticipantTypeValue,
                            out RegionParticipantTypeId typeId),
                        Is.True);
                    Assert.That(
                        RegionParticipantModeId.TryCreate(
                            RegionBuiltInPoolCatalog.ModeValue,
                            out RegionParticipantModeId modeId),
                        Is.True);
                    Assert.That(
                        catalog.TryGetRegistration(
                            typeId,
                            modeId,
                            out RegionParticipantRegistration
                                registration),
                        Is.True);
                    Assert.That(
                        registration.ConfigurationType,
                        Is.EqualTo(
                            typeof(
                                PoolRegionParticipantConfig)));
                    Assert.That(
                        registration.CandidateType,
                        Is.EqualTo(
                            typeof(
                                PoolRegionParticipantCandidate)));
                    CollectionAssert.Contains(
                        RegionBuiltInPoolCatalog.AotTypes
                            .ToArray(),
                        typeof(PoolRegionParticipantCandidate));

                    string testAssembly =
                        typeof(
                                MapPoolingPublicIntegrationPlayModeTests)
                            .Assembly
                            .GetName()
                            .Name;
                    Assert.That(
                        HasFriendAccess(
                            typeof(CoCoMapHost).Assembly,
                            testAssembly),
                        Is.False,
                        "The runtime Pooling test must compile through the public Map SDK.");
                    Assert.That(
                        HasFriendAccess(
                            typeof(RegionBuiltInPoolCatalog)
                                .Assembly,
                            testAssembly),
                        Is.False,
                        "The runtime Pooling test must compile through the public adapter SDK.");
                }
                finally
                {
                    if (poolRuntime != null)
                    {
                        await poolRuntime.ShutdownAsync();
                    }

                    if (contentRuntime != null)
                    {
                        await contentRuntime.ShutdownAsync();
                    }

                    Object.DestroyImmediate(prefab);
                    Object.DestroyImmediate(owner);
                }
            });

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
                        System.StringComparison.Ordinal));

        private static PoolProfile CreateProfile(
            GameObject prefab)
        {
            Assert.That(
                ContentId.TryCreate(
                    "tests.map.pooling.prefab",
                    out ContentId contentId),
                Is.True);
            Assert.That(
                ContentReference
                    .TryCreateDirectPrefabSource(
                        contentId,
                        prefab,
                        out ContentReference reference),
                Is.True);
            Assert.That(
                PoolId.TryCreate(
                    "tests.map.pooling",
                    out PoolId poolId),
                Is.True);
            Assert.That(
                PoolProfile.TryCreate(
                    poolId,
                    reference,
                    0,
                    1,
                    out PoolProfile profile),
                Is.True);
            return profile;
        }

        private sealed class PublicPoolBinding :
            IRegionPoolParticipantBinding
        {
            private readonly PoolRuntime runtime;
            private readonly PoolProfile profile;
            private PoolScope committedScope;

            internal PublicPoolBinding(
                PoolRuntime runtime,
                PoolProfile profile)
            {
                this.runtime = runtime;
                this.profile = profile;
            }

            public bool TryGetPoolRuntime(
                RegionPlanNodeId nodeId,
                out PoolRuntime result,
                out CoCoDiagnostic diagnostic)
            {
                result = runtime;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryCreateCandidateScope(
                RegionPlanNodeId nodeId,
                long candidateSequence,
                out PoolScope scope,
                out CoCoDiagnostic diagnostic)
            {
                Assert.That(
                    ContentOwnerId.TryCreate(
                        "tests.map.pooling." +
                        candidateSequence,
                        out ContentOwnerId ownerId),
                    Is.True);
                return runtime.TryCreateScope(
                    ownerId,
                    out scope,
                    out diagnostic);
            }

            public bool TryResolveProfile(
                in RegionPoolProfilePlan profilePlan,
                out PoolProfile result,
                out CoCoDiagnostic diagnostic)
            {
                result = profile;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryPublishCommittedScope(
                RegionPlanNodeId nodeId,
                PoolScope scope,
                out CoCoDiagnostic diagnostic)
            {
                committedScope = scope;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public bool TryReleaseCommittedScope(
                RegionPlanNodeId nodeId,
                PoolScope expectedScope,
                out CoCoDiagnostic diagnostic)
            {
                if (!ReferenceEquals(
                        committedScope,
                        expectedScope))
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Map,
                        CoCoDiagnosticCode
                            .RegionCleanupBlocked,
                        "The public Pool binding received another Scope.");
                    return false;
                }

                committedScope = null;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }
    }
}
