using System.Collections.Generic;
using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;

// Host 按编译图把每个描述符接到 Logic 和 Memory 工厂，实例启动时据此分配独立对象。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>为项目工厂提供节点的冻结配置与其所属图的已编译出边。</summary>
    public sealed class StateFactoryContext
    {
        private readonly TransitionHandle[] _transitions;
        private readonly IReadOnlyList<TransitionHandle> _readOnlyTransitions;

        public GraphInstanceId GraphInstanceId { get; }
        public LayerId LayerId { get; }
        public int LayerIndex { get; }
        public StateId StateId { get; }
        public int PathDepth { get; }
        public FrozenConfigSnapshot Config { get; }
        public IReadOnlyList<TransitionHandle> OutgoingTransitions => _readOnlyTransitions;

        #region Public API

        /// <summary>在该工厂所对应节点的编译出边中查找转换，未找到时返回 false。</summary>
        public bool TryGetTransition(TransitionId transitionId, out TransitionHandle handle)
        {
            for (int index = 0; index < _transitions.Length; index++)
            {
                if (_transitions[index].TransitionId == transitionId)
                {
                    handle = _transitions[index];
                    return true;
                }
            }

            handle = default;
            return false;
        }

        #endregion

        #region Internal Logic

        internal StateFactoryContext(
            GraphInstanceId graphInstanceId,
            LayerId layerId,
            int layerIndex,
            StateId stateId,
            int pathDepth,
            FrozenConfigSnapshot config,
            TransitionHandle[] transitions)
        {
            GraphInstanceId = graphInstanceId;
            LayerId = layerId;
            LayerIndex = layerIndex;
            StateId = stateId;
            PathDepth = pathDepth;
            Config = config;
            _transitions = transitions ?? Array.Empty<TransitionHandle>();
            _readOnlyTransitions = Array.AsReadOnly(_transitions);
        }

        #endregion
    }

    /// <summary>定义实例创建 Logic、分配激活记忆以及复制、复位记忆的操作。</summary>
    public interface IStateRuntimeFactory
    {
        Type LogicType { get; }
        Type ActivationMemoryType { get; }

        /// <summary>根据节点的冻结配置创建供该实例独占的 Logic，实例启动时会校验类型和对象归属。</summary>
        StateLogic CreateLogic(StateFactoryContext context);
        /// <summary>为一次激活分配所需的记忆对象，不能返回供其他实例共享的可变记忆。</summary>
        StateActivationMemory CreateMemory();
        /// <summary>将源激活的玩法字段复制到目标记忆，供准备候选和确认提交使用。</summary>
        void CopyMemory(StateActivationMemory source, StateActivationMemory destination);
        /// <summary>将该记忆复位为新激活的初始状态，避免复用到上次进入的玩法值。</summary>
        void ResetMemory(StateActivationMemory memory);
        /// <summary>取得当前激活记忆的语义指纹，供快照对照或恢复检查。</summary>
        ulong GetMemoryFingerprint(StateActivationMemory memory);
    }

    /// <summary>用项目提供的创建与复制函数实现特定 Logic 和记忆类型的实例绑定。</summary>
    public sealed class StateRuntimeFactory<TLogic, TMemory> : IStateRuntimeFactory
        where TLogic : StateLogic, IStateUpdate
        where TMemory : StateActivationMemory
    {
        private readonly Func<StateFactoryContext, TLogic> _logicFactory;
        private readonly Func<TMemory> _memoryFactory;
        private readonly Action<TMemory, TMemory> _memoryCopier;
        private readonly Action<TMemory> _memoryResetter;
        private readonly Func<TMemory, ulong> _memoryFingerprinter;

        public Type LogicType => typeof(TLogic);
        public Type ActivationMemoryType => typeof(TMemory);

        #region Public API

        /// <summary>保存创建 Logic、分配、复制、复位和计算记忆指纹的委托，缺少任一委托时抛出异常。</summary>
        public StateRuntimeFactory(
            Func<StateFactoryContext, TLogic> logicFactory,
            Func<TMemory> memoryFactory,
            Action<TMemory, TMemory> memoryCopier,
            Action<TMemory> memoryResetter,
            Func<TMemory, ulong> memoryFingerprinter)
        {
            _logicFactory = logicFactory ?? throw new ArgumentNullException(nameof(logicFactory));
            _memoryFactory = memoryFactory ?? throw new ArgumentNullException(nameof(memoryFactory));
            _memoryCopier = memoryCopier ?? throw new ArgumentNullException(nameof(memoryCopier));
            _memoryResetter = memoryResetter ?? throw new ArgumentNullException(nameof(memoryResetter));
            _memoryFingerprinter = memoryFingerprinter ??
                                   throw new ArgumentNullException(nameof(memoryFingerprinter));
        }

        /// <summary>根据节点的冻结配置创建供该实例独占的 Logic，实例启动时会校验类型和对象归属。</summary>
        public StateLogic CreateLogic(StateFactoryContext context) =>
            _logicFactory(context) ??
            throw new InvalidOperationException("A State runtime factory returned null logic.");

        /// <summary>为一次激活分配所需的记忆对象，不能返回供其他实例共享的可变记忆。</summary>
        public StateActivationMemory CreateMemory() =>
            _memoryFactory() ??
            throw new InvalidOperationException("A State runtime factory returned null memory.");

        /// <summary>将源激活的玩法字段复制到目标记忆，供准备候选和确认提交使用。</summary>
        public void CopyMemory(StateActivationMemory source, StateActivationMemory destination)
        {
            if (!(source is TMemory typedSource) || !(destination is TMemory typedDestination))
            {
                throw new InvalidOperationException("State memory does not match its runtime factory.");
            }

            _memoryCopier(typedSource, typedDestination);
        }

        /// <summary>将该记忆复位为新激活的初始状态，避免复用到上次进入的玩法值。</summary>
        public void ResetMemory(StateActivationMemory memory)
        {
            if (!(memory is TMemory typedMemory))
            {
                throw new InvalidOperationException("State memory does not match its runtime factory.");
            }

            _memoryResetter(typedMemory);
        }

        /// <summary>取得当前激活记忆的语义指纹，供快照对照或恢复检查。</summary>
        public ulong GetMemoryFingerprint(StateActivationMemory memory)
        {
            if (!(memory is TMemory typedMemory))
            {
                throw new InvalidOperationException("State memory does not match its runtime factory.");
            }

            return _memoryFingerprinter(typedMemory);
        }

        #endregion
    }

    /// <summary>将已编译的状态描述符绑定到对应项目工厂。</summary>
    public sealed class StateGraphLogicBindingsBuilder
    {
        private readonly CompiledStateGraph _graph;
        private readonly Dictionary<StateDescriptorId, StateDescriptor> _requiredStates;
        private readonly Dictionary<StateDescriptorId, IStateRuntimeFactory> _stateFactories;
        private bool _isFrozen;

        public CompiledStateGraph Graph => _graph;
        public bool IsFrozen => _isFrozen;
        public int RequiredStateFactoryCount => _requiredStates.Count;

        #region Public API

        /// <summary>为给定的已编译图建立尚可登记项目工厂的绑定集合。</summary>
        public StateGraphLogicBindingsBuilder(CompiledStateGraph graph)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _requiredStates = new Dictionary<StateDescriptorId, StateDescriptor>();
            _stateFactories = new Dictionary<StateDescriptorId, IStateRuntimeFactory>();
            CollectRequirements(graph, _requiredStates);
        }
        /// <summary>为已编译图中的描述符绑定 Logic 和记忆工厂，类型不匹配或重复绑定时返回 false 和诊断。</summary>
        public bool TryBindState(
            StateDescriptorId descriptorId,
            IStateRuntimeFactory factory,
            out CoCoDiagnostic diagnostic)
        {
            if (!CanBind(out diagnostic))
            {
                return false;
            }

            if (!_requiredStates.TryGetValue(descriptorId, out StateDescriptor descriptor) || factory == null)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "State runtime bindings may contain only descriptors required by this Graph.");
                return false;
            }

            if (_stateFactories.ContainsKey(descriptorId))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DuplicateIdentifier,
                    "A Graph may bind each State descriptor exactly once.");
                return false;
            }

            if (factory.LogicType != descriptor.LogicType ||
                factory.ActivationMemoryType != descriptor.ActivationMemoryType ||
                !typeof(IStateUpdate).IsAssignableFrom(factory.LogicType))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "State logic or StateActivationMemory factory type does not match the compiled descriptor.");
                return false;
            }

            _stateFactories.Add(descriptorId, factory);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        /// <summary>验证节点的运行绑定是否齐全，成功生成只读绑定，缺项时返回 false 和诊断。</summary>
        public bool TryFreeze(
            out StateGraphLogicBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                bindings = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph logic bindings are already frozen.");
                return false;
            }

            _isFrozen = true;
            if (_stateFactories.Count != _requiredStates.Count)
            {
                bindings = null;
                diagnostic = Error(
                    CoCoDiagnosticCode.MissingDescriptor,
                    "StateGraph runtime bindings must exactly cover every compiled State descriptor.");
                return false;
            }

            bindings = new StateGraphLogicBindings(
                _graph,
                _stateFactories);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        #endregion

        #region Internal Logic

        private bool CanBind(out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph logic bindings are frozen.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static void CollectRequirements(
            CompiledStateGraph graph,
            Dictionary<StateDescriptorId, StateDescriptor> states)
        {
            for (int layerIndex = 0; layerIndex < graph.Layers.Count; layerIndex++)
            {
                CompiledStateLayer layer = graph.Layers[layerIndex];
                for (int stateIndex = 0; stateIndex < layer.States.Count; stateIndex++)
                {
                    StateDescriptor descriptor = layer.States[stateIndex].Descriptor;
                    if (!states.ContainsKey(descriptor.DescriptorId))
                    {
                        states.Add(descriptor.DescriptorId, descriptor);
                    }
                }

            }
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);

        #endregion
    }

    /// <summary>按描述符为运行实例提供冻结后的 Logic 和激活记忆工厂。</summary>
    public sealed class StateGraphLogicBindings
    {
        private readonly Dictionary<StateDescriptorId, IStateRuntimeFactory> _stateFactories;

        public CompiledStateGraph Graph { get; }
        public int StateFactoryCount => _stateFactories.Count;

        #region Internal Logic

        internal StateGraphLogicBindings(
            CompiledStateGraph graph,
            Dictionary<StateDescriptorId, IStateRuntimeFactory> stateFactories)
        {
            Graph = graph;
            _stateFactories = new Dictionary<StateDescriptorId, IStateRuntimeFactory>(stateFactories);
        }
        internal bool TryGetStateFactory(
            StateDescriptorId descriptorId,
            out IStateRuntimeFactory factory) =>
            _stateFactories.TryGetValue(descriptorId, out factory);

        #endregion

    }
}
