using System;
using System.Collections.Generic;
using JetBrains.Annotations;

namespace LightWeight.PerformanceCounters
{
    public class DictionaryPool<TKey, TValue>
    {
        private static Queue<Dictionary<TKey, TValue>> _queue = new Queue<Dictionary<TKey, TValue>>();

        [NotNull]
        public static Dictionary<TKey, TValue> Rent()
        {
            if (_queue.Count > 0)
            {
                var dictionary = _queue.Dequeue();
                dictionary.Clear();
                return dictionary;
            }

            return new Dictionary<TKey, TValue>();
        }

        public static void Return([NotNull] Dictionary<TKey, TValue> rentedObject)
        {
            if (rentedObject == null) throw new ArgumentNullException(nameof(rentedObject));

            _queue.Enqueue(rentedObject);
        }
    }
}