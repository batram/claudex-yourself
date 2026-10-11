using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

namespace ClaudexYourself;

internal static class BackgroundWorker
{
    private static readonly object StartGate = new();
    internal static Process Start(string command, params string[] arguments)
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot locate the worker executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
        start.ArgumentList.Add(command);
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        lock (StartGate)
        {
            // Windows can inherit other inheritable handles in addition to the redirected
            // streams. Exclude the launcher's original capture pipes from that inheritance.
            var excluded = new List<(IntPtr Handle, uint Flags)>();
            try
            {
                if (OperatingSystem.IsWindows())
                    foreach (var kind in new[] { -10, -11, -12 })
                    {
                        var handle = GetStdHandle(kind);
                        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;
                        if (GetHandleInformation(handle, out var flags) && (flags & 1) != 0)
                        {
                            if (!SetHandleInformation(handle, 1, 0)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                            excluded.Add((handle, flags));
                        }
                    }
                return Process.Start(start) ?? throw new InvalidOperationException($"Could not start {command}.");
            }
            finally
            {
                if (OperatingSystem.IsWindows())
                    foreach (var item in excluded) SetHandleInformation(item.Handle, 1, item.Flags & 1);
            }
        }
    }

    internal static async Task<int> RunAsync(string command, Func<Task<int>> run)
    {
        // Independent pipes let captured launches finish. Diagnostic files remain writable
        // after the launcher exits, including when network checks fail.
        var directory = Path.Combine(Program.StateDirectory, "worker-logs");
        Directory.CreateDirectory(directory);
        using var stream = new FileStream(Path.Combine(directory, command + ".log"), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
        Console.SetOut(writer);
        Console.SetError(writer);
        try { return await run(); }
        catch (Exception exception) { writer.WriteLine($"{DateTime.UtcNow:O} {exception}"); return 1; }
    }
    [DllImport("kernel32.dll")] private static extern IntPtr GetStdHandle(int kind);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetHandleInformation(IntPtr handle, out uint flags);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetHandleInformation(IntPtr handle, uint mask, uint flags);
}
