#if NET40
using System.Collections.Generic;

namespace System.Reflection
{
    internal static class CompatibilityExtensions
    {
        public static MethodInfo GetMethodInfo(this Delegate @delegate)
        {
            return @delegate.Method;
        }

        public static IEnumerable<MethodInfo> GetRuntimeMethods(this Type type)
        {
            return type.GetMethods();
        }
    }
}
#endif
