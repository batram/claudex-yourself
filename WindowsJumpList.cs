using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ClaudexYourself;

[SupportedOSPlatform("windows")]
internal static class WindowsJumpList
{
    internal static void Install(string appId, string executable, string icon)
    {
        var list = (ICustomDestinationList)Create("77F10CF0-3DB5-4966-B520-B7C54FD35ED6");
        var tasks = (IObjectCollection)Create("2D3468C1-36A7-43B6-AC24-D3F02FD9607A");
        var taskLinks = new List<IShellLink>();
        object? removed = null;
        var begun = false;
        try
        {
            foreach (var entry in Entries)
            {
                var task = (IShellLink)Create("00021401-0000-0000-C000-000000000046");
                taskLinks.Add(task);
                Check(task.SetPath(executable));
                Check(task.SetArguments($"taskbar {entry.Action}"));
                Check(task.SetWorkingDirectory(Path.GetDirectoryName(executable)!));
                Check(task.SetIconLocation(icon, 0));
                Check(task.SetShowCmd(0)); // Hide the console; task failures are shown in a dialog.
                Check(task.SetDescription(entry.Description));
                WindowsShortcut.SetStringProperty(task, new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9"), 2, entry.Title);
                Check(tasks.AddObject(task));
            }
            Check(list.SetAppID(appId));
            var arrayId = typeof(IObjectArray).GUID;
            Check(list.BeginList(out _, ref arrayId, out removed));
            begun = true;
            // Preserve Windows' automatic Recent category when the user permits history.
            var recent = list.AppendKnownCategory(2);
            if (recent != unchecked((int)0x80070005)) Check(recent);
            Check(list.AddUserTasks((IObjectArray)tasks));
            Check(list.CommitList());
            begun = false;
        }
        finally
        {
            if (begun) list.AbortList();
            if (removed is not null) Marshal.FinalReleaseComObject(removed);
            foreach (var task in taskLinks) Marshal.FinalReleaseComObject(task);
            Marshal.FinalReleaseComObject(tasks);
            Marshal.FinalReleaseComObject(list);
        }
    }

    internal sealed record Entry(string Action, string Title, string Description);
    internal static readonly Entry[] Entries =
    [
        new("claudex", "Open Claudex", "Open Codex with Claudex customization. Switching from vanilla quits and restarts Codex normally."),
        new("vanilla", "Open vanilla Codex", "Open Codex without DevTools or Claudex startup scripts. Switching from Claudex restarts Codex."),
        new("restart", "Restart Claudex", "Quit the running Codex session normally and restart with Claudex customization. Cancelling quit aborts the restart."),
        new("quit", "Quit Claudex", "Quit the running Codex app normally in either mode, respecting Codex's confirmation.")
    ];

    internal static void ShowError(string message) => MessageBox(IntPtr.Zero, message, "Claudex — taskbar action failed", 0x10);
    private static object Create(string clsid) => Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid(clsid), throwOnError: true)!)!;
    private static void Check(int result) => Marshal.ThrowExceptionForHR(result);

    [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
    private static extern int MessageBox(IntPtr window, string text, string caption, uint type);

    [ComImport, Guid("6332DEBF-87B5-4670-90C0-5E57B408A49E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICustomDestinationList
    {
        [PreserveSig] int SetAppID([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int BeginList(out uint slots, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        [PreserveSig] int AppendCategory([MarshalAs(UnmanagedType.LPWStr)] string category, IObjectArray items);
        [PreserveSig] int AppendKnownCategory(uint category);
        [PreserveSig] int AddUserTasks(IObjectArray tasks);
        [PreserveSig] int CommitList();
        [PreserveSig] int GetRemovedDestinations(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object removed);
        [PreserveSig] int DeleteList([MarshalAs(UnmanagedType.LPWStr)] string appId);
        [PreserveSig] int AbortList();
    }

    [ComImport, Guid("92CA9DCD-5622-4BBA-A805-5E9F541BD8C9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectArray
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);
    }

    [ComImport, Guid("5632B1A4-E38A-400A-928A-D4CD63230295"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IObjectCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object item);
        [PreserveSig] int AddObject([MarshalAs(UnmanagedType.IUnknown)] object item);
        [PreserveSig] int AddFromArray(IObjectArray items);
        [PreserveSig] int RemoveObjectAt(uint index);
        [PreserveSig] int Clear();
    }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLink
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr idList);
        [PreserveSig] int SetIDList(IntPtr idList);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder text, int length);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int length);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int length);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out ushort hotkey);
        [PreserveSig] int SetHotkey(ushort hotkey);
        [PreserveSig] int GetShowCmd(out int command);
        [PreserveSig] int SetShowCmd(int command);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int length, out int index);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int index);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }
}
