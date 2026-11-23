using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;

namespace CoCoFlow.Tests.StateGraphAuthoringDependencyFixtures
{
    [Serializable]
    public sealed class AuthoringDependencyConfig : StateConfig
    {
        public int Value;
    }

    public readonly struct AuthoringDependencyConfigSchema : IFrozenConfigSchema
    {
    }

    public static class AuthoringDependencySchemas
    {
        static AuthoringDependencySchemas()
        {
            var builder = new FrozenConfigSchemaBuilder<AuthoringDependencyConfigSchema>();
            if (!builder.TryFreeze(
                    out FrozenConfigSchema<AuthoringDependencyConfigSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Schema = schema;
        }

        public static readonly FrozenConfigSchema<AuthoringDependencyConfigSchema> Schema;
    }

    public sealed class AuthoringDependencyLogic : StateLogic
    {
        public static Type ReferencedAuthoringType => typeof(StateGraphAssetCompiler);
    }

    public sealed class AuthoringDependencyMemory : ActivationMemory
    {
    }

    public sealed class AuthoringDependencyFreezer :
        IConfigFreezer<AuthoringDependencyConfig, AuthoringDependencyConfigSchema>
    {
        public bool TryFreeze(
            AuthoringDependencyConfig source,
            FrozenConfigWriter<AuthoringDependencyConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = source == null
                ? CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Config is required.")
                : CoCoDiagnostic.None;
            return source != null;
        }
    }
}
