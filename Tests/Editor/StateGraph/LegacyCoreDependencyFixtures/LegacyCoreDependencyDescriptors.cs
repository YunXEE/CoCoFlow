using System;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.LegacyCoreDependencyFixtures
{
    [Serializable]
    public sealed class LegacyCoreDependencyAuthoringConfig : CoCoStateConfig
    {
        public int Value;
    }

    public readonly struct LegacyCoreDependencyConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public static class LegacyCoreDependencySchemas
    {
        static LegacyCoreDependencySchemas()
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<LegacyCoreDependencyConfigSchema>();
            if (!builder.TryFreeze(
                    out CoCoFrozenConfigSchema<LegacyCoreDependencyConfigSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Schema = schema;
        }

        public static readonly CoCoFrozenConfigSchema<LegacyCoreDependencyConfigSchema> Schema;
    }

    public sealed class LegacyCoreDependencyLogic : CoCoStateLogic
    {
        public static Type ReferencedLegacyCoreType => typeof(CoCoServices);
    }

    public sealed class LegacyCoreDependencyMemory : CoCoActivationMemory
    {
    }

    public sealed class LegacyCoreDependencyFreezer :
        ICoCoConfigFreezer<
            LegacyCoreDependencyAuthoringConfig,
            LegacyCoreDependencyConfigSchema>
    {
        public bool TryFreeze(
            LegacyCoreDependencyAuthoringConfig source,
            CoCoFrozenConfigWriter<LegacyCoreDependencyConfigSchema> writer,
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
