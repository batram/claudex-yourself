using System.Diagnostics;

namespace ClaudexYourself;

// One activation/debug-registration/shutdown operation at a time, including
// shortcut opens, recent-chat opens, updater recovery, and taskbar actions.
internal sealed class WindowsLaunchOperation : IDisposable
{
    private static readonly AsyncLocal<WindowsLaunchOperation?> Current = new();
    private readonly FileStream lease;
    private bool disposed;

    private WindowsLaunchOperation(FileStream lease) { this.lease = lease; }

    internal static IDisposable Enter(string? directory = null, TimeSpan? timeout = null)
    {
        if (Current.Value is { disposed: false }) return new NestedLease();
        directory ??= Program.StateDirectory;
        Directory.CreateDirectory(directory);
        var timer = Stopwatch.StartNew();
        var limit = timeout ?? TimeSpan.FromSeconds(90);
        while (true)
        {
            try
            {
                var stream = new FileStream(Path.Combine(directory, "taskbar-operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                var operation = new WindowsLaunchOperation(stream);
                Current.Value = operation;
                return operation;
            }
            catch (IOException exception) when ((exception.HResult & 0xffff) is 32 or 33)
            {
                if (timer.Elapsed >= limit)
                    throw new TimeoutException("Another Codex launch or shutdown is still running. This request could not acquire the launch lock.", exception);
                Thread.Sleep(50);
            }
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lease.Dispose();
        Current.Value = null;
    }

    private sealed class NestedLease : IDisposable { public void Dispose() { } }
}
