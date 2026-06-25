using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Item;
using CoCoFlow.Runtime.Modules.Persistence;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.ChestSamples
{
    public sealed class CCS_Chest_Opening : CCS_Chest_StateBase
    {
        [Header("Container Path")]
        [SerializeField] private string rewardId = ChestSampleSceneInstaller.ChestRewardId;
        [SerializeField] private string targetInventoryContainerId =
            ChestSampleSceneInstaller.PlayerInventoryContainerId;
        [SerializeField] private string openedFactId = ChestSampleSceneInstaller.ChestOpenedFactId;
        [SerializeField] private string worldFactContainerId = ChestSampleSceneInstaller.WorldFactContainerId;
        [SerializeField] private string openedEventId = ChestSampleSceneInstaller.ChestOpenedEventId;
        [SerializeField] private string eventLogContainerId = ChestSampleSceneInstaller.EventLogContainerId;

        [Header("Timing")]
        [SerializeField] private float openDuration = 0.1f;

        private bool containerCommandsDispatched;
        private float finishTime;

        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);

            containerCommandsDispatched = false;
            finishTime = Time.time + Mathf.Max(0f, openDuration);
            if (ItemLifeCycle != null)
            {
                ItemLifeCycle.SetOpening();
            }
            else
            {
                (context as ItemContext)?.SetOpening();
            }
            DispatchContainerCommands();
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);
            if (Time.time < finishTime) return;

            if (ItemLifeCycle != null)
            {
                ItemLifeCycle.SetOpened();
            }
            else
            {
                var itemContext = context as ItemContext;
                itemContext?.SetOpened();
                itemContext?.Intent.Clear();
            }

            if (IfHasState<CCS_Chest_Opened>())
            {
                ChangeState<CCS_Chest_Opened>();
            }
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder
                .WritesContext<ItemContext>("ItemState.Opening", "Context path records that the chest is opening")
                .WritesContext<ItemContext>("ItemState.Opened", "Context path restores the opened chest on load")
                .UsesOperation<PersistenceContainerBridge>("Container path grants reward and writes world fact")
                .CanTransitionTo<CCS_Chest_Opened>("Open duration finished");
        }

        #endregion

        #region Internal Logic

        private void DispatchContainerCommands()
        {
            if (containerCommandsDispatched || ContainerBridge == null) return;

            containerCommandsDispatched = true;
            ContainerBridge.RequestGrantReward(rewardId, targetInventoryContainerId);
            ContainerBridge.RequestSetFactBool(openedFactId, true, worldFactContainerId);
            ContainerBridge.RequestSetEventState(openedEventId, "Opened", eventLogContainerId);
        }

        #endregion
    }
}
