using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ClaudexYourself;

[SupportedOSPlatform("windows")]
internal static class WindowsCodexDiscovery
{
    internal const string Family = "OpenAI.Codex_2p2nqsd0c76g0";

    internal static Program.CodexPackage FindPackage()
    {
        // Query current-user registration, not staged packages or directory guesses.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            uint count = 0, length = 0;
            var result = GetPackagesByPackageFamily(Family, ref count, null, ref length, IntPtr.Zero);
            if (result == 0 && count == 0) throw new InvalidOperationException("Codex is not registered for the current Windows user.");
            if (result != 122) throw new Win32Exception(result, "Could not query registered Codex packages.");
            var names = new IntPtr[checked((int)count)];
            var buffer = Marshal.AllocHGlobal(checked((int)length * 2));
            try
            {
                result = GetPackagesByPackageFamily(Family, ref count, names, ref length, buffer);
                if (result == 122) continue; // Registration changed during the query.
                if (result != 0) throw new Win32Exception(result);
                var fullName = SelectPackage(names.Take((int)count).Select(pointer => Marshal.PtrToStringUni(pointer)!));
                uint pathLength = 0;
                result = GetPackagePathByFullName(fullName, ref pathLength, null);
                if (result != 122) throw new Win32Exception(result, "Could not resolve the registered Codex package path.");
                var path = new StringBuilder(checked((int)pathLength));
                result = GetPackagePathByFullName(fullName, ref pathLength, path);
                if (result != 0) throw new Win32Exception(result);
                return new(fullName, Family, path.ToString());
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("Codex registration kept changing. Retry after the Windows update finishes.");
    }

    internal static string SelectPackage(IEnumerable<string> names) => names
        .Select(name => (Name: name, Parts: name.Split('_')))
        .Where(item => item.Parts.Length == 5 && item.Parts[0] == "OpenAI.Codex"
            && item.Parts[3] == "" && item.Parts[4] == "2p2nqsd0c76g0" && Version.TryParse(item.Parts[1], out _))
        .OrderByDescending(item => Version.Parse(item.Parts[1]))
        .Select(item => item.Name).FirstOrDefault()
        ?? throw new InvalidOperationException("No registered Codex application package was found.");

    internal static CodexLaunchMode ReadLaunchMode()
    {
        // Use WMI directly; spawning PowerShell costs more than the query itself.
        object? locator = null, services = null, rows = null;
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")!);
            services = ((dynamic)locator!).ConnectServer(".", "root\\cimv2");
            rows = ((dynamic)services).ExecQuery("SELECT ExecutablePath, CommandLine FROM Win32_Process WHERE Name = 'ChatGPT.exe'");
            var mode = CodexLaunchMode.Closed;
            foreach (var row in (System.Collections.IEnumerable)rows)
            {
                try
                {
                    var path = ReadProperty(row, "ExecutablePath");
                    if (path is null || !path.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase)
                        || !path.EndsWith("\\app\\ChatGPT.exe", StringComparison.OrdinalIgnoreCase)) continue;
                    var command = ReadProperty(row, "CommandLine");
                    if (string.IsNullOrWhiteSpace(command)) throw new InvalidOperationException("Could not read the Codex process command line.");
                    if (command.Contains("--type=", StringComparison.Ordinal)) continue;
                    if (VanillaLaunch.HasDebuggingArguments(command)) return CodexLaunchMode.Claudex;
                    mode = CodexLaunchMode.Vanilla;
                }
                finally { Marshal.FinalReleaseComObject(row); }
            }
            return mode;
        }
        finally
        {
            if (rows is not null) Marshal.FinalReleaseComObject(rows);
            if (services is not null) Marshal.FinalReleaseComObject(services);
            if (locator is not null) Marshal.FinalReleaseComObject(locator);
        }
    }

    private static string? ReadProperty(object row, string name)
    {
        // WMI's per-object dynamic DISPIDs are not stable across queries.
        // Use the fixed property-collection interface instead of caching them
        // through C#'s dynamic binder (which can produce E_FAIL on a second query).
        object properties = ((dynamic)row).Properties_;
        object? property = null;
        try
        {
            property = ((dynamic)properties).Item(name, 0);
            return (string?)((dynamic)property).Value;
        }
        finally
        {
            if (property is not null) Marshal.FinalReleaseComObject(property);
            Marshal.FinalReleaseComObject(properties);
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagesByPackageFamily(string family, ref uint count,
        [Out] IntPtr[]? names, ref uint bufferLength, IntPtr buffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetPackagePathByFullName(string fullName, ref uint pathLength, StringBuilder? path);
}
