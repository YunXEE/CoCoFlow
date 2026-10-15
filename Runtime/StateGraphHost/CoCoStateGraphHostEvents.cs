using System.Reflection;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    public sealed partial class CoCoStateGraphHost
    {
        private CoCoStateEventInbox _stateInbox;
        private CoCoEventAgent _stateEventAgent;

        private void StartStateEvents()
        {
            _stateInbox = new CoCoStateEventInbox(eventLaneCapacity);
            _stateEventAgent = new CoCoEventAgent();
            _runtime.AttachEvents(_stateInbox, _transaction);
            foreach (var component in EventAdapters)
            {
                if (component == null) continue;
                foreach (var contract in component.GetType().GetInterfaces())
                {
                    if (!contract.IsGenericType ||
                        contract.GetGenericTypeDefinition() != typeof(ICoCoEventInboxAdapter<>)) continue;
                    typeof(CoCoStateGraphHost).GetMethod(nameof(BindStateEvent),
                            BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(contract.GetGenericArguments())
                        .Invoke(this, new object[] { component });
                }
            }
        }

        private void BindStateEvent<TEvent>(MonoBehaviour component) where TEvent : unmanaged
        {
            var lane = _stateInbox.Register<TEvent>();
            if (lane.Adapters.Count == 0)
                _stateEventAgent.Subscribe((ref TEvent payload) =>
                {
                    if (!CanAcceptEventInput || _isTemporalOperation ||
                        _temporal?.Mode == CoCoTemporalMode.Previewing) return;
                    foreach (var adapter in lane.Adapters)
                    {
                        if ((MonoBehaviour)adapter == null || !adapter.Accept(in payload)) continue;
                        if (!lane.TryEnqueue(in payload)) _reliableOverflowPending = true;
                        break;
                    }
                });
            lane.Adapters.Add((ICoCoEventInboxAdapter<TEvent>)component);
        }
    }
}
