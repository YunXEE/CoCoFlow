using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Item;
using CoCoFlow.Runtime.Modules.Persistence;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.ChestSamples
{
    public abstract class CCS_Chest_StateBase : CoCoStateBase
    {
        protected ItemContext ItemContext => Controller != null ? Controller.Context as ItemContext : null;
        protected ItemLifeCycle ItemLifeCycle { get; private set; }
        protected PersistenceContainerBridge ContainerBridge { get; private set; }

        #region Public API

        public override void Init(CoCoStateController targetController)
        {
            base.Init(targetController);

            var actorTransform = targetController.transform.parent != null
                ? targetController.transform.parent
                : targetController.transform;
            ItemLifeCycle = actorTransform.GetComponent<ItemLifeCycle>();
            ContainerBridge = actorTransform.GetComponent<PersistenceContainerBridge>();
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            builder
                .ReadsContext<ItemContext>("ItemState", "Chest states read the durable item semantic state")
                .ReadsContext<ItemContext>("Intent.openRequested", "Open intent is transient and not saved")
                .UsesOperation<ItemLifeCycle>("Write item lifecycle facts through the gameplay facade")
                .UsesOperation<PersistenceContainerBridge>("Dispatch container commands without talking to the store");
        }

        protected void ChangeToOpenedIfNeeded(ItemContext context)
        {
            if (context?.ItemState == ItemSemanticState.Opened && IfHasState<CCS_Chest_Opened>())
            {
                ChangeState<CCS_Chest_Opened>();
            }
        }

        #endregion
    }
}
