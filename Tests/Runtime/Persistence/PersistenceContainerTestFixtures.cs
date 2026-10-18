using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CoCoFlow.Tests.Runtime.Persistence
{
    // These are JSON data fields, following the documented public Container API.
    // ReSharper disable InconsistentNaming
    [Serializable, PersistenceData]
    public struct ContainerTestItem { public string Key; public int Count; }

    [Serializable, PersistenceData]
    public struct ContainerTestChest
    {
        public int Coins;
        public ContainerTestItem[] Items;
        public List<ContainerTestItem> Rewards;
    }

    [Serializable, PersistenceData]
    public struct ContainerTestQuest { public int Progress; }

    [Serializable, PersistenceData]
    public struct ContainerTestFields
    {
        public int Stored;
        [NonSerialized] public int Ignored;
        public readonly int Readonly;
        public int Property => throw new InvalidOperationException("Properties must not be read.");
    }

    public struct ContainerUnmarked { public int Value; }
    [Serializable] public struct ContainerMissingMarker { public int Value; }
    [PersistenceData] public struct ContainerMissingSerializable { public int Value; }
    // These shapes are intentionally invalid fixtures for our type validator.
#pragma warning disable UAC1001, UAC1009
    [Serializable, PersistenceData] public struct ContainerBadNested { public ContainerUnmarked Child; }
    [Serializable, PersistenceData] public struct ContainerBadObject { public GameObject Object; }
    [Serializable, PersistenceData] public struct ContainerBadDictionary { public Dictionary<string, int> Values; }
    [Serializable, PersistenceData] public struct ContainerBadArray { public int[,] Values; }
    [Serializable, PersistenceData] public struct ContainerBadLists { public List<List<int>> Values; }

    [Serializable, PersistenceData]
    public struct ContainerBadPrivate
    {
        [SerializeField] private int value;
        public int Value => value;
        public ContainerBadPrivate(int initialValue) { value = initialValue; }
    }

#pragma warning restore UAC1001, UAC1009
    // ReSharper restore InconsistentNaming

    [RequireComponent(typeof(PersistenceContainer))]
    public sealed class ContainerTestChestBusiness : MonoBehaviour
    {
        public int InitialCoins { get; set; } = 5;
        public int AwakeCount { get; private set; }
        public int StartCount { get; private set; }
        public bool BoundAtStart { get; private set; }
        private PersistenceContainerHandle<ContainerTestChest> _data;

        private void Awake()
        {
            AwakeCount++;
            GetComponent<PersistenceContainer>().SetInitialData(new ContainerTestChest { Coins = InitialCoins });
        }

        private void Start()
        {
            StartCount++;
            var container = GetComponent<PersistenceContainer>();
            BoundAtStart = container.IsBound;
            _data = container.GetHandle<ContainerTestChest>();
        }

        public int TakeCoins(int requested)
        {
            ref ContainerTestChest data = ref _data.Value;
            int taken = Mathf.Min(Mathf.Max(requested, 0), data.Coins);
            data.Coins -= taken;
            return taken;
        }
    }

    public abstract class PersistenceContainerTestBase
    {
        private readonly List<GameObject> _objects = new List<GameObject>();
        protected PersistenceContainerHost Host;

        [SetUp]
        public void SetUpContainerHost()
        {
            PersistenceSession.ClearPendingDocument();
            Host = NewObject("ContainerHost").AddComponent<PersistenceContainerHost>();
        }

        [TearDown]
        public void TearDownContainerHost()
        {
            for (int i = _objects.Count - 1; i >= 0; i--)
                if (_objects[i] != null) Object.DestroyImmediate(_objects[i]);
            _objects.Clear();
            PersistenceSession.ClearPendingDocument();
        }

        protected GameObject NewObject(string name, bool active = true)
        {
            var root = new GameObject(name);
            root.SetActive(active);
            _objects.Add(root);
            return root;
        }

        protected PersistenceContainer NewContainer<T>(string id, T initial = default, bool active = true) where T : struct
        {
            GameObject root = NewObject(id, false);
            var container = root.AddComponent<PersistenceContainer>();
            SetField(container, "id", id);
            SetField(container, "dataTypeId", PersistenceDataTypeRegistry.GetTypeId(typeof(T)));
            container.SetInitialData(initial);
            if (active) root.SetActive(true);
            return container;
        }

        protected static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, name);
            field.SetValue(target, value);
        }
    }
}
