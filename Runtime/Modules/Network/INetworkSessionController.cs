using Fusion;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Modules.Network
{
    public interface INetworkSessionController
    {
        UniTask StartHost(string sessionName, SceneRef scene);
        UniTask StartClient(string sessionName);
        void Disconnect();
    }
}
