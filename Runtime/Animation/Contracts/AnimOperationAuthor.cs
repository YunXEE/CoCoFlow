using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.Animation.Contracts
{
    /// <summary>
    /// Engine-free, callback-scoped animation author. Writes the State's declared
    /// Operation Sections; arbitration and Animator execution remain with the Host.
    /// </summary>
    public readonly struct AnimOperationAuthor
    {
        private readonly CoCoStateOperationWriter _writer;
        public AnimOperationAuthor(CoCoStateOperationWriter writer) => _writer = writer;
        public bool IsValid => _writer.IsValid;

        public bool SetParameter(int lane, in AnimParameterCommand command) =>
            _writer.Write(_writer.ResolveField<IAnimParameterOperationSection, AnimParameterCommand>(lane), command);

        public bool SetTrigger(int lane, in AnimTriggerCommand command) =>
            _writer.Write(_writer.ResolveField<IAnimTriggerOperationSection, AnimTriggerCommand>(lane), command) &&
            _writer.TryEnableDiscrete<IAnimTriggerOperationSection>();
    }
}
