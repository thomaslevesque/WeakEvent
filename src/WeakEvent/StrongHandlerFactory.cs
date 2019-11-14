using System;
using System.Reflection;

namespace WeakEvent
{
    internal delegate TStrongHandler StrongHandlerFactory<TOpenEventHandler, TStrongHandler>(object? lifetimeObject, object? target, TOpenEventHandler openHandler, MethodInfo method)
        where TOpenEventHandler : Delegate
        where TStrongHandler : struct, IStrongHandler<TOpenEventHandler>;
}
