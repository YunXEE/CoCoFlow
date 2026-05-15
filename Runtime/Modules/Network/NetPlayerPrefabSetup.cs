using UnityEngine;
using Fusion;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Modules.Network.Character;
using CoCoFlow.Runtime.Modules.Network.Lifecycle;
using CoCoFlow.Runtime.Modules.Network.Animator;

namespace CoCoFlow.Runtime.Modules.Network
{
    /// <summary>
    /// NetPlayer 预制体配置助手。
    /// 挂载到空 GameObject 后在 Inspector 中 Reset，即可自动添加 NetPlayer 预制体所需的全部组件。
    /// 此脚本仅用于 Editor 阶段的预制体创建辅助，运行时不执行任何逻辑。
    ///
    /// <para><b>最终预制体必须包含的组件清单（按添加顺序）：</b></para>
    /// <list type="number">
    /// <item><b>NetworkObject</b> (Fusion) — 网络对象根组件，所有 NetworkBehaviour 的宿主</item>
    /// <item><b>CharacterController</b> (Unity) — 角色移动碰撞体，CharacterLocomotion 依赖此组件</item>
    /// <item><b>CharacterLocomotion</b> (CoCoFlow.Character) — 驱动角色移动、旋转与重力计算</item>
    /// <item><b>CharacterLifeCycle</b> (CoCoFlow.Character) — 角色生命值系统，管理受伤/死亡/复活</item>
    /// <item><b>Animator</b> (Unity) — 动画状态机播放，需配置 Avatar 与 AnimatorController</item>
    /// <item><b>NetworkMecanimAnimator</b> (Fusion) — 跨网络同步 Animator 全部参数</item>
    /// <item><b>NetCharacter</b> (NetworkBehaviour) — 网络角色中枢，协调 NetCharacterMotion 与 NetStateSyncHandler</item>
    /// <item><b>NetCharacterMotion</b> (NetworkBehaviour) — 基于 Fusion FixedUpdateNetwork 的角色运动驱动</item>
    /// <item><b>NetStateSyncHandler</b> (NetworkBehaviour) — 跨网络同步 CoCoStateMachineController 状态</item>
    /// <item><b>NetCharacterLifecycle</b> (NetworkBehaviour) — 网络同步生命值，通过 RPC 桥接至 CoCoEventBus</item>
    /// <item><b>NetAnimatorSync</b> (NetworkBehaviour) — 从 CharacterLocomotion/CharacterLifeCycle 读取状态并同步动画参数</item>
    /// </list>
    ///
    /// <para><b>创建步骤：</b></para>
    /// <para>1. 创建空 GameObject，命名为 NetPlayer</para>
    /// <para>2. 挂载此脚本</para>
    /// <para>3. 在 Inspector 中右键 → Reset（或 Add Component 时自动触发）</para>
    /// <para>4. 所有组件添加完毕后，可移除此脚本或将 GameObject 拖入 Project 窗口创建 .prefab</para>
    /// </summary>
    [HelpURL("https://github.com/yunxee/CoCoFlow/wiki/Prefab-Setup#netplayer")]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(CharacterLocomotion))]
    [RequireComponent(typeof(CharacterLifeCycle))]
    [RequireComponent(typeof(Animator))]
    [RequireComponent(typeof(NetworkMecanimAnimator))]
    public class NetPlayerPrefabSetup : MonoBehaviour
    {
        #region Editor Setup

        /// <summary>
        /// Editor 中 Reset 时自动添加所有缺失的组件。已存在的组件不会重复添加。
        /// </summary>
        private void Reset()
        {
            AddIfMissing<NetworkObject>();
            AddIfMissing<CharacterController>();
            AddIfMissing<CharacterLocomotion>();
            AddIfMissing<CharacterLifeCycle>();
            AddIfMissing<Animator>();
            AddIfMissing<NetworkMecanimAnimator>();
            AddIfMissing<NetCharacter>();
            AddIfMissing<NetCharacterMotion>();
            AddIfMissing<NetStateSyncHandler>();
            AddIfMissing<NetCharacterLifecycle>();
            AddIfMissing<NetAnimatorSync>();
        }

        #endregion

        #region Internal Logic

        private void AddIfMissing<T>() where T : Component
        {
            if (GetComponent<T>() == null)
                gameObject.AddComponent<T>();
        }

        #endregion
    }
}
