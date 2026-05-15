using Fusion;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Modules.Network
{
    public interface INetworkRunnerProvider
    {
        NetworkRunner Runner { get; }
        bool IsConnected { get; }
        PlayerRef LocalPlayer { get; }
    }
}
