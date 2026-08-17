using Dalamud.Plugin.Services;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TouristMod.Services;

public sealed class NotificationSchedulerService(NotificationMasterIpc ipc, IPluginLog log) : IDisposable
{
    private readonly ConcurrentDictionary<int, DateTimeOffset> _scheduled = new();
    private readonly CancellationTokenSource _cts = new();

    // Called from Draw(); safe to spam — dedupes by idx+fireAt.
    public void Schedule(int idx, DateTimeOffset fireAt, string message)
    {
        // If already scheduled for (approximately) the same time, skip.
        if (_scheduled.TryGetValue(idx, out var existing) &&
            Math.Abs((existing - fireAt).TotalSeconds) < 5)
            return;

        _scheduled[idx] = fireAt;
        _ = RunAsync(idx, fireAt, message, _cts.Token);
        log.Debug($"Created notify task: '{message}' at {fireAt.ToLocalTime()}");
    }

    private async Task RunAsync(int idx, DateTimeOffset fireAt, string message, CancellationToken ct)
    {
        try
        {
            var delay = fireAt - DateTimeOffset.UtcNow;
            if (delay > TimeSpan.Zero)
                await Task.Delay(delay, ct);

            // Re-check we're still the current scheduled entry (avoid stale fires).
            if (_scheduled.TryGetValue(idx, out var current) && current == fireAt)
            {
                ipc.DisplayTray("Tourist", message);
                _scheduled.TryRemove(new KeyValuePair<int, DateTimeOffset>(idx, fireAt));
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { log.Error(ex, "Tourist notification failed"); }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
