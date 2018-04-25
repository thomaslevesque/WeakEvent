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
            lock (_handlers)
            {
                var failedHandlers = new List<int>();
                int i = 0;
                foreach (var handler in _handlers.ToArray())
                {
                    if (handler == null || !handler.Invoke(sender, e))
                    {
                        failedHandlers.Add(i);
                    }

                    i++;
                }

                foreach (var index in failedHandlers)
                    _handlers.Invalidate(index);

                _handlers.CollectDeleted();
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

        class WeakDelegate
        {
#region Open handler generation and cache

            private delegate void OpenEventHandler(object target, object sender, TEventArgs e);

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

            public bool Invoke(object sender, TEventArgs e)
            {
                object target = null;
                if (_weakTarget != null)
                {
                    target = _weakTarget.Target;
                    if (target == null)
                        return false;
                }
                _openHandler(target, sender, e);
                return true;
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


        private class DelegateCollection : IEnumerable<WeakDelegate>
        {
            private List<WeakDelegate> Delegates { get; set; }

            private Dictionary<long, List<int>> Index { get; set; }

            private int DeletedCount { get; set; }

            public DelegateCollection()
            {
                Delegates = new List<WeakDelegate>();
                Index = new Dictionary<long, List<int>>();
            }

            public void Add(EventHandler<TEventArgs> singleHandler)
            {
                Delegates.Add(new WeakDelegate(singleHandler));
                var index = Delegates.Count - 1;
                AddToIndex(singleHandler, index);
            }

            public void Invalidate(int index)
            {
                Delegates[index] = null;
                DeletedCount++;
            }

            internal void Remove(EventHandler<TEventArgs> singleHandler)
            {
                var hashCode = WeakDelegate.GetHashCode(singleHandler);

                if (!Index.ContainsKey(hashCode))
                    return;

                var indices = Index[hashCode];
                for (int i = indices.Count - 1; i >= 0; i--)
                {
                    int index = indices[i];
                    if (Delegates[index] != null &&
                        Delegates[index].IsMatch(singleHandler))
                    {
                        Delegates[index] = null;
                        DeletedCount++;
                        indices.Remove(i);
                    }
                }

                if (indices.Count == 0)
                    Index.Remove(hashCode);
            }

            public void CollectDeleted()
            {
                if (DeletedCount < Delegates.Count / 4)
                    return;

                Dictionary<int, int> newIndices = new Dictionary<int, int>();
                var newDelegates = new List<WeakDelegate>();
                int oldIndex = 0;
                int newIndex = 0;
                foreach (var item in Delegates)
                {
                    if (item != null)
                    {
                        newDelegates.Add(item);
                        newIndices.Add(oldIndex, newIndex);
                        newIndex++;
                    }

                    oldIndex++;
                }

                Delegates = newDelegates;

                var hashCodes = Index.Keys.ToList();
                foreach (var hashCode in hashCodes)
                {
                    Index[hashCode] = Index[hashCode]
                        .Where(oi => newIndices.ContainsKey(oi))
                        .Select(oi => newIndices[oi]).ToList();
                }

                DeletedCount = 0;
            }

            private void AddToIndex(EventHandler<TEventArgs> singleHandler, int index)
            {
                var hashCode = WeakDelegate.GetHashCode(singleHandler);
                if (Index.ContainsKey(hashCode))
                    Index[hashCode].Add(index);
                else
                    Index.Add(hashCode, new List<int> { index });
            }

            WeakDelegate this[int index]
            {
                get { return Delegates[index]; }
            }

            /// <summary>Returns an enumerator that iterates through the collection.</summary>
            /// <returns>A <see cref="T:System.Collections.Generic.IEnumerator`1" /> that can be used to iterate through the collection.</returns>
            public IEnumerator<WeakDelegate> GetEnumerator()
            {
                return Delegates.GetEnumerator();
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
