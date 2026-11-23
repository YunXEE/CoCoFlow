using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

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

    public sealed class RuntimeFixtureMemory : ActivationMemory
    {
        public int Value;
    }

    public sealed class RuntimeFixtureStateLogic :
        StateLogic,
        IStateEnter,
        IStateUpdate,
        IStateExit
    {
        private readonly IRuntimeStateGraphFixtureObserver _observer;
        private readonly StateId _stateId;
        private readonly StateId _stateA;
        private readonly StateId _stateB;
        private readonly string _name;
        private readonly TransitionHandle _transition;

        public RuntimeFixtureStateLogic(
            StateFactoryContext context,
            IRuntimeStateGraphFixtureObserver observer,
            StateId root,
            StateId stateA,
            StateId stateB)
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

        public void OnEnter(StateExecutionContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("enter:" + _name + ":" + context.Memory<RuntimeFixtureMemory>().Value);
            }
        }

        public void Update(StateExecutionContext context)
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

        public void OnExit(StateExecutionContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("exit:" + _name + ":" + context.Memory<RuntimeFixtureMemory>().Value);
            }
        }
    }

    public sealed class RuntimeFixtureCondition :
        StateCondition,
        IStateConditionEvaluator
    {
        private readonly IRuntimeStateGraphFixtureObserver _observer;

        public RuntimeFixtureCondition(
            ConditionFactoryContext context,
            IRuntimeStateGraphFixtureObserver observer)
        {
            _observer = observer ?? throw new ArgumentNullException(nameof(observer));
        }

        public bool Evaluate(ConditionEvaluationContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("condition:a-b");
            }
            return true;
        }
    }

    [Serializable]
    public sealed class RuntimeFixtureStateAuthoringConfig : StateConfig
    {
        public int Value;
    }

    [Serializable]
    public sealed class RuntimeFixtureConditionAuthoringConfig : ConditionConfig
    {
        public int Value;
    }

    public readonly struct RuntimeFixtureStateConfigSchema : IFrozenConfigSchema
    {
    }

    public readonly struct RuntimeFixtureConditionConfigSchema : IFrozenConfigSchema
    {
    }

    public sealed class RuntimeFixtureStateConfigFreezer :
        IConfigFreezer<RuntimeFixtureStateAuthoringConfig, RuntimeFixtureStateConfigSchema>
    {
        public bool TryFreeze(
            RuntimeFixtureStateAuthoringConfig source,
            FrozenConfigWriter<RuntimeFixtureStateConfigSchema> writer,
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
        IConfigFreezer<RuntimeFixtureConditionAuthoringConfig, RuntimeFixtureConditionConfigSchema>
    {
        public bool TryFreeze(
            RuntimeFixtureConditionAuthoringConfig source,
            FrozenConfigWriter<RuntimeFixtureConditionConfigSchema> writer,
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
            var stateBuilder = new FrozenConfigSchemaBuilder<RuntimeFixtureStateConfigSchema>();
            CoCoDiagnostic stateDiagnostic = CoCoDiagnostic.None;
            if (!FrozenConfigFieldId.TryCreate(0x44UL, 1UL, out FrozenConfigFieldId stateId) ||
                !stateBuilder.TryAddField(stateId, out StateValue, out stateDiagnostic) ||
                !stateBuilder.TryFreeze(out State, out stateDiagnostic))
            {
                throw new InvalidOperationException(stateDiagnostic.Message);
            }

            var conditionBuilder = new FrozenConfigSchemaBuilder<RuntimeFixtureConditionConfigSchema>();
            CoCoDiagnostic conditionDiagnostic = CoCoDiagnostic.None;
            if (!FrozenConfigFieldId.TryCreate(
                    0x44UL,
                    2UL,
                    out FrozenConfigFieldId conditionId) ||
                !conditionBuilder.TryAddField(
                    conditionId,
                    out ConditionValue,
                    out conditionDiagnostic) ||
                !conditionBuilder.TryFreeze(out Condition, out conditionDiagnostic))
            {
                throw new InvalidOperationException(conditionDiagnostic.Message);
            }
        }

        public static readonly FrozenConfigField<RuntimeFixtureStateConfigSchema, int> StateValue;
        public static readonly FrozenConfigSchema<RuntimeFixtureStateConfigSchema> State;
        public static readonly FrozenConfigField<RuntimeFixtureConditionConfigSchema, int> ConditionValue;
        public static readonly FrozenConfigSchema<RuntimeFixtureConditionConfigSchema> Condition;
    }
}
