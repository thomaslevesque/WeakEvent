using System;
using System.Reflection;

namespace WeakEvent
{
    internal class WeakDelegate<TOpenEventHandler, TStrongHandler>
        where TOpenEventHandler : Delegate
        where TStrongHandler : struct
    {
        private readonly WeakReference? _weakLifetimeObject;
        private readonly WeakReference? _weakTarget;
        private readonly MethodInfo _method;
        private readonly TOpenEventHandler _openHandler;
        private readonly StrongHandlerFactory<TOpenEventHandler, TStrongHandler> _createStrongHandler;

        public WeakDelegate(
            object? lifetimeObject,
            Delegate handler,
            TOpenEventHandler openHandler,
            StrongHandlerFactory<TOpenEventHandler, TStrongHandler> createStrongHandler)
        {
            _weakLifetimeObject = lifetimeObject is {} ? new WeakReference(lifetimeObject) : null;
            _weakTarget = handler.Target is {} ? new WeakReference(handler.Target) : null;
            _method = handler.GetMethodInfo();
            _openHandler = openHandler;
            _createStrongHandler = createStrongHandler;
        }

        public object? LifetimeObject => _weakLifetimeObject?.Target;
        public bool IsAlive => _weakTarget?.IsAlive ?? true;

        public TStrongHandler? TryGetStrongHandler()
        {
            object? target = null;
            if (_weakTarget is {})
            {
                target = _weakTarget.Target;
                if (target is null)
                    return null;
            }

            return _createStrongHandler(target, _openHandler);
        }

        public bool IsMatch(Delegate handler)
        {
            return ReferenceEquals(handler.Target, _weakTarget?.Target)
                    && handler.GetMethodInfo().Equals(_method);
        }
    }
}