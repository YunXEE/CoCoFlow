using System;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    public interface IRuntimeSafetyFixtureObserver
    {
        void OnUpdate(
            StateExecutionContext context,
            RuntimeSafetyFixtureMemory memory);
    }

    public sealed class RuntimeSafetyFixtureMemory : ActivationMemory
    {
        public int Value;
    }

    public sealed class RuntimeSafetyFixtureLogic : StateLogic, IStateUpdate
    {
        private readonly IRuntimeSafetyFixtureObserver _observer;

        public RuntimeSafetyFixtureLogic(IRuntimeSafetyFixtureObserver observer)
        {
            _observer = observer;
        }

        public void Update(StateExecutionContext context)
        {
            RuntimeSafetyFixtureMemory memory = context.Memory<RuntimeSafetyFixtureMemory>();
            memory.Value++;
            _observer.OnUpdate(context, memory);
        }
    }

    public sealed class RuntimeSafetyFixtureCondition :
        StateCondition,
        IStateConditionEvaluator
    {
        public bool Evaluate(ConditionEvaluationContext context) => true;
    }

    public interface IRuntimeSafetyOperationFixtureObserver
    {
        void OnUpdate(
            StateId stateId,
            StateExecutionContext context,
            RuntimeSafetyFixtureMemory memory);
    }

    public sealed class RuntimeSafetyOperationFixtureLogic :
        StateLogic,
        IStateUpdate
    {
        private readonly IRuntimeSafetyOperationFixtureObserver _observer;
        private readonly StateId _stateId;

        public RuntimeSafetyOperationFixtureLogic(
            StateFactoryContext context,
            IRuntimeSafetyOperationFixtureObserver observer)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _stateId = context.StateId;
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public void Update(StateExecutionContext context)
        {
            RuntimeSafetyFixtureMemory memory = context.Memory<RuntimeSafetyFixtureMemory>();
            memory.Value++;
            _observer.OnUpdate(_stateId, context, memory);
        }
    }

    public interface IRuntimeSafetyOperationSection : IOperationSection
    {
        int Value { get; }
    }

    public sealed class RuntimeSafetyOperationSectionView : IRuntimeSafetyOperationSection
    {
        private readonly OperationSectionReader _reader;
        private readonly OperationSectionField<int> _valueField;

        public RuntimeSafetyOperationSectionView(
            OperationSectionReader reader,
            OperationSectionField<int> valueField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _valueField = valueField;
        }

        public int Value => _reader.Read(_valueField);
    }

    public sealed class RuntimeSafetyOperationSectionViewFactory :
        IOperationSectionViewFactory<IRuntimeSafetyOperationSection>
    {
        public IRuntimeSafetyOperationSection Create(
            in OperationSectionViewContext<IRuntimeSafetyOperationSection> context)
        {
            if (!context.IsValid ||
                !context.TryGetField(0, out OperationSectionField<int> valueField))
            {
                throw new InvalidOperationException(
                    "Runtime safety Operation Section field could not be resolved.");
            }

            return new RuntimeSafetyOperationSectionView(context.Reader, valueField);
        }
    }
}
