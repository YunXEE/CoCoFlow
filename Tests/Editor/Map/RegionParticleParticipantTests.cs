using System.Collections;
using System.Threading;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace CoCoFlow.Runtime.Modules.Map.Tests
{
    public sealed class RegionParticleParticipantTests
    {
        [UnityTest]
        public IEnumerator MissingLaterTargetDoesNotPartiallyMutateEarlierTarget() =>
            UniTask.ToCoroutine(async () =>
            {
                ParticleFixture fixture = await CreateFixtureAsync();
                try
                {
                    Object.DestroyImmediate(fixture.Second.gameObject);

                    Assert.That(
                        fixture.Candidate.TryCommit(
                            fixture.CommitContext,
                            out CoCoDiagnostic diagnostic),
                        Is.False);
                    Assert.That(
                        diagnostic.Code,
                        Is.EqualTo(
                            CoCoDiagnosticCode.RegionCommitFaulted));
                    Assert.That(
                        fixture.First.isPlaying,
                        Is.False,
                        "Commit must validate every ParticleSystem before mutating the first target.");

                    RegionParticipantCleanupResult cleanup =
                        await fixture.Candidate.CleanupAsync(
                            RegionParticipantCleanupReason
                                .CandidateFailed,
                            CancellationToken.None);
                    Assert.That(cleanup.Succeeded, Is.True);
                }
                finally
                {
                    fixture.Dispose();
                }
            });

        [UnityTest]
        public IEnumerator HostShutdownRestoresCommittedParticleState() =>
            UniTask.ToCoroutine(async () =>
            {
                ParticleFixture fixture = await CreateFixtureAsync();
                try
                {
                    Assert.That(
                        fixture.Candidate.TryCommit(
                            fixture.CommitContext,
                            out CoCoDiagnostic diagnostic),
                        Is.True,
                        diagnostic.Message);
                    Assert.That(fixture.First.isPlaying, Is.True);
                    Assert.That(fixture.Second.isPlaying, Is.True);

                    RegionParticipantCleanupResult cleanup =
                        await fixture.Candidate.CleanupAsync(
                            RegionParticipantCleanupReason.HostShutdown,
                            CancellationToken.None);
                    Assert.That(cleanup.Succeeded, Is.True);
                    Assert.That(fixture.First.isPlaying, Is.False);
                    Assert.That(fixture.Second.isPlaying, Is.False);
                }
                finally
                {
                    fixture.Dispose();
                }
            });

        private static async UniTask<ParticleFixture> CreateFixtureAsync()
        {
            var root = new GameObject(
                "Region Particle Test Root",
                typeof(ParticleSystem));
            var child = new GameObject(
                "Region Particle Test Child",
                typeof(ParticleSystem));
            child.transform.SetParent(root.transform, false);
            ParticleSystem first = root.GetComponent<ParticleSystem>();
            ParticleSystem second = child.GetComponent<ParticleSystem>();
            first.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);
            second.Stop(
                true,
                ParticleSystemStopBehavior.StopEmittingAndClear);

            Assert.That(
                RegionId.TryCreate(
                    "tests.particle",
                    out RegionId regionId),
                Is.True);
            Assert.That(
                RegionChunkId.TryCreate(
                    "chunk",
                    out RegionChunkId chunkId),
                Is.True);
            Assert.That(
                RegionParticipantSlotId.TryCreate(
                    "particles",
                    out RegionParticipantSlotId slotId),
                Is.True);
            Assert.That(
                RegionPlanNodeId.TryCreateChunk(
                    regionId,
                    chunkId,
                    slotId,
                    out RegionPlanNodeId nodeId),
                Is.True);
            Assert.That(
                RegionTierId.TryCreate(
                    "full",
                    out RegionTierId tierId),
                Is.True);
            Assert.That(
                RegionCapabilitySet.TryCreate(
                    new[] { RegionCapabilityId.Full },
                    out RegionCapabilitySet capabilities),
                Is.True);

            var catalog = new RegionParticipantCatalog();
            Assert.That(
                RegionParticleParticipant.TryRegister(
                    catalog,
                    out CoCoDiagnostic registerDiagnostic),
                Is.True,
                registerDiagnostic.Message);
            Assert.That(
                catalog.TryGetRegistration(
                    RegionParticleParticipant.TypeId,
                    RegionParticleParticipant.ModeId,
                    out RegionParticipantRegistration registration),
                Is.True);

            var config = new RegionParticleParticipantConfig();
            var freezeContext = new RegionParticipantFreezeContext(
                nodeId,
                tierId,
                capabilities,
                "particles",
                default);
            Assert.That(
                registration.ConfigFreezer.TryFreeze(
                    freezeContext,
                    config,
                    out IRegionParticipantPlan plan,
                    out CoCoDiagnostic freezeDiagnostic),
                Is.True,
                freezeDiagnostic.Message);

            var resolver = new FixedResolver(root);
            var createContext = new RegionParticipantCreateContext(
                nodeId,
                tierId,
                capabilities,
                "particles",
                resolver);
            Assert.That(
                registration.Factory.TryCreateCandidate(
                    createContext,
                    plan,
                    out IRegionParticipantCandidate candidate,
                    out CoCoDiagnostic createDiagnostic),
                Is.True,
                createDiagnostic.Message);

            var prepareContext = new RegionParticipantPrepareContext(
                nodeId,
                tierId,
                capabilities,
                1L,
                resolver);
            RegionParticipantPrepareResult prepared =
                await candidate.PrepareAsync(
                    prepareContext,
                    CancellationToken.None);
            Assert.That(
                prepared.Succeeded,
                Is.True,
                prepared.Diagnostic.Message);
            return new ParticleFixture(
                root,
                first,
                second,
                candidate,
                new RegionParticipantCommitContext(
                    nodeId,
                    tierId,
                    capabilities,
                    1L));
        }

        private sealed class FixedResolver : IRegionFragmentResolver
        {
            private readonly GameObject root;

            internal FixedResolver(GameObject root)
            {
                this.root = root;
            }

            public bool TryResolveGameObject(
                string fragmentId,
                out GameObject gameObject,
                out CoCoDiagnostic diagnostic)
            {
                gameObject = root;
                diagnostic = root == null
                    ? RegionErrors.SceneContract(
                        "Particle test root is missing.")
                    : CoCoDiagnostic.None;
                return root != null;
            }
        }

        private sealed class ParticleFixture
        {
            internal ParticleFixture(
                GameObject root,
                ParticleSystem first,
                ParticleSystem second,
                IRegionParticipantCandidate candidate,
                RegionParticipantCommitContext commitContext)
            {
                Root = root;
                First = first;
                Second = second;
                Candidate = candidate;
                CommitContext = commitContext;
            }

            internal GameObject Root { get; }
            internal ParticleSystem First { get; }
            internal ParticleSystem Second { get; }
            internal IRegionParticipantCandidate Candidate { get; }
            internal RegionParticipantCommitContext CommitContext { get; }

            internal void Dispose()
            {
                if (Root != null)
                {
                    Object.DestroyImmediate(Root);
                }
            }
        }
    }
}
