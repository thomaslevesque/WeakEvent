using System;

namespace WeakEvent
{
    internal delegate TStrongHandler StrongHandlerFactory<TOpenEventHandler, TStrongHandler>(object? target, TOpenEventHandler openHandler)
        where TOpenEventHandler : Delegate
        where TStrongHandler : struct;
}
