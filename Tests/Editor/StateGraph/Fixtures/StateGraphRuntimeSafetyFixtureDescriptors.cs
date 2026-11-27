using System;
using CoCoFlow.Runtime.StateGraph;

// Safety 测试用这些状态和输出视图尝试重入、复用对象与过期写入，核对实例拒绝路径。
namespace CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures
{
    /// <summary>在实际 Update 内执行测试指定的动作，供重入和记忆保护检查。</summary>
    public interface IRuntimeSafetyFixtureObserver
    {
        /// <summary>在有效状态回调中接收执行上下文，供安全测试尝试重入或改动记忆。</summary>
        void OnUpdate(
            StateExecutionContext context,
            RuntimeSafetyFixtureMemory memory);
    }

    /// <summary>为安全测试暴露可观察的激活值，使回滚与非法别名可验证。</summary>
    public sealed class RuntimeSafetyFixtureMemory : StateActivationMemory
    {
        public int Value;
    }

    /// <summary>先访问当前激活记忆，再把回调交给安全测试的观察器。</summary>
    public sealed class RuntimeSafetyFixtureLogic : StateLogic, IStateUpdate
    {

        #region Public API

        /// <summary>保存本实例的安全观察器，后续更新会在真实回调内通知该观察器。</summary>
        public RuntimeSafetyFixtureLogic(IRuntimeSafetyFixtureObserver observer)
        {
            _observer = observer;
        }

        /// <summary>更新该次激活的记忆并触发安全观察器，供测试检查失败回滚。</summary>
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

    /// <summary>在安全测试的回调中使用实际 Operation 写入者，检查租约和可写范围。</summary>
    public interface IRuntimeSafetyOperationFixtureObserver
    {
        /// <summary>接收操作测试的回调与字段绑定，供检查状态能否使用该写入者。</summary>
        void OnUpdate(
            StateId stateId,
            StateExecutionContext context,
            RuntimeSafetyFixtureMemory memory);
    }

    /// <summary>向观察器提供当前状态的真实输出写入上下文，用来测试越权和过期写入。</summary>
    public sealed class RuntimeSafetyOperationFixtureLogic :
        StateLogic,
        IStateUpdate
    {

        #region Public API

        /// <summary>从工厂上下文绑定测试所用的输出字段和观察器，缺少必要上下文时拒绝构造。</summary>
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

        /// <summary>将本步操作写入者交给观察器，使安全测试走实际的写入与租约校验。</summary>
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

    /// <summary>为写入权限测试定义需要声明和注册的输出字段。</summary>
    public interface IRuntimeSafetyOperationSection : IOperation
    {
        int Value { get; }
    }

    /// <summary>将安全测试的操作字段绑定到实际输出帧。</summary>
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

    /// <summary>为已注册的安全测试操作构造字段类型匹配的视图。</summary>
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
