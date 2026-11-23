using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.StateGraphTransitiveDependencyHelper;

namespace CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor
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
        public static int ReadCoreViaHelper() =>
            TransitiveDependencyHelperToken.Create().Value;
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
