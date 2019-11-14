using System;
using System.Reflection;

namespace WeakEvent
{
    internal interface IStrongHandler<TOpenEventHandler>
        where TOpenEventHandler : Delegate
    {
        object? LifetimeObject { get; }
        object? Target { get; }
        TOpenEventHandler OpenHandler { get; }
        MethodInfo Method { get; }
    }
}