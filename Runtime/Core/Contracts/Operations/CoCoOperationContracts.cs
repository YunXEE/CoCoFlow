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
        void Submit<TCommand>(CoCoOperationRequirement requirement, TCommand command)
            where TCommand : unmanaged, ICoCoOperationCommand;
    }

    public interface ICoCoNoOpOperation
    {
    }

    public readonly struct CoCoOperationRequirement : IEquatable<CoCoOperationRequirement>
    {
        private CoCoOperationRequirement(Type portType)
        {
            PortType = portType;
        }

        public Type PortType { get; }
        public bool IsValid => PortType != null;

        public static CoCoOperationRequirement For<TPort>()
            where TPort : ICoCoOperationPort
        {
            return RequirementCache<TPort>.Value;
        }

        public bool Equals(CoCoOperationRequirement other) => PortType == other.PortType;
        public override bool Equals(object obj) => obj is CoCoOperationRequirement other && Equals(other);
        public override int GetHashCode() => PortType?.GetHashCode() ?? 0;

        public static bool operator ==(CoCoOperationRequirement left, CoCoOperationRequirement right) => left.Equals(right);
        public static bool operator !=(CoCoOperationRequirement left, CoCoOperationRequirement right) => !left.Equals(right);

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
            public static readonly CoCoOperationRequirement Value =
                IsPortInterface(typeof(TPort))
                    ? new CoCoOperationRequirement(typeof(TPort))
                    : default;
        }
    }
}
