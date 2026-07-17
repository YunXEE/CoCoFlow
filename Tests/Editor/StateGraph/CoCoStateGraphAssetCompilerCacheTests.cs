using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Runtime.Core.StateGraph.Tests
{
    public sealed class CoCoStateGraphAssetCompilerCacheTests
    {
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
        }

        [Test]
        public void UnchangedAssetSharesSuccessfulResultAndClearCreatesANewEntry()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                var cache = new CoCoStateGraphCompilationCache();
                var compiler = new CoCoStateGraphAssetCompiler(cache);

                CoCoStateGraphAssetCompileResult first = compiler.Compile(asset, catalog);
                CoCoStateGraphAssetCompileResult second = compiler.Compile(asset, catalog);

                Assert.IsTrue(first.Succeeded);
                Assert.AreSame(first, second);
                Assert.AreSame(first.Graph, second.Graph);

                cache.Clear();
                CoCoStateGraphAssetCompileResult afterClear = compiler.Compile(asset, catalog);
                Assert.IsTrue(afterClear.Succeeded);
                Assert.AreNotSame(first, afterClear);
                Assert.AreNotSame(first.Graph, afterClear.Graph);
                Assert.AreEqual(first.ContentFingerprint, afterClear.ContentFingerprint);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [TestCase(10)]
        [TestCase(100)]
        public void RepeatedSameKeyRequestsShareResultAndGraphIdentity(int requestCount)
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());
                CoCoGraphDescriptorCatalog catalog =
                    CoCoStateGraphTestFactory.CreateCatalog(false);
                CoCoStateGraphAssetCompileResult first = compiler.Compile(asset, catalog);

                Assert.IsTrue(first.Succeeded);
                for (int requestIndex = 1; requestIndex < requestCount; requestIndex++)
                {
                    CoCoStateGraphAssetCompileResult repeated = compiler.Compile(asset, catalog);
                    Assert.AreSame(first, repeated);
                    Assert.AreSame(first.Graph, repeated.Graph);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void FailedCompileResultIsCachedWithTheSameIdentity()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: false);
            try
            {
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());

                CoCoStateGraphAssetCompileResult first = compiler.Compile(asset, catalog);
                CoCoStateGraphAssetCompileResult second = compiler.Compile(asset, catalog);

                Assert.IsFalse(first.Succeeded);
                Assert.IsTrue(first.HasErrors);
                Assert.IsNull(first.Graph);
                Assert.IsTrue(first.Diagnostics.Any(diagnostic => diagnostic.IsError));
                Assert.AreSame(first, second);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ConfigContentChangeInvalidatesTheAssetCacheKey()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());
                CoCoStateGraphAssetCompileResult before = compiler.Compile(asset, catalog);

                ((TestStateAuthoringConfig)asset.Layers[0].States[0].Config).Value = 42;
                CoCoStateGraphAssetCompileResult after = compiler.Compile(asset, catalog);

                Assert.IsTrue(before.Succeeded);
                Assert.IsTrue(after.Succeeded);
                Assert.AreNotSame(before, after);
                Assert.AreNotSame(before.Graph, after.Graph);
                Assert.AreNotEqual(before.ContentFingerprint, after.ContentFingerprint);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void FailedConfigContentChangeInvalidatesTheCachedDiagnostic()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoGraphDescriptorCatalog catalog = CreateStateCatalog(
                    new ThrowingStateConfigFreezer());
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());

                CoCoStateGraphAssetCompileResult before = compiler.Compile(asset, catalog);
                ((TestStateAuthoringConfig)asset.Layers[0].States[0].Config).Value = 42;
                CoCoStateGraphAssetCompileResult after = compiler.Compile(asset, catalog);

                Assert.IsFalse(before.Succeeded);
                Assert.IsFalse(after.Succeeded);
                Assert.AreNotSame(before, after);
                Assert.AreNotEqual(before.ContentFingerprint, after.ContentFingerprint);
                Assert.IsTrue(after.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidFrozenConfig));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void FailedConfigReasonChangeInvalidatesTheCachedDiagnostic()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                var freezer = new MutableFailureStateConfigFreezer();
                CoCoGraphDescriptorCatalog catalog = CreateStateCatalog(freezer);
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());

                CoCoStateGraphAssetCompileResult before = compiler.Compile(asset, catalog);
                freezer.FailureMessage = "Changed synthetic failure.";
                CoCoStateGraphAssetCompileResult after = compiler.Compile(asset, catalog);

                Assert.IsFalse(before.Succeeded);
                Assert.IsFalse(after.Succeeded);
                Assert.AreNotSame(before, after);
                Assert.AreNotEqual(before.ContentFingerprint, after.ContentFingerprint);
                Assert.IsTrue(before.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Message == "Initial synthetic failure."));
                Assert.IsTrue(after.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Message == "Changed synthetic failure."));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void InvalidWindowContentChangeInvalidatesTheCachedFailure()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoLayerId layerId = ToLayerId(asset.Layers[0].LayerId);
                CoCoStateId stateId = ToStateId(asset.Layers[0].States[0].StateId);
                CoCoStateGraphAuthoringOperations.AddTransition(
                    asset,
                    layerId,
                    stateId,
                    stateId,
                    priority: 0);
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());

                SetTransitionWindow(asset, CoCoTransitionWindowMode.LocalSeconds, -1d, 1d);
                CoCoStateGraphAssetCompileResult before = compiler.Compile(asset, catalog);
                SetTransitionWindow(asset, CoCoTransitionWindowMode.LocalSeconds, 1d, 1d);
                CoCoStateGraphAssetCompileResult after = compiler.Compile(asset, catalog);

                Assert.IsFalse(before.Succeeded);
                Assert.IsFalse(after.Succeeded);
                Assert.AreNotSame(before, after);
                Assert.AreNotEqual(before.ContentFingerprint, after.ContentFingerprint);
                Assert.IsTrue(before.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidTransitionWindow));
                Assert.IsTrue(after.Diagnostics.Any(diagnostic =>
                    diagnostic.Diagnostic.Code == CoCoDiagnosticCode.InvalidTransitionWindow));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void RepairingBlankAssetGuidStampInvalidatesOnlyTheFailureCacheKey()
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            try
            {
                Assert.IsTrue(asset.EnsureAssetIdentity("original-guid"));
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("assetGuidStamp").stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());

                CoCoStateGraphAssetCompileResult before = compiler.Compile(asset, catalog);
                Assert.IsTrue(asset.EnsureAssetIdentity("repaired-guid"));
                CoCoStateGraphAssetCompileResult after = compiler.Compile(asset, catalog);

                Assert.IsFalse(before.Succeeded);
                Assert.IsFalse(after.Succeeded);
                Assert.AreNotSame(before, after);
                Assert.AreEqual(before.ContentFingerprint, after.ContentFingerprint);
                Assert.IsTrue(before.Diagnostics.Any(diagnostic =>
                    diagnostic.Location.Field == CoCoGraphField.AssetGuidStamp));
                Assert.IsFalse(after.Diagnostics.Any(diagnostic =>
                    diagnostic.Location.Field == CoCoGraphField.AssetGuidStamp));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CatalogRevisionChangeInvalidatesTheCacheWithoutChangingAssetFingerprint()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoGraphDescriptorCatalog firstCatalog =
                    CoCoStateGraphTestFactory.CreateCatalog(false, 1U);
                CoCoGraphDescriptorCatalog secondCatalog =
                    CoCoStateGraphTestFactory.CreateCatalog(false, 2U);
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());

                CoCoStateGraphAssetCompileResult first = compiler.Compile(asset, firstCatalog);
                CoCoStateGraphAssetCompileResult second = compiler.Compile(asset, secondCatalog);

                Assert.IsTrue(first.Succeeded);
                Assert.IsTrue(second.Succeeded);
                Assert.AreNotSame(first, second);
                Assert.AreEqual(first.ContentFingerprint, second.ContentFingerprint);
                Assert.AreNotEqual(first.Graph.CatalogFingerprint, second.Graph.CatalogFingerprint);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ConcurrentCacheMissExecutesFactoryOnceAndSharesTheResult()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                CoCoStateGraphAssetCompileResult expected = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache()).Compile(asset, catalog);
                var cache = new CoCoStateGraphCompilationCache();
                var key = new CoCoStateGraphCompilationCacheKey(
                    asset.GraphId,
                    expected.ContentFingerprint,
                    catalog.Fingerprint,
                    CoCoStateGraphCompiler.CurrentSchemaVersion);
                var results = new CoCoStateGraphAssetCompileResult[32];
                int factoryCalls = 0;

                Parallel.For(0, results.Length, index =>
                {
                    results[index] = cache.GetOrAdd(key, () =>
                    {
                        Interlocked.Increment(ref factoryCalls);
                        Thread.SpinWait(10000);
                        return expected;
                    });
                });

                Assert.AreEqual(1, factoryCalls);
                Assert.IsTrue(results.All(result => ReferenceEquals(expected, result)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void AssetCompilerRejectsWorkerThreadBeforeReadingUnityAsset()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoStateGraphMainThreadGuard.CaptureCurrentThread();
                var compiler = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache());
                CoCoGraphDescriptorCatalog catalog =
                    CoCoStateGraphTestFactory.CreateCatalog(false);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    Task.Run(() => compiler.Compile(asset, catalog)).GetAwaiter().GetResult());

                StringAssert.Contains("Unity main thread", exception.Message);
                Assert.AreEqual(0, CoCoStateGraphFixtureCounters.StateFreezeCalls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ThrowingCacheFactoryIsEvictedInsteadOfPoisoningTheKey()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                CoCoStateGraphAssetCompileResult expected = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache()).Compile(asset, catalog);
                var cache = new CoCoStateGraphCompilationCache();
                var key = new CoCoStateGraphCompilationCacheKey(
                    asset.GraphId,
                    expected.ContentFingerprint + 1UL,
                    catalog.Fingerprint,
                    CoCoStateGraphCompiler.CurrentSchemaVersion);

                Assert.Throws<InvalidOperationException>(() => cache.GetOrAdd(
                    key,
                    () => throw new InvalidOperationException("Synthetic cache failure.")));

                Assert.AreSame(expected, cache.GetOrAdd(key, () => expected));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void StaleFaultedWaiterCannotRemoveAReplacementForTheSameKey()
        {
            CoCoStateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                CoCoGraphDescriptorCatalog catalog = CoCoStateGraphTestFactory.CreateCatalog(false);
                CoCoStateGraphAssetCompileResult expected = new CoCoStateGraphAssetCompiler(
                    new CoCoStateGraphCompilationCache()).Compile(asset, catalog);
                var cache = new CoCoStateGraphCompilationCache();
                var key = new CoCoStateGraphCompilationCacheKey(
                    asset.GraphId,
                    expected.ContentFingerprint + 2UL,
                    catalog.Fingerprint,
                    CoCoStateGraphCompiler.CurrentSchemaVersion);
                var staleFaultedEntry = new Lazy<CoCoStateGraphAssetCompileResult>(
                    () => throw new InvalidOperationException("Stale failure."));

                Assert.Throws<InvalidOperationException>(() =>
                {
                    CoCoStateGraphAssetCompileResult _ = staleFaultedEntry.Value;
                });

                Assert.AreSame(expected, cache.GetOrAdd(key, () => expected));

                // Models a lagging waiter that caught the old Lazy after another waiter evicted
                // it and a new successful Lazy was already published for the same key.
                cache.TryRemoveIfSame(key, staleFaultedEntry);

                int replacementFactoryCalls = 0;
                CoCoStateGraphAssetCompileResult retained = cache.GetOrAdd(key, () =>
                {
                    Interlocked.Increment(ref replacementFactoryCalls);
                    return expected;
                });
                Assert.AreSame(expected, retained);
                Assert.AreEqual(0, replacementFactoryCalls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        private static CoCoGraphDescriptorCatalog CreateStateCatalog(
            ICoCoConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema> freezer)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                CoCoStateGraphTestFactory.StateDescriptorId,
                1U,
                freezer,
                new CoCoStateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic), registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return catalog;
        }

        private static CoCoStateGraphAsset CreateAsset(bool validConfig)
        {
            var asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N")));
            CoCoLayerId layerId = CoCoStateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            CoCoStateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                CoCoStateGraphTestFactory.StateDescriptorId,
                validConfig ? new TestStateAuthoringConfig { Value = 1 } : null,
                "Idle");
            return asset;
        }

        private static void SetTransitionWindow(
            CoCoStateGraphAsset asset,
            CoCoTransitionWindowMode mode,
            double startInclusive,
            double endExclusive)
        {
            var serialized = new SerializedObject(asset);
            SerializedProperty transition = serialized.FindProperty("layers")
                .GetArrayElementAtIndex(0)
                .FindPropertyRelative("transitions")
                .GetArrayElementAtIndex(0);
            transition.FindPropertyRelative("windowMode").intValue = (int)mode;
            transition.FindPropertyRelative("windowStartInclusive").doubleValue = startInclusive;
            transition.FindPropertyRelative("windowEndExclusive").doubleValue = endExclusive;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static CoCoLayerId ToLayerId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoLayerId.TryCreate(id.High, id.Low, out CoCoLayerId value));
            return value;
        }

        private static CoCoStateId ToStateId(CoCoSerializedId128 id)
        {
            Assert.IsTrue(CoCoStateId.TryCreate(id.High, id.Low, out CoCoStateId value));
            return value;
        }
    }
}
