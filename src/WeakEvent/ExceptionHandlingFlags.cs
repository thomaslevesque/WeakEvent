using System;

namespace WeakEvent
{
    /// <summary>
    /// Flags that specify what to do with exceptions thrown by individual event handlers.
    /// An exception handler passed to <c>Raise</c> or <c>RaiseAsync</c> can return any combination
    /// of thse flags.
    /// </summary>
    [Flags]
    public enum ExceptionHandlingFlags
    {
        /// <summary>Do nothing.</summary>
        None = 0,
        /// <summary>Mark the exception as handled. If this flag isn't set, the exception will be rethrown.</summary>
        Handled = 1,
        /// <summary>Unsubscribe the handler that caused the exception.</summary>
        Unsubscribe = 2
    }
}