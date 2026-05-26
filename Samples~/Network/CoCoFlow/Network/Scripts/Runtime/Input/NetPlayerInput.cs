using Fusion;
using UnityEngine;

namespace CoCoFlow.Runtime.Addon.Network.Input
{
    public struct NetPlayerInput : INetworkInput
    {
        public Vector2 MoveDirection;
        public Vector2 LookDirection;
        public NetworkBool Jump;
        public NetworkBool Interact;
        public NetworkButtons Buttons;
    }
}
