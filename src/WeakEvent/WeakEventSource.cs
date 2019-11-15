using System;
using System.Reflection;
using static WeakEvent.WeakEventSourceHelper;

namespace WeakEvent
{
    /// <summary>
    /// An event with weak subscription, i.e. it won't keep handlers from being garbage collected.
    /// </summary>
    /// <typeparam name="TEventArgs">The type of the event's arguments.</typeparam>
    public class WeakEventSource<TEventArgs>
#if NET40
        where TEventArgs : EventArgs
#endif
    {
        internal DelegateCollection? _handlers;

        /// <summary>
        /// Raises the event by invoking each handler that hasn't been garbage collected.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="args">An object that contains the event data.</param>
        /// <remarks>The handlers are invoked one after the other, in the order they were subscribed in.</remarks>
        public void Raise(object? sender, TEventArgs args)
        {
            var validHandlers = GetValidHandlers(_handlers);
            foreach (var handler in validHandlers)
            {
                handler.Invoke(sender, args);
            }
        }

        /// <summary>
        /// Raises the event by invoking each handler that hasn't been garbage collected. Exceptions thrown by
        /// individual handlers are passed to the specified <c>exceptionHandler</c> to decide what to do with them.
        /// </summary>
        /// <param name="sender">The source of the event.</param>
        /// <param name="args">An object that contains the event data.</param>
        /// <param name="exceptionHandler">A delegate that processes exceptions thrown by individual handlers.</param>
        /// <remarks>The handlers are invoked one after the other, in the order they were subscribed in.</remarks>
        public void Raise(object? sender, TEventArgs args, Func<Exception, ExceptionHandlingFlags> exceptionHandler)
        {
            if (exceptionHandler is null) throw new ArgumentNullException(nameof(exceptionHandler));
            var validHandlers = GetValidHandlers(_handlers);
            foreach (var handler in validHandlers)
            {
                try
                {
                    handler.Invoke(sender, args);
                }
                catch (Exception ex) when (HandleException(_handlers, handler, exceptionHandler(ex)))
                {
                }
            }
        }

        /// <summary>
        /// Adds an event handler.
        /// </summary>
        /// <param name="handler">The handler to subscribe.</param>
        /// <remarks>Only a weak reference to the handler's <c>Target</c> is kept, so that it can be garbage collected.</remarks>
        public void Subscribe(EventHandler<TEventArgs> handler)
        {
            Subscribe(null, handler);
        }

        /// <summary>
        /// Adds an event handler, specifying a lifetime object.
        /// </summary>
        /// <param name="lifetimeObject">An object that keeps the handler alive as long as it's alive.</param>
        /// <param name="handler">The handler to subscribe.</param>
        /// <remarks>Only a weak reference to the handler's <c>Target</c> is kept, so that it can be garbage collected.
        /// However, as long as the <c>lifetime</c> object is alive, the handler will be kept alive. This is useful for
        /// subscribing with anonymous methods (e.g. lambda expressions).</remarks>
        public void Subscribe(object? lifetimeObject, EventHandler<TEventArgs> handler)
        {
            Subscribe<DelegateCollection, OpenEventHandler, StrongHandler>(lifetimeObject, ref _handlers, handler);
        }

        /// <summary>
        /// Removes an event handler.
        /// </summary>
        /// <param name="handler">The handler to unsubscribe.</param>
        /// <remarks>The behavior is the same as that of <see cref="Delegate.Remove(Delegate, Delegate)"/>. Only the last instance
        /// of the handler's invocation list is removed. If the exact invocation list is not found, nothing is removed.</remarks>
        public void Unsubscribe(EventHandler<TEventArgs> handler)
        {
            Unsubscribe<OpenEventHandler, StrongHandler>(_handlers, handler);
        }

        /// <summary>
        /// Removes an event handler that was subscribed with a lifetime object.
        /// </summary>
        /// <param name="lifetimeObject">The lifetime object that was associated with the handler.</param>
        /// <param name="handler">The handler to unsubscribe.</param>
        /// <remarks>The behavior is the same as that of <see cref="Delegate.Remove(Delegate, Delegate)"/>. Only the last instance
        /// of the handler's invocation list is removed. If the exact invocation list is not found, nothing is removed.</remarks>
        [Obsolete("This method is obsolete and will be removed in a future version. Use the Unsubscribe overload that doesn't take a lifetime object instead.")]
        public void Unsubscribe(object? lifetimeObject, EventHandler<TEventArgs> handler)
        {
            Unsubscribe(handler);
        }

        internal delegate void OpenEventHandler(object? target, object? sender, TEventArgs e);

        internal struct StrongHandler : IStrongHandler<OpenEventHandler, StrongHandler>
        {
            public StrongHandler(object? target, WeakDelegate<OpenEventHandler, StrongHandler> weakHandler)
            {
                Target = target;
                WeakHandler = weakHandler;
            }

            public object? Target { get; }
            public WeakDelegate<OpenEventHandler, StrongHandler> WeakHandler { get; }

            public void Invoke(object? sender, TEventArgs e)
            {
                WeakHandler.OpenHandler(Target, sender, e);
            }
        }

        internal class DelegateCollection : DelegateCollectionBase<OpenEventHandler, StrongHandler>
        {
            public DelegateCollection()
                : base((target, weakHandler) => new StrongHandler(target, weakHandler))
            {
            }
        }
    }
}