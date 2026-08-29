using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal static class CoCoStateGraphHostBoundary
    {
        internal static bool Contains(
            CoCoStateGraphHost host,
            MonoBehaviour component)
        {
            if (host == null || component == null)
            {
                return false;
            }

            Transform current = component.transform;
            while (current != null)
            {
                CoCoStateGraphHost boundary = current.GetComponent<CoCoStateGraphHost>();
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
