using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 项目 Operator 在此声明消费的操作、可写结果和出件。标准 Provider 把声明登记进图目录并绑定到 Host。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>将一个具体操作的类型、模式和字段布局加入标准注册。</summary>
    internal readonly struct StandardOperationRegistration
    {
        public StandardOperationRegistration(
            Type operationType,
            OperationId operationId)
        {
            OperationType = operationType ??
                          throw new ArgumentNullException(nameof(operationType));
            OperationId = operationId;
        }

        public Type OperationType { get; }
        public OperationId OperationId { get; }
        public bool IsValid => OperationType != null &&
                               typeof(IOperation).IsAssignableFrom(OperationType) &&
                               OperationId.IsValid;
    }

    /// <summary>收集项目 Operator 的要件、结果槽和出件定义，供图编译与 Host 装配共用。</summary>
    public sealed class HostOperatorBuilder
    {

        #region Public API

        /// <summary>声明当前 Operator 消费的合同，返回读取该操作所需的要件。未声明合法模式时拒绝注册。</summary>
        /// <typeparam name="TOperation">项目声明的操作合同类型。</typeparam>
        /// <returns>可供该 Operator 读取的操作要件。重复或无效声明会抛出异常。</returns>
        public OperationRequirement Require<TOperation>()
            where TOperation : class, IOperation =>
            Require<TOperation>(DeriveOperationId(typeof(TOperation)), 1);

        /// <summary>声明当前 Operator 消费的合同，返回读取该操作所需的要件。未声明合法模式时拒绝注册。</summary>
        /// <param name="id">本项声明使用的稳定标识。</param>
        /// <param name="semanticFingerprint">注册者提供的稳定语义指纹。</param>
        /// <typeparam name="TOperation">项目声明的操作合同类型。</typeparam>
        /// <returns>可供该 Operator 读取的操作要件。重复或无效声明会抛出异常。</returns>
        public OperationRequirement Require<TOperation>(OperationId id,
            ulong semanticFingerprint) where TOperation : class, IOperation
        {
            var mode = GetMode(typeof(TOperation));
            return Require<TOperation, GeneratedOperationViewFactory<TOperation>>(id, semanticFingerprint,
                new GeneratedOperationViewFactory<TOperation>(), mode);
        }

        /// <summary>声明由本 Operator 写入的结果块与槽，使 Host 在装配时校验其类型与恢复策略。</summary>
        /// <param name="block">拥有此结果槽的状态块。</param>
        /// <param name="slot">目标布局中的类型化槽或槽标识。</param>
        /// <param name="projection">该槽参与的持久化或时间投影。</param>
        /// <param name="restorePolicy">恢复 Context 时该槽的取值策略。</param>
        /// <param name="initialValue">首次提交前结果槽使用的默认值。</param>
        /// <param name="semanticFingerprint">注册者提供的稳定语义指纹。</param>
        /// <typeparam name="TValue">状态槽的非托管值类型。</typeparam>
        public void OwnOutcome<TValue>(StateBlockId block, StateSlotId slot,
            ContextProjection projection, ContextRestorePolicy restorePolicy,
            TValue initialValue, ulong semanticFingerprint) where TValue : unmanaged
        {
            Ensure(_descriptor.TryOwnOutcome<TValue>(slot, out var diagnostic), diagnostic);
            _outcomes.Add(new Outcome(block, slot,
                (GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic error) =>
                    catalog.TryRegisterStateSlot(block, slot, projection, restorePolicy,
                        initialValue, semanticFingerprint, default, null, out error),
                (HostBindingBuilder host, out CoCoDiagnostic error) =>
                    host.TryBindContextSlot(block, slot, initialValue, semanticFingerprint, out error)));
        }

        /// <summary>为指定载荷创建含事件域、标识与容量的出件声明，返回供执行阶段使用的要件。</summary>
        /// <param name="id">要发布的事件类型标识。</param>
        /// <param name="domain">发件使用的事件域。</param>
        /// <param name="capacity">要分配的固定容量。</param>
        /// <typeparam name="TEvent">此事件的非托管载荷类型。</typeparam>
        /// <returns>创建成功的发件要件。载荷和容量不合约时抛出异常。</returns>
        public EventOutboxRequirement Emit<TEvent>(EventTypeId id, EventDomainId domain, int capacity)
            where TEvent : unmanaged
        {
            Ensure(_descriptor.TryEmit<TEvent>(id, domain, capacity, out var requirement, out var diagnostic), diagnostic);
            return requirement;
        }

        /// <summary>将本 Operator 的稳定标识与已登记的要件、结果和出件整理为标准定义。</summary>
        /// <param name="id">本项声明使用的稳定标识。</param>
        /// <typeparam name="TOperator">对该声明负责的 Operator 类型。</typeparam>
        /// <returns>供项目注册的冻结定义。无效的 Operator 声明会抛出异常。</returns>
        public HostOperatorDefinition Freeze<TOperator>(OperatorId id) where TOperator : class, IOperator
        {
            Ensure(_descriptor.TryFreeze<TOperator>(id, out var descriptor, out var diagnostic), diagnostic);
            return new HostOperatorDefinition(descriptor, _operations.ToArray(), _outcomes.ToArray());
        }

        #endregion

        #region Internal Logic

        private readonly OperatorDescriptorBuilder _descriptor = new OperatorDescriptorBuilder();
        private readonly List<Operation> _operations = new List<Operation>();
        private readonly List<Outcome> _outcomes = new List<Outcome>();

        internal OperationRequirement Require<TOperation, TFactory>(OperationId id,
            ulong semanticFingerprint, TFactory factory)
            where TOperation : class, IOperation
            where TFactory : IOperationViewFactory<TOperation> =>
            Require<TOperation, TFactory>(id, semanticFingerprint, factory, GetMode(typeof(TOperation)));

        private OperationRequirement Require<TOperation, TFactory>(OperationId id,
            ulong semanticFingerprint, TFactory factory, Mode mode)
            where TOperation : class, IOperation
            where TFactory : IOperationViewFactory<TOperation>
        {
            var section = typeof(TOperation);
            Ensure(_descriptor.TryRequire<TOperation>(id, mode, out var requirement, out var diagnostic), diagnostic);
            _operations.Add(new Operation(new StandardOperationRegistration(section, id),
                (GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic error) =>
                    catalog.TryRegisterOperation(id, mode, new OperationViewFactoryToken<TOperation, TFactory>(semanticFingerprint), out error),
                (HostBindingBuilder host, out CoCoDiagnostic error) =>
                    host.TryRegisterOperation<TOperation>(id, mode, factory, semanticFingerprint, out _, out error)));
            return requirement;
        }

        private static Mode GetMode(Type section)
        {
            var declaration = (OperationAttribute)Attribute.GetCustomAttribute(section,
                typeof(OperationAttribute));
            if (declaration == null || !OperationContract.IsDefinedMode(declaration.Mode))
                throw new InvalidOperationException($"Operation Section {section.FullName} must declare Continuous or Discrete mode.");
            return declaration.Mode;
        }

        private static OperationId DeriveOperationId(Type section)
        {
            OperationId.TryCreate(0x434F434F4F505345UL, HostBindingIds.Hash(section), out var id);
            return id;
        }

        private static void Ensure(bool succeeded, CoCoDiagnostic diagnostic)
        {
            if (!succeeded) throw new InvalidOperationException(diagnostic.Message);
        }

        /// <summary>在编译目录中登记 Operator 声明所涉及的类型化合同。</summary>
        /// <param name="catalog">接收此声明的项目描述目录。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        internal delegate bool RegisterCatalog(GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic diagnostic);
        /// <summary>在 Host 装配阶段把同一声明接入实际运行时绑定。</summary>
        /// <param name="host">为实例接收场景绑定的构建器。</param>
        /// <param name="diagnostic">失败原因，成功时为无诊断。</param>
        internal delegate bool BindHost(HostBindingBuilder host, out CoCoDiagnostic diagnostic);

        /// <summary>保存单项操作登记与运行绑定需要执行的委托。</summary>
        internal readonly struct Operation
        {
            internal Operation(StandardOperationRegistration registration, RegisterCatalog register, BindHost bind)
            {
                Registration = registration;
                Register = register;
                Bind = bind;
            }

            internal StandardOperationRegistration Registration { get; }
            internal RegisterCatalog Register { get; }
            internal BindHost Bind { get; }
        }

        /// <summary>保存结果槽的注册及 Host 装配动作。</summary>
        internal readonly struct Outcome
        {
            internal Outcome(StateBlockId block, StateSlotId slot, RegisterCatalog register, BindHost bind)
            {
                Block = block;
                Slot = slot;
                Register = register;
                Bind = bind;
            }

            internal StateBlockId Block { get; }
            internal StateSlotId Slot { get; }
            internal RegisterCatalog Register { get; }
            internal BindHost Bind { get; }
        }

        #endregion
    }

    /// <summary>向 Host 提供已冻结的 Operator 描述符及目录与实例装配函数。</summary>
    public sealed class HostOperatorDefinition
    {
        private readonly HostOperatorBuilder.Operation[] _operations;
        private readonly HostOperatorBuilder.Outcome[] _outcomes;
        private readonly StandardOperationRegistration[] _operationsBySection;
        private readonly StateBlockId[] _blocks;

        /// <summary>已冻结的 Operator 声明，包含所读操作、结果槽和发件要件。</summary>
        public OperatorDescriptor Descriptor { get; }
        internal IReadOnlyList<StandardOperationRegistration> Operations => _operationsBySection;
        internal IReadOnlyList<StateBlockId> ContextBlocks => _blocks;

        #region Internal Logic

        internal HostOperatorDefinition(OperatorDescriptor descriptor,
            HostOperatorBuilder.Operation[] operations, HostOperatorBuilder.Outcome[] outcomes)
        {
            Descriptor = descriptor;
            _operations = operations;
            _outcomes = outcomes;
            _operationsBySection = Array.ConvertAll(operations, operation => operation.Registration);
            var blocks = new HashSet<StateBlockId>();
            foreach (var outcome in outcomes) blocks.Add(outcome.Block);
            _blocks = new StateBlockId[blocks.Count];
            blocks.CopyTo(_blocks);
        }

        internal void RegisterCatalog(GraphDescriptorCatalogBuilder catalog)
        {
            foreach (var operation in _operations)
                if (!operation.Register(catalog, out var diagnostic)) throw new InvalidOperationException(diagnostic.Message);
            foreach (var block in _blocks)
                if (!catalog.TryRegisterStateBlock(block, StateBlockOwner.Operator, out var diagnostic))
                    throw new InvalidOperationException(diagnostic.Message);
            foreach (var outcome in _outcomes)
                if (!outcome.Register(catalog, out var diagnostic)) throw new InvalidOperationException(diagnostic.Message);
        }

        internal bool TryBindOperation(OperationId section,
            HostBindingBuilder host, out CoCoDiagnostic diagnostic)
        {
            foreach (var operation in _operations)
                if (operation.Registration.OperationId == section) return operation.Bind(host, out diagnostic);
            diagnostic = Missing($"Operator does not declare Operation Section {section}.");
            return false;
        }

        internal bool TryBindContextSlot(StateBlockId block, StateSlotId slot,
            HostBindingBuilder host, out CoCoDiagnostic diagnostic)
        {
            foreach (var outcome in _outcomes)
                if (outcome.Block == block && outcome.Slot == slot) return outcome.Bind(host, out diagnostic);
            diagnostic = Missing($"Operator does not declare Context slot {slot} in block {block}.");
            return false;
        }

        private static CoCoDiagnostic Missing(string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, CoCoDiagnosticCode.MissingDescriptor, message);

        #endregion
    }
}
