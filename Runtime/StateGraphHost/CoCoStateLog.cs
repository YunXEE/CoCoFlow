using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Engine-side log bridge for state logics living in engine-free
    /// assemblies: CoCoStateLog.Print("...") forwards to Debug.Log so
    /// attributed states never reference UnityEngine themselves.
    /// </summary>
    public static class CoCoStateLog
    {
        public static void Print(object message) => Debug.Log(message);
    }
}
