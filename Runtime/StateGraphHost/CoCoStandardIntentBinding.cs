using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public interface ICoCoStandardIntentRegistration
    {
        Type IntentType { get; }
        CoCoIntentId IntentId { get; }
        bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder);
        bool TryRegisterHost(CoCoStateGraphHostBindingBuilder builder,
            out ICoCoStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic);
    }

    public interface ICoCoStandardIntentSourceBinding
    {
        bool Accepts(MonoBehaviour component);
        bool TryBind(CoCoStateGraphHostBindingBuilder builder,
            int hostSourceIndex, out CoCoDiagnostic diagnostic);
    }

    /// <summary>Immutable module registration; source handles belong to each Host.</summary>
    public class CoCoStandardIntentRegistration<TIntent, TReducer, TFactory> :
        ICoCoStandardIntentRegistration
        where TIntent : unmanaged
        where TReducer : unmanaged, ICoCoIntentReducer<TIntent>
        where TFactory : ICoCoIntentReducerFactory<TIntent, TReducer>, new()
    {
        private readonly int _maxContributions;
        private readonly ulong _fingerprint;
        public Type IntentType => typeof(TIntent);
        public CoCoIntentId IntentId { get; }

        public CoCoStandardIntentRegistration(CoCoIntentId intentId,
            int maxContributions, ulong semanticFingerprint)
        {
            IntentId = intentId;
            _maxContributions = maxContributions;
            _fingerprint = semanticFingerprint;
        }

        public bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder)
        {
            if (!builder.TryRegisterIntent(IntentId, _maxContributions,
                    new CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory>(_fingerprint), out var diagnostic))
                throw new InvalidOperationException(diagnostic.Message);
            return true;
        }

        public bool TryRegisterHost(CoCoStateGraphHostBindingBuilder builder,
            out ICoCoStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic)
        {
            binding = null;
            if (!builder.TryRegisterIntent<TIntent, TReducer, TFactory>(IntentId,
                    new TFactory(), _fingerprint, out var handle, out diagnostic)) return false;
            binding = new SourceBinding(handle);
            return true;
        }

        private sealed class SourceBinding : ICoCoStandardIntentSourceBinding
        {
            private readonly CoCoIntentHandle<TIntent> _handle;
            public SourceBinding(CoCoIntentHandle<TIntent> handle) { _handle = handle; }
            public bool Accepts(MonoBehaviour component) =>
                component != null && component is ICoCoIntentFrameSource<TIntent>;

            public bool TryBind(CoCoStateGraphHostBindingBuilder builder,
                int hostSourceIndex, out CoCoDiagnostic diagnostic)
            {
                if (!CoCoIntentSourceRequirement<TIntent>.TryCreate(_handle, 1, out var source))
                {
                    diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry,
                        CoCoDiagnosticCode.MissingIntentReducer, "Intent source requirement is invalid.");
                    return false;
                }
                return builder.TryBindIntentSource(hostSourceIndex, source, out diagnostic);
            }
        }
    }

}
