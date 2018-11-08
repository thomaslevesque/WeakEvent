using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace WeakEvent
{
    public class WeakEventSource<TEventArgs>
#if NET40 || NET35
        where TEventArgs : EventArgs
#endif
    {
        private readonly DelegateCollection _handlers;

        public WeakEventSource()
        {
            _handlers = new DelegateCollection();
        }

        public void Raise(object sender, TEventArgs e)
        {
            List<StrongHandler> validHandlers;
            lock (_handlers)
            {
                var weakHandlers = _handlers.ToArray();
                validHandlers = new List<StrongHandler>(weakHandlers.Length);
                int i = 0;
                foreach (var weakHandler in weakHandlers)
                {
                    if (weakHandler != null)
                    {
                        if (weakHandler.TryGetStrongHandler() is StrongHandler handler)
                            validHandlers.Add(handler);
                        else
                            _handlers.Invalidate(i);
                    }

                    i++;
                }

                _handlers.CollectDeleted();
            }

            foreach (var handler in validHandlers)
            {
                handler.Invoke(sender, e);
            }
        }

        public void Subscribe(EventHandler<TEventArgs> handler)
        {
            var singleHandlers = handler
                .GetInvocationList()
                .Cast<EventHandler<TEventArgs>>()
                .ToList();

            lock (_handlers)
            {
                foreach (var h in singleHandlers)
                    _handlers.Add(h);
            }
        }

        public void Unsubscribe(EventHandler<TEventArgs> handler)
        {
            var singleHandlers = handler
                .GetInvocationList()
                .Cast<EventHandler<TEventArgs>>();

            lock (_handlers)
            {
                foreach (var singleHandler in singleHandlers)
                {
                    _handlers.Remove(singleHandler);
                }

                _handlers.CollectDeleted();
            }
        }

        private delegate void OpenEventHandler(object target, object sender, TEventArgs e);

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

            public bool IsMatch(EventHandler<TEventArgs> handler)
            {
                return ReferenceEquals(handler.Target, _weakTarget?.Target)
                       && handler.GetMethodInfo().Equals(_method);
            }

            public static int GetHashCode(EventHandler<TEventArgs> handler)
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

            public void Invoke(object sender, TEventArgs e)
            {
                _openHandler(_target, sender, e);
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

            public void Add(EventHandler<TEventArgs> singleHandler)
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

            internal void Remove(EventHandler<TEventArgs> singleHandler)
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

            private void AddToIndex(EventHandler<TEventArgs> singleHandler, int index)
            {
                var hashCode = WeakDelegate.GetHashCode(singleHandler);
                if (_index.ContainsKey(hashCode))
                    _index[hashCode].Add(index);
                else
                    _index.Add(hashCode, new List<int> { index });
            }

            WeakDelegate this[int index] => _delegates[index];

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
        }
    }
}