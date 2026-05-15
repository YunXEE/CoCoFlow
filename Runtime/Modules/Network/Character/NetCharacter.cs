using System;
using Fusion;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Network.Character
{
    /// <summary>
    /// 网络角色的中枢 NetworkBehaviour，协调 NetCharacterMotion 和 NetStateSyncHandler。
    /// </summary>
    public class NetCharacter : NetworkBehaviour
    {
        [Header("Networked State")]
        [Networked] public Vector3 NetworkPosition { get; set; }
        [Networked] public Vector3 NetworkVelocity { get; set; }

        [Header("Components")]
        private NetCharacterMotion _motion;
        private NetStateSyncHandler _stateSync;
        private MonoBehaviour _locomotion; // CharacterLocomotion（反射代理，避免 asmdef 循环依赖）

        // 反射缓存的 Locomotion 方法委托（零反射热路径）
        private Action<Vector3> _setMovementVelocity;
        private Action<Vector3> _setRotation;
        private Func<Vector3> _getCurrentVelocity; // 缓存 CurrentVelocity getter 委托，避免 FUN 热路径反射装箱

        #region Unity + Fusion Lifecycle

        public override void Spawned()
        {
            _motion = GetComponent<NetCharacterMotion>();
            _stateSync = GetComponent<NetStateSyncHandler>();

            // 通过反射获取 CharacterLocomotion 并缓存方法委托，解耦程序集依赖
            CacheLocomotionDelegates();

            if (_motion != null)
            {
                _motion.Initialize(_setMovementVelocity, _setRotation);
            }

            if (Object.HasStateAuthority)
            {
                NetworkPosition = transform.position;
                NetworkVelocity = Vector3.zero;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (_motion == null) return;

            if (GetInput<NetPlayerInput>(out var input))
            {
                var inputDir = input.MoveDirection;

                if (Object.HasStateAuthority)
                {
                    _motion.ProcessMovement(inputDir);
                    NetworkPosition = transform.position;
                    NetworkVelocity = _getCurrentVelocity?.Invoke() ?? Vector3.zero;
                }
                else if (Object.HasInputAuthority)
                {
                    _motion.ProcessMovementPrediction(inputDir);
                }
            }
        }

        public override void Render()
        {
            if (_motion == null) return;

            if (!Object.HasStateAuthority && !Object.HasInputAuthority)
            {
                // 代理端：插值到权威端位置
                _motion.InterpolateTo(NetworkPosition, NetworkVelocity);
            }
        }

        #endregion

        #region Internal Logic

        /// <summary>
        /// 通过反射查找 CharacterLocomotion 组件并缓存 SetMovementVelocity / SetRotation 委托。
        /// CharacterLocomotion 位于 Runtime.Gameplay 程序集，Network 模块不直接引用，故使用反射。
        /// </summary>
        private void CacheLocomotionDelegates()
        {
            var locomotionType = Type.GetType("CoCoFlow.Runtime.Gameplay.Character.CharacterLocomotion, CoCoFlow.Runtime.Gameplay");
            if (locomotionType == null) return;

            var components = GetComponents<MonoBehaviour>();
            foreach (var comp in components)
            {
                if (locomotionType.IsInstanceOfType(comp))
                {
                    _locomotion = comp;
                    break;
                }
            }

            if (_locomotion == null)
            {
                // 尝试 GetComponent 通过类型名
                var comp = GetComponent(locomotionType);
                if (comp != null)
                {
                    _locomotion = comp as MonoBehaviour;
                }
            }

            if (_locomotion == null) return;

            var setVelMethod = locomotionType.GetMethod("SetMovementVelocity", new[] { typeof(Vector3) });
            var setRotMethod = locomotionType.GetMethod("SetRotation", new[] { typeof(Vector3) });

            if (setVelMethod != null)
                _setMovementVelocity = (Action<Vector3>)Delegate.CreateDelegate(typeof(Action<Vector3>), _locomotion, setVelMethod);
            if (setRotMethod != null)
                _setRotation = (Action<Vector3>)Delegate.CreateDelegate(typeof(Action<Vector3>), _locomotion, setRotMethod);

            var getVelProp = locomotionType.GetProperty("CurrentVelocity");
            if (getVelProp != null)
            {
                var getterMethod = getVelProp.GetGetMethod();
                _getCurrentVelocity = (Func<Vector3>)Delegate.CreateDelegate(typeof(Func<Vector3>), _locomotion, getterMethod);
            }
        }

        #endregion
    }
}
