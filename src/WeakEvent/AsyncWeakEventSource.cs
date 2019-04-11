using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;

namespace WeakEvent
{
    public delegate Task AsyncEventHandler<TEventArgs>(object sender, TEventArgs e);
  
    public class AsyncWeakEventSource<TEventArgs>
    {
        private readonly DelegateCollection _handlers;

        public AsyncWeakEventSource()
        {
            _handlers = new DelegateCollection();
        }

        public async Task RaiseAsync(object sender, TEventArgs e)
        {
            List<StrongHandler> validHandlers;
            lock (_handlers)
            {
                validHandlers = new List<StrongHandler>(_handlers.Count);
                for (int i = 0; i < _handlers.Count; i++)
                {
                    var weakHandler = _handlers[i];
                    if (weakHandler != null)
                    {
                        if (weakHandler.TryGetStrongHandler() is StrongHandler handler)
                            validHandlers.Add(handler);
                        else
                            _handlers.Invalidate(i);
                    }
                }

                _handlers.CollectDeleted();
            }

            foreach (var handler in validHandlers)
            {
                await handler.Invoke(sender, e).ConfigureAwait(false);
            }
        }

        public void Subscribe(AsyncEventHandler<TEventArgs> handler)
        {
            var singleHandlers = handler
                .GetInvocationList()
                .Cast<AsyncEventHandler<TEventArgs>>()
                .ToList();

            lock (_handlers)
            {
                foreach (var h in singleHandlers)
                    _handlers.Add(h);
            }
        }

        public void Unsubscribe(AsyncEventHandler<TEventArgs> handler)
        {
            var singleHandlers = handler
                .GetInvocationList()
                .Cast<AsyncEventHandler<TEventArgs>>();

            lock (_handlers)
            {
                foreach (var singleHandler in singleHandlers)
                {
                    _handlers.Remove(singleHandler);
                }

                _handlers.CollectDeleted();
            }
        }

        private delegate Task OpenEventHandler(object target, object sender, TEventArgs e);

        private class WeakDelegate
        {
            #region Open handler generation and cache

            // ReSharper disable once StaticMemberInGenericType (by design)
            private static readonly ConcurrentDictionary<MethodInfo, OpenEventHandler> OpenHandlerCache =
                new ConcurrentDictionary<MethodInfo, OpenEventHandler>();

            private static OpenEventHandler CreateOpenHandler(MethodInfo method)
            {
                var target = Expression.Parameter(typeof(object), "target");
                var sender = Expression.Parameter(typeof(object), "sender");
                var e = Expression.Parameter(typeof(TEventArgs), "e");

                if (method.IsStatic)
                {
                    var expr = Expression.Lambda<OpenEventHandler>(
                        Expression.Call(
                            method,
                            sender, e),
                        target, sender, e);
                    return expr.Compile();
                }
                else
                {
                    var expr = Expression.Lambda<OpenEventHandler>(
                        Expression.Call(
                            Expression.Convert(target, method.DeclaringType),
                            method,
                            sender, e),
                        target, sender, e);
                    return expr.Compile();
                }
            }

            #endregion

            private readonly WeakReference _weakTarget;
            private readonly MethodInfo _method;
            private readonly OpenEventHandler _openHandler;

            public WeakDelegate(Delegate handler)
            {
                _weakTarget = handler.Target != null ? new WeakReference(handler.Target) : null;
                _method = handler.GetMethodInfo();
                _openHandler = OpenHandlerCache.GetOrAdd(_method, CreateOpenHandler);
            }

            public StrongHandler? TryGetStrongHandler()
            {
                object target = null;
                if (_weakTarget != null)
                {
                    target = _weakTarget.Target;
                    if (target == null)
                        return null;
                }

                return new StrongHandler(target, _openHandler);
            }

            public bool IsMatch(AsyncEventHandler<TEventArgs> handler)
            {
                return ReferenceEquals(handler.Target, _weakTarget?.Target)
                       && handler.GetMethodInfo().Equals(_method);
            }

            public static int GetHashCode(AsyncEventHandler<TEventArgs> handler)
            {
                var hashCode = -335093136;
                hashCode = hashCode * -1521134295 + (handler?.Target?.GetHashCode()).GetValueOrDefault();
                hashCode = hashCode * -1521134295 + (handler?.GetMethodInfo()?.GetHashCode()).GetValueOrDefault();
                return hashCode;
            }
        }

        private struct StrongHandler
        {
            private readonly object _target;
            private readonly OpenEventHandler _openHandler;

            public StrongHandler(object target, OpenEventHandler openHandler)
            {
                _target = target;
                _openHandler = openHandler;
            }

            public Task Invoke(object sender, TEventArgs e)
            {
                return _openHandler(_target, sender, e);
            }
        }

        private class DelegateCollection : IEnumerable<WeakDelegate>
        {
            private List<WeakDelegate> _delegates;

            private Dictionary<long, List<int>> _index;

            private int _deletedCount;

            public DelegateCollection()
            {
                _delegates = new List<WeakDelegate>();
                _index = new Dictionary<long, List<int>>();
            }

            public void Add(AsyncEventHandler<TEventArgs> singleHandler)
            {
                _delegates.Add(new WeakDelegate(singleHandler));
                var index = _delegates.Count - 1;
                AddToIndex(singleHandler, index);
            }

            public void Invalidate(int index)
            {
                _delegates[index] = null;
                _deletedCount++;
            }

            internal void Remove(AsyncEventHandler<TEventArgs> singleHandler)
            {
                var hashCode = WeakDelegate.GetHashCode(singleHandler);

                if (!_index.ContainsKey(hashCode))
                    return;

                var indices = _index[hashCode];
                for (int i = indices.Count - 1; i >= 0; i--)
                {
                    int index = indices[i];
                    if (_delegates[index] != null &&
                        _delegates[index].IsMatch(singleHandler))
                    {
                        _delegates[index] = null;
                        _deletedCount++;
                        indices.Remove(i);
                    }
                }

                if (indices.Count == 0)
                    _index.Remove(hashCode);
            }

            public void CollectDeleted()
            {
                if (_deletedCount < _delegates.Count / 4)
                    return;

                Dictionary<int, int> newIndices = new Dictionary<int, int>();
                var newDelegates = new List<WeakDelegate>();
                int oldIndex = 0;
                int newIndex = 0;
                foreach (var item in _delegates)
                {
                    if (item != null)
                    {
                        newDelegates.Add(item);
                        newIndices.Add(oldIndex, newIndex);
                        newIndex++;
                    }

                    oldIndex++;
                }

                _delegates = newDelegates;

                var hashCodes = _index.Keys.ToList();
                foreach (var hashCode in hashCodes)
                {
                    _index[hashCode] = _index[hashCode]
                        .Where(oi => newIndices.ContainsKey(oi))
                        .Select(oi => newIndices[oi]).ToList();
                }

                _deletedCount = 0;
            }

            private void AddToIndex(AsyncEventHandler<TEventArgs> singleHandler, int index)
            {
                var hashCode = WeakDelegate.GetHashCode(singleHandler);
                if (_index.ContainsKey(hashCode))
                    _index[hashCode].Add(index);
                else
                    _index.Add(hashCode, new List<int> { index });
            }

            public WeakDelegate this[int index] => _delegates[index];

            /// <summary>Returns an enumerator that iterates through the collection.</summary>
            /// <returns>A <see cref="T:System.Collections.Generic.IEnumerator`1" /> that can be used to iterate through the collection.</returns>
            public IEnumerator<WeakDelegate> GetEnumerator()
            {
                return _delegates.GetEnumerator();
            }

            /// <summary>Returns an enumerator that iterates through a collection.</summary>
            /// <returns>An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.</returns>
            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }

            public int Count => _delegates.Count;
        }
    }
}
