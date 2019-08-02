using System;
using System.Reflection;

namespace WeakEvent
{
    internal class WeakDelegate<TOpenEventHandler, TStrongHandler>
        where TOpenEventHandler : Delegate
        where TStrongHandler : struct
    {
        private readonly WeakReference _weakTarget;
        private readonly MethodInfo _method;
        private readonly TOpenEventHandler _openHandler;
        private readonly Func<object, TOpenEventHandler, TStrongHandler> _createStrongHandler;

        public WeakDelegate(
            Delegate handler,
            TOpenEventHandler openHandler,
            Func<object, TOpenEventHandler, TStrongHandler> createStrongHandler)
        {
            _weakTarget = handler.Target != null ? new WeakReference(handler.Target) : null;
            _method = handler.GetMethodInfo();
            _openHandler = openHandler;
            _createStrongHandler = createStrongHandler;
        }

        public TStrongHandler? TryGetStrongHandler()
        {
            object target = null;
            if (_weakTarget != null)
            {
                target = _weakTarget.Target;
                if (target == null)
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