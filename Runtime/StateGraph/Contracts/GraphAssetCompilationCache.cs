using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

// 多个 Host 可复用同一图版本的编译结果；失败条目允许重新编译。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>用图身份、内容、Catalog 及编译器版本标识一次编译输入。</summary>
    internal readonly struct GraphAssetCompilationCacheKey :
        IEquatable<GraphAssetCompilationCacheKey>
    {
        public GraphAssetCompilationCacheKey(
            GraphId graphId,
            ulong contentFingerprint,
            ulong catalogFingerprint,
            uint compilerSchemaVersion)
        {
            GraphId = graphId;
            ContentFingerprint = contentFingerprint;
            CatalogFingerprint = catalogFingerprint;
            CompilerSchemaVersion = compilerSchemaVersion;
        }

        public GraphId GraphId { get; }
        public ulong ContentFingerprint { get; }
        public ulong CatalogFingerprint { get; }
        public uint CompilerSchemaVersion { get; }

        public bool Equals(GraphAssetCompilationCacheKey other)
        {
            return GraphId == other.GraphId &&
                   ContentFingerprint == other.ContentFingerprint &&
                   CatalogFingerprint == other.CatalogFingerprint &&
                   CompilerSchemaVersion == other.CompilerSchemaVersion;
        }

        public override bool Equals(object obj) =>
            obj is GraphAssetCompilationCacheKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hashCode = GraphId.GetHashCode();
                hashCode = (hashCode * 397) ^ ContentFingerprint.GetHashCode();
                hashCode = (hashCode * 397) ^ CatalogFingerprint.GetHashCode();
                hashCode = (hashCode * 397) ^ CompilerSchemaVersion.GetHashCode();
                return hashCode;
            }
        }

        public static bool operator ==(
            GraphAssetCompilationCacheKey left,
            GraphAssetCompilationCacheKey right) => left.Equals(right);

        public static bool operator !=(
            GraphAssetCompilationCacheKey left,
            GraphAssetCompilationCacheKey right) => !left.Equals(right);
    }

    /// <summary>按完整输入身份查取编译结果，并支持清除已缓存的图。</summary>
    internal interface IGraphAssetCompilationCache
    {
        GraphAssetCompileResult GetOrAdd(
            GraphAssetCompilationCacheKey key,
            Func<GraphAssetCompileResult> valueFactory);

        void Clear();
    }

    /// <summary>让并发调用复用一次编译，并只移除引发异常的同一缓存条目。</summary>
    internal sealed class GraphAssetCompilationCache : IGraphAssetCompilationCache
    {
        private readonly ConcurrentDictionary<
            GraphAssetCompilationCacheKey,
            Lazy<GraphAssetCompileResult>> entries =
            new ConcurrentDictionary<
                GraphAssetCompilationCacheKey,
                Lazy<GraphAssetCompileResult>>();

        internal static GraphAssetCompilationCache Shared { get; } =
            new GraphAssetCompilationCache();

        public GraphAssetCompileResult GetOrAdd(
            GraphAssetCompilationCacheKey key,
            Func<GraphAssetCompileResult> valueFactory)
        {
            if (valueFactory == null)
            {
                throw new ArgumentNullException(nameof(valueFactory));
            }

            Lazy<GraphAssetCompileResult> candidate =
                new Lazy<GraphAssetCompileResult>(
                    valueFactory,
                    LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<GraphAssetCompileResult> stored = entries.GetOrAdd(key, candidate);
            try
            {
                return stored.Value;
            }
            catch
            {
                TryRemoveIfSame(key, stored);
                throw;
            }
        }

        public void Clear() => entries.Clear();

        internal void TryRemoveIfSame(
            GraphAssetCompilationCacheKey key,
            Lazy<GraphAssetCompileResult> stored)
        {
            // ConcurrentDictionary's KeyValuePair removal compares both key and value atomically.
            // A waiter observing an older faulted Lazy must not evict a replacement published by
            // another caller after the first waiter removed that faulted entry.
            var entry = new KeyValuePair<
                GraphAssetCompilationCacheKey,
                Lazy<GraphAssetCompileResult>>(key, stored);
            ((ICollection<KeyValuePair<
                GraphAssetCompilationCacheKey,
                Lazy<GraphAssetCompileResult>>>)entries).Remove(entry);
        }
    }
}
