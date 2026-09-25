namespace Backend.Api.Services.GeoCodingService;

using System.Diagnostics;

public static class NominatimRateLimiter
{
    private static readonly SemaphoreSlim Semaphore = new(1, 1);
    private static long _lastRequestTimestamp;
    private static readonly TimeSpan MinInterval = TimeSpan.FromMilliseconds(1100);

    public static async Task WaitAsync(CancellationToken ct = default)
    {
        await Semaphore.WaitAsync(ct);
        try
        {
            if (_lastRequestTimestamp != 0)
            {
                var elapsed = Stopwatch.GetElapsedTime(_lastRequestTimestamp);
                if (elapsed < MinInterval)
                {
                    await Task.Delay(MinInterval - elapsed, ct);
                }
            }

            _lastRequestTimestamp = Stopwatch.GetTimestamp();
        }
        finally
        {
            Semaphore.Release();
        }
    }
}
