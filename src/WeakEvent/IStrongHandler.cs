using System;

namespace WeakEvent
{
    internal interface IStrongHandler<TOpenEventHandler, TStrongHandler>
        where TOpenEventHandler : Delegate
        where TStrongHandler : struct, IStrongHandler<TOpenEventHandler, TStrongHandler>
    {
        object? Target { get; }
        WeakDelegate<TOpenEventHandler, TStrongHandler> WeakHandler { get; }
    }
}