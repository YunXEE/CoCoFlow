using System;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>A standard discovery failure with a stable diagnostic code.</summary>
    public sealed class CoCoStandardBindingException : InvalidOperationException
    {
        public CoCoStandardBindingException(CoCoDiagnostic diagnostic)
            : base(diagnostic.Code + ": " + diagnostic.Message) => Diagnostic = diagnostic;
        public CoCoDiagnostic Diagnostic { get; }
    }

    /// <summary>
    /// One module-owned Intent type and identity, shared by catalog compilation
    /// and Host binding. Standard declarations have one identity per value type.
    /// Implementations must not store per-Host handles in the registrar.
    /// </summary>
    public interface ICoCoStandardIntentRegistrar
    {
        Type IntentType { get; }
        CoCoIntentId IntentId { get; }
        bool TryRegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder,
            out CoCoDiagnostic diagnostic);
        bool TryRegisterRuntime(CoCoStateGraphHostBindingBuilder builder,
            out ICoCoStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic);
    }

    /// <summary>A binding created for exactly one Host, after all reducers register.</summary>
    public interface ICoCoStandardIntentSourceBinding
    {
        bool TryBind(out CoCoDiagnostic diagnostic);
    }

    /// <summary>
    /// Standard registrar for one explicit source and a module-owned reducer.
    /// Derive a public parameterless registrar, annotate it with
    /// CoCoIntentRegistration, and supply a new factory for each Host.
    /// </summary>
    public abstract class CoCoStandardIntentRegistrar<TIntent, TReducer, TFactory> :
        ICoCoStandardIntentRegistrar
        where TIntent : unmanaged
        where TReducer : unmanaged, ICoCoIntentReducer<TIntent>
        where TFactory : ICoCoIntentReducerFactory<TIntent, TReducer>
    {
        protected CoCoStandardIntentRegistrar(CoCoIntentId intentId,
            int maxContributions, ulong semanticFingerprint)
        {
            if (!intentId.IsValid || maxContributions <= 0 || semanticFingerprint == 0UL)
                throw new ArgumentException("Intent id, capacity and fingerprint must be valid.");
            IntentId = intentId;
            MaxContributions = maxContributions;
            SemanticFingerprint = semanticFingerprint;
        }

        public Type IntentType => typeof(TIntent);
        public CoCoIntentId IntentId { get; }
        public int MaxContributions { get; }
        public ulong SemanticFingerprint { get; }
        protected abstract TFactory CreateReducerFactory();

        public bool TryRegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder,
            out CoCoDiagnostic diagnostic) => builder.TryRegisterIntent(
                IntentId, MaxContributions,
                new CoCoIntentReducerFactoryToken<TIntent, TReducer, TFactory>(SemanticFingerprint),
                out diagnostic);

        public bool TryRegisterRuntime(CoCoStateGraphHostBindingBuilder builder,
            out ICoCoStandardIntentSourceBinding binding, out CoCoDiagnostic diagnostic)
        {
            binding = null;
            if (!builder.TryRegisterIntent<TIntent, TReducer, TFactory>(
                    IntentId, CreateReducerFactory(), SemanticFingerprint,
                    out CoCoIntentHandle<TIntent> handle, out diagnostic))
                return false;
            binding = new SourceBinding(builder, handle);
            return true;
        }

        private sealed class SourceBinding : ICoCoStandardIntentSourceBinding
        {
            private readonly CoCoStateGraphHostBindingBuilder _builder;
            private readonly CoCoIntentHandle<TIntent> _handle;

            public SourceBinding(CoCoStateGraphHostBindingBuilder builder, CoCoIntentHandle<TIntent> handle)
            {
                _builder = builder;
                _handle = handle;
            }

            public bool TryBind(out CoCoDiagnostic diagnostic) =>
                _builder.TryBindStandardIntentSource(_handle, out diagnostic);
        }
    }
}
