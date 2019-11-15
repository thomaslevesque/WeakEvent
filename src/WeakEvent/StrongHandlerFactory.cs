using System;

namespace WeakEvent
{
    internal delegate TStrongHandler StrongHandlerFactory<TOpenEventHandler, TStrongHandler>(object? target, WeakDelegate<TOpenEventHandler, TStrongHandler> weakHandler)
        where TOpenEventHandler : Delegate
        where TStrongHandler : struct, IStrongHandler<TOpenEventHandler, TStrongHandler>;
}
