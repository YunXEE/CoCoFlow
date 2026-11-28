using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// 暂存一个 Host 本步产生的 State 事件和 Operator 事件。整步接受后，按原顺序发布。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>持有候选事件和通道内的位置。事务负责决定发布或丢弃的时点。</summary>
    internal sealed class HostEventOutbox : IStateEventOutbox, ICommittedEventPublisher
    {
        private readonly IEventOutboxLane[] _lanes;
        private readonly LaneEntry[] _ledger;
        private readonly List<IPendingStateEvent> _stateEvents = new List<IPendingStateEvent>();
        private readonly Dictionary<Type, IStateEventPool> _pools = new Dictionary<Type, IStateEventPool>();
        private int _ledgerCount;

        internal HostEventOutbox(IEventOutboxLane[] lanes, int capacity)
        {
            _lanes = lanes;
            _ledger = capacity == 0 ? Array.Empty<LaneEntry>() : new LaneEntry[capacity];
        }

        internal int Count => _stateEvents.Count + _ledgerCount;
        internal bool HasWriteFault { get; private set; }

        internal void RejectWrite() => HasWriteFault = true;

        internal EventOutboxWriteResult TryWrite<TEvent>(EventOutboxRequirement requirement,
            EventOutboxTarget target, in TEvent payload) where TEvent : unmanaged
        {
            if (Count >= _ledger.Length)
            {
                HasWriteFault = true;
                return EventOutboxWriteResult.CapacityExceeded;
            }

            for (int i = 0; i < _lanes.Length; i++)
            {
                IEventOutboxLane lane = _lanes[i];
                if (lane.Requirement != requirement) continue;
                if (!(lane is IEventOutboxLane<TEvent> typedLane))
                {
                    HasWriteFault = true;
                    return EventOutboxWriteResult.PayloadTypeMismatch;
                }

                if (!typedLane.TryAppend(target, payload, out int index))
                {
                    HasWriteFault = true;
                    return EventOutboxWriteResult.CapacityExceeded;
                }

                _ledger[_ledgerCount++] = new LaneEntry(i, index);
                return EventOutboxWriteResult.Accepted;
            }

            HasWriteFault = true;
            return EventOutboxWriteResult.UndeclaredEventType;
        }

        void IStateEventOutbox.Publish<TEvent>(in TEvent payload)
        {
            if (Count >= _ledger.Length)
            {
                HasWriteFault = true;
                return;
            }

            if (!_pools.TryGetValue(typeof(TEvent), out IStateEventPool pool))
            {
                pool = new StateEventPool<TEvent>();
                _pools.Add(typeof(TEvent), pool);
            }

            _stateEvents.Add(((StateEventPool<TEvent>)pool).Append(payload));
        }

        internal bool TryReserveSequences(ulong lastCommitted, out EventSequence first,
            out EventSequence last, out CoCoDiagnostic diagnostic)
        {
            first = last = default;
            diagnostic = CoCoDiagnostic.None;
            if (_ledgerCount == 0) return true;

            ulong count = (ulong)_ledgerCount;
            if (lastCommitted <= ulong.MaxValue - count &&
                EventSequence.TryCreate(lastCommitted + 1UL, out first) &&
                EventSequence.TryCreate(lastCommitted + count, out last))
            {
                return true;
            }

            diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.EventOutbox,
                CoCoDiagnosticCode.EventSequenceExhausted, "EventSequence cannot allocate the candidate Outbox range.");
            return false;
        }

        internal bool PublishCommitted(GraphInstanceId instanceId, in TickFrame tick,
            EventSequence first, StateFlowTraceBuffer trace, StateFlowTraceFrameReference frame,
            out CoCoDiagnostic diagnostic)
        {
            bool succeeded = true;
            foreach (IPendingStateEvent pending in _stateEvents)
            {
                try
                {
                    pending.Publish();
                }
                catch (Exception)
                {
                    succeeded = false;
                }
            }

            for (int i = 0; i < _ledgerCount; i++)
            {
                LaneEntry entry = _ledger[i];
                if (!EventSequence.TryCreate(first.Value + (ulong)i, out EventSequence sequence))
                {
                    succeeded = false;
                    continue;
                }
                var source = new CommittedEventSource(instanceId, tick.TimelineEpoch, tick.Tick, sequence);
                try
                {
                    if (!_lanes[entry.Lane].TryPublish(entry.Index, source, this))
                    {
                        succeeded = false;
                        continue;
                    }
                }
                catch (Exception)
                {
                    succeeded = false;
                    continue;
                }

                trace?.Append(StateFlowTraceEntry.Published(instanceId, tick, sequence, frame));
            }

            diagnostic = CoCoDiagnostic.None;
            if (!succeeded)
            {
                diagnostic = CoCoDiagnostic.Error(CoCoDiagnosticDomain.EventOutbox, CoCoDiagnosticCode.EventPublishFailed,
                    "One or more committed EventOutbox packets could not be published; assigned Sequences remain consumed.");
                trace?.Append(StateFlowTraceEntry.Diagnostic(instanceId, tick, diagnostic, frame: frame));
            }

            return succeeded;
        }

        bool ICommittedEventPublisher.TryPublish<TEvent>(in EventPacket<TEvent> packet)
        {
            EventPacket<TEvent> copy = packet;
            CoCoEventBus.Publish(ref copy);
            return true;
        }

        internal void Clear()
        {
            _stateEvents.Clear();
            foreach (IStateEventPool pool in _pools.Values) pool.Reset();
            foreach (IEventOutboxLane lane in _lanes) lane.Reset();
            _ledgerCount = 0;
            HasWriteFault = false;
        }

        /// <summary>记录一个 Operator 事件在冻结通道和通道缓冲区中的位置。</summary>
        private readonly struct LaneEntry
        {
            internal readonly int Lane;
            internal readonly int Index;
            internal LaneEntry(int lane, int index)
            {
                Lane = lane;
                Index = index;
            }
        }

        /// <summary>按 State 调用时的顺序发布暂存的载荷。</summary>
        private interface IPendingStateEvent
        {
            void Publish();
        }

        /// <summary>在步进完成或取消后归还本步使用的事件缓存。</summary>
        private interface IStateEventPool
        {
            void Reset();
        }

        /// <summary>复用同类型的 State 事件，已分配容量内的后续步进无需重新分配。</summary>
        private sealed class StateEventPool<TEvent> : IStateEventPool where TEvent : unmanaged
        {
            private readonly List<PendingStateEvent<TEvent>> _items = new List<PendingStateEvent<TEvent>>();
            private int _count;

            internal IPendingStateEvent Append(in TEvent payload)
            {
                if (_count == _items.Count) _items.Add(new PendingStateEvent<TEvent>());
                PendingStateEvent<TEvent> item = _items[_count++];
                item.Payload = payload;
                return item;
            }

            public void Reset() => _count = 0;
        }

        /// <summary>保存单个 State 事件的值，发布时把副本传给总线。</summary>
        private sealed class PendingStateEvent<TEvent> : IPendingStateEvent where TEvent : unmanaged
        {
            internal TEvent Payload;
            public void Publish()
            {
                TEvent copy = Payload;
                CoCoEventBus.Publish(ref copy);
            }
        }
    }
}
