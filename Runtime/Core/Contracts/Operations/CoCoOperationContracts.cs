using System;

namespace CoCoFlow.Runtime.Core
{
    public interface ICoCoOperationPort
    {
    }

    public interface ICoCoOperationCommand
    {
    }

    public interface ICoCoOperationCommandSink
    {
        void Submit<TCommand>(CoCoOperationPortRequirement requirement, TCommand command)
            where TCommand : unmanaged, ICoCoOperationCommand;
    }

    public interface ICoCoNoOpOperation
    {
    }

    public readonly struct CoCoOperationPortRequirement : IEquatable<CoCoOperationPortRequirement>
    {
        private CoCoOperationPortRequirement(Type portType)
        {
            PortType = portType;
        }

        public Type PortType { get; }
        public bool IsValid => PortType != null;

        public static CoCoOperationPortRequirement For<TPort>()
            where TPort : ICoCoOperationPort
        {
            return RequirementCache<TPort>.Value;
        }

        public bool Equals(CoCoOperationPortRequirement other) => PortType == other.PortType;
        public override bool Equals(object obj) => obj is CoCoOperationPortRequirement other && Equals(other);
        public override int GetHashCode() => PortType?.GetHashCode() ?? 0;

        public static bool operator ==(
            CoCoOperationPortRequirement left,
            CoCoOperationPortRequirement right) => left.Equals(right);

        public static bool operator !=(
            CoCoOperationPortRequirement left,
            CoCoOperationPortRequirement right) => !left.Equals(right);

        private static bool IsPortInterface(Type portType)
        {
            return portType != null &&
                   portType.IsInterface &&
                   portType != typeof(ICoCoOperationPort) &&
                   typeof(ICoCoOperationPort).IsAssignableFrom(portType);
        }

        private static class RequirementCache<TPort>
            where TPort : ICoCoOperationPort
        {
            public static readonly CoCoOperationPortRequirement Value =
                IsPortInterface(typeof(TPort))
                    ? new CoCoOperationPortRequirement(typeof(TPort))
                    : default;
        }
    }
}
