using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace WeakEvent
{
    internal abstract class DelegateCollectionBase<TOpenEventHandler, TStrongHandler>
        where TOpenEventHandler : Delegate
        where TStrongHandler : struct
    {
        #region Open handler generation and cache

        // ReSharper disable once StaticMemberInGenericType (by design)
        private static readonly ConcurrentDictionary<MethodInfo, TOpenEventHandler> OpenHandlerCache =
            new ConcurrentDictionary<MethodInfo, TOpenEventHandler>();

        private static readonly Type EventArgsType = typeof(TOpenEventHandler)
            .GetRuntimeMethods()
            .Single(m => m.Name == "Invoke")
            .GetParameters()
            .Last()
            .ParameterType;

        private static TOpenEventHandler CreateOpenHandler(MethodInfo method)
        {
            var target = Expression.Parameter(typeof(object), "target");
            var sender = Expression.Parameter(typeof(object), "sender");
            var e = Expression.Parameter(EventArgsType, "e");

            if (method.IsStatic)
            {
                var expr = Expression.Lambda<TOpenEventHandler>(
                    Expression.Call(
                        method,
                        sender, e),
                    target, sender, e);
                return expr.Compile();
            }
            else
            {
                var expr = Expression.Lambda<TOpenEventHandler>(
                    Expression.Call(
                        Expression.Convert(target, method.DeclaringType),
                        method,
                        sender, e),
                    target, sender, e);
                return expr.Compile();
            }
        }

        #endregion

        /// <summary>
        /// List of weak delegates subscribed to the event.
        /// </summary>
        private List<WeakDelegate<TOpenEventHandler, TStrongHandler>?> _delegates;

        /// <summary>
        /// Quick lookup index for individual handlers.
        /// The index is the handler's hashcode (computed by GetDelegateHashCode).
        /// The value is a list of indices in _delegates where there's a weak delegate
        /// for a handler with that hashcode.
        /// </summary>
        private readonly Dictionary<int, List<int>> _index;

        private int _deletedCount;

        private ConditionalWeakTable<object, List<object>>? _targetLifetimes;

        private readonly Func<object?, TOpenEventHandler, TStrongHandler> _createStrongHandler;

        protected DelegateCollectionBase(Func<object?, TOpenEventHandler, TStrongHandler> createStrongHandler)
        {
            _delegates = new List<WeakDelegate<TOpenEventHandler, TStrongHandler>?>();
            _index = new Dictionary<int, List<int>>();
            _createStrongHandler = createStrongHandler;
        }

        public void Add(object? lifetimeObject, Delegate[] invocationList)
        {
            foreach (var singleHandler in invocationList)
            {
                var openHandler = OpenHandlerCache.GetOrAdd(singleHandler.GetMethodInfo(), CreateOpenHandler);
                _delegates.Add(new WeakDelegate<TOpenEventHandler, TStrongHandler>(singleHandler, openHandler, _createStrongHandler));
                var index = _delegates.Count - 1;
                AddToIndex(singleHandler, index);
                KeepTargetAlive(lifetimeObject, singleHandler.Target);
            }
        }

        /// <summary>
        /// Removes the last occurrence of delegate's invocation list.
        /// </summary>
        /// <remarks>
        /// Follows the same logic as MulticastDelegate.Remove.
        /// </remarks>
        public void Remove(object? lifetimeObject, Delegate[] invocationList)
        {
            int matchIndex = GetIndexOfInvocationListLastOccurrence(invocationList);

            if (matchIndex < 0)
                return;

            for (int invocationIndex = invocationList.Length - 1; invocationIndex >= 0; invocationIndex--)
            {
                var singleHandler = invocationList[invocationIndex];
                var index = matchIndex + invocationIndex;
                _delegates[index] = null;
                var hashCode = GetDelegateHashCode(singleHandler);
                if (_index.TryGetValue(hashCode, out var indices))
                {
                    int lastIndex = indices.LastIndexOf(index);
                    if (lastIndex >= 0)
                    {
                        indices.RemoveAt(lastIndex);
                    }
                }

                _deletedCount++;
                StopKeepingTargetAlive(lifetimeObject, singleHandler.Target);
            }
        }

        public void Invalidate(int index)
        {
            _delegates[index] = null;
            _deletedCount++;
        }

        public void CollectDeleted()
        {
            // Only run collection if at least 25% of the handlers are dead
            if (_deletedCount < _delegates.Count / 4)
                return;

            // Make a new list with only live delegates, keeping track of the old and new indices
            int newCount = _delegates.Count - _deletedCount;
            var newIndices = new Dictionary<int, int>(newCount);
            var newDelegates = new List<WeakDelegate<TOpenEventHandler, TStrongHandler>?>(newCount);
            for (int oldIndex = 0; oldIndex < _delegates.Count; oldIndex++)
            {
                var oldDelegate = _delegates[oldIndex];
                if (oldDelegate != null)
                {
                    newDelegates.Add(oldDelegate);
                    newIndices.Add(oldIndex, newIndices.Count);
                }
            }

            _delegates = newDelegates;

            var hashCodes = _index.Keys.ToList();
            foreach (var hashCode in hashCodes)
            {
                if (_index[hashCode].Count == 0)
                {
                    _index.Remove(hashCode);
                }
                else
                {
                    var oldIndexList = _index[hashCode];
                    var newIndexList = new List<int>(oldIndexList.Count);
                    foreach (var oi in oldIndexList)
                    {
                        if (newIndices.TryGetValue(oi, out int ni))
                            newIndexList.Add(ni);
                    }
                    _index[hashCode] = newIndexList;
                }
            }

            _deletedCount = 0;
        }

        public WeakDelegate<TOpenEventHandler, TStrongHandler>? this[int index] => _delegates[index];

        public int Count => _delegates.Count;

        private static int GetDelegateHashCode(Delegate handler)
        {
            var hashCode = -335093136;
            hashCode = hashCode * -1521134295 + (handler?.Target?.GetHashCode()).GetValueOrDefault();
            hashCode = hashCode * -1521134295 + (handler?.GetMethodInfo()?.GetHashCode()).GetValueOrDefault();
            return hashCode;
        }

        private void AddToIndex(Delegate singleHandler, int index)
        {
            var hashCode = GetDelegateHashCode(singleHandler);
            if (_index.ContainsKey(hashCode))
                _index[hashCode].Add(index);
            else
                _index.Add(hashCode, new List<int> { index });
        }

        private void KeepTargetAlive(object? lifetimeObject, object? target)
        {
            // If the lifetime object isn't the same as the target,
            // keep the target alive while the lifetime object is alive

            if (lifetimeObject is null || target is null || lifetimeObject == target)
                return;

            LazyInitializer.EnsureInitialized(ref _targetLifetimes);
            var targets = _targetLifetimes!.GetOrCreateValue(lifetimeObject);
            targets.Add(target);
        }

        private void StopKeepingTargetAlive(object? lifetimeObject, object? target)
        {
            if (lifetimeObject is null || target is null || lifetimeObject == target)
                return;

            if (_targetLifetimes is null)
                return;

            if (_targetLifetimes.TryGetValue(lifetimeObject, out var targets))
            {
                int index = targets.LastIndexOf(target);
                if (index >= 0)
                    targets.RemoveAt(index);
            }
        }

        private int GetIndexOfInvocationListLastOccurrence(Delegate[] invocationList)
        {
            int lastMatchStartIndex = _delegates.Count;
            while (lastMatchStartIndex > 0)
            {
                int currentIndex = -1;
                for (int handlerIndex = invocationList.Length - 1; handlerIndex >= 0; handlerIndex--)
                {
                    var singleHandler = invocationList[handlerIndex];

                    if (currentIndex < 0)
                    {
                        // First iteration: find the last occurrence of the last handler of the invocation list.
                        // Note: we don't look before invocationList.Length - 1, because there wouldn't be
                        // enough room for the full invocation list.
                        currentIndex = GetIndexOfLastMatch(singleHandler, invocationList.Length - 1, lastMatchStartIndex - 1);
                        lastMatchStartIndex = currentIndex;

                        if (currentIndex < 0)
                        {
                            // No match
                            return -1;
                        }

                        // Full match found, return it
                        if (handlerIndex == 0)
                            return currentIndex;
                    }
                    else if (currentIndex > 0)
                    {
                        // We have a partial match, check if it continues to match
                        var @delegate = _delegates[currentIndex - 1];
                        if (@delegate != null && @delegate.IsMatch(singleHandler))
                        {
                            currentIndex--;

                            // Full match found, return it
                            if (handlerIndex == 0)
                                return currentIndex;
                        }
                        else
                        {
                            // Mismatch: we'll restart search from the index just before
                            // where the previous match started.
                            break;
                        }
                    }
                    else
                    {
                        // We should never get there due to previous checks.
                        // If we do anyway, there's no match
                        return -1;
                    }
                }
            }

            return -1;
        }

        private int GetIndexOfLastMatch(Delegate singleHandler, int start, int end)
        {
            for (int i = end; i >= start; i--)
            {
                var @delegate = _delegates[i];
                if (@delegate != null && @delegate.IsMatch(singleHandler))
                    return i;
            }

            return -1;
        }
    }
}