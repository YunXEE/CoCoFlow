using System;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    [DisallowMultipleComponent, DefaultExecutionOrder(10000)]
    public sealed class PersistenceContainer : MonoBehaviour
    {
        [SerializeField] private string id = string.Empty;
        [SerializeField, HideInInspector] private string dataTypeId = string.Empty;
        [SerializeField, HideInInspector] private string initialDataJson = "{}";
        private PersistenceContainerHost _host;
        private object _candidateData;

        public string Id => id;
        public Type DataType => string.IsNullOrEmpty(dataTypeId) ? null : PersistenceDataTypeRegistry.Resolve(dataTypeId);
        public bool IsBound => _host != null && _host.Contains(DataType, id);

        // Preserve both the approved named argument and Unity's serialized field name.
        // ReSharper disable once ParameterHidesMember
        public void SetId(string id)
        {
            EnsureUnbound();
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Container ID must not be empty.", nameof(id));
            this.id = id;
        }

        public void SetInitialData<T>(in T initialData) where T : struct
        {
            EnsureUnbound();
            ValidateType<T>();
            _candidateData = PersistenceContainerCodec.Clone(typeof(T), initialData);
        }

        public PersistenceContainerHandle<T> GetHandle<T>() where T : struct
        {
            ValidateType<T>();
            if (!TryGetHandle(out PersistenceContainerHandle<T> handle))
                throw new InvalidOperationException($"Container '{id}' has not been bound.");
            return handle;
        }

        public bool TryGetHandle<T>(out PersistenceContainerHandle<T> handle) where T : struct
        {
            ValidateType<T>();
            handle = IsBound ? new PersistenceContainerHandle<T>(_host, id) : default;
            return handle.IsValid;
        }

        internal object GetCandidateData()
        {
            return _candidateData ?? (_candidateData = PersistenceContainerCodec.ParseInitialData(DataType, initialDataJson));
        }

        private void ValidateType<T>() where T : struct
        {
            PersistenceDataTypeValidator.Validate(typeof(T));
            if (DataType != typeof(T))
                throw new ArgumentException($"Container '{id}' is locked to '{dataTypeId}', not '{typeof(T).FullName}'.");
        }

        private void EnsureUnbound()
        {
            if (_host != null) throw new InvalidOperationException("Container configuration cannot change while bound.");
        }

        private void OnEnable()
        {
            try
            {
                PersistenceContainerHost host = PersistenceContainerHost.ActiveHost;
                if (host == null) throw new InvalidOperationException("PersistenceContainerHost must be ready before Container.OnEnable.");
                host.Register(this);
                _host = host;
            }
            catch (Exception exception)
            {
                _host = null;
                Debug.LogError($"[Persistence] Container '{id}' registration failed: {exception.Message}", this);
            }
        }

        private void OnDisable()
        {
            if (_host != null) _host.Unregister(this);
            _host = null;
        }
    }
}
