using CoCoFlow.Runtime.Addon.Network.Input;
using Fusion;

namespace CoCoFlow.Runtime.Addon.Network.Character
{
    /// <summary>
    /// 网络角色的中枢 NetworkBehaviour，负责在 Fusion Tick 中消费玩家输入。
    /// Transform 同步交给 Fusion.NetworkTransform，避免首轮骨架依赖自定义 [Networked] 属性。
    /// </summary>
    public class NetCharacter : NetworkBehaviour
    {
        private NetCharacterMotor _motor;

        #region Unity + Fusion Lifecycle

        public override void Spawned()
        {
            _motor = GetComponent<NetCharacterMotor>();

            if (_motor == null)
                _motor = gameObject.AddComponent<NetCharacterMotor>();
        }

        public override void FixedUpdateNetwork()
        {
            if (_motor == null) return;

            if (GetInput<NetPlayerInput>(out var input))
            {
                var inputDir = input.MoveDirection;

                if (Object.HasStateAuthority)
                {
                    _motor.Simulate(inputDir, Runner.DeltaTime);
                }
            }
        }

        #endregion
    }
}
