using System;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoStateEventReadView
    {
        private readonly CoCoStateExecutionContext _context;
        private readonly ulong _generation;
        internal CoCoStateEventReadView(CoCoStateExecutionContext context, ulong generation)
        {
            _context = context;
            _generation = generation;
        }
        public bool TryGetBatch<TEvent>(out CoCoStateEventBatch<TEvent> batch) where TEvent : unmanaged
        {
            batch = default;
            return _context != null && _context.TryGetEventBatch(_generation, out batch);
        }
    }

    public readonly struct CoCoStateEventBatch<TEvent> where TEvent : unmanaged
    {
        private readonly CoCoStateExecutionContext _context;
        private readonly ulong _generation;
        private readonly CoCoStateEventLane<TEvent> _lane;
        internal CoCoStateEventBatch(CoCoStateExecutionContext context, ulong generation,
            CoCoStateEventLane<TEvent> lane)
        {
            _context = context;
            _generation = generation;
            _lane = lane;
        }
        public bool IsValid => _context != null && _context.IsEventCallbackCurrent(_generation);
        public int Count => IsValid ? _lane.Count : 0;
        public bool TryRead(int index, out TEvent payload)
        {
            payload = default;
            if (!IsValid || (uint)index >= (uint)_lane.Count) return false;
            payload = _lane.Read(index);
            return true;
        }
    }

    public readonly struct CoCoStateEventOutboxWriter
    {
        private readonly CoCoStateExecutionContext _context;
        private readonly ulong _generation;
        internal CoCoStateEventOutboxWriter(CoCoStateExecutionContext context, ulong generation)
        {
            _context = context;
            _generation = generation;
        }
        public void Publish<TEvent>(in TEvent payload) where TEvent : unmanaged
        {
            if (_context == null) throw new InvalidOperationException("State event writer is unavailable.");
            _context.PublishEvent(_generation, in payload);
        }
    }

    internal interface ICoCoStateEventOutbox
    {
        void Publish<TEvent>(in TEvent payload) where TEvent : unmanaged;
    }

}
