using System;
using CoCoFlow.Runtime.Core.StateGraph.Tests.TransitiveDependencyHelper;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.TransitiveDependencyAuthor
{
    [Serializable]
    public sealed class TransitiveDependencyAuthoringConfig : CoCoStateConfig
    {
        public int Value;
    }

    public readonly struct TransitiveDependencyConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public static class TransitiveDependencySchemas
    {
        static TransitiveDependencySchemas()
        {
            var builder = new CoCoFrozenConfigSchemaBuilder<TransitiveDependencyConfigSchema>();
            if (!builder.TryFreeze(
                    out CoCoFrozenConfigSchema<TransitiveDependencyConfigSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Schema = schema;
        }

        public static readonly CoCoFrozenConfigSchema<TransitiveDependencyConfigSchema> Schema;
    }

    public sealed class TransitiveDependencyLogic : CoCoStateLogic
    {
        public static TransitiveDependencyHelperToken HelperToken =>
            TransitiveDependencyHelperToken.Create();
    }

    public sealed class TransitiveDependencyMemory : CoCoActivationMemory
    {
    }

    public sealed class TransitiveDependencyFreezer :
        ICoCoConfigFreezer<
            TransitiveDependencyAuthoringConfig,
            TransitiveDependencyConfigSchema>
    {
        public bool TryFreeze(
            TransitiveDependencyAuthoringConfig source,
            CoCoFrozenConfigWriter<TransitiveDependencyConfigSchema> writer,
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
