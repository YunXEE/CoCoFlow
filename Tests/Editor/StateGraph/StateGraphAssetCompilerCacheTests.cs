using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Editor.StateGraph;
using CoCoFlow.Runtime.Core.StateGraph.Tests.Fixtures;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Tests.Editor.StateGraph
{
    public sealed class StateGraphAssetCompilerCacheTests
    {
        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
        }

        [Test]
        public void UnchangedAssetSharesSuccessfulResultAndClearCreatesANewEntry()
        {
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                var cache = new GraphAssetCompilationCache();
                var compiler = new GraphAssetCompiler(cache);

                GraphAssetCompileResult first = compiler.Compile(asset, catalog);
                GraphAssetCompileResult second = compiler.Compile(asset, catalog);

                Assert.IsTrue(first.Succeeded);
                Assert.AreSame(first, second);
                Assert.AreSame(first.Graph, second.Graph);

                cache.Clear();
                GraphAssetCompileResult afterClear = compiler.Compile(asset, catalog);
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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());
                GraphDescriptorCatalog catalog =
                    StateGraphTestFactory.CreateCatalog(false);
                GraphAssetCompileResult first = compiler.Compile(asset, catalog);

                Assert.IsTrue(first.Succeeded);
                for (int requestIndex = 1; requestIndex < requestCount; requestIndex++)
                {
                    GraphAssetCompileResult repeated = compiler.Compile(asset, catalog);
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
            StateGraphAsset asset = CreateAsset(validConfig: false);
            try
            {
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());

                GraphAssetCompileResult first = compiler.Compile(asset, catalog);
                GraphAssetCompileResult second = compiler.Compile(asset, catalog);

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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());
                GraphAssetCompileResult before = compiler.Compile(asset, catalog);

                ((TestStateAuthoringConfig)asset.Layers[0].States[0].Config).Value = 42;
                GraphAssetCompileResult after = compiler.Compile(asset, catalog);

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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphDescriptorCatalog catalog = CreateStateCatalog(
                    new ThrowingStateConfigFreezer());
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());

                GraphAssetCompileResult before = compiler.Compile(asset, catalog);
                ((TestStateAuthoringConfig)asset.Layers[0].States[0].Config).Value = 42;
                GraphAssetCompileResult after = compiler.Compile(asset, catalog);

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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                var freezer = new MutableFailureStateConfigFreezer();
                GraphDescriptorCatalog catalog = CreateStateCatalog(freezer);
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());

                GraphAssetCompileResult before = compiler.Compile(asset, catalog);
                freezer.FailureMessage = "Changed synthetic failure.";
                GraphAssetCompileResult after = compiler.Compile(asset, catalog);

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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                LayerId layerId = ToLayerId(asset.Layers[0].LayerId);
                StateId stateId = ToStateId(asset.Layers[0].States[0].StateId);
                StateGraphAuthoringOperations.AddTransition(
                    asset,
                    layerId,
                    stateId,
                    stateId,
                    priority: 0);
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());

                SetTransitionWindow(asset, TransitionWindowMode.LocalSeconds, -1d, 1d);
                GraphAssetCompileResult before = compiler.Compile(asset, catalog);
                SetTransitionWindow(asset, TransitionWindowMode.LocalSeconds, 1d, 1d);
                GraphAssetCompileResult after = compiler.Compile(asset, catalog);

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
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            try
            {
                Assert.IsTrue(asset.EnsureAssetIdentity("original-guid"));
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("assetGuidStamp").stringValue = string.Empty;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());

                GraphAssetCompileResult before = compiler.Compile(asset, catalog);
                Assert.IsTrue(asset.EnsureAssetIdentity("repaired-guid"));
                GraphAssetCompileResult after = compiler.Compile(asset, catalog);

                Assert.IsFalse(before.Succeeded);
                Assert.IsFalse(after.Succeeded);
                Assert.AreNotSame(before, after);
                Assert.AreEqual(before.ContentFingerprint, after.ContentFingerprint);
                Assert.IsTrue(before.Diagnostics.Any(diagnostic =>
                    diagnostic.Location.Field == GraphField.AssetGuidStamp));
                Assert.IsFalse(after.Diagnostics.Any(diagnostic =>
                    diagnostic.Location.Field == GraphField.AssetGuidStamp));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void CatalogRevisionChangeInvalidatesTheCacheWithoutChangingAssetFingerprint()
        {
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphDescriptorCatalog firstCatalog =
                    StateGraphTestFactory.CreateCatalog(false, 1U);
                GraphDescriptorCatalog secondCatalog =
                    StateGraphTestFactory.CreateCatalog(false, 2U);
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());

                GraphAssetCompileResult first = compiler.Compile(asset, firstCatalog);
                GraphAssetCompileResult second = compiler.Compile(asset, secondCatalog);

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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                GraphAssetCompileResult expected = new GraphAssetCompiler(
                    new GraphAssetCompilationCache()).Compile(asset, catalog);
                var cache = new GraphAssetCompilationCache();
                var key = new GraphAssetCompilationCacheKey(
                    asset.GraphId,
                    expected.ContentFingerprint,
                    catalog.Fingerprint,
                    StateGraphCompiler.CurrentSchemaVersion);
                var results = new GraphAssetCompileResult[32];
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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphAssetCompiler.CaptureCurrentThread();
                var compiler = new GraphAssetCompiler(
                    new GraphAssetCompilationCache());
                GraphDescriptorCatalog catalog =
                    StateGraphTestFactory.CreateCatalog(false);

                InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
                    Task.Run(() => compiler.Compile(asset, catalog)).GetAwaiter().GetResult());

                StringAssert.Contains("Unity main thread", exception.Message);
                Assert.AreEqual(0, StateGraphFixtureCounters.StateFreezeCalls);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void ThrowingCacheFactoryIsEvictedInsteadOfPoisoningTheKey()
        {
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                GraphAssetCompileResult expected = new GraphAssetCompiler(
                    new GraphAssetCompilationCache()).Compile(asset, catalog);
                var cache = new GraphAssetCompilationCache();
                var key = new GraphAssetCompilationCacheKey(
                    asset.GraphId,
                    expected.ContentFingerprint + 1UL,
                    catalog.Fingerprint,
                    StateGraphCompiler.CurrentSchemaVersion);

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
            StateGraphAsset asset = CreateAsset(validConfig: true);
            try
            {
                GraphDescriptorCatalog catalog = StateGraphTestFactory.CreateCatalog(false);
                GraphAssetCompileResult expected = new GraphAssetCompiler(
                    new GraphAssetCompilationCache()).Compile(asset, catalog);
                var cache = new GraphAssetCompilationCache();
                var key = new GraphAssetCompilationCacheKey(
                    asset.GraphId,
                    expected.ContentFingerprint + 2UL,
                    catalog.Fingerprint,
                    StateGraphCompiler.CurrentSchemaVersion);
                var staleFaultedEntry = new Lazy<GraphAssetCompileResult>(
                    () => throw new InvalidOperationException("Stale failure."));

                Assert.Throws<InvalidOperationException>(() =>
                {
                    GraphAssetCompileResult _ = staleFaultedEntry.Value;
                });

                Assert.AreSame(expected, cache.GetOrAdd(key, () => expected));

                // Models a lagging waiter that caught the old Lazy after another waiter evicted
                // it and a new successful Lazy was already published for the same key.
                cache.TryRemoveIfSame(key, staleFaultedEntry);

                int replacementFactoryCalls = 0;
                GraphAssetCompileResult retained = cache.GetOrAdd(key, () =>
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

        private static GraphDescriptorCatalog CreateStateCatalog(
            IConfigFreezer<TestStateAuthoringConfig, TestStateConfigSchema> freezer)
        {
            var builder = new GraphDescriptorCatalogBuilder();
            Assert.IsTrue(builder.TryRegisterState(
                StateGraphTestFactory.StateDescriptorId,
                1U,
                freezer,
                new StateRuntimeRegistration<
                    TestStateLogic,
                    TestStateConfigSchema,
                    TestActivationMemory>(TestFrozenConfigSchemas.StateSchema),
                null,
                null,
                null,
                out CoCoDiagnostic registrationDiagnostic), registrationDiagnostic.Message);
            Assert.IsTrue(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freezeDiagnostic), freezeDiagnostic.Message);
            return catalog;
        }

        private static StateGraphAsset CreateAsset(bool validConfig)
        {
            var asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            Assert.IsTrue(asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N")));
            LayerId layerId = StateGraphAuthoringOperations.AddLayer(asset, "Gameplay");
            StateGraphAuthoringOperations.AddState(
                asset,
                layerId,
                default,
                StateGraphTestFactory.StateDescriptorId,
                validConfig ? new TestStateAuthoringConfig { Value = 1 } : null,
                "Idle");
            return asset;
        }

        private static void SetTransitionWindow(
            StateGraphAsset asset,
            TransitionWindowMode mode,
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

        private static LayerId ToLayerId(SerializedId128 id)
        {
            Assert.IsTrue(LayerId.TryCreate(id.High, id.Low, out LayerId value));
            return value;
        }

        private static StateId ToStateId(SerializedId128 id)
        {
            Assert.IsTrue(StateId.TryCreate(id.High, id.Low, out StateId value));
            return value;
        }
    }
}
