using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ClaudexYourself;

[SupportedOSPlatform("windows")]
internal static class WindowsShortcut
{
    // Set identity on the link itself: the launcher and packaged app have different executables.
    internal static void SetAppId(string path, string appId)
    {
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreFromParsingName(path, IntPtr.Zero, 2, ref iid, out var store));
        try
        {
            SetStringProperty(store, new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5, appId);
        }
        finally
        {
            Marshal.FinalReleaseComObject(store);
        }
    }

    internal static void SetWindowIdentity(IntPtr window, string appId, string command, string displayName, string icon)
    {
        var iid = typeof(IPropertyStore).GUID;
        Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(window, ref iid, out var store));
        try
        {
            var format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
            // Relaunch properties must precede the ID so Shell sees a complete identity.
            SetStringProperty(store, format, 2, command, commit: false);
            SetStringProperty(store, format, 4, displayName, commit: false);
            SetStringProperty(store, format, 3, icon, commit: false);
            SetStringProperty(store, format, 5, appId, commit: false);
            var key = new PropertyKey { Format = format, Id = 5 };
            Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
            try
            {
                if (value.Type != 31 || Marshal.PtrToStringUni(value.Pointer) != appId)
                    throw new InvalidOperationException("Windows did not accept the controlled window's taskbar identity.");
            }
            finally { PropVariantClear(ref value); }
        }
        finally { Marshal.FinalReleaseComObject(store); }
    }

    internal static void SetStringProperty(object target, Guid format, uint id, string text, bool commit = true)
    {
        var store = (IPropertyStore)target;
        var key = new PropertyKey { Format = format, Id = id };
        var value = new PropVariant { Type = 31, Pointer = Marshal.StringToCoTaskMemUni(text) }; // VT_LPWSTR
        try
        {
            Marshal.ThrowExceptionForHR(store.SetValue(ref key, ref value));
            if (commit) Marshal.ThrowExceptionForHR(store.Commit());
        }
        finally { Marshal.FreeCoTaskMem(value.Pointer); }
    }

    internal static string? GetStringProperty(object target, Guid format, uint id)
    {
        var store = (IPropertyStore)target;
        var key = new PropertyKey { Format = format, Id = id };
        Marshal.ThrowExceptionForHR(store.GetValue(ref key, out var value));
        try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
        finally { PropVariantClear(ref value); }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreForWindow(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr bindContext, uint flags,
        ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey { public Guid Format; public uint Id; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1, Reserved2, Reserved3;
        public IntPtr Pointer;
        public IntPtr Padding;
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
}
