using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace WeakEvent
{
    public class WeakEventSource<TEventArgs>
#if NET40 || NET35
        where TEventArgs : EventArgs
#endif
    {
        private DelegateCollection _handlers;

        public void Raise(object sender, TEventArgs e)
        {
            if (_handlers is null)
                return;

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
                handler.Invoke(sender, e);
            }
        }

        public void Subscribe(EventHandler<TEventArgs> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            var singleHandlers = handler
                .GetInvocationList()
                .Cast<EventHandler<TEventArgs>>()
                .ToList();

            LazyInitializer.EnsureInitialized(ref _handlers);
            lock (_handlers)
            {
                foreach (var h in singleHandlers)
                    _handlers.Add(h);
            }
        }

        public void Unsubscribe(EventHandler<TEventArgs> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            if (_handlers is null)
                return;

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

        private class DelegateCollection : DelegateCollectionBase<OpenEventHandler, StrongHandler>
        {
            public DelegateCollection()
                : base((target, openHandler) => new StrongHandler(target, openHandler))
            {
            }
        }
    }
}