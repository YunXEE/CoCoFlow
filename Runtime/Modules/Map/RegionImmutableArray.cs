using System;
using System.Collections;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Map
{
    public sealed class RegionImmutableArray<T> :
        IReadOnlyList<T>
    {
        private static readonly RegionImmutableArray<T> EmptyInstance =
            new RegionImmutableArray<T>(Array.Empty<T>());

        private readonly T[] items;

        public RegionImmutableArray(IEnumerable<T> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            var copy = new List<T>();
            foreach (T item in source)
            {
                copy.Add(item);
            }

            items = copy.ToArray();
        }

        public static RegionImmutableArray<T> Empty => EmptyInstance;
        public int Count => items.Length;
        public T this[int index] => items[index];

        public IEnumerator<T> GetEnumerator()
        {
            for (int index = 0; index < items.Length; index++)
            {
                yield return items[index];
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
