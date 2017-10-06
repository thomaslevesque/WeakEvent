#if NET40 || NET35
using System;
using System.Reflection;

namespace WeakEvent
{
    internal static class CompatibilityExtensions
    {
        public static MethodInfo GetMethodInfo(this Delegate @delegate)
        {
            return @delegate.Method;
        }
    }
}
#endif
