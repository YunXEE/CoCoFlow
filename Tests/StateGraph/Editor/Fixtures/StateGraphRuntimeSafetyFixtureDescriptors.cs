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

    public sealed class RuntimeSafetyFixtureMemory : StateActivationMemory
    {
        public int Value;
    }

    public sealed class RuntimeSafetyFixtureLogic : StateLogic, IStateUpdate
    {

        #region Public API

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

        #endregion

        #region Internal Logic

        private readonly IRuntimeSafetyFixtureObserver _observer;

        #endregion
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

        #region Public API

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

        #endregion

        #region Internal Logic

        private readonly IRuntimeSafetyOperationFixtureObserver _observer;
        private readonly StateId _stateId;

        #endregion
    }

    public interface IRuntimeSafetyOperationSection : IOperation
    {
        int Value { get; }
    }

    public sealed class RuntimeSafetyOperationSectionView : IRuntimeSafetyOperationSection
    {
        private readonly OperationReader _reader;
        private readonly OperationField<int> _valueField;

        public int Value => _reader.Read(_valueField);

        #region Internal Logic

        internal RuntimeSafetyOperationSectionView(OperationReader reader,
            OperationField<int> valueField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _valueField = valueField;
        }

        #endregion
    }

    public sealed class RuntimeSafetyOperationSectionViewFactory :
        IOperationViewFactory<IRuntimeSafetyOperationSection>
    {
        IRuntimeSafetyOperationSection IOperationViewFactory<IRuntimeSafetyOperationSection>.Create(
            in OperationViewContext<IRuntimeSafetyOperationSection> context)
        {
            if (!context.IsValid ||
                !context.TryGetField(0, out OperationField<int> valueField))
            {
                throw new InvalidOperationException(
                    "Runtime safety Operation Section field could not be resolved.");
            }

            return new RuntimeSafetyOperationSectionView(context.Reader, valueField);
        }
    }
}
