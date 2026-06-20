using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_Move : CCS_Player_StateBase
    {
        [Header("Move")]
        [SerializeField] private float moveSpeed = 5f;

        #region Public API

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var characterContext = context as CharacterContext;
            if (!HasMoveInput(characterContext) ||
                characterContext?.Intent.attack == true ||
                characterContext?.Intent.interact == true ||
                characterContext?.Intent.jump == true)
            {
                ChangeToBestAvailableState(characterContext);
                return;
            }

            ApplyMove(characterContext, moveSpeed);
        }

        #endregion
    }
}
