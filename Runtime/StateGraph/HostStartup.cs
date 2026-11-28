using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>在发布实例前准备图、接线、事务和历史，并在失败时释放已取得的资源。</summary>
    internal sealed class HostStartup
    {
        internal StateGraphInstance Instance;
        internal HostRuntimeBindings Bindings;
        internal HostStepTransaction Transaction;
        internal HostRestoreBindings Restore;
        internal HostHistoryController History;
        internal IEnumerable<Type> InboxEventTypes;

        private HostStartup() { }

        internal static bool TryPrepare(StateGraphAsset asset, HostComponentBoundary boundary,
            IReadOnlyList<MonoBehaviour> intentSources, IReadOnlyList<MonoBehaviour> operators,
            HostOperatorRecovery[] recovery, MonoBehaviour contextBinding, MonoBehaviour restoreBinding,
            ulong domain, int historyCapacity, int contextCapacity, int outboxCapacity, int traceCapacity,
            IStateGraphCommitGuard guard, IHostEventPublication publication, out HostStartup ready, out CoCoDiagnostic diagnostic)
        {
            ready = null;
            if (asset == null)
            {
                diagnostic = RegistryError(CoCoDiagnosticCode.MissingDescriptor, "StateGraphHost requires one StateGraph Asset.");
                return false;
            }

            var provider = HostProjectBindings.Provider;
            if (provider == null)
            {
                diagnostic = RegistryError(CoCoDiagnosticCode.RegistryNotFrozen, "No StateGraph project binding provider was installed.");
                return false;
            }

            if (provider is HostBindingProvider && asset.EventAdapterDeclarations.Count != 0)
            {
                diagnostic = RegistryError(CoCoDiagnosticCode.ManifestConflict,
                    "Remove the old Event-to-Intent declarations from this graph and read events through State Inbox.");
                return false;
            }

            GraphAssetCompileResult result;
            StateGraphAsset configured = null;
            try
            {
                configured = ConfiguredGraph(asset, ref provider);
                result = new GraphAssetCompiler().Compile(configured, provider.Catalog);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, boundary.Owner);
                diagnostic = RegistryError(CoCoDiagnosticCode.CommitPreparationFailed, "StateGraph Asset compilation threw during Host setup.");
                return false;
            }
            finally
            {
                ReleaseConfiguredGraph(asset, configured);
            }

            if (!result.Succeeded)
            {
                diagnostic = FirstCompileError(result);
                return false;
            }

            if (Cancelled(guard, out diagnostic)) return false;
            var id = InstanceIdAllocator.Next();
            var graph = result.Graph;
            var builder = new HostBindingBuilder(graph, id, boundary, intentSources);
            var candidate = new HostStartup();
            try
            {
                if (!provider.TryConfigure(builder, out diagnostic) || diagnostic.IsError ||
                    Cancelled(guard, out diagnostic) || !builder.TryFreeze(out candidate.Bindings, out diagnostic))
                {
                    if (!diagnostic.IsError)
                        diagnostic = RegistryError(CoCoDiagnosticCode.MissingDescriptor, "Project StateGraph bindings were incomplete.");
                    return false;
                }

                var bindings = candidate.Bindings;
                if (Cancelled(guard, out diagnostic) ||
                    !TryValidateConfiguration(boundary, bindings.ContextLayout, bindings.ContextCodecs, restoreBinding,
                        historyCapacity, out diagnostic) ||
                    !HostStepTransaction.TryPreflight(boundary, publication, contextBinding, operators, graph, id,
                        bindings.ContextLayout, bindings.Operations, bindings.ContextProducers, contextCapacity,
                        outboxCapacity, traceCapacity, out candidate.Transaction, out diagnostic))
                    return false;

                candidate.Restore = new HostRestoreBindings(boundary, operators, recovery ?? Array.Empty<HostOperatorRecovery>(),
                    bindings.ContextLayout, () => guard.IsCommitCancellationRequested);
                if (Cancelled(guard, out diagnostic) ||
                    !TryCreateTimelineId(graph.GraphId, id, out TimelineId timeline, out diagnostic) ||
                    !ClockDomainId.TryCreate(domain, out ClockDomainId clockDomain) ||
                    !InstanceClock.TryCreate(timeline, clockDomain, new TimelineEpoch(0), id, out var clock, out diagnostic) ||
                    !StateGraphInstance.TryCreate(graph, id, bindings.Logic, bindings.Operations, clock, out candidate.Instance, out diagnostic) ||
                    Cancelled(guard, out diagnostic) || !candidate.Instance.TryStart(out diagnostic) ||
                    Cancelled(guard, out diagnostic) ||
                    !candidate.Transaction.TryValidateInitialGraphContextDefaults(candidate.Instance, guard, out diagnostic) ||
                    !candidate.Restore.TryBindScene(restoreBinding, historyCapacity, out diagnostic) ||
                    !TryCreateHistory(bindings.ContextLayout, bindings.ContextCodecs, candidate.Restore, historyCapacity, out candidate.History, out diagnostic) ||
                    Cancelled(guard, out diagnostic))
                    return false;

                candidate.InboxEventTypes = provider is HostBindingProvider standard
                    ? standard.InboxEventTypes(graph) : Array.Empty<Type>();
                ready = candidate;
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, boundary.Owner);
                diagnostic = RegistryError(CoCoDiagnosticCode.CommitPreparationFailed,
                    "Host setup failed before the StateGraph instance became observable.");
                return false;
            }
            finally
            {
                if (ready == null)
                {
                    candidate.Restore?.Dispose();
                    candidate.History?.Dispose();
                    candidate.Instance?.Dispose();
                    candidate.Transaction?.Dispose();
                    if (candidate.Bindings == null) builder.Abandon();
                    else candidate.Bindings.Dispose();
                }
            }
        }

        private static bool Cancelled(IStateGraphCommitGuard guard, out CoCoDiagnostic diagnostic)
        {
            diagnostic = guard.IsCommitCancellationRequested
                ? LifecycleError("Unity destruction cancelled StateGraph Host startup before Runtime publication.")
                : CoCoDiagnostic.None;
            return diagnostic.IsError;
        }

        private static bool TryCreateTimelineId(
            GraphId graphId,
            GraphInstanceId graphInstanceId,
            out TimelineId timelineId,
            out CoCoDiagnostic diagnostic)
        {
            if (!graphId.IsValid ||
                !graphInstanceId.IsValid ||
                !TimelineId.TryCreate(
                    graphInstanceId.Value,
                    graphId.High ^ graphId.Low,
                    out timelineId))
            {
                timelineId = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Time,
                    CoCoDiagnosticCode.InvalidIdentifier,
                    "StateGraph Host could not derive a valid TimelineId from its Graph and instance identities.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic FirstCompileError(GraphAssetCompileResult result)
        {
            for (int index = 0; index < result.Diagnostics.Count; index++)
            {
                if (result.Diagnostics[index].IsError)
                {
                    return result.Diagnostics[index].Diagnostic;
                }
            }

            return RegistryError(
                CoCoDiagnosticCode.CommitPreparationFailed,
                "StateGraph Asset compilation did not produce a runnable Graph.");
        }

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);

        private static CoCoDiagnostic LifecycleError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Lifecycle,
                CoCoDiagnosticCode.InvalidLifecycleTransition,
                message);

        internal static StateGraphAsset ConfiguredGraph(StateGraphAsset stateGraphAsset, ref IHostBindingProvider provider)
        {
            if (!(provider is HostBindingProvider standard)) return stateGraphAsset;
            var graph = UnityEngine.Object.Instantiate(stateGraphAsset);
            try
            {
                var aliases = new Dictionary<StateDescriptorId, Type>();
                foreach (var layer in graph.Layers)
                {
                    foreach (var state in layer.States)
                        ConfigureStateAlias(state, provider.Catalog, aliases);
                    if (layer.AnyState != null)
                        ConfigureStateAlias(layer.AnyState, provider.Catalog, aliases);
                }
                provider = HostBindingProvider.Build(standard.Assemblies, aliases);
                return graph;
            }
            catch
            {
                // Until return, the creator still owns the configured copy.
                ReleaseConfiguredGraph(stateGraphAsset, graph);
                throw;
            }
        }

        internal static void ReleaseConfiguredGraph(StateGraphAsset stateGraphAsset, StateGraphAsset graph)
        {
            if (graph == null || graph == stateGraphAsset) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(graph);
            else
                UnityEngine.Object.DestroyImmediate(graph);
        }

        internal static bool TryValidateConfiguration(
            HostComponentBoundary boundary,
            ContextFrameLayout layout,
            ContextCodecRegistry codecs,
            MonoBehaviour bindingComponent,
            int capacity,
            out CoCoDiagnostic diagnostic)
        {
            if (!boundary.HasOwner ||
                layout == null ||
                codecs == null ||
                (capacity != 0 && capacity < 2))
            {
                diagnostic = ConfigurationError(
                    "Temporal configuration requires one Host, frozen Context layout and codec registry, and a capacity of zero or at least two committed entries.");
                return false;
            }

            if (!HostRestoreBindings.TryValidateBinding(boundary, bindingComponent, capacity, out diagnostic))
                return false;

            ContextProjectionCodec codec = null;
            if (capacity > 0 &&
                !ContextProjectionCodec.TryCreate(
                    layout,
                    codecs,
                    ContextProjection.Temporal,
                    out codec,
                    out CoCoDiagnosticCode diagnosticCode))
            {
                diagnostic = HistoryError(
                    diagnosticCode,
                    "Temporal projection codec preflight rejected the Context layout or codec registry.");
                return false;
            }

            if (capacity > 0 && codec != null &&
                ((long)capacity + 1L) * codec.MaxEncodedSize + layout.ByteSize > int.MaxValue)
            {
                diagnostic = HistoryError(
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "Temporal history exceeds the checked managed payload budget.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryCreateHistory(ContextFrameLayout layout, ContextCodecRegistry codecs,
            HostRestoreBindings restore, int capacity, out HostHistoryController controller, out CoCoDiagnostic diagnostic)
        {
            controller = null;
            diagnostic = CoCoDiagnostic.None;
            TemporalHistory history = null;
            if (capacity > 0)
            {
                if (!TemporalHistory.TryCreate(layout, codecs, capacity, out history, out CoCoDiagnosticCode code))
                {
                    diagnostic = HistoryError(code, "Temporal history could not allocate its fixed projection payload.");
                    return false;
                }
                if (!restore.TryAttachParticipant(capacity, out diagnostic))
                {
                    history.Dispose();
                    return false;
                }
            }
            controller = new HostHistoryController(history);
            return true;
        }

        private static CoCoDiagnostic ConfigurationError(string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Context,
                CoCoDiagnosticCode.InvalidHostConfiguration,
                message);

        private static CoCoDiagnostic HistoryError(
            CoCoDiagnosticCode code,
            string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                code == CoCoDiagnosticCode.None
                    ? CoCoDiagnosticCode.InvalidRestoreMetadata
                    : code,
                message);

        private static void ConfigureStateAlias(StateGraphStateRecord state,
            GraphDescriptorCatalog catalog, Dictionary<StateDescriptorId, Type> aliases)
        {
            StateDescriptorId.TryCreate(state.StateDescriptorId.High, state.StateDescriptorId.Low, out var original);
            catalog.TryGetStateDescriptor(original, out var descriptor);
            string stateId = $"{state.StateId.High:x16}{state.StateId.Low:x16}";
            StandardDescriptors.TryCreate(descriptor.LogicType, "State:" + stateId, out var alias);
            aliases.Add(alias, descriptor.LogicType);
            state.StateDescriptorId = new SerializedId128(alias.High, alias.Low);
            if (descriptor.AuthoringConfigType == typeof(FieldConfig))
                state.Config = state.Config as FieldConfig ?? new FieldConfig();
        }
    }
}
