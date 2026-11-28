using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.StateGraphLegacyCoreDependencyFixtures
{
    [Serializable]
    public sealed class LegacyCoreDependencyAuthoringConfig : StateConfig
    {
        public int Value;
    }

    public readonly struct LegacyCoreDependencyConfigSchema : IFrozenConfigSchema
    {
    }

    public static class LegacyCoreDependencySchemas
    {
        static LegacyCoreDependencySchemas()
        {
            var builder = new FrozenConfigSchemaBuilder<LegacyCoreDependencyConfigSchema>();
            if (!builder.TryFreeze(
                    out FrozenConfigSchema<LegacyCoreDependencyConfigSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Schema = schema;
        }

        public static readonly FrozenConfigSchema<LegacyCoreDependencyConfigSchema> Schema;
    }

    public sealed class LegacyCoreDependencyLogic : StateLogic
    {
        public static Type ReferencedLegacyCoreType => typeof(CoCoServices);
    }

    public sealed class LegacyCoreDependencyMemory : StateActivationMemory
    {
    }

    public sealed class LegacyCoreDependencyFreezer :
        IConfigFreezer<
            LegacyCoreDependencyAuthoringConfig,
            LegacyCoreDependencyConfigSchema>
    {
        public bool TryFreeze(
            LegacyCoreDependencyAuthoringConfig source,
            FrozenConfigWriter<LegacyCoreDependencyConfigSchema> writer,
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
