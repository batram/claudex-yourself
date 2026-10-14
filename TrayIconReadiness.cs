using System.Diagnostics;

namespace ClaudexYourself;

internal static class TrayIconReadiness
{
    // Discovery can be retried safely; sending Exit cannot, because the user
    // may cancel its confirmation. Keep this wait entirely before that action.
    internal static async Task<T> WaitAsync<T>(Func<T[]> read, Func<Task> reveal,
        TimeSpan timeout, Func<TimeSpan, Task>? delay = null)
    {
        var elapsed = Stopwatch.StartNew();
        delay ??= Task.Delay;
        while (true)
        {
            var icons = read();
            if (icons.Length == 0)
            {
                await reveal();
                icons = read();
            }
            if (icons.Length == 1) return icons[0];
            if (icons.Length > 1 || elapsed.Elapsed >= timeout)
                throw new InvalidOperationException($"Expected one Codex tray icon; found {icons.Length}. No quit action was sent.");
            await delay(TimeSpan.FromMilliseconds(250));
        }
    }
}
