using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    [Serializable]
    public sealed class HostTestStateConfig : StateConfig
    {
        public int Value;
    }

    public readonly struct HostTestStateConfigSchema : IFrozenConfigSchema
    {
    }

    public sealed class HostTestStateConfigFreezer :
        IConfigFreezer<HostTestStateConfig, HostTestStateConfigSchema>
    {
        public bool TryFreeze(
            HostTestStateConfig source,
            FrozenConfigWriter<HostTestStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Host test config is required.");
                return false;
            }

            return writer.TryWrite(HostTestSchemas.Value, source.Value, out diagnostic);
        }
    }

    public static class HostTestSchemas
    {
        static HostTestSchemas()
        {
            var builder = new FrozenConfigSchemaBuilder<HostTestStateConfigSchema>();
            FrozenConfigFieldId.TryCreate(1UL, 1UL, out FrozenConfigFieldId id);
            if (!builder.TryAddField(
                    id,
                    out FrozenConfigField<HostTestStateConfigSchema, int> field,
                    out CoCoDiagnostic diagnostic) ||
                !builder.TryFreeze(
                    out FrozenConfigSchema<HostTestStateConfigSchema> schema,
                    out diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Value = field;
            State = schema;
        }

        public static readonly FrozenConfigField<HostTestStateConfigSchema, int> Value;
        public static readonly FrozenConfigSchema<HostTestStateConfigSchema> State;
    }

    public sealed class HostTestMemory : ActivationMemory
    {
        public int Value;
    }

    public struct HostTestIntent
    {
        public int Value;
    }

    public struct HostTestEvent
    {
        public int Value;
    }

    public struct HostTestIntentReducer : IIntentReducer<HostTestIntent>
    {
        public HostTestIntent Reduce(
            in HostTestIntent current,
            in HostTestIntent candidate) => candidate;
    }

    public sealed class HostTestIntentReducerFactory :
        IIntentReducerFactory<HostTestIntent, HostTestIntentReducer>
    {
        public HostTestIntentReducer Create(GraphInstanceId graphInstanceId) => default;
    }

    public interface IHostTestDiscreteSection : IOperationSection
    {
        int Value { get; }
    }

    public sealed class HostTestDiscreteSectionView : IHostTestDiscreteSection
    {
        private readonly OperationSectionReader _reader;
        private readonly OperationSectionField<int> _valueField;

        public HostTestDiscreteSectionView(
            OperationSectionReader reader,
            OperationSectionField<int> valueField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _valueField = valueField;
        }

        public int Value => _reader.Read(_valueField);
    }

    public sealed class HostTestDiscreteSectionViewFactory :
        IOperationSectionViewFactory<IHostTestDiscreteSection>
    {
        public OperationSectionHandle<IHostTestDiscreteSection> Handle { get; private set; }
        public OperationSectionField<int> ValueField { get; private set; }

        public IHostTestDiscreteSection Create(
            in OperationSectionViewContext<IHostTestDiscreteSection> context)
        {
            if (!context.IsValid ||
                !context.TryGetField(0, out OperationSectionField<int> valueField))
            {
                throw new InvalidOperationException(
                    "Host test Discrete Section field could not be resolved.");
            }

            Handle = context.Handle;
            ValueField = valueField;
            return new HostTestDiscreteSectionView(context.Reader, valueField);
        }
    }

    public sealed class HostTestLogic :
        StateLogic,
        IStateEnter,
        IStateUpdate,
        IStateExit
    {
        private static readonly Dictionary<GraphInstanceId, int> EventByInstance =
            new Dictionary<GraphInstanceId, int>();
        private static readonly Dictionary<GraphInstanceId, int> UpdatesByInstance =
            new Dictionary<GraphInstanceId, int>();
        private static readonly Dictionary<GraphInstanceId, int> MemoryByInstance =
            new Dictionary<GraphInstanceId, int>();
        private readonly GraphInstanceId _graphInstanceId;
        private readonly OperationSectionHandle<IHostTestDiscreteSection> _operationHandle;
        private readonly OperationSectionField<int> _operationField;

        public HostTestLogic()
        {
        }

        public HostTestLogic(
            GraphInstanceId graphInstanceId,
            OperationSectionHandle<IHostTestDiscreteSection> operationHandle = default,
            OperationSectionField<int> operationField = default)
        {
            _graphInstanceId = graphInstanceId;
            _operationHandle = operationHandle;
            _operationField = operationField;
        }

        public static int EnterCount { get; private set; }
        public static int UpdateCount { get; private set; }
        public static int ExitCount { get; private set; }
        public static int EventCount { get; private set; }
        public static int LastEventValue { get; private set; }
        public static Action UpdateCallback { get; set; }
        public static Action MemoryFingerprintCallback { get; set; }

        public static void Reset()
        {
            EnterCount = 0;
            UpdateCount = 0;
            ExitCount = 0;
            EventCount = 0;
            LastEventValue = 0;
            UpdateCallback = null;
            MemoryFingerprintCallback = null;
            EventByInstance.Clear();
            UpdatesByInstance.Clear();
            MemoryByInstance.Clear();
        }

        public static int GetLastEvent(GraphInstanceId graphInstanceId) =>
            EventByInstance.TryGetValue(graphInstanceId, out int value) ? value : 0;

        public static int GetUpdateCount(GraphInstanceId graphInstanceId) =>
            UpdatesByInstance.TryGetValue(graphInstanceId, out int value) ? value : 0;

        public static int GetMemoryValue(GraphInstanceId graphInstanceId) =>
            MemoryByInstance.TryGetValue(graphInstanceId, out int value) ? value : 0;

        public static ulong GetMemoryFingerprint(HostTestMemory memory)
        {
            MemoryFingerprintCallback?.Invoke();
            return unchecked((ulong)(uint)memory.Value);
        }

        public void OnEnter(StateExecutionContext context)
        {
            EnterCount++;
        }

        public void Update(StateExecutionContext context)
        {
            UpdateCount++;
            if (_graphInstanceId.IsValid)
            {
                UpdatesByInstance.TryGetValue(_graphInstanceId, out int count);
                UpdatesByInstance[_graphInstanceId] = count + 1;
            }

            HostTestMemory memory = context.Memory<HostTestMemory>();
            memory.Value++;
            if (_operationHandle.IsValid && _operationField.IsValid)
            {
                context.Operations.Write(_operationField, memory.Value);
                context.Operations.EnableDiscrete(_operationHandle);
            }

            if (_graphInstanceId.IsValid)
            {
                MemoryByInstance[_graphInstanceId] = memory.Value;
            }
            if (context.Inbox.TryGetBatch<HostTestEvent>(out var batch))
            {
                for (int index = 0; batch.TryRead(index, out HostTestEvent payload); index++)
                {
                    EventCount++;
                    LastEventValue = payload.Value;
                    if (_graphInstanceId.IsValid)
                        EventByInstance[_graphInstanceId] = payload.Value;
                }
            }

            UpdateCallback?.Invoke();
        }

        public void OnExit(StateExecutionContext context)
        {
            ExitCount++;
        }
    }

    [State(nameof(HostTestMissingUpdateLogic))]
    public sealed class HostTestMissingUpdateLogic : StateLogic
    {
    }

    [State(nameof(HostTestStartupLogic))]
    public sealed class HostTestStartupLogic : StateLogic, IStateUpdate
    {
        public void Update(StateExecutionContext context)
        {
        }
    }

    public sealed class HostTestMismatchedLogic : StateLogic, IStateUpdate
    {
        public void Update(StateExecutionContext context)
        {
        }
    }
}
