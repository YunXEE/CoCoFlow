using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// Host 过滤后的直接事件先入各类型的待收队列，再为一次 State 回调交换成只读批次。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>供直接收件队列按步交换、清空或读取对应类型的事件。</summary>
    internal interface IStateEventLane
    {
        void Seal();
        void Clear();
    }

    /// <summary>按已注册的载荷类型为 State 冻结当前步的直接收件。</summary>
    internal sealed class StateEventInbox : IStateEventInboxReader
    {
        private readonly Dictionary<Type, IStateEventLane> _lanes = new Dictionary<Type, IStateEventLane>();
        private readonly int _capacity;
        private TickFrame _tick;
        internal StateEventInbox(int capacity) { _capacity = capacity; }
        internal StateEventLane<TEvent> Find<TEvent>() where TEvent : unmanaged =>
            _lanes.TryGetValue(typeof(TEvent), out var lane) ? (StateEventLane<TEvent>)lane : null;
        IStateEventBatchReader<TEvent> IStateEventInboxReader.Find<TEvent>() => Find<TEvent>();

        internal StateEventLane<TEvent> Register<TEvent>() where TEvent : unmanaged
        {
            var lane = Find<TEvent>();
            if (lane != null) return lane;
            lane = new StateEventLane<TEvent>(_capacity);
            _lanes.Add(typeof(TEvent), lane);
            return lane;
        }
        internal void Seal(in TickFrame tick)
        {
            if (_tick == tick) return;
            foreach (var lane in _lanes.Values) lane.Seal();
            _tick = tick;
        }
        internal void Clear()
        {
            foreach (var lane in _lanes.Values) lane.Clear();
            _tick = default;
        }
    }

    /// <summary>分开保存一个类型的待收事件和回调可见的已冻结批次。</summary>
    internal sealed class StateEventLane<TEvent> : IStateEventLane, IStateEventBatchReader<TEvent> where TEvent : unmanaged
    {
        private TEvent[] _incoming;
        private TEvent[] _sealed;
        private int _incomingCount;
        internal readonly List<IEventInboxAdapter<TEvent>> Adapters = new List<IEventInboxAdapter<TEvent>>();
        public int Count { get; private set; }
        internal StateEventLane(int capacity)
        {
            _incoming = new TEvent[capacity];
            _sealed = new TEvent[capacity];
        }
        internal bool TryEnqueue(in TEvent payload)
        {
            if (_incomingCount == _incoming.Length) return false;
            _incoming[_incomingCount++] = payload;
            return true;
        }
        public TEvent Read(int index) => _sealed[index];
        public void Seal()
        {
            var previous = _sealed;
            _sealed = _incoming;
            _incoming = previous;
            Count = _incomingCount;
            _incomingCount = 0;
        }
        public void Clear() { Count = _incomingCount = 0; }
    }
}
