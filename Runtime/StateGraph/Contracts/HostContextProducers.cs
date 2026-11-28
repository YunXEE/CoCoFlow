using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CoCoFlow.Runtime.Core;

// 将单个 Host 所用的图状态、玩法值和 Operator 占用映射到纯 Context 布局。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>区分图中的状态记录、派生玩法值与 Operator 占用。</summary>
    internal enum GraphContextProducerKind
    {
        State = 1,
        Value = 2,
        Claim = 3
    }

    /// <summary>把一个图内供值来源固定到同一份布局中的块和槽。</summary>
    internal interface IGraphContextProducerBinding
    {
        GraphContextProducerKind Kind { get; }
        StateBlockId BlockId { get; }
        StateSlotId SlotId { get; }
        Type ValueType { get; }

        bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>把实例的激活记忆映射到状态记录，并从已验证的恢复数据准备候选记忆。</summary>
    internal interface IGraphStateContextBinding : IGraphContextProducerBinding
    {
        LayerId LayerId { get; }
        StateId StateId { get; }
        Type MemoryType { get; }

        bool TryCapture(
            ulong token,
            StateActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic);

        bool TryValidateInitialDefault(
            StateActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            ContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic);

        bool TryGetCaptured<TState>(
            ulong token,
            out GraphStateRecord<TState> record)
            where TState : unmanaged;

        bool TryGetCapturedHeader(
            ulong token,
            out bool isOnActivePath);

        bool TryReadRestoreHeader(
            ContextRestoreReadView source,
            out StateGraphRestoreState header);

        bool TryPrepareRestore(
            ContextRestoreReadView source,
            StateActivationMemory candidateMemory,
            out CoCoDiagnostic diagnostic);

        void ClearCapture();
    }

    /// <summary>从候选图与 Operation 读取数据，将其写入已声明的图槽。</summary>
    internal interface IGraphValueContextBinding : IGraphContextProducerBinding
    {
        bool TryCapture(
            in GraphContextCaptureContext context,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic);
    }

    /// <summary>保存一个状态与它的记忆绑定。已捕获值只在本次采集凭据有效时可读。</summary>
    internal sealed class GraphStateContextBinding<TMemory, TState, TBinding> :
        IGraphStateContextBinding
        where TMemory : StateActivationMemory
        where TState : unmanaged
        where TBinding : IActivationMemoryStateBinding<TMemory, TState>
    {
        private readonly TBinding _binding;
        private StateBlockHandle _block;
        private StateSlot<GraphStateRecord<TState>> _slot;
        private GraphStateRecord<TState> _captured;
        private ulong _captureToken;

        internal GraphStateContextBinding(
            LayerId layerId,
            StateId stateId,
            StateBlockId blockId,
            StateSlotId slotId,
            TBinding binding)
        {
            LayerId = layerId;
            StateId = stateId;
            BlockId = blockId;
            SlotId = slotId;
            _binding = binding;
        }

        public GraphContextProducerKind Kind => GraphContextProducerKind.State;
        public LayerId LayerId { get; }
        public StateId StateId { get; }
        public StateBlockId BlockId { get; }
        public StateSlotId SlotId { get; }
        public Type ValueType => typeof(GraphStateRecord<TState>);
        public Type MemoryType => typeof(TMemory);

        public bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            if (layout == null ||
                _binding.SemanticFingerprint == 0UL ||
                !layout.TryResolveBlock(BlockId, out _block) ||
                _block.Owner != StateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out _slot) ||
                FindSlot(layout, SlotId)?.RestorePolicy == ContextRestorePolicy.Derived)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Graph State producer must resolve one writable Graph-owned State record Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCapture(
            ulong token,
            StateActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic)
        {
            if (token == 0UL || !(memory is TMemory typedMemory))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph State memory did not match its registered portable binding.");
                return false;
            }

            TState state;
            try
            {
                if (!_binding.TryCapture(typedMemory, out state, out diagnostic) || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.ContextCaptureFailed,
                            "Graph State memory binding rejected candidate capture.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph State memory binding threw during candidate capture.");
                return false;
            }

            if (!GraphStateRecord<TState>.TryCreate(
                    LayerId,
                    StateId,
                    isOnActivePath,
                    activationId,
                    localSeconds,
                    actionProgress,
                    enterPending,
                    memoryFingerprint,
                    state,
                    out _captured) ||
                !prepared.TryGetWriter(_block, out ContextFrameWriter writer) ||
                !writer.Write(_slot, _captured))
            {
                _captured = default;
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph State record could not be written to its declared Context Slot.");
                return false;
            }

            _captureToken = token;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryValidateInitialDefault(
            StateActivationMemory memory,
            bool isOnActivePath,
            ActivationId activationId,
            double localSeconds,
            double actionProgress,
            bool enterPending,
            ulong memoryFingerprint,
            ContextFrameReadView defaults,
            out CoCoDiagnostic diagnostic)
        {
            if (!(memory is TMemory typedMemory) || !defaults.IsValid)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Initial Graph State validation received incompatible memory or defaults.");
                return false;
            }

            TState state;
            try
            {
                if (!_binding.TryCapture(typedMemory, out state, out diagnostic) || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.ContextCaptureFailed,
                            "Initial Graph State capture was rejected.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Initial Graph State capture threw.");
                return false;
            }

            if (!GraphStateRecord<TState>.TryCreate(
                    LayerId,
                    StateId,
                    isOnActivePath,
                    activationId,
                    localSeconds,
                    actionProgress,
                    enterPending,
                    memoryFingerprint,
                    state,
                    out GraphStateRecord<TState> captured) ||
                defaults.Read(_slot) != captured)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Trusted Layout defaults do not match the Runtime's initial Graph authority.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryGetCaptured<TRequested>(
            ulong token,
            out GraphStateRecord<TRequested> record)
            where TRequested : unmanaged
        {
            if (token == 0UL || token != _captureToken || typeof(TRequested) != typeof(TState))
            {
                record = default;
                return false;
            }

            ReadOnlySpan<GraphStateRecord<TState>> source =
                MemoryMarshal.CreateReadOnlySpan(ref _captured, 1);
            record = MemoryMarshal.Cast<
                GraphStateRecord<TState>,
                GraphStateRecord<TRequested>>(source)[0];
            return true;
        }

        public bool TryGetCapturedHeader(
            ulong token,
            out bool isOnActivePath)
        {
            if (token == 0UL || token != _captureToken)
            {
                isOnActivePath = false;
                return false;
            }

            isOnActivePath = _captured.IsOnActivePath;
            return true;
        }

        public bool TryReadRestoreHeader(
            ContextRestoreReadView source,
            out StateGraphRestoreState header)
        {
            if (!source.TryRead(_slot, out GraphStateRecord<TState> record))
            {
                header = default;
                return false;
            }

            header = new StateGraphRestoreState(
                record.LayerId,
                record.StateId,
                record.IsOnActivePath,
                record.ActivationId,
                record.LocalSeconds,
                record.ActionProgress,
                record.EnterPending,
                record.MemoryFingerprint,
                record.IsValid);
            return header.IsValid;
        }

        public bool TryPrepareRestore(
            ContextRestoreReadView source,
            StateActivationMemory candidateMemory,
            out CoCoDiagnostic diagnostic)
        {
            if (!source.TryRead(_slot, out GraphStateRecord<TState> record) ||
                !(candidateMemory is TMemory typedMemory))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Graph restore memory did not match its registered State binding.");
                return false;
            }

            try
            {
                if (!_binding.TryPrepareRestore(record.State, typedMemory, out diagnostic) ||
                    diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.InvalidGraphRestore,
                            "Graph State memory binding rejected restore preparation.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidGraphRestore,
                    "Graph State memory binding threw during restore preparation.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public void ClearCapture()
        {
            _captureToken = 0UL;
            _captured = default;
        }

        private static StateSlotDescriptor FindSlot(
            ContextFrameLayout layout,
            StateSlotId slotId)
        {
            for (int index = 0; index < layout.Slots.Count; index++)
            {
                if (layout.Slots[index].SlotId == slotId)
                {
                    return layout.Slots[index];
                }
            }

            return null;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);
    }

    /// <summary>在当前图采集期间调用项目供值器，并检查写入目标。</summary>
    internal sealed class GraphValueContextBinding<TValue, TProducer> :
        IGraphValueContextBinding
        where TValue : unmanaged
        where TProducer : IGraphContextValueProducer<TValue>
    {
        private readonly TProducer _producer;
        private StateBlockHandle _block;
        private StateSlot<TValue> _slot;

        internal GraphValueContextBinding(
            StateBlockId blockId,
            StateSlotId slotId,
            TProducer producer)
        {
            BlockId = blockId;
            SlotId = slotId;
            _producer = producer;
        }

        public GraphContextProducerKind Kind => GraphContextProducerKind.Value;
        public StateBlockId BlockId { get; }
        public StateSlotId SlotId { get; }
        public Type ValueType => typeof(TValue);

        public bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            if (layout == null ||
                _producer.SemanticFingerprint == 0UL ||
                !layout.TryResolveBlock(BlockId, out _block) ||
                _block.Owner != StateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out _slot))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Graph value producer must resolve one writable Graph-owned Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryCapture(
            in GraphContextCaptureContext context,
            in PreparedContextCommit prepared,
            out CoCoDiagnostic diagnostic)
        {
            TValue value;
            try
            {
                if (!_producer.TryProduce(context, out value, out diagnostic) || diagnostic.IsError)
                {
                    if (!diagnostic.IsError)
                    {
                        diagnostic = Error(
                            CoCoDiagnosticCode.ContextCaptureFailed,
                            "Graph value producer rejected candidate capture.");
                    }

                    return false;
                }
            }
            catch (Exception)
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph value producer threw during candidate capture.");
                return false;
            }

            if (!prepared.TryGetWriter(_block, out ContextFrameWriter writer) ||
                !writer.Write(_slot, value))
            {
                diagnostic = Error(
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Graph value could not be written to its declared Context Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic Error(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Context, code, message);
    }

    /// <summary>将一个 Operator 资源占用映射到可写的图槽。</summary>
    internal sealed class GraphClaimContextBinding : IGraphContextProducerBinding
    {
        internal GraphClaimContextBinding(
            StateBlockId blockId,
            StateSlotId slotId)
        {
            BlockId = blockId;
            SlotId = slotId;
        }

        public GraphContextProducerKind Kind => GraphContextProducerKind.Claim;
        public StateBlockId BlockId { get; }
        public StateSlotId SlotId { get; }
        public Type ValueType => typeof(OperatorClaimState);

        public bool TryResolve(
            ContextFrameLayout layout,
            out CoCoDiagnostic diagnostic)
        {
            StateSlotDescriptor descriptor = null;
            for (int index = 0; layout != null && index < layout.Slots.Count; index++)
            {
                if (layout.Slots[index].SlotId == SlotId)
                {
                    descriptor = layout.Slots[index];
                    break;
                }
            }

            if (layout == null ||
                !layout.TryResolveBlock(BlockId, out StateBlockHandle block) ||
                block.Owner != StateBlockOwner.Graph ||
                !layout.TryResolveSlot(SlotId, out StateSlot<OperatorClaimState> slot) ||
                descriptor?.RestorePolicy == ContextRestorePolicy.Derived)
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidContextProducer,
                    "Claim producer must resolve one writable Graph-owned Claim State Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }
}
