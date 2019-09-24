using System.Threading.Tasks;
using static WeakEvent.WeakEventSourceHelper;

namespace WeakEvent
{
    public delegate Task AsyncEventHandler<TEventArgs>(object sender, TEventArgs e);
  
    public class AsyncWeakEventSource<TEventArgs>
    {
        private DelegateCollection? _handlers;

        public async Task RaiseAsync(object? sender, TEventArgs e)
        {
            var validHandlers = GetValidHandlers(_handlers);
            foreach (var handler in validHandlers)
            {
                await handler.Invoke(sender, e);
            }
        }

        public void Subscribe(AsyncEventHandler<TEventArgs> handler)
        {
            Subscribe(null, handler);
        }

        public void Subscribe(object? lifetimeObject, AsyncEventHandler<TEventArgs> handler)
        {
            Subscribe<DelegateCollection, OpenEventHandler, StrongHandler>(lifetimeObject, ref _handlers, handler);
        }

        public void Unsubscribe(AsyncEventHandler<TEventArgs> handler)
        {
            Unsubscribe(null, handler);
        }

        public void Unsubscribe(object? lifetimeObject, AsyncEventHandler<TEventArgs> handler)
        {
            Unsubscribe<OpenEventHandler, StrongHandler>(lifetimeObject, _handlers, handler);
        }

        private delegate Task OpenEventHandler(object? target, object? sender, TEventArgs e);

        private struct StrongHandler
        {
            private readonly object? _target;
            private readonly OpenEventHandler _openHandler;

            public StrongHandler(object? target, OpenEventHandler openHandler)
            {
                _target = target;
                _openHandler = openHandler;
            }

            public Task Invoke(object? sender, TEventArgs e)
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