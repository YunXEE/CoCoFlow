using System;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    public interface IRuntimeSafetyFixtureObserver
    {
        void OnUpdate(
            CoCoStateExecutionContext context,
            RuntimeSafetyFixtureMemory memory);
    }

    public sealed class RuntimeSafetyFixtureMemory : CoCoActivationMemory
    {
        public int Value;
    }

    public sealed class RuntimeSafetyFixtureLogic : CoCoStateLogic, ICoCoStateUpdate
    {
        private readonly IRuntimeSafetyFixtureObserver _observer;

        public RuntimeSafetyFixtureLogic(IRuntimeSafetyFixtureObserver observer)
        {
            _observer = observer;
        }

        public void Update(CoCoStateExecutionContext context)
        {
            RuntimeSafetyFixtureMemory memory = context.Memory<RuntimeSafetyFixtureMemory>();
            memory.Value++;
            _observer.OnUpdate(context, memory);
        }
    }

    public sealed class RuntimeSafetyFixtureCondition :
        CoCoStateCondition,
        ICoCoStateConditionEvaluator
    {
        public bool Evaluate(CoCoConditionEvaluationContext context) => true;
    }

    public interface IRuntimeSafetyOperationFixtureObserver
    {
        void OnUpdate(
            CoCoStateId stateId,
            CoCoStateExecutionContext context,
            RuntimeSafetyFixtureMemory memory);
    }

    public sealed class RuntimeSafetyOperationFixtureLogic :
        CoCoStateLogic,
        ICoCoStateUpdate
    {
        private readonly IRuntimeSafetyOperationFixtureObserver _observer;
        private readonly CoCoStateId _stateId;

        public RuntimeSafetyOperationFixtureLogic(
            CoCoStateFactoryContext context,
            IRuntimeSafetyOperationFixtureObserver observer)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _stateId = context.StateId;
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public void Update(CoCoStateExecutionContext context)
        {
            RuntimeSafetyFixtureMemory memory = context.Memory<RuntimeSafetyFixtureMemory>();
            memory.Value++;
            _observer.OnUpdate(_stateId, context, memory);
        }
    }

    public interface IRuntimeSafetyOperationSection : ICoCoOperationSection
    {
        int Value { get; }
    }

    public sealed class RuntimeSafetyOperationSectionView : IRuntimeSafetyOperationSection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<int> _valueField;

        public RuntimeSafetyOperationSectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<int> valueField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _valueField = valueField;
        }

        public int Value => _reader.Read(_valueField);
    }

    public sealed class RuntimeSafetyOperationSectionViewFactory :
        ICoCoOperationSectionViewFactory<IRuntimeSafetyOperationSection>
    {
        public IRuntimeSafetyOperationSection Create(
            in CoCoOperationSectionViewContext<IRuntimeSafetyOperationSection> context)
        {
            if (!context.IsValid ||
                !context.TryGetField(0, out CoCoOperationSectionField<int> valueField))
            {
                throw new InvalidOperationException(
                    "Runtime safety Operation Section field could not be resolved.");
            }

            return new RuntimeSafetyOperationSectionView(context.Reader, valueField);
        }
    }
}
