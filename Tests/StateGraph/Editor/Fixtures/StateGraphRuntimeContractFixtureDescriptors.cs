using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    public enum RuntimeContractCallbackPhase
    {
        Enter = 1,
        Update = 2,
        Exit = 3
    }

    public interface IRuntimeContractObserver
    {
        bool ShouldRequest(TransitionId transitionId);
        double GetActionProgress(StateId stateId, double currentValue);
        void OnStateCallback(
            RuntimeContractCallbackPhase phase,
            StateId stateId,
            StateExecutionContext context);
    }

    public sealed class RuntimeContractMemory : StateActivationMemory
    {
        public int UpdateCount;
    }

    public abstract class RuntimeContractStateLogicBase : StateLogic
    {

        #region Public API

        protected RuntimeContractStateLogicBase(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            _stateId = context.StateId;
            _transitionCount = context.OutgoingTransitions.Count;
            if (_transitionCount > 2)
            {
                throw new InvalidOperationException(
                    "Runtime contract fixtures support at most two outgoing Transitions per State.");
            }

            if (_transitionCount > 0)
            {
                _firstTransition = context.OutgoingTransitions[0];
            }

            if (_transitionCount > 1)
            {
                _secondTransition = context.OutgoingTransitions[1];
            }
        }

        protected void Enter(StateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Enter, _stateId, context);
        }

        protected void UpdateState(StateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Update, _stateId, context);
            context.Memory<RuntimeContractMemory>().UpdateCount++;
            context.TrySetActionProgress(
                _observer.GetActionProgress(_stateId, context.ActionProgress));

            RequestIfSelected(context, _firstTransition, _transitionCount > 0);
            RequestIfSelected(context, _secondTransition, _transitionCount > 1);
        }

        protected void Exit(StateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Exit, _stateId, context);
        }

        #endregion

        #region Internal Logic

        private readonly IRuntimeContractObserver _observer;
        private readonly StateId _stateId;
        private readonly TransitionHandle _firstTransition;
        private readonly TransitionHandle _secondTransition;
        private readonly int _transitionCount;

        private void RequestIfSelected(
            StateExecutionContext context,
            TransitionHandle transition,
            bool exists)
        {
            if (exists && _observer.ShouldRequest(transition.TransitionId))
            {
                context.RequestTransition(transition);
            }
        }

        #endregion
    }

    public sealed class RuntimeContractAnyLogic : AnyStateLogic
    {

        #region Public API

        public RuntimeContractAnyLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
        {
            _update = new RuntimeContractUpdateOnlyLogic(context, observer);
        }

        public override void Update(StateExecutionContext context) => _update.Update(context);

        #endregion

        #region Internal Logic

        private readonly RuntimeContractUpdateOnlyLogic _update;

        #endregion
    }

    public sealed class RuntimeContractUpdateOnlyLogic :
        RuntimeContractStateLogicBase,
        IStateUpdate
    {
        public RuntimeContractUpdateOnlyLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void Update(StateExecutionContext context) => UpdateState(context);
    }

    public sealed class RuntimeContractEnterUpdateLogic :
        RuntimeContractStateLogicBase,
        IStateEnter,
        IStateUpdate
    {
        public RuntimeContractEnterUpdateLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void OnEnter(StateExecutionContext context) => Enter(context);
        public void Update(StateExecutionContext context) => UpdateState(context);
    }

    public sealed class RuntimeContractUpdateExitLogic :
        RuntimeContractStateLogicBase,
        IStateUpdate,
        IStateExit
    {
        public RuntimeContractUpdateExitLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void Update(StateExecutionContext context) => UpdateState(context);
        public void OnExit(StateExecutionContext context) => Exit(context);
    }

    public sealed class RuntimeContractEnterUpdateExitLogic :
        RuntimeContractStateLogicBase,
        IStateEnter,
        IStateUpdate,
        IStateExit
    {
        public RuntimeContractEnterUpdateExitLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void OnEnter(StateExecutionContext context) => Enter(context);
        public void Update(StateExecutionContext context) => UpdateState(context);
        public void OnExit(StateExecutionContext context) => Exit(context);
    }

    [Serializable]
    public sealed class RuntimeContractStateConfig : StateConfig
    {
        public int Value;
    }

    public readonly struct RuntimeContractStateConfigSchema : IFrozenConfigSchema
    {
    }

    public sealed class RuntimeContractStateConfigFreezer :
        IConfigFreezer<RuntimeContractStateConfig, RuntimeContractStateConfigSchema>
    {
        public bool TryFreeze(
            RuntimeContractStateConfig source,
            FrozenConfigWriter<RuntimeContractStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Runtime contract State config is required.");
                return false;
            }

            return writer.TryWrite(RuntimeContractSchemas.StateValue, source.Value, out diagnostic);
        }
    }

    public static class RuntimeContractSchemas
    {

        #region Public API

        public static readonly FrozenConfigField<RuntimeContractStateConfigSchema, int>
            StateValue;
        public static readonly FrozenConfigSchema<RuntimeContractStateConfigSchema> State;

        #endregion

        #region Internal Logic

        static RuntimeContractSchemas()
        {
            var stateBuilder = new FrozenConfigSchemaBuilder<RuntimeContractStateConfigSchema>();
            CoCoDiagnostic stateDiagnostic = CoCoDiagnostic.None;
            if (!FrozenConfigFieldId.TryCreate(
                    0xC04UL,
                    1UL,
                    out FrozenConfigFieldId stateId) ||
                !stateBuilder.TryAddField(stateId, out StateValue, out stateDiagnostic) ||
                !stateBuilder.TryFreeze(out State, out stateDiagnostic))
            {
                throw new InvalidOperationException(stateDiagnostic.Message);
            }

        }

        #endregion
    }

    public interface IRuntimeContractOperationSection : IOperation
    {
        int Value { get; }
    }

    public sealed class RuntimeContractOperationSectionView : IRuntimeContractOperationSection
    {
        private readonly OperationReader _reader;
        private readonly OperationField<int> _valueField;

        public int Value => _reader.Read(_valueField);

        #region Internal Logic

        internal RuntimeContractOperationSectionView(OperationReader reader,
            OperationField<int> valueField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _valueField = valueField;
        }

        #endregion
    }

    public sealed class RuntimeContractOperationSectionViewFactory :
        IOperationViewFactory<IRuntimeContractOperationSection>
    {
        IRuntimeContractOperationSection IOperationViewFactory<IRuntimeContractOperationSection>.Create(
            in OperationViewContext<IRuntimeContractOperationSection> context)
        {
            if (!context.IsValid ||
                !context.TryGetField(0, out OperationField<int> valueField))
            {
                throw new InvalidOperationException(
                    "Runtime contract Operation Section field could not be resolved.");
            }

            return new RuntimeContractOperationSectionView(context.Reader, valueField);
        }
    }
}
