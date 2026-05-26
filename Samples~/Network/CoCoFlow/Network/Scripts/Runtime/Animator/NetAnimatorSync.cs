using Fusion;
using CoCoFlow.Runtime.Gameplay.Character;
using CoCoFlow.Runtime.Addon.Network.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.Animations
{
    /// <summary>
    /// 网络同步 Animator：通过 Fusion NetworkMecanimAnimator 将角色动画参数
    /// 从 StateAuthority/InputAuthority 同步到所有客户端。
    /// 所有动画参数统一使用 bool 类型（不使用 Trigger，避免回滚时序问题）。
    /// </summary>
    [RequireComponent(typeof(NetworkMecanimAnimator))]
    [RequireComponent(typeof(UnityEngine.Animator))]
    public class NetAnimatorSync : NetworkBehaviour
    {
        private NetworkMecanimAnimator _nma;
        private NetCharacterMotor _netMotor;
        private CharacterLocomotion _locomotion;
        private CharacterLifeCycle _lifecycle;

        // Animator 参数 hash —— 使用 bool 而非 Trigger，确保 Fusion 回滚时状态一致
        private static readonly int IsMovingHash = UnityEngine.Animator.StringToHash("IsMoving");
        private static readonly int IsJumpingHash = UnityEngine.Animator.StringToHash("IsJumping");
        private static readonly int IsDeadHash = UnityEngine.Animator.StringToHash("IsDead");

        public override void Spawned()
        {
            _nma = GetComponent<NetworkMecanimAnimator>();
            _netMotor = GetComponent<NetCharacterMotor>();
            _locomotion = GetComponent<CharacterLocomotion>();
            _lifecycle = GetComponent<CharacterLifeCycle>();
        }

        public override void FixedUpdateNetwork()
        {
            // 防止回滚期间的动画状态污染
            if (!Runner.IsForward) return;

            if (HasStateAuthority || HasInputAuthority)
                UpdateAnimationParameters();
        }

        #region Public API
        #endregion

        #region Internal Logic

        /// <summary>
        /// 从角色组件读取状态，通过 NetworkMecanimAnimator 同步到所有客户端。
        /// 注意：SetTrigger 方法名是历史命名，实际设置的是 bool 参数。
        /// </summary>
        private void UpdateAnimationParameters()
        {
            if (_nma == null) return;

            if (_netMotor != null)
            {
                bool isMoving = _netMotor.CurrentVelocity.magnitude > 0.1f;
                _nma.SetTrigger(IsMovingHash, isMoving);

                bool isJumping = !_netMotor.IsGrounded;
                _nma.SetTrigger(IsJumpingHash, isJumping);
            }
            else if (_locomotion != null)
            {
                // 单机/旧预制体兜底：没有 NetCharacterMotor 时仍可读取 CharacterLocomotion。
                bool isMoving = _locomotion.CurrentVelocity.magnitude > 0.1f;
                _nma.SetTrigger(IsMovingHash, isMoving);

                bool isJumping = !_locomotion.IsGrounded;
                _nma.SetTrigger(IsJumpingHash, isJumping);
            }

            // 死亡状态：_lifecycle 可能为 null（敌人不一定有 CharacterLifeCycle）
            bool isDead = _lifecycle != null && _lifecycle.IsDead;
            _nma.SetTrigger(IsDeadHash, isDead);
        }

        #endregion
    }
}
