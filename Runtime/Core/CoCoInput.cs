using System;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// 仅暴露每帧轮询用的输入向量。给 Camera / Locomotion 等高频 poll 使用。
    /// </summary>
    public interface IInputStateProvider
    {
        /// <summary> 移动输入向量。 </summary>
        Vector2 MoveInput { get; }
        /// <summary> 视角/看向输入向量。 </summary>
        Vector2 LookInput { get; }
        /// <summary> 缩放/滚轮输入向量。 </summary>
        Vector2 ZoomInput { get; }
    }

    /// <summary>
    /// 按键事件流。给 UI / Widget / Gameplay 的离散动作触发使用。
    /// </summary>
    public interface IInputEventSource
    {
        /// <summary> 动作触发事件。 </summary>
        event Action<string> OnActionPerformed;
        /// <summary> 动作取消事件。 </summary>
        event Action<string> OnActionCanceled;

        /// <summary>
        /// 尝试消费 InputBuffer 中的目标动作（用于跳跃/攻击的提前输入容错）。
        /// </summary>
        bool TryConsumeBufferedAction(string actionName);
    }

    /// <summary>
    /// 控制输入模式。一般只有 UIManager / GameMode 这类高层会用到。
    /// </summary>
    public interface IInputModeController
    {
        /// <summary>
        /// 切换 ActionMap。使用字符串契约，避免 Core 被业务枚举污染。
        /// 推荐使用 InputMapNames 中的常量。
        /// </summary>
        void SwitchActionMap(string mapName);

        /// <summary>清空当前的输入缓冲。</summary>
        void ClearBuffer();
    }

    /// <summary>
    /// ActionMap 命名约定常量。集中放在 Core，方便所有上层共享，避免魔法字符串。
    /// 上层若有自定义 Map（如 "Driving"），可直接传字符串给 SwitchActionMap。
    /// </summary>
    public static class InputMapNames
    {
        public const string Player = "Player";
        public const string UI     = "UI";
        public const string None   = "None";
    }
}
