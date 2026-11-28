using System.Threading;
using UnityEngine;
using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>在主线程冻结图资产配置，按内容指纹复用编译结果。</summary>
    internal sealed class GraphAssetCompiler
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
                    "StateGraphAsset compilation must start on the Unity main thread.");
            }
        }

        private readonly IGraphAssetCompilationCache cache;

        public GraphAssetCompiler()
            : this(GraphAssetCompilationCache.Shared)
        {
        }

        internal GraphAssetCompiler(IGraphAssetCompilationCache cache)
        {
            this.cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Freezes Unity-serialized authoring data and compiles it through the pure StateGraph compiler.
        /// The call must originate on Unity's main thread because it reads a ScriptableObject graph.
        /// </summary>
        public GraphAssetCompileResult Compile(
            StateGraphAsset asset,
            GraphDescriptorCatalog catalog)
        {
            if (ReferenceEquals(asset, null))
            {
                throw new ArgumentNullException(nameof(asset));
            }

            if (catalog == null)
            {
                throw new ArgumentNullException(nameof(catalog));
            }

            ThrowIfNotMainThread();
            if (asset == null)
            {
                throw new ArgumentException(
                    "The StateGraph Asset has been destroyed.",
                    nameof(asset));
            }

            GraphAssetReferenceInspection managedReferenceInspection =
                GraphAssetReferenceInspectionBridge.Inspect(asset);
            GraphAssetSnapshot snapshot =
                GraphAssetSnapshotBuilder.Build(
                    asset,
                    catalog,
                    managedReferenceInspection);
            var key = new GraphAssetCompilationCacheKey(
                snapshot.Source.GraphId,
                snapshot.CacheFingerprint,
                catalog.Fingerprint,
                StateGraphCompiler.CurrentSchemaVersion);
            return cache.GetOrAdd(key, () => CompileSnapshot(snapshot, catalog));
        }

        private static GraphAssetCompileResult CompileSnapshot(
            GraphAssetSnapshot snapshot,
            GraphDescriptorCatalog catalog)
        {
            StateGraphCompileResult compilerResult =
                new StateGraphCompiler().Compile(snapshot.Source, catalog);
            return new GraphAssetCompileResult(
                compilerResult.Graph,
                snapshot.ContentFingerprint,
                snapshot.PreflightDiagnostics,
                snapshot.AdapterDiagnostics,
                snapshot.FreezingSkippedGlobally,
                compilerResult);
        }
    }
}
