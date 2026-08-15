using System.Threading;

namespace TouristMod.Util;

internal static class SemaphoreExt
{
    internal static OnDispose With(this SemaphoreSlim semaphore)
    {
        semaphore.Wait();
        return new OnDispose(() => semaphore.Release());
    }
}
