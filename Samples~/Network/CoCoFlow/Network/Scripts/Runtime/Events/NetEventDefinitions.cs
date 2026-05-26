using Fusion;
using Fusion.Sockets;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Addon.Network.Events
{
    public struct NetSessionStateChangedEvent
    {
        public NetSessionState State;
        public NetSessionMode Mode;
        public string SessionName;
        public int PlayerCount;
        public string Message;
        public NetworkRunner Runner;
    }

    public struct NetSessionStartFailedEvent
    {
        public NetSessionMode Mode;
        public string SessionName;
        public ShutdownReason Reason;
        public string ErrorMessage;
    }

    // Connection lifecycle
    public struct NetConnectedEvent { public NetworkRunner Runner; }
    public struct NetDisconnectedEvent { public NetDisconnectReason Reason; }
    public struct NetShutdownEvent { public ShutdownReason Reason; }

    // Player lifecycle
    public struct NetPlayerJoinedEvent { public PlayerRef Player; }
    public struct NetPlayerLeftEvent { public PlayerRef Player; }

    // Session info
    public struct NetSessionListEvent { public List<SessionInfo> Sessions; }

    // Scene loading
    public struct NetSceneReadyEvent { public NetworkRunner Runner; }
    public struct NetMatchStartedEvent { public NetworkRunner Runner; public SceneRef Scene; }

    // Character events
    public struct NetCharacterSpawnedEvent { public PlayerRef Player; public NetworkObject Object; }
    public struct NetCharacterDestroyedEvent { public PlayerRef Player; }
    public struct NetPlayerObjectSpawnedEvent { public PlayerRef Player; public NetworkObject Object; }
    public struct NetPlayerObjectDespawnedEvent { public PlayerRef Player; public NetworkObject Object; }

    // Health/Damage events
    public struct NetHealthChangedEvent { public PlayerRef Player; public float CurrentHealth; public float MaxHealth; }
    public struct NetDeathEvent { public PlayerRef Player; }
    public struct NetReviveEvent { public PlayerRef Player; }
}
