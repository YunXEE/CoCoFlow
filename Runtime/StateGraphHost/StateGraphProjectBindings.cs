using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraphHost
{
    /// <summary>
    /// Internal setup contract for a frozen catalog and per-Host bindings.
    /// </summary>
    internal interface IStateGraphProjectBindingProvider
    {
        GraphDescriptorCatalog Catalog { get; }

        bool TryConfigure(
            StateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic);
    }

    public static class StateGraphProjectBindings
    {
        private static IStateGraphProjectBindingProvider _provider;

        public static bool IsInstalled => _provider != null;

        public static bool TryInstall(StandardBindingProvider provider, out CoCoDiagnostic diagnostic) =>
            TryInstall((IStateGraphProjectBindingProvider)provider, out diagnostic);

        internal static bool TryInstall(
            IStateGraphProjectBindingProvider provider,
            out CoCoDiagnostic diagnostic)
        {
            if (provider == null || provider.Catalog == null || !provider.Catalog.IsFrozen)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "StateGraph project bindings require one provider with a frozen descriptor Catalog.");
                return false;
            }

            if (_provider != null)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph project bindings are immutable after the first installation.");
                return false;
            }

            _provider = provider;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static IStateGraphProjectBindingProvider Provider => _provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAtSubsystemRegistration()
        {
            _provider = null;
            StateGraphHostIdentity.Reset();
        }

        internal static void ResetForTests()
        {
            ResetAtSubsystemRegistration();
        }

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);
    }

    internal sealed class StateGraphHostRuntimeBindings : IDisposable
    {
        private readonly IHostIntentContribution[] _intentContributions;
        private TickFrame _cachedIntentTick;
        private IIntentFrame _cachedIntentFrame;
        private bool _hasCachedIntentTick;

        public StateGraphHostRuntimeBindings(
            StateGraphLogicBindings logic,
            OperationFrame operations,
            ContextFrameLayout contextLayout,
            ContextCodecRegistry contextCodecs,
            IGraphContextProducerBinding[] contextProducers,
            IntentFrameRuntime intents,
            IHostIntentContribution[] intentContributions)
        {
            Logic = logic;
            Operations = operations;
            ContextLayout = contextLayout;
            ContextCodecs = contextCodecs;
            ContextProducers = contextProducers ?? Array.Empty<IGraphContextProducerBinding>();
            Intents = intents;
            _intentContributions = intentContributions;
        }

        public StateGraphLogicBindings Logic { get; }
        public OperationFrame Operations { get; }
        public ContextFrameLayout ContextLayout { get; }
        public ContextCodecRegistry ContextCodecs { get; }
        internal IReadOnlyList<IGraphContextProducerBinding> ContextProducers { get; }
        public IntentFrameRuntime Intents { get; }

        public bool TryCollectIntents(
            in TickFrame tickFrame,
            out IIntentFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (_hasCachedIntentTick)
            {
                if (_cachedIntentTick == tickFrame)
                {
                    frame = _cachedIntentFrame;
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                frame = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "The unresolved Intent Tick must be retried or discarded before collecting a later Tick.");
                return false;
            }

            frame = null;
            if (Intents == null)
            {
                _cachedIntentTick = tickFrame;
                _cachedIntentFrame = null;
                _hasCachedIntentTick = true;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (!StateFlowFrameHeader.TryCreate(
                    Intents.GraphInstanceId,
                    Intents.LayoutId,
                    StateFlowFrameKind.Intent,
                    tickFrame,
                    out StateFlowFrameHeader header))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Frame,
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "IntentFrame header did not match the Host Tick and layout.");
                return false;
            }

            if (!Intents.TryBegin(header, out diagnostic))
            {
                return false;
            }

            for (int index = 0; index < _intentContributions.Length; index++)
            {
                if (!_intentContributions[index].TryCollect(Intents, tickFrame))
                {
                    Intents.CancelCollection();
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Intent,
                        CoCoDiagnosticCode.InvalidIntentContribution,
                        "An Intent Source or Event Adapter failed while collecting the Tick.");
                    return false;
                }
            }

            if (!Intents.TryFreeze(out diagnostic))
            {
                return false;
            }

            frame = Intents.Frame;
            _cachedIntentTick = tickFrame;
            _cachedIntentFrame = frame;
            _hasCachedIntentTick = true;
            return true;
        }

        public void ResolveIntentTick(in TickFrame tickFrame)
        {
            if (!IsIntentTickCommitReady(tickFrame))
            {
                return;
            }

            ResolveIntentTickNoFail();
        }

        internal bool IsIntentTickCommitReady(in TickFrame tickFrame) =>
            _hasCachedIntentTick && _cachedIntentTick == tickFrame;

        internal void CommitIntentTickNoFail()
        {
            foreach (IHostIntentContribution contribution in _intentContributions)
            {
                IntentSourceCursor cursor = contribution.Cursor;
                if (cursor != null)
                {
                    cursor.Committed = cursor.Prepared;
                }
            }

            ResolveIntentTickNoFail();
        }

        private void ResolveIntentTickNoFail()
        {
            _cachedIntentTick = default;
            _cachedIntentFrame = null;
            _hasCachedIntentTick = false;
        }

        public void Dispose()
        {
            _cachedIntentTick = default;
            _cachedIntentFrame = null;
            _hasCachedIntentTick = false;
            Intents?.Dispose();
        }
    }

    internal interface IHostIntentContribution
    {
        int IntentSourceHostIndex { get; }

        IntentSourceCursor Cursor { get; }

        bool TryAssignRegistrationOrder(int registrationOrder);

        bool TryCollect(IntentFrameRuntime runtime, in TickFrame tickFrame);
    }

    internal sealed class HostIntentSourceContribution<TIntent> : IHostIntentContribution
    {
        private readonly IntentSourceBinding<TIntent> _binding;
        private readonly int _hostIndex;

        public HostIntentSourceContribution(
            IntentSourceBinding<TIntent> binding,
            int hostIndex)
        {
            _binding = binding;
            _hostIndex = hostIndex;
            _binding.AttachHostCursor();
        }

        public int IntentSourceHostIndex => _hostIndex;

        public IntentSourceCursor Cursor => _binding.Cursor;

        public bool TryAssignRegistrationOrder(int registrationOrder) =>
            _binding.TryAssignRegistrationOrder(registrationOrder);

        public bool TryCollect(IntentFrameRuntime runtime, in TickFrame tickFrame)
        {
            IntentSourceSampleResult result = runtime.TrySample(_binding, tickFrame);
            return result == IntentSourceSampleResult.Contributed ||
                   result == IntentSourceSampleResult.NoValue;
        }
    }

}
