using System;
using UnityEngine.InputSystem;

namespace CoCoFlow.Runtime.Modules.Input
{
    public enum InputCommandPhase : byte
    {
        Performed = 1,
        Canceled = 2
    }

    public readonly struct InputCommand<TCommand>
        where TCommand : unmanaged
    {
        public InputCommand(
            in TCommand command,
            InputCommandPhase phase,
            ulong sequence)
        {
            Command = command;
            Phase = phase;
            Sequence = sequence;
        }

        public TCommand Command { get; }
        public InputCommandPhase Phase { get; }
        public ulong Sequence { get; }
        public bool IsValid => Sequence != 0UL &&
                               (Phase == InputCommandPhase.Performed ||
                                Phase == InputCommandPhase.Canceled);
    }

    public struct InputCommandBatch<TCommand>
        where TCommand : unmanaged
    {
        public const int Capacity = 8;

        private InputCommand<TCommand> _item0;
        private InputCommand<TCommand> _item1;
        private InputCommand<TCommand> _item2;
        private InputCommand<TCommand> _item3;
        private InputCommand<TCommand> _item4;
        private InputCommand<TCommand> _item5;
        private InputCommand<TCommand> _item6;
        private InputCommand<TCommand> _item7;
        private int _count;

        public int Count => _count;

        public bool TryGet(int index, out InputCommand<TCommand> command)
        {
            if (index < 0 || index >= _count)
            {
                command = default;
                return false;
            }

            switch (index)
            {
                case 0: command = _item0; return true;
                case 1: command = _item1; return true;
                case 2: command = _item2; return true;
                case 3: command = _item3; return true;
                case 4: command = _item4; return true;
                case 5: command = _item5; return true;
                case 6: command = _item6; return true;
                case 7: command = _item7; return true;
                default:
                    command = default;
                    return false;
            }
        }

        internal bool TryAdd(in InputCommand<TCommand> command)
        {
            if (_count >= Capacity || !command.IsValid)
            {
                return false;
            }

            switch (_count)
            {
                case 0: _item0 = command; break;
                case 1: _item1 = command; break;
                case 2: _item2 = command; break;
                case 3: _item3 = command; break;
                case 4: _item4 = command; break;
                case 5: _item5 = command; break;
                case 6: _item6 = command; break;
                case 7: _item7 = command; break;
                default: return false;
            }

            _count++;
            return true;
        }
    }

    public readonly struct InputActionEvent
    {
        public InputActionEvent(InputAction action, InputActionPhase phase)
        {
            Action = action;
            Phase = phase;
        }

        public InputAction Action { get; }
        public InputActionPhase Phase { get; }
        public bool IsValid => Action != null &&
                               (Phase == InputActionPhase.Performed ||
                                Phase == InputActionPhase.Canceled);
    }

    public interface IInputBindingOverrideStore
    {
        bool TryLoad(string storageKey, out string overrideJson);

        bool TrySave(string storageKey, string overrideJson);
    }
}
