using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures
{
    [Serializable]
    public sealed class HostTestStateConfig : CoCoStateConfig
    {
        public int Value;
    }

    public readonly struct HostTestStateConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public sealed class HostTestStateConfigFreezer :
        ICoCoConfigFreezer<HostTestStateConfig, HostTestStateConfigSchema>
    {
        public bool TryFreeze(
            HostTestStateConfig source,
            CoCoFrozenConfigWriter<HostTestStateConfigSchema> writer,
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
            var builder = new CoCoFrozenConfigSchemaBuilder<HostTestStateConfigSchema>();
            CoCoFrozenConfigFieldId.TryCreate(1UL, 1UL, out CoCoFrozenConfigFieldId id);
            if (!builder.TryAddField(
                    id,
                    out CoCoFrozenConfigField<HostTestStateConfigSchema, int> field,
                    out CoCoDiagnostic diagnostic) ||
                !builder.TryFreeze(
                    out CoCoFrozenConfigSchema<HostTestStateConfigSchema> schema,
                    out diagnostic))
            {
                throw new InvalidOperationException(diagnostic.Message);
            }

            Value = field;
            State = schema;
        }

        public static readonly CoCoFrozenConfigField<HostTestStateConfigSchema, int> Value;
        public static readonly CoCoFrozenConfigSchema<HostTestStateConfigSchema> State;
    }

    public sealed class HostTestMemory : CoCoActivationMemory
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

    public struct HostTestIntentReducer : ICoCoIntentReducer<HostTestIntent>
    {
        public HostTestIntent Reduce(
            in HostTestIntent current,
            in HostTestIntent candidate) => candidate;
    }

    public sealed class HostTestIntentReducerFactory :
        ICoCoIntentReducerFactory<HostTestIntent, HostTestIntentReducer>
    {
        public HostTestIntentReducer Create(CoCoGraphInstanceId graphInstanceId) => default;
    }

    public sealed class HostTestEventAdapter :
        ICoCoEventToIntentAdapter<HostTestEvent, HostTestIntent>
    {
        public static int ProjectionCount { get; private set; }

        public static void Reset()
        {
            ProjectionCount = 0;
        }

        public bool TryProject(
            in CoCoEventPacket<HostTestEvent> packet,
            out HostTestIntent intent)
        {
            ProjectionCount++;
            intent = new HostTestIntent { Value = packet.Payload.Value };
            return true;
        }
    }

    public interface IHostTestDiscreteSection : ICoCoOperationSection
    {
        int Value { get; }
    }

    public sealed class HostTestDiscreteSectionView : IHostTestDiscreteSection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<int> _valueField;

        public HostTestDiscreteSectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<int> valueField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _valueField = valueField;
        }

        public int Value => _reader.Read(_valueField);
    }

    public sealed class HostTestDiscreteSectionViewFactory :
        ICoCoOperationSectionViewFactory<IHostTestDiscreteSection>
    {
        public CoCoOperationSectionHandle<IHostTestDiscreteSection> Handle { get; private set; }
        public CoCoOperationSectionField<int> ValueField { get; private set; }

        public IHostTestDiscreteSection Create(
            in CoCoOperationSectionViewContext<IHostTestDiscreteSection> context)
        {
            if (!context.IsValid ||
                !context.TryGetField(0, out CoCoOperationSectionField<int> valueField))
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
        CoCoStateLogic,
        ICoCoStateEnter,
        ICoCoStateUpdate,
        ICoCoStateExit
    {
        private static readonly Dictionary<CoCoGraphInstanceId, int> IntentByInstance =
            new Dictionary<CoCoGraphInstanceId, int>();
        private static readonly Dictionary<CoCoGraphInstanceId, int> UpdatesByInstance =
            new Dictionary<CoCoGraphInstanceId, int>();
        private static readonly Dictionary<CoCoGraphInstanceId, int> MemoryByInstance =
            new Dictionary<CoCoGraphInstanceId, int>();
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoIntentHandle<HostTestIntent> _intent;
        private readonly CoCoOperationSectionHandle<IHostTestDiscreteSection> _operationHandle;
        private readonly CoCoOperationSectionField<int> _operationField;

        public HostTestLogic()
        {
        }

        public HostTestLogic(
            CoCoGraphInstanceId graphInstanceId,
            CoCoIntentHandle<HostTestIntent> intent = default,
            CoCoOperationSectionHandle<IHostTestDiscreteSection> operationHandle = default,
            CoCoOperationSectionField<int> operationField = default)
        {
            _graphInstanceId = graphInstanceId;
            _intent = intent;
            _operationHandle = operationHandle;
            _operationField = operationField;
        }

        public static int EnterCount { get; private set; }
        public static int UpdateCount { get; private set; }
        public static int ExitCount { get; private set; }
        public static int LastIntentValue { get; private set; }

        public static void Reset()
        {
            EnterCount = 0;
            UpdateCount = 0;
            ExitCount = 0;
            LastIntentValue = 0;
            IntentByInstance.Clear();
            UpdatesByInstance.Clear();
            MemoryByInstance.Clear();
        }

        public static int GetLastIntent(CoCoGraphInstanceId graphInstanceId) =>
            IntentByInstance.TryGetValue(graphInstanceId, out int value) ? value : 0;

        public static int GetUpdateCount(CoCoGraphInstanceId graphInstanceId) =>
            UpdatesByInstance.TryGetValue(graphInstanceId, out int value) ? value : 0;

        public static int GetMemoryValue(CoCoGraphInstanceId graphInstanceId) =>
            MemoryByInstance.TryGetValue(graphInstanceId, out int value) ? value : 0;

        public void OnEnter(CoCoStateExecutionContext context)
        {
            EnterCount++;
        }

        public void Update(CoCoStateExecutionContext context)
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
            if (_intent.IsValid &&
                context.Intents != null &&
                context.Intents.TryGet(_intent, out HostTestIntent intent))
            {
                LastIntentValue = intent.Value;
                if (_graphInstanceId.IsValid)
                {
                    IntentByInstance[_graphInstanceId] = intent.Value;
                }
            }
        }

        public void OnExit(CoCoStateExecutionContext context)
        {
            ExitCount++;
        }
    }

    public sealed class HostTestMismatchedLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        public void Update(CoCoStateExecutionContext context)
        {
        }
    }
}
