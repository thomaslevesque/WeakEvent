#if NET35
namespace System.Threading
{
    static class LazyInitializer
    {
        public static T EnsureInitialized<T>(ref T target) where T : class, new()
        {
            if (Volatile.Read(ref target) is null)
            {
                var value = new T();
                Interlocked.CompareExchange(ref target, value, null);
            }
            return target;
        }
    }

    static class Volatile
    {
        public static T Read<T>(ref T location) where T : class
        {
            var value = location;
            Thread.MemoryBarrier();
            return value;
        }
    }
}
#endif