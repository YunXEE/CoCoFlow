using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// Runtime 测试通过这些节点工厂和冻结配置驱动真实执行器并记录回调。
namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    /// <summary>使运行测试能决定更新行为并收集真实的进入、更新和退出记录。</summary>
    public interface IRuntimeStateGraphFixtureObserver
    {
        bool RequestAtoB { get; }
        bool RequestBSelf { get; }
        bool ThrowOnUpdate { get; }
        bool RecordCallbacks { get; }
        double ActionProgress { get; }
        /// <summary>接收节点回调记录，供场景用例核对精确的进入、更新和退出顺序。</summary>
        void Record(string value);
    }

    /// <summary>为运行测试保留激活级数据，以验证提交、切换和恢复。</summary>
    public sealed class RuntimeFixtureMemory : StateActivationMemory
    {
        public int Value;
    }

    /// <summary>按测试观察器的请求更新记忆、写出状态并记录生命周期。</summary>
    public sealed class RuntimeFixtureStateLogic :
        StateLogic,
        IStateEnter,
        IStateUpdate,
        IStateExit
    {

        #region Public API

        /// <summary>保存观察器和工厂上下文中的节点身份，缺少观察器时抛出异常。</summary>
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

        /// <summary>按观察器的记录开关写入本次进入的状态名和调用信息。</summary>
        public void OnEnter(StateExecutionContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("enter:" + _name + ":" + context.Memory<RuntimeFixtureMemory>().Value);
            }
        }

        /// <summary>由观察器控制记忆更新、出边请求及模拟故障，用于比较候选与已提交状态。</summary>
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

        /// <summary>按观察器的记录开关写入本次退出的状态名和调用信息。</summary>
        public void OnExit(StateExecutionContext context)
        {
            if (_observer.RecordCallbacks)
            {
                _observer.Record("exit:" + _name + ":" + context.Memory<RuntimeFixtureMemory>().Value);
            }
        }

        #endregion

        #region Internal Logic

        private readonly IRuntimeStateGraphFixtureObserver _observer;
        private readonly StateId _stateId;
        private readonly StateId _stateA;
        private readonly StateId _stateB;
        private readonly string _name;
        private readonly TransitionHandle _transition;

        #endregion
    }

    /// <summary>供运行测试在节点级配置实例工厂需要的初始值。</summary>
    [Serializable]
    public sealed class RuntimeFixtureStateAuthoringConfig : StateConfig
    {
        public int Value;
    }

    /// <summary>声明运行测试工厂读取的冻结字段结构。</summary>
    public readonly struct RuntimeFixtureStateConfigSchema : IFrozenConfigSchema
    {
    }

    /// <summary>将运行测试的节点字段写入预定义 schema，缺少源对象时报告诊断。</summary>
    public sealed class RuntimeFixtureStateConfigFreezer :
        IConfigFreezer<RuntimeFixtureStateAuthoringConfig, RuntimeFixtureStateConfigSchema>
    {
        /// <summary>将运行测试的初始配置写入类型化字段，源对象缺失时返回失败诊断。</summary>
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

    /// <summary>维护运行测试所共享的 schema 和字段句柄。</summary>
    public static class RuntimeFixtureSchemas
    {

        #region Public API

        public static readonly FrozenConfigField<RuntimeFixtureStateConfigSchema, int> StateValue;
        public static readonly FrozenConfigSchema<RuntimeFixtureStateConfigSchema> State;

        #endregion

        #region Internal Logic

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

        }

        #endregion
    }
}
