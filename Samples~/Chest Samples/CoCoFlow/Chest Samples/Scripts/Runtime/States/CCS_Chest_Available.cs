using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Item;

namespace CoCoFlow.Runtime.Addon.ChestSamples
{
    public sealed class CCS_Chest_Available : CCS_Chest_StateBase
    {
        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);

            var itemContext = context as ItemContext;
            if (itemContext?.ItemState == ItemSemanticState.Inactive)
            {
                itemContext.SetAvailable();
            }
        }

        public override void OnStateUpdate(ICoCoContext context)
        {
            base.OnStateUpdate(context);

            var itemContext = context as ItemContext;
            ChangeToOpenedIfNeeded(itemContext);
            if (itemContext?.Intent.openRequested == true && IfHasState<CCS_Chest_Opening>())
            {
                ChangeState<CCS_Chest_Opening>();
            }
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder
                .WritesContext<ItemContext>("ItemState.Available", "Initializes an inactive chest as available")
                .CanTransitionTo<CCS_Chest_Opening>("Open intent requested")
                .CanTransitionTo<CCS_Chest_Opened>("Context was restored as opened");
        }

        #endregion
    }
}
