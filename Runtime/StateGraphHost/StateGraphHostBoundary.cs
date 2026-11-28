using UnityEngine;

namespace CoCoFlow.Runtime.StateGraph
{
    internal static class StateGraphHostBoundary
    {
        internal static bool Contains(
            StateGraphHost host,
            MonoBehaviour component)
        {
            if (host == null || component == null)
            {
                return false;
            }

            Transform current = component.transform;
            while (current != null)
            {
                StateGraphHost boundary = current.GetComponent<StateGraphHost>();
                if (boundary != null)
                {
                    return ReferenceEquals(boundary, host);
                }

                current = current.parent;
            }

            return false;
        }
    }
}
