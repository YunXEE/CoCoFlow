using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

namespace CoCoFlow.Tests.GameplayBinding
{
    public sealed class InteractionReceiver : MonoBehaviour, ICoCoEventInboxAdapter<Interaction>
    {
        public int PlayerId { get; set; }
        public bool Accept(in Interaction payload) => payload.PlayerId == PlayerId;
    }
}
