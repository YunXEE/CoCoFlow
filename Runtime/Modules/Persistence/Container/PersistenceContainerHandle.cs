using System;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    /// <summary>A fixed type/ID key; resolve Value again after loading a save.</summary>
    public readonly struct PersistenceContainerHandle<T> where T : struct
    {
        private readonly PersistenceContainerHost _host;
        public string Id { get; }
        public bool IsValid => _host != null && _host.Contains(typeof(T), Id);

        public ref T Value
        {
            get
            {
                if (!IsValid)
                    throw new InvalidOperationException($"Container handle '{typeof(T).FullName}:{Id}' is invalid.");
                return ref _host.GetValue<T>(Id);
            }
        }

        internal PersistenceContainerHandle(PersistenceContainerHost host, string id)
        {
            _host = host;
            Id = id;
        }
    }
}
