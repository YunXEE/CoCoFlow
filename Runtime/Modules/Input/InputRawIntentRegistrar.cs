using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Modules.Input
{
    /// <summary>Preserves explicit RawInputIntent declarations until Input migration.</summary>
    [CoCoIntentRegistration(typeof(InputRawIntentRegistrar))]
    public sealed class InputRawIntentRegistrar :
        CoCoStandardIntentRegistrar<RawInputIntent, RawInputPassThroughReducer, RawInputReducerFactory>
    {
        public InputRawIntentRegistrar() : base(RawIntents.Player, 2, 1UL) { }
        protected override RawInputReducerFactory CreateReducerFactory() => new RawInputReducerFactory();
    }
}
