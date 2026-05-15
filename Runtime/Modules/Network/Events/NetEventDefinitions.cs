using Fusion;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Network.Events
{
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

    // Character events
    public struct NetCharacterSpawnedEvent { public PlayerRef Player; public NetworkObject Object; }
    public struct NetCharacterDestroyedEvent { public PlayerRef Player; }

    // Health/Damage events
    public struct NetHealthChangedEvent { public PlayerRef Player; public float CurrentHealth; public float MaxHealth; }
    public struct NetDeathEvent { public PlayerRef Player; }
    public struct NetReviveEvent { public PlayerRef Player; }
}
