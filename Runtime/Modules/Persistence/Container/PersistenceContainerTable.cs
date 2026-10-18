using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Persistence.Container
{
    internal sealed class PersistenceContainerTable
    {
        private readonly Type _dataType;
        private readonly Dictionary<string, object> _rows = new Dictionary<string, object>(StringComparer.Ordinal);
        internal int Count => _rows.Count;

        internal PersistenceContainerTable(Type dataType) { _dataType = dataType; }
        internal bool Contains(string id) => _rows.ContainsKey(id);
        internal void Add(string id, object value) => _rows.Add(id, value);

        internal ref T GetValue<T>(string id) where T : struct
        {
            if (!_rows.TryGetValue(id, out object value))
                throw new InvalidOperationException($"Container row '{_dataType.FullName}:{id}' does not exist.");
            // Move the initial value once into an owned typed cell. Never return
            // a reference to an unboxed copy or a Dictionary bucket.
            if (!(value is PersistenceContainerCell<T> cell))
            {
                cell = new PersistenceContainerCell<T>((T)value);
                _rows[id] = cell;
            }
            return ref cell.Value;
        }

        internal void Capture(List<PersistenceContainerRecord> records)
        {
            string typeId = PersistenceDataTypeRegistry.GetTypeId(_dataType);
            foreach (KeyValuePair<string, object> pair in _rows)
            {
                object value = pair.Value is IPersistenceContainerCell cell ? cell.BoxedValue : pair.Value;
                records.Add(new PersistenceContainerRecord
                {
                    TypeId = typeId,
                    Id = pair.Key,
                    Data = PersistenceContainerCodec.Encode(_dataType, value)
                });
            }
        }
    }

    internal interface IPersistenceContainerCell { object BoxedValue { get; } }

    internal sealed class PersistenceContainerCell<T> : IPersistenceContainerCell where T : struct
    {
        internal T Value;
        public object BoxedValue => Value;
        internal PersistenceContainerCell(T value) { Value = value; }
    }
}
