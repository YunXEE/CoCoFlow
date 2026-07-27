using System;

namespace CoCoFlow.Runtime.Modules.Input
{
    public sealed class InputCommandQueue<TCommand>
        where TCommand : unmanaged
    {
        public const int DefaultCapacity = 32;

        private readonly InputCommand<TCommand>[] _items;
        private int _head;
        private int _count;

        public InputCommandQueue(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new InputCommand<TCommand>[capacity];
        }

        public int Count => _count;
        public int Capacity => _items.Length;
        public ulong OverflowCount { get; private set; }

        public bool TryEnqueue(
            in TCommand command,
            InputCommandPhase phase,
            ulong sequence) =>
            TryEnqueue(new InputCommand<TCommand>(command, phase, sequence));

        public bool TryEnqueue(in InputCommand<TCommand> command)
        {
            if (!command.IsValid)
            {
                return false;
            }

            if (_count == _items.Length)
            {
                OverflowCount++;
                return false;
            }

            int tail = (_head + _count) % _items.Length;
            _items[tail] = command;
            _count++;
            return true;
        }

        public int DrainTo(ref InputCommandBatch<TCommand> batch)
        {
            int drained = 0;
            while (_count > 0 && batch.Count < InputCommandBatch<TCommand>.Capacity)
            {
                InputCommand<TCommand> command = _items[_head];
                if (!batch.TryAdd(command))
                {
                    break;
                }

                _items[_head] = default;
                _head = (_head + 1) % _items.Length;
                _count--;
                drained++;
            }

            return drained;
        }

        public void Clear()
        {
            Array.Clear(_items, 0, _items.Length);
            _head = 0;
            _count = 0;
        }

        public void ClearOverflowCount()
        {
            OverflowCount = 0UL;
        }
    }
}
