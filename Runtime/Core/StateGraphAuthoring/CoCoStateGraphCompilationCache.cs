using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace CoCoFlow.Runtime.Core
{
    public readonly struct CoCoStateGraphCompilationCacheKey :
        IEquatable<CoCoStateGraphCompilationCacheKey>
    {
        public CoCoStateGraphCompilationCacheKey(
            CoCoGraphId graphId,
            ulong contentFingerprint,
            ulong catalogFingerprint,
            uint compilerSchemaVersion)
        {
            GraphId = graphId;
            ContentFingerprint = contentFingerprint;
            CatalogFingerprint = catalogFingerprint;
            CompilerSchemaVersion = compilerSchemaVersion;
        }

        public CoCoGraphId GraphId { get; }
        public ulong ContentFingerprint { get; }
        public ulong CatalogFingerprint { get; }
        public uint CompilerSchemaVersion { get; }

        public bool Equals(CoCoStateGraphCompilationCacheKey other)
        {
            return GraphId == other.GraphId &&
                   ContentFingerprint == other.ContentFingerprint &&
                   CatalogFingerprint == other.CatalogFingerprint &&
                   CompilerSchemaVersion == other.CompilerSchemaVersion;
        }

        public override bool Equals(object obj) =>
            obj is CoCoStateGraphCompilationCacheKey other && Equals(other);

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
            CoCoStateGraphCompilationCacheKey left,
            CoCoStateGraphCompilationCacheKey right) => left.Equals(right);

        public static bool operator !=(
            CoCoStateGraphCompilationCacheKey left,
            CoCoStateGraphCompilationCacheKey right) => !left.Equals(right);
    }

    public interface ICoCoStateGraphCompilationCache
    {
        CoCoStateGraphAssetCompileResult GetOrAdd(
            CoCoStateGraphCompilationCacheKey key,
            Func<CoCoStateGraphAssetCompileResult> valueFactory);

        void Clear();
    }

    public sealed class CoCoStateGraphCompilationCache : ICoCoStateGraphCompilationCache
    {
        private readonly ConcurrentDictionary<
            CoCoStateGraphCompilationCacheKey,
            Lazy<CoCoStateGraphAssetCompileResult>> entries =
            new ConcurrentDictionary<
                CoCoStateGraphCompilationCacheKey,
                Lazy<CoCoStateGraphAssetCompileResult>>();

        public static CoCoStateGraphCompilationCache Shared { get; } =
            new CoCoStateGraphCompilationCache();

        public CoCoStateGraphAssetCompileResult GetOrAdd(
            CoCoStateGraphCompilationCacheKey key,
            Func<CoCoStateGraphAssetCompileResult> valueFactory)
        {
            if (valueFactory == null)
            {
                throw new ArgumentNullException(nameof(valueFactory));
            }

            Lazy<CoCoStateGraphAssetCompileResult> candidate =
                new Lazy<CoCoStateGraphAssetCompileResult>(
                    valueFactory,
                    LazyThreadSafetyMode.ExecutionAndPublication);
            Lazy<CoCoStateGraphAssetCompileResult> stored = entries.GetOrAdd(key, candidate);
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
            CoCoStateGraphCompilationCacheKey key,
            Lazy<CoCoStateGraphAssetCompileResult> stored)
        {
            // ConcurrentDictionary's KeyValuePair removal compares both key and value atomically.
            // A waiter observing an older faulted Lazy must not evict a replacement published by
            // another caller after the first waiter removed that faulted entry.
            var entry = new KeyValuePair<
                CoCoStateGraphCompilationCacheKey,
                Lazy<CoCoStateGraphAssetCompileResult>>(key, stored);
            ((ICollection<KeyValuePair<
                CoCoStateGraphCompilationCacheKey,
                Lazy<CoCoStateGraphAssetCompileResult>>>)entries).Remove(entry);
        }
    }
}
