using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_Attack : CCS_Player_StateBase
    {
        [Header("Attack")]
        [SerializeField] private float attackDuration = 0.35f;
        [SerializeField] private bool allowMoveDuringAttack;
        [SerializeField] private float attackMoveSpeed = 2f;

        private float _finishTime;

        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            _finishTime = Time.time + attackDuration;
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            if (allowMoveDuringAttack)
            {
                ApplyMove(characterContext, attackMoveSpeed);
            }

            if (Time.time < _finishTime) return;

            ChangeToBestAvailableState(characterContext);
        }

        #endregion
    }
}
