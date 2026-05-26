using Fusion;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Addon.Network
{
    public interface INetworkRunnerProvider
    {
        NetworkRunner Runner { get; }
        bool IsConnected { get; }
        PlayerRef LocalPlayer { get; }
        GameMode CurrentGameMode { get; }
        string CurrentSessionName { get; }
        bool CanSpawnPlayerObjects { get; }
    }
}
