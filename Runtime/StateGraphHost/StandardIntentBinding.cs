using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Runtime.StateGraphHost
{
    internal interface IStandardIntentRegistration
    {
        Type IntentType { get; }
        IntentId IntentId { get; }
        bool RegisterCatalog(GraphDescriptorCatalogBuilder builder);
        bool TryRegisterHost(StateGraphHostBindingBuilder builder,
            out IStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic);
    }

    internal interface IStandardIntentSourceBinding
    {
        bool Accepts(MonoBehaviour component);
        bool TryBind(StateGraphHostBindingBuilder builder,
            int hostSourceIndex, out CoCoDiagnostic diagnostic);
    }

    /// <summary>Immutable module registration; source handles belong to each Host.</summary>
    public class StandardIntentRegistration<TIntent, TReducer, TFactory> :
        IStandardIntentRegistration
        where TReducer : unmanaged, IIntentReducer<TIntent>
        where TFactory : IIntentReducerFactory<TIntent, TReducer>, new()
    {
        private readonly int _maxContributions;
        private readonly ulong _fingerprint;
        public Type IntentType => typeof(TIntent);
        public IntentId IntentId { get; }

        public StandardIntentRegistration(IntentId intentId,
            int maxContributions, ulong semanticFingerprint)
        {
            IntentId = intentId;
            _maxContributions = maxContributions;
            _fingerprint = semanticFingerprint;
        }

        bool IStandardIntentRegistration.RegisterCatalog(GraphDescriptorCatalogBuilder builder)
        {
            if (!builder.TryRegisterIntent(IntentId, _maxContributions,
                    new IntentReducerFactoryToken<TIntent, TReducer, TFactory>(_fingerprint), out var diagnostic))
                throw new InvalidOperationException(diagnostic.Message);
            return true;
        }

        bool IStandardIntentRegistration.TryRegisterHost(StateGraphHostBindingBuilder builder,
            out IStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic)
        {
            binding = null;
            if (!builder.TryRegisterIntent<TIntent, TReducer, TFactory>(IntentId,
                    new TFactory(), _fingerprint, out var handle, out diagnostic)) return false;
            binding = new SourceBinding(handle);
            return true;
        }

        private sealed class SourceBinding : IStandardIntentSourceBinding
        {
            private readonly IntentHandle<TIntent> _handle;
            public SourceBinding(IntentHandle<TIntent> handle) { _handle = handle; }
            public bool Accepts(MonoBehaviour component) =>
                component != null && component is IIntentFrameSource<TIntent>;

            public bool TryBind(StateGraphHostBindingBuilder builder,
                int hostSourceIndex, out CoCoDiagnostic diagnostic)
            {
                if (!IntentSourceRequirement<TIntent>.TryCreate(_handle, 1, out var source))
                {
                    diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry,
                        CoCoDiagnosticCode.MissingIntentReducer, "Intent source requirement is invalid.");
                    return false;
                }
                return builder.TryBindIntentSource(hostSourceIndex, source, out diagnostic);
            }
        }
    }

    internal static class StandardSingleIntent
    {
        private static readonly MethodInfo CreateRegistration = typeof(StandardSingleIntent)
            .GetMethod(nameof(CreateTyped), BindingFlags.Static | BindingFlags.NonPublic);

        internal static IStandardIntentRegistration For(Type payload) =>
            (IStandardIntentRegistration)CreateRegistration.MakeGenericMethod(payload).Invoke(null, null);

        private static IStandardIntentRegistration CreateTyped<T>()
        {
            IntentId.TryCreate(0x434F434F494E544EUL, StandardTypeIdentity.Hash(typeof(T)), out var id);
            return new StandardIntentRegistration<T, IntentPassThrough<T>, IntentPassThroughFactory<T>>(id, 1, 1);
        }

    }

    internal static class StandardTypeIdentity
    {
        internal static ulong Hash(Type type)
        {
            ulong hash = GraphCatalogHash.OffsetBasis;
            GraphCatalogHash.Add(ref hash, type);
            return hash;
        }
    }
}
