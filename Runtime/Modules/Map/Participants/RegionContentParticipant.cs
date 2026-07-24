using System;
using System.Collections.Generic;
using System.Threading;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CoCoFlow.Runtime.Modules.Map
{
    [Serializable]
    public sealed class RegionContentParticipantConfig :
        RegionParticipantConfig
    {
    }

    public static class RegionContentParticipant
    {
        private sealed class ContentPlan : IRegionParticipantPlan
        {
            internal ContentPlan(RegionCompiledSceneReference sceneReference)
            {
                SceneReference = sceneReference;
                Fingerprint =
                    "content-v1|" +
                    sceneReference.ContentId.Value + "|" +
                    (int)sceneReference.SourceKind + "|" +
                    sceneReference.Locator + "|" +
                    sceneReference.CanonicalScenePath;
            }

            internal RegionCompiledSceneReference SceneReference { get; }
            public string Fingerprint { get; }
        }

        private sealed class ContentFreezer :
            IRegionParticipantConfigFreezer
        {
            public Type ConfigurationType =>
                typeof(RegionContentParticipantConfig);

            public Type PlanType => typeof(ContentPlan);

            public bool TryFreeze(
                in RegionParticipantFreezeContext context,
                RegionParticipantConfig configuration,
                out IRegionParticipantPlan plan,
                out CoCoDiagnostic diagnostic)
            {
                plan = null;
                if (!(configuration is RegionContentParticipantConfig))
                {
                    diagnostic = RegionErrors.InvalidProfile(
                        "The Content participant requires a Content configuration.");
                    return false;
                }

                if (!context.NodeId.HasChunkId ||
                    !context.SceneReference.IsValid ||
                    !string.IsNullOrEmpty(context.FragmentId))
                {
                    diagnostic = RegionErrors.SceneContract(
                        "The Content participant must own one Chunk Scene and cannot target a fragment.");
                    return false;
                }

                plan = new ContentPlan(context.SceneReference);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ContentFactory : IRegionParticipantFactory
        {
            public Type CandidateType => typeof(ContentCandidate);

            public bool TryCreateCandidate(
                in RegionParticipantCreateContext context,
                IRegionParticipantPlan plan,
                out IRegionParticipantCandidate candidate,
                out CoCoDiagnostic diagnostic)
            {
                candidate = null;
                if (!(plan is ContentPlan contentPlan) ||
                    !(context.FragmentResolver is
                        IRegionContentParticipantRuntime runtime))
                {
                    diagnostic = RegionErrors.TransitionFailed(
                        "The Content participant requires the Map-owned Content runtime bridge.");
                    return false;
                }

                candidate = new ContentCandidate(
                    context.NodeId,
                    contentPlan,
                    runtime);
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        private sealed class ContentCandidate :
            IRegionParticipantCandidate,
            IRegionParticipantTerminalCleanup,
            IRegionChunkAnchorSource,
            IRegionContentMonitorSource
        {
            private readonly RegionPlanNodeId nodeId;
            private readonly ContentPlan plan;
            private readonly IRegionContentParticipantRuntime runtime;
            private ContentScope scope;
            private ContentLease<Scene> lease;
            private CoCoRegionChunkAnchor anchor;
            private bool registered;
            private bool prepared;
            private bool cleaned;

            internal ContentCandidate(
                RegionPlanNodeId nodeId,
                ContentPlan plan,
                IRegionContentParticipantRuntime runtime)
            {
                this.nodeId = nodeId;
                this.plan = plan;
                this.runtime = runtime;
            }

            public UniTask<RegionParticipantPrepareResult> PrepareAsync(
                in RegionParticipantPrepareContext context,
                CancellationToken cancellationToken)
            {
                return PrepareCoreAsync(context, cancellationToken);
            }

            public bool TryCommit(
                in RegionParticipantCommitContext context,
                out CoCoDiagnostic diagnostic)
            {
                if (!prepared ||
                    cleaned ||
                    anchor == null ||
                    lease == null ||
                    lease.IsReleased)
                {
                    diagnostic = RegionErrors.CommitFaulted(
                        "The Content participant lost its prepared leased Scene before commit.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            public UniTask<RegionParticipantCleanupResult> CleanupAsync(
                RegionParticipantCleanupReason reason,
                CancellationToken cancellationToken)
            {
                return UniTask.FromResult(CleanupNoThrow());
            }

            public void ForceCleanupNoFail()
            {
                CleanupNoThrow();
            }

            public bool TryGetAnchor(out CoCoRegionChunkAnchor result)
            {
                result = !cleaned && anchor != null ? anchor : null;
                return result != null;
            }

            ContentId IRegionContentMonitorSource.ContentId =>
                plan.SceneReference.ContentId;

            long IRegionContentMonitorSource.ContentScopeSequence =>
                scope == null ? 0L : scope.ScopeSequence;

            long IRegionContentMonitorSource.ContentLeaseSequence =>
                lease == null ? 0L : lease.LeaseSequence;

            private async UniTask<RegionParticipantPrepareResult>
                PrepareCoreAsync(
                    RegionParticipantPrepareContext context,
                    CancellationToken cancellationToken)
            {
                if (cleaned ||
                    prepared ||
                    context.NodeId != nodeId ||
                    !plan.SceneReference.TryCreateContentReference(
                        out ContentReference reference))
                {
                    return RegionParticipantPrepareResult.Failure(
                        RegionErrors.TransitionFailed(
                            "The Content participant received an invalid or repeated Prepare request."));
                }

                if (!runtime.TryCreateContentScope(
                        nodeId,
                        context.TransitionGeneration,
                        out scope,
                        out CoCoDiagnostic scopeDiagnostic))
                {
                    return RegionParticipantPrepareResult.Failure(
                        scopeDiagnostic);
                }

                ContentAcquireResult<Scene> acquireResult;
                try
                {
                    acquireResult = await scope.AcquireAdditiveSceneAsync(
                        reference,
                        cancellationToken);
                }
                catch (Exception exception)
                {
                    DisposeScope();
                    return RegionParticipantPrepareResult.Failure(
                        RegionErrors.TransitionFailed(
                            "The Content participant could not acquire its Chunk Scene: " +
                            exception.Message));
                }

                if (!acquireResult.Succeeded)
                {
                    DisposeScope();
                    CoCoDiagnostic failure =
                        acquireResult.Diagnostic.IsNone
                            ? RegionErrors.TransitionFailed(
                                "The Content participant did not acquire its Chunk Scene.")
                            : acquireResult.Diagnostic;
                    return RegionParticipantPrepareResult.Failure(failure);
                }

                lease = acquireResult.Lease;
                Scene scene = lease.Value;
                if (!IsExpectedLeasedScene(scene, plan.SceneReference))
                {
                    DisposeScope();
                    return RegionParticipantPrepareResult.Failure(
                        RegionErrors.SceneContract(
                            "Content returned a Scene that does not match the compiled canonical Chunk Scene."));
                }

                if (!TryFindUniqueAnchor(
                        scene,
                        out anchor,
                        out CoCoDiagnostic anchorDiagnostic) ||
                    !anchor.TryValidateColdStart(
                        nodeId.RegionId,
                        nodeId.ChunkId,
                        out anchorDiagnostic))
                {
                    DisposeScope();
                    anchor = null;
                    return RegionParticipantPrepareResult.Failure(
                        anchorDiagnostic);
                }

                if (!runtime.TryRegisterChunkAnchor(
                        nodeId,
                        context.TransitionGeneration,
                        anchor,
                        out CoCoDiagnostic registrationDiagnostic))
                {
                    DisposeScope();
                    anchor = null;
                    return RegionParticipantPrepareResult.Failure(
                        registrationDiagnostic);
                }

                registered = true;
                prepared = true;
                return RegionParticipantPrepareResult.Success();
            }

            private RegionParticipantCleanupResult CleanupNoThrow()
            {
                if (cleaned)
                {
                    return RegionParticipantCleanupResult.Success();
                }

                try
                {
                    if (registered)
                    {
                        runtime.UnregisterChunkAnchor(nodeId, anchor);
                        registered = false;
                    }

                    DisposeScope();
                    anchor = null;
                    prepared = false;
                    cleaned = true;
                    return RegionParticipantCleanupResult.Success();
                }
                catch (Exception exception)
                {
                    return RegionParticipantCleanupResult.Failure(
                        RegionErrors.CleanupBlocked(
                            "The Content participant could not release its Chunk Scene ownership: " +
                            exception.Message));
                }
            }

            private void DisposeScope()
            {
                lease = null;
                ContentScope ownedScope = scope;
                scope = null;
                ownedScope?.Dispose();
            }

            private static bool IsExpectedLeasedScene(
                Scene scene,
                RegionCompiledSceneReference reference)
            {
                if (!scene.IsValid() || !scene.isLoaded)
                {
                    return false;
                }

                return string.Equals(
                    NormalizePath(scene.path),
                    NormalizePath(reference.CanonicalScenePath),
                    StringComparison.Ordinal);
            }

            private static string NormalizePath(string path) =>
                string.IsNullOrEmpty(path)
                    ? string.Empty
                    : path.Replace('\\', '/');

            private static bool TryFindUniqueAnchor(
                Scene scene,
                out CoCoRegionChunkAnchor anchor,
                out CoCoDiagnostic diagnostic)
            {
                anchor = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int rootIndex = 0;
                     rootIndex < roots.Length;
                     rootIndex++)
                {
                    CoCoRegionChunkAnchor[] matches =
                        roots[rootIndex]
                            .GetComponentsInChildren<
                                CoCoRegionChunkAnchor>(true);
                    for (int matchIndex = 0;
                         matchIndex < matches.Length;
                         matchIndex++)
                    {
                        CoCoRegionChunkAnchor candidate =
                            matches[matchIndex];
                        if (candidate == null ||
                            candidate.gameObject.scene != scene)
                        {
                            continue;
                        }

                        if (anchor != null)
                        {
                            diagnostic = RegionErrors.SceneContract(
                                "The exact leased Chunk Scene contains more than one Region Anchor.");
                            anchor = null;
                            return false;
                        }

                        anchor = candidate;
                    }
                }

                if (anchor == null)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "The exact leased Chunk Scene contains no Region Anchor.");
                    return false;
                }

                diagnostic = CoCoDiagnostic.None;
                return true;
            }
        }

        public static RegionParticipantTypeId TypeId =>
            RegionBuiltInParticipantUtilities.ContentTypeId;

        public static RegionParticipantModeId ModeId =>
            RegionBuiltInParticipantUtilities.DefaultModeId;

        public static bool TryRegister(
            RegionParticipantCatalog catalog,
            out CoCoDiagnostic diagnostic)
        {
            if (catalog == null)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "A participant catalog is required.");
                return false;
            }

            if (!RegionParticipantRegistration.TryCreateOwningContent(
                    TypeId,
                    ModeId,
                    RegionBuiltInParticipantUtilities.StandardCapabilities,
                    new ContentFreezer(),
                    new ContentFactory(),
                    out RegionParticipantRegistration registration,
                    out diagnostic))
            {
                return false;
            }

            return catalog.TryRegisterParticipant(
                registration,
                out diagnostic);
        }
    }

    public static class RegionBuiltInParticipants
    {
        public static bool TryRegisterAll(
            RegionParticipantCatalog catalog,
            out CoCoDiagnostic diagnostic)
        {
            if (!RegionContentParticipant.TryRegister(
                    catalog,
                    out diagnostic) ||
                !RegionGameObjectParticipant.TryRegister(
                    catalog,
                    out diagnostic) ||
                !RegionColliderParticipant.TryRegister(
                    catalog,
                    out diagnostic) ||
                !RegionRendererParticipant.TryRegister(
                    catalog,
                    out diagnostic) ||
                !RegionAnimatorParticipant.TryRegister(
                    catalog,
                    out diagnostic) ||
                !RegionParticleParticipant.TryRegister(
                    catalog,
                    out diagnostic) ||
                !RegionBehaviourParticipant.TryRegister(
                    catalog,
                    out diagnostic))
            {
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    [DisallowMultipleComponent]
    public sealed class CoCoDefaultRegionCatalogProvider :
        MonoBehaviour,
        IRegionParticipantCatalogProvider
    {
        public bool TryGetCatalog(
            out RegionParticipantCatalog catalog,
            out CoCoDiagnostic diagnostic)
        {
            catalog = new RegionParticipantCatalog();
            if (!RegionBuiltInParticipants.TryRegisterAll(
                    catalog,
                    out diagnostic))
            {
                catalog = null;
                return false;
            }

            catalog.Seal();
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    internal interface IRegionContentParticipantRuntime
    {
        bool TryCreateContentScope(
            RegionPlanNodeId nodeId,
            long transitionGeneration,
            out ContentScope scope,
            out CoCoDiagnostic diagnostic);

        bool TryRegisterChunkAnchor(
            RegionPlanNodeId nodeId,
            long transitionGeneration,
            CoCoRegionChunkAnchor anchor,
            out CoCoDiagnostic diagnostic);

        void UnregisterChunkAnchor(
            RegionPlanNodeId nodeId,
            CoCoRegionChunkAnchor anchor);
    }

    internal interface IRegionChunkAnchorSource
    {
        bool TryGetAnchor(out CoCoRegionChunkAnchor anchor);
    }

    internal static class RegionBuiltInParticipantUtilities
    {
        internal static readonly RegionParticipantTypeId ContentTypeId =
            CreateTypeId("cocoflow.content");
        internal static readonly RegionParticipantTypeId GameObjectTypeId =
            CreateTypeId("cocoflow.game-object");
        internal static readonly RegionParticipantTypeId ColliderTypeId =
            CreateTypeId("cocoflow.collider");
        internal static readonly RegionParticipantTypeId RendererTypeId =
            CreateTypeId("cocoflow.renderer");
        internal static readonly RegionParticipantTypeId AnimatorTypeId =
            CreateTypeId("cocoflow.animator");
        internal static readonly RegionParticipantTypeId ParticleTypeId =
            CreateTypeId("cocoflow.particle");
        internal static readonly RegionParticipantTypeId BehaviourTypeId =
            CreateTypeId("cocoflow.behaviour");
        internal static readonly RegionParticipantModeId DefaultModeId =
            CreateModeId("cocoflow.default");
        internal static readonly RegionCapabilitySet StandardCapabilities =
            CreateStandardCapabilities();

        internal static bool TryValidateFragmentConfiguration(
            in RegionParticipantFreezeContext context,
            RegionParticipantConfig configuration,
            Type expectedConfigurationType,
            string participantName,
            out CoCoDiagnostic diagnostic)
        {
            if (configuration == null ||
                !expectedConfigurationType.IsInstanceOfType(configuration))
            {
                diagnostic = RegionErrors.InvalidProfile(
                    "The " + participantName +
                    " participant received the wrong configuration type.");
                return false;
            }

            if (!context.NodeId.HasChunkId ||
                string.IsNullOrWhiteSpace(context.FragmentId) ||
                context.SceneReference.IsValid)
            {
                diagnostic = RegionErrors.InvalidProfile(
                    "The " + participantName +
                    " participant must target a non-empty fragment in one Chunk and cannot own the Scene lease.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryResolveGameObject(
            IRegionFragmentResolver resolver,
            string fragmentId,
            out GameObject target,
            out CoCoDiagnostic diagnostic)
        {
            target = null;
            diagnostic = CoCoDiagnostic.None;
            if (resolver == null ||
                !resolver.TryResolveGameObject(
                    fragmentId,
                    out target,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "The participant fragment could not be resolved.");
                }

                return false;
            }

            if (target != null) return true;

            diagnostic = RegionErrors.SceneContract(
                "The participant fragment resolved to a missing GameObject.");
            return false;
        }

        internal static bool TryResolveComponents<T>(
            IRegionFragmentResolver resolver,
            string fragmentId,
            bool includeChildren,
            out T[] components,
            out CoCoDiagnostic diagnostic)
            where T : Component
        {
            components = Array.Empty<T>();
            if (!TryResolveGameObject(
                    resolver,
                    fragmentId,
                    out GameObject target,
                    out diagnostic))
            {
                return false;
            }

            components = includeChildren
                ? target.GetComponentsInChildren<T>(true)
                : target.GetComponents<T>();
            if (components != null && components.Length > 0)
            {
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            components = Array.Empty<T>();
            diagnostic = RegionErrors.SceneContract(
                "Fragment '" + fragmentId + "' contains no " +
                typeof(T).Name + " component.");
            return false;
        }

        private static RegionParticipantTypeId CreateTypeId(string value)
        {
            if (RegionParticipantTypeId.TryCreate(
                    value,
                    out RegionParticipantTypeId id))
            {
                return id;
            }

            throw new InvalidOperationException(
                "The built-in Region participant type id is invalid.");
        }

        private static RegionParticipantModeId CreateModeId(string value)
        {
            if (RegionParticipantModeId.TryCreate(
                    value,
                    out RegionParticipantModeId id))
            {
                return id;
            }

            throw new InvalidOperationException(
                "The built-in Region participant mode id is invalid.");
        }

        private static RegionCapabilitySet CreateStandardCapabilities()
        {
            if (RegionCapabilitySet.TryCreate(
                    new[]
                    {
                        RegionCapabilityId.Represented,
                        RegionCapabilityId.Background,
                        RegionCapabilityId.Enterable,
                        RegionCapabilityId.Full
                    },
                    out RegionCapabilitySet capabilities))
            {
                return capabilities;
            }

            throw new InvalidOperationException(
                "The built-in Region capabilities are invalid.");
        }
    }
}
