using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Gameplay.Item;

namespace CoCoFlow.Runtime.Addon.ChestSamples
{
    public sealed class CCS_Chest_Opened : CCS_Chest_StateBase
    {
        #region Public API

        public override void Enter(ICoCoContext context)
        {
            base.Enter(context);

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
        }

        #endregion

        #region Protected API

        protected override void DefineState(CoCoStateDefinitionBuilder builder)
        {
            base.DefineState(builder);
            builder
                .WritesContext<ItemContext>("ItemState.Opened", "Opened state is durable through PersistenceContext");
        }

        #endregion
    }
}
