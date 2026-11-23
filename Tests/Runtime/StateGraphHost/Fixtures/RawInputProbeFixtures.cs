using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

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
        public static bool RejectAfterReading;
    }

    public struct RawInputProbeReducer : IIntentReducer<InputIntentData>
    {
        public InputIntentData Reduce(
            in InputIntentData current,
            in InputIntentData candidate) => candidate;
    }

    public sealed class RawInputProbeReducerFactory :
        IIntentReducerFactory<InputIntentData, RawInputProbeReducer>
    {
        public RawInputProbeReducer Create(
            GraphInstanceId graphInstanceId) => default;
    }

    public sealed class RawInputProbeLogic :
        StateLogic,
        IStateUpdate
    {
        private readonly IntentHandle<InputIntentData> _intent;

        public RawInputProbeLogic(
            StateFactoryContext context,
            IntentHandle<InputIntentData> intent)
        {
            _intent = intent;
        }

        public void Update(StateExecutionContext context)
        {
            if (_intent.IsValid &&
                context.Intents != null &&
                context.Intents.TryGet(_intent, out InputIntentData intent))
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
                if (RawInputProbeCapture.RejectAfterReading)
                    throw new System.InvalidOperationException("Input probe rejects this sampled tick.");

            }
        }
    }

    [System.Serializable]
    public sealed class RawInputProbeConfig : StateConfig { }

    public readonly struct RawInputProbeConfigSchema : IFrozenConfigSchema { }

    public sealed class RawInputProbeConfigFreezer :
        IConfigFreezer<RawInputProbeConfig, RawInputProbeConfigSchema>
    {
        public bool TryFreeze(
            RawInputProbeConfig source,
            FrozenConfigWriter<RawInputProbeConfigSchema> writer,
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
                new FrozenConfigSchemaBuilder<RawInputProbeConfigSchema>();
            _ = builder.TryFreeze(
                out FrozenConfigSchema<RawInputProbeConfigSchema> schema,
                out _);
            State = schema;
        }

        public static readonly FrozenConfigSchema<RawInputProbeConfigSchema> State;
    }

    public sealed class RawInputProbeMemory : ActivationMemory { }

    public sealed class RawInputProbeMemoryBinding :
        IActivationMemoryStateBinding<RawInputProbeMemory, int>
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
