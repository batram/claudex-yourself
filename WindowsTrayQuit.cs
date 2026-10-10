using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClaudexYourself;

[SupportedOSPlatform("windows")]
internal static class WindowsTrayQuit
{
    private sealed record Item(object Accessible, int Child, string Name, int Role, int X, int Y, int Width, int Height);
    private static readonly Guid AccessibleId = new("618736E0-3C3D-11CF-810C-00AA00389B71");

    internal static string[] Inspect() => TrayItems().Where(item => IsCodexIcon(item) || IsChevron(item)).Select(item => $"{item.Name} (role {item.Role})").Distinct().ToArray();

    private static HashSet<uint> AppProcessIds(string executablePath)
    {
        var ids = new HashSet<uint>();
        foreach (var process in Process.GetProcessesByName("ChatGPT"))
        {
            using (process)
            {
                try { if (string.Equals(process.MainModule?.FileName, executablePath, StringComparison.OrdinalIgnoreCase)) ids.Add((uint)process.Id); }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }
        return ids;
    }

    internal static async Task<string[]> InspectHiddenAsync()
    {
        await RevealHiddenAsync();
        return Inspect();
    }

    private static async Task RevealHiddenAsync()
    {
        var chevron = TrayItems().FirstOrDefault(IsChevron);
        if (chevron is null) return;
        ((IAccessible)chevron.Accessible).DoDefaultAction(chevron.Child);
        await Task.Delay(250);
    }

    internal static async Task RequestAsync(string executablePath, bool invoke = true)
    {
        var ids = AppProcessIds(executablePath);
        if (ids.Count == 0) throw new InvalidOperationException("No running Codex app was found for tray Exit.");
        var alreadyOpen = ExitItems(ids);
        if (alreadyOpen.Count == 1)
        {
            InvokeExit(alreadyOpen[0], invoke);
            return;
        }
        var icons = TrayItems().Where(IsCodexIcon).ToArray();
        if (icons.Length == 0) { await RevealHiddenAsync(); icons = TrayItems().Where(IsCodexIcon).ToArray(); }
        // A freshly auto-activated package can precede its tray icon. Wait only
        // before sending an Exit action; never retry a quit the user may cancel.
        var ready = Stopwatch.StartNew();
        while (icons.Length == 0 && ready.Elapsed < TimeSpan.FromSeconds(5))
        {
            await Task.Delay(100);
            icons = TrayItems().Where(IsCodexIcon).ToArray();
        }
        if (icons.Length != 1) throw new InvalidOperationException($"Expected one Codex tray icon; found {icons.Length}. No quit action was sent.");
        var icon = icons[0];
        if (icon.Width <= 0 || icon.Height <= 0) throw new InvalidOperationException("Codex's tray icon is not accessible.");
        var x = icon.X + icon.Width / 2;
        var y = icon.Y + icon.Height / 2;
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("Could not target the Codex tray icon.");
        MouseInput[] inputs = [Mouse(0x0008), Mouse(0x0010)];
        if (SendInput(2, inputs, Marshal.SizeOf<MouseInput>()) != 2) throw new InvalidOperationException("Could not open Codex's tray menu.");
        Console.WriteLine("Opened Codex tray menu; locating Exit.");
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(5))
        {
            var exits = ExitItems(ids);
            if (exits.Count == 1)
            {
                InvokeExit(exits[0], invoke);
                return;
            }
            if (exits.Count > 1) throw new InvalidOperationException("Codex exposed multiple Exit items; no quit action was sent.");
            await Task.Delay(100);
        }
        throw new InvalidOperationException("Codex's tray Exit item was not accessible. No quit action was sent.");
    }

    private static List<Item> ExitItems(HashSet<uint> ids)
    {
        var exits = new List<Item>();
        EnumWindows((window, _) =>
        {
            GetWindowThreadProcessId(window, out var id);
            if (ids.Contains(id) && IsWindowVisible(window))
                exits.AddRange(Items(window).Where(item => item.Role is 12 or 43 && item.Name is "Exit" or "Quit" or "Exit Codex" or "Quit Codex" or "Quit ChatGPT"));
            return true;
        }, IntPtr.Zero);
        return exits;
    }

    private static void InvokeExit(Item exit, bool invoke)
    {
        if (!invoke) { Console.WriteLine($"Verified Codex-owned tray Exit item (role {exit.Role}) without invoking it."); return; }
        ((IAccessible)exit.Accessible).DoDefaultAction(exit.Child);
        Console.WriteLine("Invoked Codex tray Exit.");
    }

    private static bool IsCodexIcon(Item item) => item.Role == 43 && item.Name is "Codex" or "ChatGPT";
    private static bool IsChevron(Item item) => item.Role == 43 && (item.Name == "Notification Chevron" || item.Name.Contains("Show hidden icons", StringComparison.OrdinalIgnoreCase));

    private static List<Item> TrayItems()
    {
        var results = new List<Item>();
        var taskbar = FindWindow("Shell_TrayWnd", null);
        var notificationArea = FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (notificationArea != IntPtr.Zero) results.AddRange(Items(notificationArea));
        var overflow = FindWindow("NotifyIconOverflowWindow", null);
        if (overflow != IntPtr.Zero && IsWindowVisible(overflow)) results.AddRange(Items(overflow));
        return results;
    }

    private static List<Item> Items(IntPtr window)
    {
        var items = new List<Item>();
        var iid = AccessibleId;
        if (AccessibleObjectFromWindow(window, unchecked((uint)-4), ref iid, out var root) != 0 || root is null) return items;
        Walk(root, 0, items, 0);
        return items;
    }

    private static void Walk(object accessible, int child, List<Item> items, int depth)
    {
        if (depth > 16 || items.Count > 500) return;
        try
        {
            var acc = (IAccessible)accessible;
            string name = acc.GetName(child) ?? "";
            int role = Convert.ToInt32(acc.GetRole(child));
            acc.Location(out int x, out int y, out int width, out int height, child);
            items.Add(new Item(accessible, child, name, role, x, y, width, height));
            if (child != 0) return;
            int count = Math.Min(acc.ChildCount, 500);
            var children = new object[count];
            if (count == 0 || AccessibleChildren(accessible, 0, count, children, out var obtained) < 0) return;
            foreach (var value in children.Take(obtained))
                if (value is int childId) Walk(accessible, childId, items, depth + 1);
                else if (value is not null) Walk(value, 0, items, depth + 1);
        }
        catch (COMException) { } // Popup or icon disappeared during traversal.
    }

    [DllImport("oleacc.dll")] private static extern int AccessibleObjectFromWindow(IntPtr window, uint objectId, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object accessible);
    [DllImport("oleacc.dll")] private static extern int AccessibleChildren([MarshalAs(UnmanagedType.Interface)] object accessible, int start, int count, [Out, MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.Struct, SizeParamIndex = 2)] object[] children, out int obtained);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint id);
    private delegate bool EnumWindowCallback(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
    private static MouseInput Mouse(uint flags) => new() { Type = 0, Data = new MouseData { Flags = flags } };
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public uint Type; public MouseData Data; }
    [StructLayout(LayoutKind.Sequential)] private struct MouseData { public int X, Y; public uint Data, Flags, Time; public UIntPtr Extra; }
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, [In] MouseInput[] inputs, int size);

    [ComImport, Guid("618736E0-3C3D-11CF-810C-00AA00389B71"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
    private interface IAccessible
    {
        object Parent { [return: MarshalAs(UnmanagedType.IDispatch)] get; }
        int ChildCount { get; }
        [return: MarshalAs(UnmanagedType.IDispatch)] object GetChild([MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.BStr)] string? GetName([MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.BStr)] string? GetValue([MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.BStr)] string? GetDescription([MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.Struct)] object GetRole([MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.Struct)] object GetState([MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.BStr)] string? GetHelp([MarshalAs(UnmanagedType.Struct)] object child);
        int GetHelpTopic([MarshalAs(UnmanagedType.BStr)] out string file, [MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.BStr)] string? GetKeyboardShortcut([MarshalAs(UnmanagedType.Struct)] object child);
        object Focus { [return: MarshalAs(UnmanagedType.Struct)] get; }
        object Selection { [return: MarshalAs(UnmanagedType.Struct)] get; }
        [return: MarshalAs(UnmanagedType.BStr)] string? GetDefaultAction([MarshalAs(UnmanagedType.Struct)] object child);
        void Select(int flags, [MarshalAs(UnmanagedType.Struct)] object child);
        void Location(out int x, out int y, out int width, out int height, [MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.Struct)] object Navigate(int direction, [MarshalAs(UnmanagedType.Struct)] object child);
        [return: MarshalAs(UnmanagedType.Struct)] object HitTest(int x, int y);
        void DoDefaultAction([MarshalAs(UnmanagedType.Struct)] object child);
        void SetName([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] string name);
        void SetValue([MarshalAs(UnmanagedType.Struct)] object child, [MarshalAs(UnmanagedType.BStr)] string value);
    }
}
