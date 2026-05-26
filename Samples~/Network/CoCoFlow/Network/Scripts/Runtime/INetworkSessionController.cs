using Fusion;
using Cysharp.Threading.Tasks;

namespace CoCoFlow.Runtime.Addon.Network
{
    public enum NetSessionMode
    {
        Host,
        Client,
        Shared
    }

    public enum NetSessionState
    {
        Idle,
        Starting,
        Connected,
        InGame,
        Failed,
        Shutdown
    }

    public struct NetSessionStartOptions
    {
        public NetSessionMode Mode;
        public string SessionName;
        public SceneRef Scene;
        public int PlayerCount;
        public bool IsOpen;
        public bool IsVisible;

        public static NetSessionStartOptions Host(string sessionName, SceneRef scene)
        {
            return Create(NetSessionMode.Host, sessionName, scene);
        }

        public static NetSessionStartOptions Client(string sessionName)
        {
            return Create(NetSessionMode.Client, sessionName, default);
        }

        public static NetSessionStartOptions Shared(string sessionName, SceneRef scene)
        {
            return Create(NetSessionMode.Shared, sessionName, scene);
        }

        private static NetSessionStartOptions Create(NetSessionMode mode, string sessionName, SceneRef scene)
        {
            return new NetSessionStartOptions
            {
                Mode = mode,
                SessionName = sessionName,
                Scene = scene,
                PlayerCount = 8,
                IsOpen = true,
                IsVisible = true
            };
        }
    }

    public interface INetworkSessionController
    {
        UniTask StartSession(NetSessionStartOptions options);
        UniTask StartHost(string sessionName, SceneRef scene);
        UniTask StartClient(string sessionName);
        UniTask StartShared(string sessionName, SceneRef scene);
        UniTask StartGame(SceneRef gameScene);
        void Disconnect();
    }
}
