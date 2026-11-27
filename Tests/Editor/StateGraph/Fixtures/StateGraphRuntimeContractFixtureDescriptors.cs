using System;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

// RuntimeContract 用这些可观察的玩法回调、配置和输出检查激活顺序与整步提交。
namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    /// <summary>记录测试观察到的进入、更新或退出阶段，用来对照父子调用顺序。</summary>
    public enum RuntimeContractCallbackPhase
    {
        Enter = 1,
        Update = 2,
        Exit = 3
    }

    /// <summary>由测试控制器记录状态回调并指定本步进度和要请求的边。</summary>
    public interface IRuntimeContractObserver
    {
        /// <summary>按当前节点与边询问测试控制器是否应在这一回调申请转换。</summary>
        bool ShouldRequest(TransitionId transitionId);
        /// <summary>取控制器为指定节点安排的本步进度，供窗口关联用例使用。</summary>
        double GetActionProgress(StateId stateId, double currentValue);
        /// <summary>接收回调所在阶段、节点与本步上下文，供测试逐项对照次序。</summary>
        void OnStateCallback(
            RuntimeContractCallbackPhase phase,
            StateId stateId,
            StateExecutionContext context);
    }

    /// <summary>保存当前激活被更新的次数，供提交、回滚和再次进入时对照。</summary>
    public sealed class RuntimeContractMemory : StateActivationMemory
    {
        public int UpdateCount;
    }

    /// <summary>为各生命周期组合统一记录回调、更新记忆并登记由观察器选定的出边。</summary>
    public abstract class RuntimeContractStateLogicBase : StateLogic
    {

        #region Public API

        /// <summary>从节点工厂上下文取得运行身份和出边，将本状态接到测试观察器。</summary>
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

        /// <summary>将本次进入的节点身份与回调上下文传给观察器。</summary>
        protected void Enter(StateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Enter, _stateId, context);
        }

        /// <summary>记录当前节点的更新，推进其候选记忆，并登记观察器指明的进度与出边。</summary>
        protected void UpdateState(StateExecutionContext context)
        {
            _observer.OnStateCallback(RuntimeContractCallbackPhase.Update, _stateId, context);
            context.Memory<RuntimeContractMemory>().UpdateCount++;
            context.TrySetActionProgress(
                _observer.GetActionProgress(_stateId, context.ActionProgress));

            RequestIfSelected(context, _firstTransition, _transitionCount > 0);
            RequestIfSelected(context, _secondTransition, _transitionCount > 1);
        }

        /// <summary>将本次退出的节点身份和回调上下文传给观察器。</summary>
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

    /// <summary>将上述可观测更新接入常驻 AnyState 以检查其与普通路径的先后次序。</summary>
    public sealed class RuntimeContractAnyLogic : AnyStateLogic
    {

        #region Public API

        /// <summary>将常驻节点的工厂上下文与观察器交给共享更新逻辑。</summary>
        public RuntimeContractAnyLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
        {
            _update = new RuntimeContractUpdateOnlyLogic(context, observer);
        }

        /// <summary>运行本层常驻节点的观察与写入，使测试能比较其请求和普通路径请求的先后。</summary>
        public override void Update(StateExecutionContext context) => _update.Update(context);

        #endregion

        #region Internal Logic

        private readonly RuntimeContractUpdateOnlyLogic _update;

        #endregion
    }

    /// <summary>提供只实现更新回调的普通状态，用于对照可选进入与退出。</summary>
    public sealed class RuntimeContractUpdateOnlyLogic :
        RuntimeContractStateLogicBase,
        IStateUpdate
    {
        /// <summary>将节点身份和测试观察器绑定到只提供更新回调的状态。</summary>
        public RuntimeContractUpdateOnlyLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        /// <summary>通过共享观察器记录本步更新、候选记忆和要请求的出边。</summary>
        public void Update(StateExecutionContext context) => UpdateState(context);
    }

    /// <summary>记录进入和更新阶段，使测试能区分未实现退出时的调用序列。</summary>
    public sealed class RuntimeContractEnterUpdateLogic :
        RuntimeContractStateLogicBase,
        IStateEnter,
        IStateUpdate
    {
        /// <summary>将节点身份和测试观察器绑定到提供进入和更新的状态。</summary>
        public RuntimeContractEnterUpdateLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        /// <summary>在新激活的进入阶段向观察器登记该节点和上下文。</summary>
        public void OnEnter(StateExecutionContext context) => Enter(context);
        /// <summary>通过共享观察器记录本步更新、候选记忆和要请求的出边。</summary>
        public void Update(StateExecutionContext context) => UpdateState(context);
    }

    /// <summary>记录更新和退出阶段，使测试能区分未实现进入时的调用序列。</summary>
    public sealed class RuntimeContractUpdateExitLogic :
        RuntimeContractStateLogicBase,
        IStateUpdate,
        IStateExit
    {
        /// <summary>将节点身份和测试观察器绑定到提供更新和退出的状态。</summary>
        public RuntimeContractUpdateExitLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        /// <summary>通过共享观察器记录本步更新、候选记忆和要请求的出边。</summary>
        public void Update(StateExecutionContext context) => UpdateState(context);
        /// <summary>在离开活动路径前向观察器登记该节点和上下文。</summary>
        public void OnExit(StateExecutionContext context) => Exit(context);
    }

    /// <summary>把完整生命周期各阶段写入观察器，供逐步比较父子切换顺序。</summary>
    public sealed class RuntimeContractEnterUpdateExitLogic :
        RuntimeContractStateLogicBase,
        IStateEnter,
        IStateUpdate,
        IStateExit
    {
        /// <summary>将节点身份和观察器绑定到实现完整生命周期的测试状态。</summary>
        public RuntimeContractEnterUpdateExitLogic(
            StateFactoryContext context,
            IRuntimeContractObserver observer)
            : base(context, observer)
        {
        }

        /// <summary>在新激活的进入阶段向观察器登记该节点和上下文。</summary>
        public void OnEnter(StateExecutionContext context) => Enter(context);
        /// <summary>通过共享观察器记录本步更新、候选记忆和要请求的出边。</summary>
        public void Update(StateExecutionContext context) => UpdateState(context);
        /// <summary>在离开活动路径前向观察器登记该节点和上下文。</summary>
        public void OnExit(StateExecutionContext context) => Exit(context);
    }

    /// <summary>携带运行合同夹具在编译节点时需要冻结的配置值。</summary>
    [Serializable]
    public sealed class RuntimeContractStateConfig : StateConfig
    {
        public int Value;
    }

    /// <summary>将合同夹具的节点配置关联到固定的冻结字段。</summary>
    public readonly struct RuntimeContractStateConfigSchema : IFrozenConfigSchema
    {
    }

    /// <summary>校验合同用例的源配置并写成实例工厂可读取的快照。</summary>
    public sealed class RuntimeContractStateConfigFreezer :
        IConfigFreezer<RuntimeContractStateConfig, RuntimeContractStateConfigSchema>
    {
        /// <summary>把合同测试节点的可编辑字段写入预定 schema，缺少配置或写入失败时返回诊断。</summary>
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

    /// <summary>提供运行合同夹具的共享 schema 和已解析字段。</summary>
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

    /// <summary>为运行合同测试提供可在不同层和路径深度合成的输出字段。</summary>
    public interface IRuntimeContractOperationSection : IOperation
    {
        int Value { get; }
    }

    /// <summary>在实际帧的绑定字段上读取运行合同夹具的输出。</summary>
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

    /// <summary>为本次帧布局创建运行合同夹具使用的类型化视图。</summary>
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
