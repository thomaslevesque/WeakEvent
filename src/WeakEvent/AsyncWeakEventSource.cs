using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WeakEvent
{
    public delegate Task AsyncEventHandler<TEventArgs>(object sender, TEventArgs e);
  
    public class AsyncWeakEventSource<TEventArgs>
    {
        private DelegateCollection _handlers;

        public async Task RaiseAsync(object sender, TEventArgs e)
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
                await handler.Invoke(sender, e);
            }
        }

        public void Subscribe(AsyncEventHandler<TEventArgs> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            var singleHandlers = handler
                .GetInvocationList()
                .Cast<AsyncEventHandler<TEventArgs>>()
                .ToList();

            LazyInitializer.EnsureInitialized(ref _handlers);
            lock (_handlers)
            {
                foreach (var h in singleHandlers)
                    _handlers.Add(h);
            }
        }

        public void Unsubscribe(AsyncEventHandler<TEventArgs> handler)
        {
            if (handler is null)
                throw new ArgumentNullException(nameof(handler));

            if (_handlers is null)
                return;

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

        private class DelegateCollection : DelegateCollectionBase<OpenEventHandler, StrongHandler>
        {
            public DelegateCollection()
                : base((target, openHandler) => new StrongHandler(target, openHandler))
            {
            }
        }
    }
}