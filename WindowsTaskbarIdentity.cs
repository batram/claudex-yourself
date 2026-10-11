using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ClaudexYourself;

[SupportedOSPlatform("windows")]
internal static class WindowsTaskbarIdentity
{
    // Shell grouping is deliberately separate from Codex's package/notification ID.
    internal const string AppId = "ClaudexYourself.ControlledCodex";
    private static readonly HashSet<(IntPtr Window, uint Process)> Applied = [];

    internal static void Refresh(string desktopExecutable, string launcher, string icon)
    {
        var processes = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(process.MainModule?.FileName, desktopExecutable, StringComparison.OrdinalIgnoreCase))
                        processes.Add((uint)process.Id);
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        var present = new HashSet<(IntPtr, uint)>();
        Exception? failure = null;
        if (!EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var pid);
            if (!processes.Contains(pid) || !IsWindowVisible(window) || GetWindow(window, 4) != IntPtr.Zero) return true;
            var name = new StringBuilder(256);
            GetClassName(window, name, name.Capacity);
            if (name.ToString() != "Chrome_WidgetWin_1") return true;
            var identity = (window, pid);
            present.Add(identity);
            if (Applied.Contains(identity)) return true;
            try
            {
                WindowsShortcut.SetWindowIdentity(window, AppId, $"\"{launcher}\" taskbar claudex", "Codex (controlled)", icon + ",0");
                Applied.Add(identity);
                Console.WriteLine($"Assigned {AppId} to controlled window 0x{window.ToInt64():X} (PID {pid}).");
            }
            catch (Exception exception) { failure = exception; }
            return true;
        }, IntPtr.Zero)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        Applied.RemoveWhere(item => !present.Contains(item));
        if (failure is not null) throw new InvalidOperationException("Could not assign Claudex taskbar identity to a controlled window.", failure);
    }

    private delegate bool WindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumWindows(WindowCallback callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int length);
}
