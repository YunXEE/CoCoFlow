using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    [DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
    public sealed class PersistenceContainerHost : MonoBehaviour
    {
        private Dictionary<Type, PersistenceContainerTable> _tables = new Dictionary<Type, PersistenceContainerTable>();
        private readonly List<PersistenceContainer> _bindings = new List<PersistenceContainer>();
        internal static PersistenceContainerHost ActiveHost { get; private set; }
        internal int TableCount => _tables.Count;
        internal int BoundContainerCount => _bindings.Count;

        internal IEnumerable<KeyValuePair<Type, int>> GetTableCounts()
        {
            foreach (KeyValuePair<Type, PersistenceContainerTable> pair in _tables)
                yield return new KeyValuePair<Type, int>(pair.Key, pair.Value.Count);
        }

        public PersistenceContainerHandle<T> GetOrCreate<T>(string id, in T initialData) where T : struct
        {
            EnsureReady();
            ValidateKey(typeof(T), id);
            PersistenceContainerTable table = GetOrAddTable(_tables, typeof(T));
            if (!table.Contains(id)) table.Add(id, PersistenceContainerCodec.Clone(typeof(T), initialData));
            return new PersistenceContainerHandle<T>(this, id);
        }

        public bool TryGet<T>(string id, out PersistenceContainerHandle<T> handle) where T : struct
        {
            EnsureReady();
            ValidateKey(typeof(T), id);
            handle = Contains(typeof(T), id) ? new PersistenceContainerHandle<T>(this, id) : default;
            return handle.IsValid;
        }

        internal static PersistenceContainerSection CaptureActiveSection()
        {
            PersistenceContainerHost host = ResolveSaveHost();
            return host != null ? host.CaptureSection() : new PersistenceContainerSection();
        }

        internal static void ApplyActiveSection(PersistenceContainerSection section)
        {
            PersistenceContainerHost host = ResolveSaveHost();
            if (host != null) host.ApplySection(section ?? new PersistenceContainerSection());
        }

        private static PersistenceContainerHost ResolveSaveHost() =>
            PersistenceHost.ActiveHost != null ? PersistenceHost.ActiveHost.ResolveContainerHost() : ActiveHost;

        internal bool Contains(Type type, string id)
        {
            return id != null && _tables.TryGetValue(type, out PersistenceContainerTable table) && table.Contains(id);
        }

        internal ref T GetValue<T>(string id) where T : struct
        {
            return ref _tables[typeof(T)].GetValue<T>(id);
        }

        internal void Register(PersistenceContainer container)
        {
            EnsureReady();
            Type type = container.DataType;
            ValidateKey(type, container.Id);
            PersistenceContainerTable table = GetOrAddTable(_tables, type);
            if (!table.Contains(container.Id))
                table.Add(container.Id, PersistenceContainerCodec.Clone(type, container.GetCandidateData()));
            if (!_bindings.Contains(container)) _bindings.Add(container);
        }

        internal void Unregister(PersistenceContainer container) => _bindings.Remove(container);

        internal PersistenceContainerSection CaptureSection()
        {
            var section = new PersistenceContainerSection();
            foreach (PersistenceContainerTable table in _tables.Values) table.Capture(section.Records);
            return section;
        }

        internal void ApplySection(PersistenceContainerSection section)
        {
            PersistenceContainerCodec.ValidateSection(section);
            var tables = new Dictionary<Type, PersistenceContainerTable>();
            foreach (PersistenceContainerRecord record in section.Records)
            {
                Type type = PersistenceDataTypeRegistry.Resolve(record.TypeId);
                GetOrAddTable(tables, type).Add(record.Id, PersistenceContainerCodec.Decode(type, record.Data));
            }
            foreach (PersistenceContainer binding in _bindings)
            {
                if (binding == null || !binding.isActiveAndEnabled) continue;
                Type type = binding.DataType;
                PersistenceContainerTable table = GetOrAddTable(tables, type);
                if (!table.Contains(binding.Id))
                    table.Add(binding.Id, PersistenceContainerCodec.Clone(type, binding.GetCandidateData()));
            }
            // Publish only after all records and active bindings have succeeded.
            _tables = tables;
        }

        internal static void ValidateKey(Type type, string id)
        {
            PersistenceDataTypeValidator.Validate(type);
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Container ID must not be empty.", nameof(id));
        }

        private void EnsureReady()
        {
            if (ActiveHost != this || !isActiveAndEnabled)
                throw new InvalidOperationException("PersistenceContainerHost must be initialized by the global bootstrap first.");
        }

        private static PersistenceContainerTable GetOrAddTable(Dictionary<Type, PersistenceContainerTable> tables, Type type)
        {
            if (!tables.TryGetValue(type, out PersistenceContainerTable table))
            {
                table = new PersistenceContainerTable(type);
                tables.Add(type, table);
            }
            return table;
        }

        private void OnEnable()
        {
            if (ActiveHost != null && ActiveHost != this)
            {
                Debug.LogError("[Persistence] Only one PersistenceContainerHost may be active.", this);
                return;
            }
            ActiveHost = this;
            try
            {
                PersistenceDataTypeRegistry.GetValidTypes();
                if (PersistenceSession.PendingDocument?.containerSection != null)
                    ApplySection(PersistenceSession.PendingDocument.containerSection);
            }
            catch (Exception exception)
            {
                ActiveHost = null;
                Debug.LogError($"[Persistence] ContainerHost initialization failed: {exception.Message}", this);
            }
        }

        private void OnDisable()
        {
            if (ActiveHost == this) ActiveHost = null;
        }
    }
}
