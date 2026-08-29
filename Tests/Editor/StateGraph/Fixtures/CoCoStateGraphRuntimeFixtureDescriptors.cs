using System;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    public interface IRuntimeStateGraphFixtureObserver
    {
        bool RequestAtoB { get; }
        bool RequestBSelf { get; }
        bool ThrowOnUpdate { get; }
        bool RecordCallbacks { get; }
        double ActionProgress { get; }
        void Record(string value);
    }

    public sealed class RuntimeFixtureMemory : CoCoActivationMemory
    {
        public int Value;
    }

    public sealed class RuntimeFixtureStateLogic :
        CoCoStateLogic,
        ICoCoStateEnter,
        ICoCoStateUpdate,
        ICoCoStateExit
    {
        private readonly IRuntimeStateGraphFixtureObserver _observer;
        private readonly CoCoStateId _stateId;
        private readonly CoCoStateId _stateA;
        private readonly CoCoStateId _stateB;
        private readonly string _name;
        private readonly CoCoTransitionHandle _transition;

        public RuntimeFixtureStateLogic(
            CoCoStateFactoryContext context,
            IRuntimeStateGraphFixtureObserver observer,
            CoCoStateId root,
            CoCoStateId stateA,
            CoCoStateId stateB)
        {
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
            _stateId = context.StateId;
            _stateA = stateA;
            _stateB = stateB;
            _name = context.StateId == root ? "root" : context.StateId == stateA ? "a" : "b";
            if (context.OutgoingTransitions.Count > 0)
            {
                _transition = context.OutgoingTransitions[0];
            }
        }

        public void OnEnter(CoCoStateExecutionContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("enter:" + _name + ":" + context.Memory<RuntimeFixtureMemory>().Value);
            }
        }

        public void Update(CoCoStateExecutionContext context)
        {
            if (_observer.ThrowOnUpdate)
            {
                throw new InvalidOperationException("synthetic callback failure");
            }

            RuntimeFixtureMemory memory = context.Memory<RuntimeFixtureMemory>();
            if (_observer.RecordCallbacks)
            {
                _observer.Record("update:" + _name + ":" + memory.Value);
            }
            memory.Value++;
            if (_stateId == _stateA || _stateId == _stateB)
            {
                context.TrySetActionProgress(_observer.ActionProgress);
            }

            if ((_stateId == _stateA && _observer.RequestAtoB) ||
                (_stateId == _stateB && _observer.RequestBSelf))
            {
                context.RequestTransition(_transition);
            }
        }

        public void OnExit(CoCoStateExecutionContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("exit:" + _name + ":" + context.Memory<RuntimeFixtureMemory>().Value);
            }
        }
    }

    public sealed class RuntimeFixtureCondition :
        CoCoStateCondition,
        ICoCoStateConditionEvaluator
    {
        private readonly IRuntimeStateGraphFixtureObserver _observer;

        public RuntimeFixtureCondition(
            CoCoConditionFactoryContext context,
            IRuntimeStateGraphFixtureObserver observer)
        {
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public bool Evaluate(CoCoConditionEvaluationContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("condition:a-b");
            }
            return true;
        }
    }

    [Serializable]
    public sealed class RuntimeFixtureStateAuthoringConfig : CoCoStateConfig
    {
        public int Value;
    }

    [Serializable]
    public sealed class RuntimeFixtureConditionAuthoringConfig : CoCoConditionConfig
    {
        public int Value;
    }

    public readonly struct RuntimeFixtureStateConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public readonly struct RuntimeFixtureConditionConfigSchema : ICoCoFrozenConfigSchema
    {
    }

    public sealed class RuntimeFixtureStateConfigFreezer :
        ICoCoConfigFreezer<RuntimeFixtureStateAuthoringConfig, RuntimeFixtureStateConfigSchema>
    {
        public bool TryFreeze(
            RuntimeFixtureStateAuthoringConfig source,
            CoCoFrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Runtime fixture State config is required.");
                return false;
            }

            return writer.TryWrite(RuntimeFixtureSchemas.StateValue, source.Value, out diagnostic);
        }
    }

    public sealed class RuntimeFixtureConditionConfigFreezer :
        ICoCoConfigFreezer<RuntimeFixtureConditionAuthoringConfig, RuntimeFixtureConditionConfigSchema>
    {
        public bool TryFreeze(
            RuntimeFixtureConditionAuthoringConfig source,
            CoCoFrozenConfigWriter<RuntimeFixtureConditionConfigSchema> writer,
            out CoCoDiagnostic diagnostic)
        {
            if (source == null)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.State,
                    CoCoDiagnosticCode.InvalidFrozenConfig,
                    "Runtime fixture Condition config is required.");
                return false;
            }

            return writer.TryWrite(RuntimeFixtureSchemas.ConditionValue, source.Value, out diagnostic);
        }
    }

    public static class RuntimeFixtureSchemas
    {
        static RuntimeFixtureSchemas()
        {
            var stateBuilder = new CoCoFrozenConfigSchemaBuilder<RuntimeFixtureStateConfigSchema>();
            CoCoDiagnostic stateDiagnostic = CoCoDiagnostic.None;
            if (!CoCoFrozenConfigFieldId.TryCreate(0x44UL, 1UL, out CoCoFrozenConfigFieldId stateId) ||
                !stateBuilder.TryAddField(stateId, out StateValue, out stateDiagnostic) ||
                !stateBuilder.TryFreeze(out State, out stateDiagnostic))
            {
                throw new InvalidOperationException(stateDiagnostic.Message);
            }

            var conditionBuilder = new CoCoFrozenConfigSchemaBuilder<RuntimeFixtureConditionConfigSchema>();
            CoCoDiagnostic conditionDiagnostic = CoCoDiagnostic.None;
            if (!CoCoFrozenConfigFieldId.TryCreate(
                    0x44UL,
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

        public static readonly CoCoFrozenConfigField<RuntimeFixtureStateConfigSchema, int> StateValue;
        public static readonly CoCoFrozenConfigSchema<RuntimeFixtureStateConfigSchema> State;
        public static readonly CoCoFrozenConfigField<RuntimeFixtureConditionConfigSchema, int> ConditionValue;
        public static readonly CoCoFrozenConfigSchema<RuntimeFixtureConditionConfigSchema> Condition;
    }
}
