using System;
using System.Threading;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    internal static class CoCoStateGraphMainThreadGuard
    {
        private static int mainThreadId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void CaptureRuntimeMainThread()
        {
            Volatile.Write(ref mainThreadId, Thread.CurrentThread.ManagedThreadId);
        }

        internal static void CaptureCurrentThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            int capturedThreadId = Interlocked.CompareExchange(
                ref mainThreadId,
                currentThreadId,
                0);
            if (capturedThreadId != 0 && capturedThreadId != currentThreadId)
            {
                throw new InvalidOperationException(
                    "The StateGraph Unity main-thread guard was initialized from another thread.");
            }
        }

        internal static void ThrowIfNotMainThread()
        {
            int capturedThreadId = Volatile.Read(ref mainThreadId);
            if (capturedThreadId == 0)
            {
                throw new InvalidOperationException(
                    "The StateGraph Unity main-thread guard has not been initialized.");
            }

            if (Thread.CurrentThread.ManagedThreadId != capturedThreadId)
            {
                throw new InvalidOperationException(
                    "CoCoStateGraphAsset compilation must start on the Unity main thread.");
            }
        }
    }
}
