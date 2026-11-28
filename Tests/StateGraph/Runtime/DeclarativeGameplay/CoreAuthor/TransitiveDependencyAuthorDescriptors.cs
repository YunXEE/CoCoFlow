using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Tests.StateGraphTransitiveDependencyHelper;

namespace CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor
{
    [Serializable]
    public sealed class TransitiveDependencyAuthoringConfig : StateConfig
    {
        public int Value;
    }

    public readonly struct TransitiveDependencyConfigSchema : IFrozenConfigSchema
    {
    }

    public static class TransitiveDependencySchemas
    {
        static TransitiveDependencySchemas()
        {
            var builder = new FrozenConfigSchemaBuilder<TransitiveDependencyConfigSchema>();
            if (!builder.TryFreeze(
                    out FrozenConfigSchema<TransitiveDependencyConfigSchema> schema,
                    out CoCoDiagnostic diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Schema = schema;
        }

        public static readonly FrozenConfigSchema<TransitiveDependencyConfigSchema> Schema;
    }

    public sealed class TransitiveDependencyLogic : StateLogic
    {
        public static int ReadCoreViaHelper() =>
            TransitiveDependencyHelperToken.Create().Value;
    }

    public sealed class TransitiveDependencyMemory : StateActivationMemory
    {
    }

    public sealed class TransitiveDependencyFreezer :
        IConfigFreezer<
            TransitiveDependencyAuthoringConfig,
            TransitiveDependencyConfigSchema>
    {
        public bool TryFreeze(
            TransitiveDependencyAuthoringConfig source,
            FrozenConfigWriter<TransitiveDependencyConfigSchema> writer,
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
