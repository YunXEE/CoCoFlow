using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    [Serializable]
    public class CoCoInputIntent : ICoCoIntent
    {
        public Vector2 move;
        public Vector2 look;
        public Vector2 zoom;
        public string performedAction;
        public string canceledAction;
        public int performedSequence;
        public int canceledSequence;

        public void ClearContinuous()
        {
            move = Vector2.zero;
            look = Vector2.zero;
            zoom = Vector2.zero;
        }

        public void ClearDiscrete()
        {
            performedAction = string.Empty;
            canceledAction = string.Empty;
        }

        public void Clear()
        {
            ClearContinuous();
            ClearDiscrete();
            performedSequence = 0;
            canceledSequence = 0;
        }
    }

    public interface IInputStateProvider
    {
        Vector2 MoveInput { get; }
        Vector2 LookInput { get; }
        Vector2 ZoomInput { get; }
    }

    public interface IInputEventSource
    {
        event Action<string> OnActionPerformed;
        event Action<string> OnActionCanceled;

        bool TryConsumeBufferedAction(string actionName);
    }

    public interface IInputModeController
    {
        void SwitchActionMap(string mapName);
        void ClearBuffer();
    }

    public static class InputMapNames
    {
        public const string Player = "Player";
        public const string UI = "UI";
        public const string None = "None";
    }
}
