using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WeakEvent
{
    internal static class WeakEventSourceHelper
    {
        public static IEnumerable<TStrongHandler> GetValidHandlers<TOpenEventHandler, TStrongHandler>(
            DelegateCollectionBase<TOpenEventHandler, TStrongHandler> handlers)
            where TOpenEventHandler : Delegate
            where TStrongHandler : struct
        {
            if (handlers is null)
                return Enumerable.Empty<TStrongHandler>();

            List<TStrongHandler> validHandlers;
            lock (handlers)
            {
                validHandlers = new List<TStrongHandler>(handlers.Count);
                for (int i = 0; i < handlers.Count; i++)
                {
                    var weakHandler = handlers[i];
                    if (weakHandler != null)
                    {
                        if (weakHandler.TryGetStrongHandler() is TStrongHandler handler)
                            validHandlers.Add(handler);
                        else
                            handlers.Invalidate(i);
                    }
                }

                handlers.CollectDeleted();
            }

            return validHandlers;
        }

        public static void Subscribe<TDelegateCollection, TOpenEventHandler, TStrongHandler>(
            ref TDelegateCollection handlers,
            Delegate handler)
            where TDelegateCollection : DelegateCollectionBase<TOpenEventHandler, TStrongHandler>, new()
            where TOpenEventHandler : Delegate
            where TStrongHandler : struct
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            var singleHandlers = handler
                .GetInvocationList();

            LazyInitializer.EnsureInitialized(ref handlers);
            lock (handlers)
            {
                foreach (var h in singleHandlers)
                    handlers.Add(h);
            }
        }

        public static void Unsubscribe<TOpenEventHandler, TStrongHandler>(
            DelegateCollectionBase<TOpenEventHandler, TStrongHandler> handlers,
            Delegate handler)
            where TOpenEventHandler : Delegate
            where TStrongHandler : struct
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            if (handlers is null)
                return;

            var singleHandlers = handler
                .GetInvocationList();

            lock (handlers)
            {
                foreach (var singleHandler in singleHandlers)
                {
                    handlers.Remove(singleHandler);
                }

                handlers.CollectDeleted();
            }
        }
    }
}