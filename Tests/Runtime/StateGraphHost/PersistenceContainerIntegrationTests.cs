using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    [Serializable, PersistenceData]
    public struct ContainerIntegrationChest
    {
        // Matches the documented public JSON data-field API.
        // ReSharper disable once InconsistentNaming
        public int Coins;
    }

    // Shares the existing Graph fixture assembly so the original Graph test
    // files and production assembly references remain unchanged.
    public sealed class PersistenceContainerIntegrationTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private string _directory;
        private string _previousDirectory;

        [SetUp]
        public void SetUp()
        {
            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
            _previousDirectory = PersistenceFileStore.SaveDirectoryOverride;
            _directory = Path.Combine(Application.temporaryCachePath, "cocoflow-044-p01-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_directory);
            PersistenceFileStore.SaveDirectoryOverride = _directory;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            PersistenceContextRegistry.Clear();
            PersistenceSession.ClearPendingDocument();
            PersistenceFileStore.SaveDirectoryOverride = _previousDirectory;
            if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
            CoCoStateGraphProjectBindings.ResetForTests();
            TemporalHostLogic.Reset();
            TemporalHostMemoryStateBinding.Reset();
            TemporalHostEventAdapter.Reset();
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void RealGraphAndOptionalContainerRoundTripThroughTheUnchangedSharedSession(bool useFacade, bool withContainer)
        {
            PersistenceHost facade = useFacade ? CreateFacade() : null;
            if (withContainer) Track(new GameObject("Container data")).AddComponent<PersistenceContainerHost>();
            TemporalHostTestScenario graph = TemporalHostTestHarness.Create(3, withDurableProjection: true);
            Track(graph.Asset);
            Track(graph.GameObject);
            graph.GameObject.SetActive(false);
            var context = graph.GameObject.AddComponent<PersistenceContext>();
            SetField(context, "stableEntityId", "p01.graph.actor");
            PersistenceContainer container = null;
            if (withContainer)
            {
                container = graph.GameObject.AddComponent<PersistenceContainer>();
                SetField(container, "id", "chest");
                SetField(container, "dataTypeId", typeof(ContainerIntegrationChest).Assembly.GetName().Name + ":" + typeof(ContainerIntegrationChest).FullName);
                SetField(container, "initialDataJson", "{\"Coins\":7}");
            }
            graph.GameObject.SetActive(true);
            Assert.IsTrue(graph.Host.TryStart(out CoCoDiagnostic start), start.Message);
            Step(graph, 12);
            var handle = withContainer ? container.GetHandle<ContainerIntegrationChest>() : default;
            if (useFacade) facade.SaveGame(0); else PersistenceSaveLoadSystem.SaveGame(0);
            Assert.IsTrue(PersistenceFileStore.TryReadDocument(0, out PersistenceSaveDocument saved));
            Assert.IsTrue(saved.contextSection.TryGetRecord("p01.graph.actor", out _));
            Assert.AreEqual(withContainer ? 1 : 0, saved.containerSection.Records.Count);
            Step(graph, 55);
            if (withContainer) handle.Value.Coins = 99;
            Assert.IsTrue(useFacade ? facade.LoadGame(0) : PersistenceSaveLoadSystem.LoadGame(0));
            Assert.AreEqual(12, TemporalHostTestHarness.ReadActorValue(graph.Host.CurrentContext, graph.Ids.ActorStateSlotId));
            if (withContainer) Assert.AreEqual(7, handle.Value.Coins);
            else Assert.IsNull(graph.GameObject.GetComponent<PersistenceContainer>());
        }

        [Test]
        public void ComponentAndStaticEntryReadTheSameSaveSlotAndRuntimeTable()
        {
            var host = Track(new GameObject("Data")).AddComponent<PersistenceContainerHost>();
            PersistenceHost facade = CreateFacade();
            var handle = host.GetOrCreate("chest", new ContainerIntegrationChest { Coins = 6 });
            facade.SaveGame(0);
            handle.Value.Coins = 9;
            Assert.IsTrue(PersistenceSaveLoadSystem.LoadGame(0));
            Assert.AreEqual(6, handle.Value.Coins);
            handle.Value.Coins = 4;
            PersistenceSaveLoadSystem.SaveGame(0);
            handle.Value.Coins = 8;
            Assert.IsTrue(facade.LoadGame(0));
            Assert.AreEqual(4, handle.Value.Coins);
            Assert.IsTrue(PersistenceSession.HasPendingDocument);
        }

        private T Track<T>(T value) where T : Object
        {
            _objects.Add(value);
            return value;
        }

        private PersistenceHost CreateFacade()
        {
            GameObject root = Track(new GameObject("Persistence entry"));
            root.SetActive(false);
            var facade = root.AddComponent<PersistenceHost>();
            SetField(facade, "saveDirectory", _directory);
            root.SetActive(true);
            return facade;
        }

        private static void Step(TemporalHostTestScenario graph, int value)
        {
            graph.Binding.Value = value;
            Assert.IsTrue(graph.Host.TryStep(0.1d, out CoCoDiagnostic diagnostic), diagnostic.Message);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }
    }
}
