using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;

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
    public sealed class StandardOperatorBuilder
    {

        #region Public API

        /// <summary>声明当前 Operator 消费的合同，返回读取该操作所需的要件。未声明合法模式时拒绝注册。</summary>
        public OperationRequirement Require<TOperation>()
            where TOperation : class, IOperation =>
            Require<TOperation>(DeriveOperationId(typeof(TOperation)), 1);

        /// <summary>声明当前 Operator 消费的合同，返回读取该操作所需的要件。未声明合法模式时拒绝注册。</summary>
        public OperationRequirement Require<TOperation>(OperationId id,
            ulong semanticFingerprint) where TOperation : class, IOperation
        {
            var mode = GetMode(typeof(TOperation));
            return Require<TOperation, GeneratedOperationViewFactory<TOperation>>(id, semanticFingerprint,
                new GeneratedOperationViewFactory<TOperation>(), mode);
        }

        /// <summary>声明由本 Operator 写入的结果块与槽，使 Host 在装配时校验其类型与恢复策略。</summary>
        public void OwnOutcome<TValue>(StateBlockId block, StateSlotId slot,
            ContextProjection projection, ContextRestorePolicy restorePolicy,
            TValue initialValue, ulong semanticFingerprint) where TValue : unmanaged
        {
            Ensure(_descriptor.TryOwnOutcome<TValue>(slot, out var diagnostic), diagnostic);
            _outcomes.Add(new Outcome(block, slot,
                (GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic error) =>
                    catalog.TryRegisterStateSlot(block, slot, projection, restorePolicy,
                        initialValue, semanticFingerprint, default, null, out error),
                (StateGraphHostBindingBuilder host, out CoCoDiagnostic error) =>
                    host.TryBindContextSlot(block, slot, initialValue, semanticFingerprint, out error)));
        }

        /// <summary>为指定载荷创建含事件域、标识与容量的出件声明，返回供执行阶段使用的要件。</summary>
        public EventOutboxRequirement Emit<TEvent>(EventTypeId id, EventDomainId domain, int capacity)
            where TEvent : unmanaged
        {
            Ensure(_descriptor.TryEmit<TEvent>(id, domain, capacity, out var requirement, out var diagnostic), diagnostic);
            return requirement;
        }

        /// <summary>将本 Operator 的稳定标识与已登记的要件、结果和出件整理为标准定义。</summary>
        public StandardOperatorDefinition Freeze<TOperator>(OperatorId id) where TOperator : class, IOperator
        {
            Ensure(_descriptor.TryFreeze<TOperator>(id, out var descriptor, out var diagnostic), diagnostic);
            return new StandardOperatorDefinition(descriptor, _operations.ToArray(), _outcomes.ToArray());
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
                (StateGraphHostBindingBuilder host, out CoCoDiagnostic error) =>
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
            OperationId.TryCreate(0x434F434F4F505345UL, StandardTypeIdentity.Hash(section), out var id);
            return id;
        }

        private static void Ensure(bool succeeded, CoCoDiagnostic diagnostic)
        {
            if (!succeeded) throw new InvalidOperationException(diagnostic.Message);
        }

        /// <summary>在编译目录中登记 Operator 声明所涉及的类型化合同。</summary>
        internal delegate bool RegisterCatalog(GraphDescriptorCatalogBuilder catalog, out CoCoDiagnostic diagnostic);
        /// <summary>在 Host 装配阶段把同一声明接入实际运行时绑定。</summary>
        internal delegate bool BindHost(StateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic);

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
    public sealed class StandardOperatorDefinition
    {
        private readonly StandardOperatorBuilder.Operation[] _operations;
        private readonly StandardOperatorBuilder.Outcome[] _outcomes;
        private readonly StandardOperationRegistration[] _operationsBySection;
        private readonly StateBlockId[] _blocks;

        public OperatorDescriptor Descriptor { get; }
        internal IReadOnlyList<StandardOperationRegistration> Operations => _operationsBySection;
        internal IReadOnlyList<StateBlockId> ContextBlocks => _blocks;

        #region Internal Logic

        internal StandardOperatorDefinition(OperatorDescriptor descriptor,
            StandardOperatorBuilder.Operation[] operations, StandardOperatorBuilder.Outcome[] outcomes)
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
            StateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic)
        {
            foreach (var operation in _operations)
                if (operation.Registration.OperationId == section) return operation.Bind(host, out diagnostic);
            diagnostic = Missing($"Operator does not declare Operation Section {section}.");
            return false;
        }

        internal bool TryBindContextSlot(StateBlockId block, StateSlotId slot,
            StateGraphHostBindingBuilder host, out CoCoDiagnostic diagnostic)
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
