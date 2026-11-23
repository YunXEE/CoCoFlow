using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraphHost
{
    internal interface ICoCoStandardIntentRegistration
    {
        Type IntentType { get; }
        CoCoIntentId IntentId { get; }
        bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder);
        bool TryRegisterHost(CoCoStateGraphHostBindingBuilder builder,
            out ICoCoStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic);
    }

    internal interface ICoCoStandardIntentSourceBinding
    {
        bool Accepts(MonoBehaviour component);
        bool TryBind(CoCoStateGraphHostBindingBuilder builder,
            int hostSourceIndex, out CoCoDiagnostic diagnostic);
    }

    /// <summary>Immutable module registration; source handles belong to each Host.</summary>
    public class CoCoStandardIntentRegistration<TIntent, TReducer, TFactory> :
        ICoCoStandardIntentRegistration
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

        bool ICoCoStandardIntentRegistration.RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder)
        {
            if (!builder.TryRegisterIntent(IntentId, _maxContributions,
                    new CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory>(_fingerprint), out var diagnostic))
                throw new InvalidOperationException(diagnostic.Message);
            return true;
        }

        bool ICoCoStandardIntentRegistration.TryRegisterHost(CoCoStateGraphHostBindingBuilder builder,
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

    internal static class CoCoStandardSingleIntent
    {
        private static readonly MethodInfo CreateRegistration = typeof(CoCoStandardSingleIntent)
            .GetMethod(nameof(CreateTyped), BindingFlags.Static | BindingFlags.NonPublic);

        internal static ICoCoStandardIntentRegistration For(Type payload) =>
            (ICoCoStandardIntentRegistration)CreateRegistration.MakeGenericMethod(payload).Invoke(null, null);

        private static ICoCoStandardIntentRegistration CreateTyped<T>()
        {
            CoCoIntentId.TryCreate(0x434F434F494E544EUL, CoCoStandardTypeIdentity.Hash(typeof(T)), out var id);
            return new CoCoStandardIntentRegistration<T, CoCoIntentPassThrough<T>, CoCoIntentPassThroughFactory<T>>(id, 1, 1);
        }

    }

    internal static class CoCoStandardTypeIdentity
    {
        internal static ulong Hash(Type type)
        {
            ulong hash = CoCoGraphCatalogHash.OffsetBasis;
            CoCoGraphCatalogHash.Add(ref hash, type);
            return hash;
        }
    }
}
