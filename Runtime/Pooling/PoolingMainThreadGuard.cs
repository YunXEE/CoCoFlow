using System;
using System.Threading;
using UnityEngine;

namespace CoCoFlow.Runtime.Pooling
{
    internal static class PoolingMainThreadGuard
    {
        private static int mainThreadId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureRuntimeMainThread()
        {
            Volatile.Write(ref mainThreadId, Environment.CurrentManagedThreadId);
        }

        internal static int MainThreadId => Volatile.Read(ref mainThreadId);

        internal static bool IsMainThread =>
            MainThreadId != 0 &&
            Environment.CurrentManagedThreadId == MainThreadId;

        internal static void CaptureCurrentThread()
        {
            int currentThreadId = Environment.CurrentManagedThreadId;
            int capturedThreadId = Interlocked.CompareExchange(
                ref mainThreadId,
                currentThreadId,
                0);
            if (capturedThreadId != 0 && capturedThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "The Pooling Unity main-thread guard was initialized from another thread.");
            }
        }
    }
}
