using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using CoCoFlow.Runtime.Content;
using CoCoFlow.Runtime.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Map
{
    [DisallowMultipleComponent]
    public sealed class CoCoMapHost : MonoBehaviour
    {
        [SerializeField] private CoCoContentHost contentHost;
        [SerializeField] private MonoBehaviour catalogProviderComponent;
        [SerializeField] private MonoBehaviour addressableSceneResolverComponent;
        [SerializeField] private List<CoCoRegionBinding> bootstrapBindings =
            new List<CoCoRegionBinding>();
        [SerializeField, Min(1f)] private float cleanupTimeoutSeconds = 30f;

        private RegionTransitionRuntime transitionRuntime;
        private IReadOnlyList<RegionCompileDiagnostic>
            compilationDiagnostics =
                Array.Empty<RegionCompileDiagnostic>();

        public CoCoContentHost ContentHost => contentHost;
        public RegionRuntime Runtime { get; private set; }
        public CoCoDiagnostic LastDiagnostic { get; private set; }
        public bool IsInitialized =>
            Runtime != null && !Runtime.IsDisposed;
        public IReadOnlyList<RegionCompileDiagnostic>
            CompilationDiagnostics => compilationDiagnostics;

        private void Awake()
        {
            if (TryInitialize(out CoCoDiagnostic diagnostic)) return;

            Debug.LogError(
                "CoCoMapHost initialization failed: " +
                diagnostic.Message,
                this);
        }

        public bool TryInitialize(out CoCoDiagnostic diagnostic)
        {
            if (Runtime != null)
            {
                diagnostic = Runtime.IsDisposed
                    ? RegionErrors.RuntimeDisposed()
                    : CoCoDiagnostic.None;
                LastDiagnostic = diagnostic;
                return !Runtime.IsDisposed;
            }

            try
            {
                RegionMainThreadGuard.CaptureCurrentThread();
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.MainThreadRequired();
                LastDiagnostic = diagnostic;
                Debug.LogException(exception, this);
                return false;
            }

            if (contentHost == null)
            {
                diagnostic = RegionErrors.DemandConflict(
                    "CoCoMapHost requires an explicit CoCoContentHost reference.");
                LastDiagnostic = diagnostic;
                return false;
            }

            if (!contentHost.TryInitialize(out diagnostic))
            {
                LastDiagnostic = diagnostic;
                return false;
            }

            if (!(catalogProviderComponent is
                    IRegionParticipantCatalogProvider catalogProvider))
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "CoCoMapHost requires an explicit component implementing IRegionParticipantCatalogProvider.");
                LastDiagnostic = diagnostic;
                return false;
            }

            RegionParticipantCatalog catalog;
            try
            {
                if (!catalogProvider.TryGetCatalog(
                        out catalog,
                        out diagnostic))
                {
                    if (diagnostic.IsNone)
                    {
                        diagnostic = RegionErrors.CatalogConflict(
                            "The Region catalog provider failed without a diagnostic.");
                    }

                    LastDiagnostic = diagnostic;
                    return false;
                }
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "The Region catalog provider threw: " +
                    exception.Message);
                LastDiagnostic = diagnostic;
                return false;
            }

            if (catalog == null)
            {
                diagnostic = RegionErrors.CatalogConflict(
                    "The Region catalog provider returned a null catalog.");
                LastDiagnostic = diagnostic;
                return false;
            }

            if (!catalog.IsSealed)
            {
                catalog.Seal();
            }

            IRegionAddressableSceneResolver addressableResolver = null;
            if (addressableSceneResolverComponent != null)
            {
                addressableResolver =
                    addressableSceneResolverComponent as
                        IRegionAddressableSceneResolver;
                if (addressableResolver == null)
                {
                    diagnostic = RegionErrors.SceneContract(
                        "The assigned addressable Scene resolver component does not implement IRegionAddressableSceneResolver.");
                    LastDiagnostic = diagnostic;
                    return false;
                }
            }

            if (bootstrapBindings == null ||
                bootstrapBindings.Count == 0)
            {
                diagnostic = RegionErrors.CompilationFailed(
                    "CoCoMapHost requires at least one bootstrap Region Binding.");
                LastDiagnostic = diagnostic;
                return false;
            }

            var compiler = new RegionBindingCompiler();
            IReadOnlyList<RegionCompileResult> results;
            try
            {
                results = compiler.CompileAll(
                    bootstrapBindings,
                    catalog,
                    addressableResolver);
            }
            catch (Exception exception)
            {
                diagnostic = RegionErrors.CompilationFailed(
                    "Bootstrap Region compilation threw: " +
                    exception.Message);
                LastDiagnostic = diagnostic;
                return false;
            }

            var diagnostics = new List<RegionCompileDiagnostic>();
            var plans = new List<RegionCompiledPlan>(results.Count);
            for (int index = 0; index < results.Count; index++)
            {
                RegionCompileResult result = results[index];
                diagnostics.AddRange(result.Diagnostics);
                if (result.Succeeded)
                {
                    plans.Add(result.Plan);
                }
            }

            compilationDiagnostics =
                new ReadOnlyCollection<RegionCompileDiagnostic>(
                    diagnostics);
            if (plans.Count != results.Count)
            {
                diagnostic = FirstCompilationError(diagnostics);
                LastDiagnostic = diagnostic;
                return false;
            }

            if (!RegionRuntime.TryCreate(
                    contentHost.Runtime,
                    out RegionRuntime runtime,
                    out diagnostic))
            {
                LastDiagnostic = diagnostic;
                return false;
            }

            if (!RegionTransitionRuntime.TryCreate(
                    runtime,
                    catalog,
                    plans,
                    TimeSpan.FromSeconds(
                        Math.Max(1f, cleanupTimeoutSeconds)),
                    out RegionTransitionRuntime transition,
                    out diagnostic))
            {
                runtime.ForceShutdown();
                LastDiagnostic = diagnostic;
                return false;
            }

            Runtime = runtime;
            transitionRuntime = transition;
            LastDiagnostic = CoCoDiagnostic.None;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCreateDemandScope(
            RegionDemandOwnerId ownerId,
            out RegionDemandScope scope,
            out CoCoDiagnostic diagnostic)
        {
            if (Runtime == null &&
                !TryInitialize(out diagnostic))
            {
                scope = null;
                return false;
            }

            bool succeeded = Runtime.TryCreateDemandScope(
                ownerId,
                out scope,
                out diagnostic);
            LastDiagnostic = diagnostic;
            return succeeded;
        }

        public bool TryRetryRegion(
            RegionId regionId,
            out CoCoDiagnostic diagnostic)
        {
            if (Runtime == null &&
                !TryInitialize(out diagnostic))
            {
                return false;
            }

            bool succeeded = Runtime.TryRetryRegion(
                regionId,
                out diagnostic);
            LastDiagnostic = diagnostic;
            return succeeded;
        }

        public RegionRuntimeSnapshot CaptureSnapshot()
        {
            if (Runtime == null)
            {
                throw new InvalidOperationException(
                    "CoCoMapHost is not initialized.");
            }

            return Runtime.CaptureSnapshot();
        }

        internal RegionMapMonitorSnapshot CaptureMonitorSnapshot()
        {
            RegionRuntimeSnapshot runtimeSnapshot =
                CaptureSnapshot();
            var temporalRetention =
                new List<RegionDemandRuntimeSnapshot>();
            for (int index = 0;
                 index < runtimeSnapshot.Demands.Count;
                 index++)
            {
                RegionDemandRuntimeSnapshot demand =
                    runtimeSnapshot.Demands[index];
                if (demand.OwnerId.Value.StartsWith(
                        "cocoflow.map.temporal.",
                        StringComparison.Ordinal))
                {
                    temporalRetention.Add(demand);
                }
            }

            var transitionRegions =
                new List<RegionTransitionMonitorRegionSnapshot>();
            if (transitionRuntime != null)
            {
                transitionRegions.AddRange(
                    transitionRuntime.CaptureMonitorRegions());
            }

            return new RegionMapMonitorSnapshot(
                runtimeSnapshot,
                Runtime.IsTemporalDispatchDeferred,
                Runtime.DeferredTransitionCount,
                transitionRegions,
                temporalRetention);
        }

        public async UniTask<CoCoDiagnostic> ShutdownAsync()
        {
            if (Runtime == null) return CoCoDiagnostic.None;

            LastDiagnostic = await Runtime.ShutdownAsync();
            return LastDiagnostic;
        }

        private void LateUpdate()
        {
            Runtime?.FlushDeferredTransitionsNoThrow();
        }

        private void OnDisable()
        {
            ShutdownOnDisableNoThrowAsync().Forget();
        }

        private void OnDestroy()
        {
            if (Runtime != null && !Runtime.IsDisposed)
            {
                Runtime.ForceShutdown(
                    RegionErrors.CleanupBlocked(
                        "CoCoMapHost destruction required terminal shutdown fallback."));
            }

            transitionRuntime = null;
        }

        private async UniTaskVoid ShutdownOnDisableNoThrowAsync()
        {
            RegionRuntime runtime = Runtime;
            if (runtime == null || runtime.IsDisposed)
            {
                return;
            }

            try
            {
                LastDiagnostic = await runtime.ShutdownAsync();
            }
            catch (Exception exception)
            {
                LastDiagnostic = RegionErrors.CleanupBlocked(
                    "CoCoMapHost disable shutdown threw: " +
                    exception.Message);
                runtime.ForceShutdown(LastDiagnostic);
            }
        }

        private void OnValidate()
        {
            if (cleanupTimeoutSeconds < 1f)
            {
                cleanupTimeoutSeconds = 1f;
            }
        }

        private static CoCoDiagnostic FirstCompilationError(
            IReadOnlyList<RegionCompileDiagnostic> diagnostics)
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                if (diagnostics[index].Diagnostic.IsError)
                {
                    return diagnostics[index].Diagnostic;
                }
            }

            return RegionErrors.CompilationFailed(
                "One or more bootstrap Region Bindings did not compile.");
        }
    }
}
