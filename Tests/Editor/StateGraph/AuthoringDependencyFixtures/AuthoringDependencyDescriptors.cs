using System;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.AuthoringDependencyFixtures
{
    [Serializable]
    public sealed class AuthoringDependencyConfig : CoCoStateConfig
    {
        public int Value;
    }

    public readonly struct AuthoringDependencyConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public static class AuthoringDependencySchemas
    {
        static AuthoringDependencySchemas()
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<AuthoringDependencyConfigSchema>();
            if (!builder.TryFreeze(
                    out CoCoFrozenConfigSchema<AuthoringDependencyConfigSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Schema = schema;
        }

        public static readonly CoCoFrozenConfigSchema<AuthoringDependencyConfigSchema> Schema;
    }

    public sealed class AuthoringDependencyLogic : CoCoStateLogic
    {
        public static Type ReferencedAuthoringType => typeof(CoCoStateGraphCompilationCache);
    }

    public sealed class AuthoringDependencyMemory : CoCoActivationMemory
    {
    }

    public sealed class AuthoringDependencyFreezer :
        ICoCoConfigFreezer<AuthoringDependencyConfig, AuthoringDependencyConfigSchema>
    {
        public bool TryFreeze(
            AuthoringDependencyConfig source,
            CoCoFrozenConfigWriter<AuthoringDependencyConfigSchema> writer,
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
