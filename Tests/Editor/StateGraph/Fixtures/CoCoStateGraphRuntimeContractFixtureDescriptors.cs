using System;

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
        bool ShouldRequest(CoCoTransitionId transitionId);
        double GetActionProgress(CoCoStateId stateId, double currentValue);
        bool EvaluateCondition(
            CoCoTransitionId transitionId,
            CoCoConditionEvaluationContext context);
        void OnStateCallback(
            RuntimeContractCallbackPhase phase,
            CoCoStateId stateId,
            CoCoStateExecutionContext context);
    }

    public sealed class RuntimeContractMemory : CoCoActivationMemory
    {
        public int UpdateCount;
    }

    public abstract class RuntimeContractStateLogicBase : CoCoStateLogic
    {
        private readonly IRuntimeContractObserver _observer;
        private readonly CoCoStateId _stateId;
        private readonly CoCoTransitionHandle _firstTransition;
        private readonly CoCoTransitionHandle _secondTransition;
        private readonly int _transitionCount;

        protected RuntimeContractStateLogicBase(
            CoCoStateFactoryContext context,
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

        protected void Enter(CoCoStateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Enter, _stateId, context);
        }

        protected void UpdateState(CoCoStateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Update, _stateId, context);
            context.Memory<RuntimeContractMemory>().UpdateCount++;
            context.TrySetActionProgress(
                _observer.GetActionProgress(_stateId, context.ActionProgress));

            RequestIfSelected(context, _firstTransition, _transitionCount > 0);
            RequestIfSelected(context, _secondTransition, _transitionCount > 1);
        }

        protected void Exit(CoCoStateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Exit, _stateId, context);
        }

        private void RequestIfSelected(
            CoCoStateExecutionContext context,
            CoCoTransitionHandle transition,
            bool exists)
        {
            if (exists && _observer.ShouldRequest(transition.TransitionId))
            {
                context.RequestTransition(transition);
            }
        }
    }

    public sealed class RuntimeContractUpdateOnlyLogic :
        RuntimeContractStateLogicBase,
        ICoCoStateUpdate
    {
        public RuntimeContractUpdateOnlyLogic(
            CoCoStateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void Update(CoCoStateExecutionContext context) => UpdateState(context);
    }

    public sealed class RuntimeContractEnterUpdateLogic :
        RuntimeContractStateLogicBase,
        ICoCoStateEnter,
        ICoCoStateUpdate
    {
        public RuntimeContractEnterUpdateLogic(
            CoCoStateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void OnEnter(CoCoStateExecutionContext context) => Enter(context);
        public void Update(CoCoStateExecutionContext context) => UpdateState(context);
    }

    public sealed class RuntimeContractUpdateExitLogic :
        RuntimeContractStateLogicBase,
        ICoCoStateUpdate,
        ICoCoStateExit
    {
        public RuntimeContractUpdateExitLogic(
            CoCoStateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void Update(CoCoStateExecutionContext context) => UpdateState(context);
        public void OnExit(CoCoStateExecutionContext context) => Exit(context);
    }

    public sealed class RuntimeContractEnterUpdateExitLogic :
        RuntimeContractStateLogicBase,
        ICoCoStateEnter,
        ICoCoStateUpdate,
        ICoCoStateExit
    {
        public RuntimeContractEnterUpdateExitLogic(
            CoCoStateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        public void OnEnter(CoCoStateExecutionContext context) => Enter(context);
        public void Update(CoCoStateExecutionContext context) => UpdateState(context);
        public void OnExit(CoCoStateExecutionContext context) => Exit(context);
    }

    public sealed class RuntimeContractCondition :
        CoCoStateCondition,
        ICoCoStateConditionEvaluator
    {
        private readonly IRuntimeContractObserver _observer;
        private readonly CoCoTransitionId _transitionId;

        public RuntimeContractCondition(
            CoCoConditionFactoryContext context,
            IRuntimeContractObserver observer)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            _transitionId = context.TransitionId;
        }

        public bool Evaluate(CoCoConditionEvaluationContext context) =>
            _observer.EvaluateCondition(_transitionId, context);
    }

    [Serializable]
    public sealed class RuntimeContractStateConfig : CoCoStateConfig
    {
        public int Value;
    }

    [Serializable]
    public sealed class RuntimeContractConditionConfig : CoCoConditionConfig
    {
        public int Value;
    }

    public readonly struct RuntimeContractStateConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public readonly struct RuntimeContractConditionConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public sealed class RuntimeContractStateConfigFreezer :
        ICoCoConfigFreezer<RuntimeContractStateConfig, RuntimeContractStateConfigSchema>
    {
        public bool TryFreeze(
            RuntimeContractStateConfig source,
            CoCoFrozenConfigWriter<RuntimeContractStateConfigSchema> writer,
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

    public sealed class RuntimeContractConditionConfigFreezer :
        ICoCoConfigFreezer<RuntimeContractConditionConfig, RuntimeContractConditionConfigSchema>
    {
        public bool TryFreeze(
            RuntimeContractConditionConfig source,
            CoCoFrozenConfigWriter<RuntimeContractConditionConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Runtime contract Condition config is required.");
                return false;
            }

            return writer.TryWrite(
                RuntimeContractSchemas.ConditionValue,
                source.Value,
                out diagnostic);
        }
    }

    public static class RuntimeContractSchemas
    {
        static RuntimeContractSchemas()
        {
            var stateBuilder = new CoCoFrozenConfigSchemaBuilder<RuntimeContractStateConfigSchema>();
            CoCoDiagnostic stateDiagnostic = CoCoDiagnostic.None;
            if (!CoCoFrozenConfigFieldId.TryCreate(
                    0xC04UL,
                    1UL,
                    out CoCoFrozenConfigFieldId stateId) ||
                !stateBuilder.TryAddField(stateId, out StateValue, out stateDiagnostic) ||
                !stateBuilder.TryFreeze(out State, out stateDiagnostic))
            {
                throw new InvalidOperationException(stateDiagnostic.Message);
            }

            var conditionBuilder =
                new CoCoFrozenConfigSchemaBuilder<RuntimeContractConditionConfigSchema>();
            CoCoDiagnostic conditionDiagnostic = CoCoDiagnostic.None;
            if (!CoCoFrozenConfigFieldId.TryCreate(
                    0xC04UL,
                    2UL,
                    out CoCoFrozenConfigFieldId conditionId) ||
                !conditionBuilder.TryAddField(
                    conditionId,
                    out ConditionValue,
                    out conditionDiagnostic) ||
                !conditionBuilder.TryFreeze(out Condition, out conditionDiagnostic))
            {
                throw new InvalidOperationException(conditionDiagnostic.Message);
            }
        }

        public static readonly CoCoFrozenConfigField<RuntimeContractStateConfigSchema, int>
            StateValue;
        public static readonly CoCoFrozenConfigSchema<RuntimeContractStateConfigSchema> State;
        public static readonly CoCoFrozenConfigField<RuntimeContractConditionConfigSchema, int>
            ConditionValue;
        public static readonly CoCoFrozenConfigSchema<RuntimeContractConditionConfigSchema> Condition;
    }

    public interface IRuntimeContractOperationSection : ICoCoOperationSection
    {
        int Value { get; }
    }

    public sealed class RuntimeContractOperationSectionView : IRuntimeContractOperationSection
    {
        private readonly CoCoOperationSectionReader _reader;
        private readonly CoCoOperationSectionField<int> _valueField;

        public RuntimeContractOperationSectionView(
            CoCoOperationSectionReader reader,
            CoCoOperationSectionField<int> valueField)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _valueField = valueField;
        }

        public int Value => _reader.Read(_valueField);
    }

    public sealed class RuntimeContractOperationSectionViewFactory :
        ICoCoOperationSectionViewFactory<IRuntimeContractOperationSection>
    {
        public IRuntimeContractOperationSection Create(
            in CoCoOperationSectionViewContext<IRuntimeContractOperationSection> context)
        {
            if (!context.IsValid ||
                !context.TryGetField(0, out CoCoOperationSectionField<int> valueField))
            {
                throw new InvalidOperationException(
                    "Runtime contract Operation Section field could not be resolved.");
            }

            return new RuntimeContractOperationSectionView(context.Reader, valueField);
        }
    }
}
