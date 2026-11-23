using System;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Engine-side log bridge for state logics living in engine-free
    /// assemblies: CoCoStateLog.Print forwards to the installed sink. The
    /// host assembly installs Debug.Log on load; without a sink (tests,
    /// headless) Print is a no-op.
    /// </summary>
    public static class CoCoStateLog
    {
        private static Action<object> _sink;

        public static void Install(Action<object> sink) => _sink = sink;

        public static void Print(object message) => _sink?.Invoke(message);
    }
}
