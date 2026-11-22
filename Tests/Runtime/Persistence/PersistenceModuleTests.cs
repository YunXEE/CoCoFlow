using System.Collections;
using System.IO;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CoCoFlow.Tests.Runtime.ContextLifecycle
{
    public class PersistenceModuleTests
    {
        [TearDown]
        public void TearDown()
        {
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
        }

        [Test]
        public void PersistenceContextGeneratesDistinctStableEntityIds()
        {
            var first = new GameObject("Persistence Context A");
            var second = new GameObject("Persistence Context B");
            try
            {
                var firstContext = first.AddComponent<PersistenceContext>();
                var secondContext = second.AddComponent<PersistenceContext>();

                firstContext.EnsureStableEntityId();
                secondContext.EnsureStableEntityId();

                Assert.IsInstanceOf<ICoCoStableEntityIdProvider>(firstContext);
                Assert.IsFalse(string.IsNullOrEmpty(firstContext.StableEntityId));
                Assert.IsFalse(string.IsNullOrEmpty(secondContext.StableEntityId));
                Assert.AreNotEqual(firstContext.StableEntityId, secondContext.StableEntityId);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void PersistenceSaveDocumentContainsContextAndContainerSections()
        {
            var document = PersistenceSaveDocument.Create(
                0,
                new PersistenceContextSection(),
                new PersistenceContainerSection());

            Assert.AreEqual(PersistenceSaveDocument.CurrentSchemaVersion, document.schemaVersion);
            Assert.IsNotNull(document.contextSection);
            Assert.IsNotNull(document.containerSection);
            Assert.IsNotNull(document.metadata);
        }

        [Test]
        public void VersionOneSaveDocumentMigratesToSchemaTwo()
        {
            var document = new PersistenceSaveDocument
            {
                schemaVersion = 1,
                metadata = null,
                contextSection = null,
                containerSection = null
            };

            PersistenceSaveDocument migrated =
                PersistenceSaveDocument.MigrateToCurrentSchema(document);

            Assert.AreSame(document, migrated);
            Assert.AreEqual(2, migrated.schemaVersion);
            Assert.IsNotNull(migrated.metadata);
            Assert.IsNotNull(migrated.contextSection);
            Assert.IsNotNull(migrated.containerSection);
        }

        [Test]
        public void SaveDocumentRejectsSchemaNewerThanCurrent()
        {
            var document = new PersistenceSaveDocument
            {
                schemaVersion = PersistenceSaveDocument.CurrentSchemaVersion + 1
            };

            Assert.Throws<System.NotSupportedException>(
                () => PersistenceSaveDocument.MigrateToCurrentSchema(document));
        }

        [Test]
        public void SelectingPendingDocumentCreatesFreshRuntimeApplyToken()
        {
            var document = PersistenceSaveDocument.Create(
                0,
                new PersistenceContextSection(),
                new PersistenceContainerSection());

            PersistenceSession.SetPendingDocument(document);
            object firstToken = GetPrivateStaticField<object>(
                typeof(PersistenceSession),
                "_pendingDocumentApplyToken");
            Assert.IsNotNull(firstToken);

            PersistenceSession.SetPendingDocument(document);
            object secondToken = GetPrivateStaticField<object>(
                typeof(PersistenceSession),
                "_pendingDocumentApplyToken");
            Assert.IsNotNull(secondToken);
            Assert.AreNotSame(firstToken, secondToken);

            PersistenceSession.ClearPendingDocument();
            Assert.IsNull(
                GetPrivateStaticField<object>(
                    typeof(PersistenceSession),
                    "_pendingDocumentApplyToken"));
        }

        [Test]
        public void StateGraphContextPayloadRoundTripsAsOpaqueBase64()
        {
            string previousOverride = PersistenceFileStore.SaveDirectoryOverride;
            string directory = CreateTempSaveDirectory();
            try
            {
                PersistenceFileStore.SaveDirectoryOverride = directory;
                var record = new PersistenceContextRecord
                {
                    stableEntityId = "scene.state-graph.actor",
                    contextType = "CoCoFlow.StateGraph.ContextFrame",
                    prefabKey = "actor.state-graph"
                };
                byte[] payload = { 1, 2, 3, 4, 5 };
                SetPrivateField(record, "stateGraphContextPayload", payload);

                var contextSection = new PersistenceContextSection();
                contextSection.AddOrReplace(record);
                PersistenceFileStore.WriteDocument(
                    0,
                    PersistenceSaveDocument.Create(
                        0,
                        contextSection,
                        new PersistenceContainerSection()));

                string json = File.ReadAllText(PersistenceFileStore.GetSaveFilePath(0));
                StringAssert.Contains("AQIDBAU=", json);
                Assert.IsTrue(PersistenceFileStore.TryReadDocument(0, out var loaded));
                Assert.AreEqual(2, loaded.schemaVersion);
                Assert.IsTrue(
                    loaded.contextSection.TryGetRecord(
                        "scene.state-graph.actor",
                        out var loadedRecord));
                CollectionAssert.AreEqual(
                    payload,
                    GetPrivateField<byte[]>(loadedRecord, "stateGraphContextPayload"));
            }
            finally
            {
                PersistenceFileStore.SaveDirectoryOverride = previousOverride;
                DeleteTempSaveDirectory(directory);
            }
        }

        [UnityTest]
        public IEnumerator DeferredCreatedAndStoppedHostsKeepLatestRecordAndCancelOnDisable()
        {
            DeferredStateGraphHostFixture created = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.created",
                false);
            DeferredStateGraphHostFixture stopped = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.stopped",
                true);
            try
            {
                PersistenceContextRecord createdFirst = CreateStateGraphRecord(
                    created.StableEntityId,
                    1);
                PersistenceContextRecord createdLatest = CreateStateGraphRecord(
                    created.StableEntityId,
                    2);
                PersistenceContextRecord stoppedFirst = CreateStateGraphRecord(
                    stopped.StableEntityId,
                    3);
                PersistenceContextRecord stoppedLatest = CreateStateGraphRecord(
                    stopped.StableEntityId,
                    4);

                Assert.IsTrue(created.Persistence.TryApply(createdFirst));
                Assert.IsTrue(created.Persistence.TryApply(createdLatest));
                Assert.AreSame(
                    createdLatest,
                    GetPrivateField<PersistenceContextRecord>(
                        created.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNotNull(
                    GetPrivateField<Coroutine>(
                        created.Persistence,
                        "_deferredApplyCoroutine"));

                Assert.IsTrue(stopped.Persistence.TryApply(stoppedFirst));
                Assert.IsTrue(stopped.Persistence.TryApply(stoppedLatest));
                Assert.AreSame(
                    stoppedLatest,
                    GetPrivateField<PersistenceContextRecord>(
                        stopped.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNotNull(
                    GetPrivateField<Coroutine>(
                        stopped.Persistence,
                        "_deferredApplyCoroutine"));

                created.Root.SetActive(false);
                stopped.Root.SetActive(false);
                Assert.IsNull(
                    GetPrivateField<PersistenceContextRecord>(
                        created.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNull(
                    GetPrivateField<Coroutine>(
                        created.Persistence,
                        "_deferredApplyCoroutine"));
                Assert.IsNull(
                    GetPrivateField<PersistenceContextRecord>(
                        stopped.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNull(
                    GetPrivateField<Coroutine>(
                        stopped.Persistence,
                        "_deferredApplyCoroutine"));

                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(created.Root);
                Object.DestroyImmediate(stopped.Root);
            }
        }

        [UnityTest]
        public IEnumerator DeferredStateGraphApplyIsCancelledWhenOwnerIsDestroyed()
        {
            DeferredStateGraphHostFixture fixture = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.destroyed-owner",
                false);
            Assert.IsTrue(
                fixture.Persistence.TryApply(
                    CreateStateGraphRecord(fixture.StableEntityId, 5)));

            Object.DestroyImmediate(fixture.Root);
            yield return null;

            LogAssert.NoUnexpectedReceived();
        }

        [UnityTest]
        public IEnumerator DeferredStateGraphApplyLogsFailureWhenHostDisappears()
        {
            DeferredStateGraphHostFixture fixture = CreateDeferredStateGraphHostFixture(
                "scene.state-graph.missing-host",
                false);
            try
            {
                Assert.IsTrue(
                    fixture.Persistence.TryApply(
                        CreateStateGraphRecord(fixture.StableEntityId, 6)));
                LogAssert.Expect(
                    LogType.Error,
                    "[PersistenceContext] Deferred StateGraph apply failed for " +
                    "'scene.state-graph.missing-host': StateGraph ContextFrame record " +
                    "requires a CoCoStateGraphHost on the same GameObject.");

                Object.DestroyImmediate(fixture.Host);
                yield return null;

                Assert.IsNull(
                    GetPrivateField<PersistenceContextRecord>(
                        fixture.Persistence,
                        "_deferredApplyRecord"));
                Assert.IsNull(
                    GetPrivateField<Coroutine>(
                        fixture.Persistence,
                        "_deferredApplyCoroutine"));
            }
            finally
            {
                Object.DestroyImmediate(fixture.Root);
            }
        }

        [Test]
        public void FileStoreOverwritesExistingSlotWithoutLeavingTemporaryFiles()
        {
            string previousOverride = PersistenceFileStore.SaveDirectoryOverride;
            string directory = CreateTempSaveDirectory();
            try
            {
                PersistenceFileStore.SaveDirectoryOverride = directory;
                var firstSection = new PersistenceContextSection();
                firstSection.AddOrReplace(new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.first"
                });
                var secondSection = new PersistenceContextSection();
                secondSection.AddOrReplace(new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.second"
                });

                PersistenceFileStore.WriteDocument(
                    1,
                    PersistenceSaveDocument.Create(
                        1,
                        firstSection,
                        new PersistenceContainerSection()));
                PersistenceFileStore.WriteDocument(
                    1,
                    PersistenceSaveDocument.Create(
                        1,
                        secondSection,
                        new PersistenceContainerSection()));

                Assert.IsTrue(PersistenceFileStore.TryReadDocument(1, out var loaded));
                Assert.IsFalse(loaded.contextSection.TryGetRecord("scene.item.first", out _));
                Assert.IsTrue(loaded.contextSection.TryGetRecord("scene.item.second", out _));

                string savePath = PersistenceFileStore.GetSaveFilePath(1);
                Assert.IsFalse(File.Exists(savePath + ".tmp"));
                Assert.IsFalse(File.Exists(savePath + ".bak"));
            }
            finally
            {
                PersistenceFileStore.SaveDirectoryOverride = previousOverride;
                DeleteTempSaveDirectory(directory);
            }
        }

        [Test]
        public void FileStoreReadsBackupWhenTargetFileIsMissing()
        {
            string previousOverride = PersistenceFileStore.SaveDirectoryOverride;
            string directory = CreateTempSaveDirectory();
            try
            {
                PersistenceFileStore.SaveDirectoryOverride = directory;
                var contextSection = new PersistenceContextSection();
                contextSection.AddOrReplace(new PersistenceContextRecord
                {
                    stableEntityId = "scene.item.backup"
                });

                PersistenceFileStore.WriteDocument(
                    2,
                    PersistenceSaveDocument.Create(
                        2,
                        contextSection,
                        new PersistenceContainerSection()));

                string savePath = PersistenceFileStore.GetSaveFilePath(2);
                File.Move(savePath, savePath + ".bak");

                Assert.IsTrue(PersistenceFileStore.TryReadDocument(2, out var loaded));
                Assert.IsTrue(loaded.contextSection.TryGetRecord("scene.item.backup", out _));
            }
            finally
            {
                PersistenceFileStore.SaveDirectoryOverride = previousOverride;
                DeleteTempSaveDirectory(directory);
            }
        }








        private static string CreateTempSaveDirectory()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "CoCoFlowPersistenceTests",
                System.Guid.NewGuid().ToString("N"));
        }

        private static void DeleteTempSaveDirectory(string directory)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {target.GetType().Name}.");
            field.SetValue(target, value);
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {target.GetType().Name}.");
            return (T)field.GetValue(target);
        }

        private static T GetPrivateStaticField<T>(System.Type type, string fieldName)
        {
            var field = type.GetField(
                fieldName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Field {fieldName} was not found on {type.Name}.");
            return (T)field.GetValue(null);
        }

        private static DeferredStateGraphHostFixture CreateDeferredStateGraphHostFixture(
            string stableEntityId,
            bool stopped)
        {
            var root = new GameObject(
                stopped
                    ? "Persistence Deferred Stopped Host Test"
                    : "Persistence Deferred Created Host Test");
            root.SetActive(false);
            Component host = root.AddComponent(ResolveStateGraphHostType());
            SetPrivateField(host, "autoStart", false);
            SetPrivateField(host, "_hasStoppedInstance", stopped);
            var persistence = root.AddComponent<PersistenceContext>();
            SetPrivateField(persistence, "stableEntityId", stableEntityId);
            root.SetActive(true);
            return new DeferredStateGraphHostFixture(
                stableEntityId,
                root,
                host,
                persistence);
        }

        private static PersistenceContextRecord CreateStateGraphRecord(
            string stableEntityId,
            byte marker)
        {
            var record = new PersistenceContextRecord
            {
                stableEntityId = stableEntityId,
                contextType = "CoCoFlow.StateGraph.ContextFrame",
                prefabKey = "pre13.state-graph"
            };
            SetPrivateField(
                record,
                "stateGraphContextPayload",
                new[] { marker });
            return record;
        }

        private static System.Type ResolveStateGraphHostType()
        {
            System.Type type = System.Type.GetType(
                "CoCoFlow.Runtime.Core.CoCoStateGraphHost, CoCoFlow.Runtime.StateGraphHost");
            Assert.IsNotNull(type, "CoCoStateGraphHost runtime type could not be resolved.");
            return type;
        }

        private static void SetHostLifecycleToRunning(Component host)
        {
            System.Type runtimeType = System.Type.GetType(
                "CoCoFlow.Runtime.Core.CoCoStateGraphRuntime, " +
                "CoCoFlow.Runtime.Core.StateGraph");
            Assert.IsNotNull(runtimeType, "CoCoStateGraphRuntime type could not be resolved.");
            object runtime =
                System.Runtime.Serialization.FormatterServices.GetUninitializedObject(
                    runtimeType);
            FieldInfo lifecycleField = runtimeType.GetField(
                "_lifecycle",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(lifecycleField);
            lifecycleField.SetValue(
                runtime,
                System.Enum.Parse(lifecycleField.FieldType, "Running"));
            SetPrivateField(host, "_runtime", runtime);
        }

        private sealed class DeferredStateGraphHostFixture
        {
            internal DeferredStateGraphHostFixture(
                string stableEntityId,
                GameObject root,
                Component host,
                PersistenceContext persistence)
            {
                StableEntityId = stableEntityId;
                Root = root;
                Host = host;
                Persistence = persistence;
            }

            internal string StableEntityId { get; }
            internal GameObject Root { get; }
            internal Component Host { get; }
            internal PersistenceContext Persistence { get; }
        }

        [OneTimeSetUp]
        public void RegisterTestContextAdapterOnce()
        {
            // F6 lifecycle: static singleton, narrow match, idempotent registration.
            PersistenceContextAdapterRegistry.Register(TestContextAdapter.Instance);
            PersistenceContextAdapterRegistry.Register(TestContextAdapter.Instance);
        }

        [Test]
        public void PendingDocumentAppliesThroughRegisteredAdapterWhenContextRegisters()
        {
            var root = new GameObject("Pending Adapter Apply Test");
            try
            {
                var provider = root.AddComponent<TestContextProviderBehaviour>();
                var persistenceContext = root.AddComponent<PersistenceContext>();
                SetPrivateField(persistenceContext, "stableEntityId", "scene.test.pending");

                var record = new PersistenceContextRecord
                {
                    stableEntityId = "scene.test.pending",
                    lifecycleState = (int)CoCoLifecycleState.Active,
                    semanticStateId = 42
                };
                record.StringFacts["test.marker"] = "applied";

                var contextSection = new PersistenceContextSection();
                contextSection.AddOrReplace(record);
                PersistenceSession.SetPendingDocument(PersistenceSaveDocument.Create(
                    0,
                    contextSection,
                    new PersistenceContainerSection()));

                PersistenceContextRegistry.Register(persistenceContext);

                Assert.AreEqual(CoCoLifecycleState.Active, provider.Context.Lifecycle.State);
                Assert.AreEqual(42, provider.Context.SemanticStateId);
                Assert.AreEqual("applied", provider.Context.LastMarker);
                Assert.AreEqual("scene.test.pending", provider.Context.Identity.StableEntityId);
            }
            finally
            {
                PersistenceSession.ClearPendingDocument();
                Object.DestroyImmediate(root);
            }
        }


        [Test]
        public void LegacyRecordUsesAdapterPathWhenRunningStateGraphHostIsCoLocated()
        {
            var root = new GameObject("Co-Located Host Adapter Path Test");
            Component host = null;
            try
            {
                root.SetActive(false);
                host = root.AddComponent(ResolveStateGraphHostType());
                SetPrivateField(host, "autoStart", false);
                SetHostLifecycleToRunning(host);

                var provider = root.AddComponent<TestContextProviderBehaviour>();
                var persistenceContext = root.AddComponent<PersistenceContext>();
                SetPrivateField(persistenceContext, "stableEntityId", "scene.test.colocated");
                root.SetActive(true);

                object lifecycle = host.GetType()
                    .GetProperty("Lifecycle", BindingFlags.Instance | BindingFlags.Public)
                    ?.GetValue(host);
                Assert.AreEqual("Running", lifecycle?.ToString());

                var record = new PersistenceContextRecord
                {
                    stableEntityId = "scene.test.colocated",
                    lifecycleState = (int)CoCoLifecycleState.Active,
                    semanticStateId = 7
                };
                record.StringFacts["test.marker"] = "colocated-applied";

                Assert.IsTrue(persistenceContext.TryApply(record));
                Assert.AreEqual(CoCoLifecycleState.Active, provider.Context.Lifecycle.State);
                Assert.AreEqual(7, provider.Context.SemanticStateId);
                Assert.AreEqual("colocated-applied", provider.Context.LastMarker);
                Assert.AreEqual(
                    "scene.test.colocated",
                    provider.Context.Identity.StableEntityId);
            }
            finally
            {
                if (host != null)
                {
                    SetPrivateField(host, "_runtime", null);
                }

                Object.DestroyImmediate(root);
            }
        }

        internal sealed class TestEntityContext : CoCoEntityContext
        {
            public string LastMarker { get; private set; } = string.Empty;

            public void ApplyMarker(string marker)
            {
                LastMarker = marker;
            }
        }

        internal sealed class TestContextProviderBehaviour :
            MonoBehaviour,
            ICoCoContextProvider<TestEntityContext>
        {
            public TestEntityContext Context { get; } = new TestEntityContext();
        }

        internal sealed class TestContextAdapter : IPersistenceContextAdapter
        {
            public static readonly TestContextAdapter Instance = new TestContextAdapter();

            private TestContextAdapter() { }

            public bool CanCapture(ICoCoContext context)
            {
                return context is TestEntityContext;
            }

            public bool CanApply(PersistenceContextRecord record, ICoCoContext context)
            {
                return context is TestEntityContext;
            }

            public PersistenceContextRecord Capture(string stableEntityId, ICoCoContext context)
            {
                var entity = (TestEntityContext)context;
                return new PersistenceContextRecord
                {
                    stableEntityId = stableEntityId,
                    contextType = entity.GetType().AssemblyQualifiedName,
                    lifecycleState = (int)entity.Lifecycle.State,
                    semanticStateId = entity.SemanticStateId,
                    actionStateId = entity.ActionStateId,
                    lastEventSequence = entity.LastEventSequence
                };
            }

            public void Apply(PersistenceContextRecord record, ICoCoContext context)
            {
                var entity = (TestEntityContext)context;
                entity.Identity.StableEntityId = record.stableEntityId;
                entity.Lifecycle.TransitionTo((CoCoLifecycleState)record.lifecycleState);
                entity.SemanticStateId = record.semanticStateId;
                entity.ActionStateId = record.actionStateId;
                entity.LastEventSequence = record.lastEventSequence;
                if (record.StringFacts.TryGetValue("test.marker", out string marker))
                {
                    entity.ApplyMarker(marker);
                }
            }
        }

    }
}
