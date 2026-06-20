using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Character;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.PlayerSamples
{
    public class CCS_Player_Interact : CCS_Player_StateBase
    {
        [Header("Interact")]
        [SerializeField] private float interactDuration = 0.25f;

        private float _finishTime;

        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);
            _finishTime = Time.time + interactDuration;
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);
            if (Time.time >= _finishTime)
            {
                ChangeToBestAvailableState(context as CharacterContext);
            }
        }

        #endregion
    }
}
