using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    /// <summary>
    /// C4 raw-input end-to-end probe graph types. Lives in the engine-free
    /// fixtures assembly (graph authoring assemblies must not reference
    /// Unity, Editor, or Module assemblies).
    /// </summary>
    public sealed class RawInputProbeCapture
    {
        public static List<RawInputRecord> Records;
    }

    public struct RawInputProbeReducer : ICoCoIntentReducer<RawInputIntent>
    {
        public RawInputIntent Reduce(
            in RawInputIntent current,
            in RawInputIntent candidate) => candidate;
    }

    public sealed class RawInputProbeReducerFactory :
        ICoCoIntentReducerFactory<RawInputIntent, RawInputProbeReducer>
    {
        public RawInputProbeReducer Create(
            CoCoGraphInstanceId graphInstanceId) => default;
    }

    public sealed class RawInputProbeLogic :
        CoCoStateLogic,
        ICoCoStateUpdate
    {
        private readonly CoCoIntentHandle<RawInputIntent> _intent;

        public RawInputProbeLogic(
            CoCoStateFactoryContext context,
            CoCoIntentHandle<RawInputIntent> intent)
        {
            _intent = intent;
        }

        public void Update(CoCoStateExecutionContext context)
        {
            if (_intent.IsValid &&
                context.Intents != null &&
                context.Intents.TryGet(_intent, out RawInputIntent intent))
            {
                if (RawInputProbeCapture.Records == null)
                {
                    return;
                }

                for (int index = 0; index < intent.Count; index++)
                {
                    if (intent.TryGet(index, out RawInputRecord record))
                    {
                        RawInputProbeCapture.Records.Add(record);
                    }
                }
            }
        }
    }

    [System.Serializable]
    public sealed class RawInputProbeConfig : CoCoStateConfig { }

    public readonly struct RawInputProbeConfigSchema : ICoCoFrozenConfigSchema { }

    public sealed class RawInputProbeConfigFreezer :
        ICoCoConfigFreezer<RawInputProbeConfig, RawInputProbeConfigSchema>
    {
        public bool TryFreeze(
            RawInputProbeConfig source,
            CoCoFrozenConfigWriter<RawInputProbeConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            diagnostic = source != null
                ? CoCoDiagnostic.None
                : CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "probe config required");
            return source != null;
        }
    }

    public static class RawInputProbeSchemas
    {
        static RawInputProbeSchemas()
        {
            var builder =
                new CoCoFrozenConfigSchemaBuilder<RawInputProbeConfigSchema>();
            _ = builder.TryFreeze(
                out CoCoFrozenConfigSchema<RawInputProbeConfigSchema> schema,
                out _);
            State = schema;
        }

        public static readonly CoCoFrozenConfigSchema<RawInputProbeConfigSchema> State;
    }

    public sealed class RawInputProbeMemory : CoCoActivationMemory { }

    public sealed class RawInputProbeMemoryBinding :
        ICoCoActivationMemoryStateBinding<RawInputProbeMemory, int>
    {
        public const ulong Fingerprint = 51457UL;

        public ulong SemanticFingerprint => Fingerprint;

        public bool TryCapture(
            RawInputProbeMemory memory,
            out int state,
            out CoCoDiagnostic diagnostic)
        {
            if (memory == null)
            {
                state = default;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Probe memory is required for Graph State capture.");
                return false;
            }

            state = 0;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryPrepareRestore(
            in int state,
            RawInputProbeMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (candidateMemory == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Probe candidate memory is required for restore.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }
}
