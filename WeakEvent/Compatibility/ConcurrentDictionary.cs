#if NET35
using System;
using System.Collections.Generic;

namespace System.Collections.Concurrent
{
    internal class ConcurrentDictionary<TKey, TValue>
    {
        private readonly IDictionary<TKey, TValue> _inner;

        public ConcurrentDictionary()
        {
            _inner = new Dictionary<TKey, TValue>();
        }

        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
        {
            lock (_inner)
            {
                if (!_inner.TryGetValue(key, out var value))
                {
                    _inner[key] = value = valueFactory(key);
                }
                return value;
            }
        }
    }
}
#endif