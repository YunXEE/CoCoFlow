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
    internal interface ICoCoStateGraphProjectBindingProvider
    {
        CoCoGraphDescriptorCatalog Catalog { get; }

        bool TryConfigure(
            CoCoStateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic);
    }

    public static class CoCoStateGraphProjectBindings
    {
        private static ICoCoStateGraphProjectBindingProvider _provider;

        public static bool IsInstalled => _provider != null;

        public static bool TryInstall(CoCoStandardBindingProvider provider, out CoCoDiagnostic diagnostic) =>
            TryInstall((ICoCoStateGraphProjectBindingProvider)provider, out diagnostic);

        internal static bool TryInstall(
            ICoCoStateGraphProjectBindingProvider provider,
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

        internal static ICoCoStateGraphProjectBindingProvider Provider => _provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAtSubsystemRegistration()
        {
            _provider = null;
            CoCoStateGraphHostIdentity.Reset();
        }

        internal static void ResetForTests()
        {
            ResetAtSubsystemRegistration();
        }

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);
    }

    internal sealed class CoCoStateGraphHostRuntimeBindings : IDisposable
    {
        private readonly ICoCoHostIntentContribution[] _intentContributions;
        private CoCoTickFrame _cachedIntentTick;
        private ICoCoIntentFrame _cachedIntentFrame;
        private bool _hasCachedIntentTick;

        public CoCoStateGraphHostRuntimeBindings(
            CoCoStateGraphLogicBindings logic,
            CoCoOperationFrame operations,
            CoCoContextFrameLayout contextLayout,
            CoCoContextCodecRegistry contextCodecs,
            ICoCoGraphContextProducerBinding[] contextProducers,
            CoCoIntentFrameRuntime intents,
            ICoCoHostIntentContribution[] intentContributions)
        {
            Logic = logic;
            Operations = operations;
            ContextLayout = contextLayout;
            ContextCodecs = contextCodecs;
            ContextProducers = contextProducers ?? Array.Empty<ICoCoGraphContextProducerBinding>();
            Intents = intents;
            _intentContributions = intentContributions;
        }

        public CoCoStateGraphLogicBindings Logic { get; }
        public CoCoOperationFrame Operations { get; }
        public CoCoContextFrameLayout ContextLayout { get; }
        public CoCoContextCodecRegistry ContextCodecs { get; }
        internal IReadOnlyList<ICoCoGraphContextProducerBinding> ContextProducers { get; }
        public CoCoIntentFrameRuntime Intents { get; }

        public bool TryCollectIntents(
            in CoCoTickFrame tickFrame,
            out ICoCoIntentFrame frame,
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

            if (!CoCoStateFlowFrameHeader.TryCreate(
                    Intents.GraphInstanceId,
                    Intents.LayoutId,
                    CoCoStateFlowFrameKind.Intent,
                    tickFrame,
                    out CoCoStateFlowFrameHeader header))
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

        public void ResolveIntentTick(in CoCoTickFrame tickFrame)
        {
            if (!IsIntentTickCommitReady(tickFrame))
            {
                return;
            }

            ResolveIntentTickNoFail();
        }

        internal bool IsIntentTickCommitReady(in CoCoTickFrame tickFrame) =>
            _hasCachedIntentTick && _cachedIntentTick == tickFrame;

        internal void CommitIntentTickNoFail()
        {
            foreach (ICoCoHostIntentContribution contribution in _intentContributions)
            {
                CoCoIntentSourceCursor cursor = contribution.Cursor;
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

    internal interface ICoCoHostIntentContribution
    {
        int IntentSourceHostIndex { get; }

        CoCoIntentSourceCursor Cursor { get; }

        bool TryAssignRegistrationOrder(int registrationOrder);

        bool TryCollect(CoCoIntentFrameRuntime runtime, in CoCoTickFrame tickFrame);
    }

    internal sealed class CoCoHostIntentSourceContribution<TIntent> : ICoCoHostIntentContribution
    {
        private readonly CoCoIntentSourceBinding<TIntent> _binding;
        private readonly int _hostIndex;

        public CoCoHostIntentSourceContribution(
            CoCoIntentSourceBinding<TIntent> binding,
            int hostIndex)
        {
            _binding = binding;
            _hostIndex = hostIndex;
            _binding.AttachHostCursor();
        }

        public int IntentSourceHostIndex => _hostIndex;

        public CoCoIntentSourceCursor Cursor => _binding.Cursor;

        public bool TryAssignRegistrationOrder(int registrationOrder) =>
            _binding.TryAssignRegistrationOrder(registrationOrder);

        public bool TryCollect(CoCoIntentFrameRuntime runtime, in CoCoTickFrame tickFrame)
        {
            CoCoIntentSourceSampleResult result = runtime.TrySample(_binding, tickFrame);
            return result == CoCoIntentSourceSampleResult.Contributed ||
                   result == CoCoIntentSourceSampleResult.NoValue;
        }
    }

}
