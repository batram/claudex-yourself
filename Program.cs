using System.Diagnostics;
using System.Net.WebSockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace ClaudexYourself;

internal static class Program
{
    private const int DevToolsPort = 9229;
    private static readonly Uri DevToolsListUri = new($"http://127.0.0.1:{DevToolsPort}/json/list");
    internal static readonly string UserScriptDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "claudex-yourself", "scripts");
    internal static readonly string BundledScriptDirectory = Path.Combine(AppContext.BaseDirectory, "scripts");
    internal static readonly string StateDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "claudex-yourself");

    public static async Task<int> Main(string[] arguments)
    {
        try
        {
            var command = arguments.FirstOrDefault()?.ToLowerInvariant() ?? "help";
            return command switch
            {
                "launch" => Launch(),
                "package-debugger" => PackageDebugger(arguments.Skip(1).ToArray()),
                "reload" => await RunNamedScriptAsync("reload-dgspy", concise: true),
                "run" => arguments.Length < 2 ? Fail("run requires a script name or path.") : await RunNamedScriptAsync(arguments[1], concise: false),
                "run-all" => await RunAllAsync(),
                "dev" => await UserscriptDevelopment.RunAsync(arguments.Skip(1).ToArray()),
                "list" => ListScripts(),
                "mcp" => await ClaudexMcpServer.RunAsync(),
                "reload-worker" => await ReloadWorkerAsync(),
                "autoload-worker" => await AutoloadScripts.RunWorkerAsync(),
                "autoload" => arguments.Length == 3
                    ? SetAutoload(arguments[1], arguments[2])
                    : Fail("autoload requires: <script-name> on|off"),
                "configure" => arguments.ElementAtOrDefault(1)?.Equals("codex", StringComparison.OrdinalIgnoreCase) == true
                    ? ConfigureCodex()
                    : Fail("configure currently requires 'codex'."),
                "status" => await StatusAsync(),
                "install-shortcut" => InstallShortcut(),
                "self-test" => SelfTest(),
                "help" or "--help" or "-h" => Help(),
                _ => Fail($"Unknown command '{command}'.")
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"claudex-yourself: {exception.Message}");
            return 1;
        }
    }

    private static int Launch()
    {
        if (IsCodexRunning())
            return ActivateRunningCodex();
        if (OperatingSystem.IsWindows()) return LaunchWindows();
        if (OperatingSystem.IsMacOS()) return LaunchMacOS();
        return Fail("Controlled launch currently supports Windows and macOS.");
    }

    private static int LaunchWindows()
    {
        var package = FindCodexPackage();
        var profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Codex");
        var switches = ChromiumSwitches();
        var environment = BuildWindowsEnvironmentBlock(new[]
        {
            "BUILD_FLAVOR=dev",
            $"CODEX_ELECTRON_USER_DATA_PATH={profile}",
            $"CODEX_ELECTRON_CHROMIUM_SWITCHES={switches}"
        });

        var debugSettings = (IPackageDebugSettings)new PackageDebugSettings();
        var environmentPointer = Marshal.StringToHGlobalUni(environment);
        var launcher = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the current executable.");
        var debuggerCommandLine = $"\"{launcher}\" package-debugger";
        uint processId;
        var debuggingEnabled = false;
        Exception? launchFailure = null;
        try
        {
            ThrowForHResult(debugSettings.EnableDebugging(package.FullName, debuggerCommandLine, environmentPointer), "enable Codex package debugging");
            debuggingEnabled = true;
            var activationManager = (IApplicationActivationManager)new ApplicationActivationManager();
            ThrowForHResult(activationManager.ActivateApplication($"{package.FamilyName}!App", null, ActivateOptions.None, out processId), "activate Codex");
        }
        catch (Exception exception)
        {
            launchFailure = exception;
            throw;
        }
        finally
        {
            Marshal.FreeHGlobal(environmentPointer);
            if (debuggingEnabled)
            {
                var disableResult = debugSettings.DisableDebugging(package.FullName);
                if (launchFailure is null) ThrowForHResult(disableResult, "disable Codex package debugging");
            }
        }

        Console.WriteLine($"Started controlled Codex (PID {processId}).");
        StartAutoloadWorker();
        return 0;
    }

    private static int LaunchMacOS()
    {
        var executable = new[]
        {
            "/Applications/Codex.app/Contents/MacOS/Codex",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Applications/Codex.app/Contents/MacOS/Codex")
        }.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Codex.app was not found in /Applications or ~/Applications.");

        var start = new ProcessStartInfo(executable) { UseShellExecute = false };
        start.Environment["BUILD_FLAVOR"] = "dev";
        start.Environment["CODEX_ELECTRON_USER_DATA_PATH"] = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Codex");
        start.Environment["CODEX_ELECTRON_CHROMIUM_SWITCHES"] = ChromiumSwitches();
        var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start Codex.app.");
        Console.WriteLine($"Started controlled Codex (PID {process.Id}).");
        process.Dispose();
        StartAutoloadWorker();
        return 0;
    }

    private static string ChromiumSwitches() => JsonSerializer.Serialize(new Dictionary<string, string?>
    {
        ["remote-debugging-port"] = DevToolsPort.ToString(),
        ["remote-debugging-address"] = "127.0.0.1"
    });

    private static string BuildWindowsEnvironmentBlock(IEnumerable<string> entries) =>
        string.Join('\0', entries.Order(StringComparer.OrdinalIgnoreCase)) + "\0\0";

    private static int PackageDebugger(string[] arguments)
    {
        if (!OperatingSystem.IsWindows()) return Fail("package-debugger is Windows-only.");
        var threadId = ReadDebuggerId(arguments, "-tid");
        using var thread = OpenThread(ThreadAccess.SuspendResume, false, threadId);
        if (thread.IsInvalid) throw new InvalidOperationException($"Could not open suspended Codex thread {threadId} (Win32 error {Marshal.GetLastPInvokeError()}).");
        var previousSuspendCount = ResumeThread(thread);
        if (previousSuspendCount == uint.MaxValue)
            throw new InvalidOperationException($"Could not resume suspended Codex thread {threadId} (Win32 error {Marshal.GetLastPInvokeError()}).");
        return 0;
    }

    private static uint ReadDebuggerId(string[] arguments, string option)
    {
        for (var index = 0; index + 1 < arguments.Length; index++)
            if (arguments[index].Equals(option, StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(arguments[index + 1], out var value)) return value;
        throw new ArgumentException($"package-debugger requires {option} <id>.");
    }

    private static void ThrowForHResult(int result, string operation)
    {
        if (result >= 0) return;
        var detail = Marshal.GetExceptionForHR(result)?.Message ?? "Unknown Windows error.";
        throw new InvalidOperationException($"Could not {operation} (HRESULT 0x{result:X8}): {detail}");
    }

    private static async Task<int> StatusAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        var page = SelectCodexPage(await ReadTargetsAsync(client));
        if (page is null) return Fail("The DevTools endpoint is reachable, but no Codex page target is available.");
        Console.WriteLine("Controlled Codex is ready.");
        Console.WriteLine($"Renderer: {page.Title}");
        Console.WriteLine($"User scripts: {UserScriptDirectory}");
        return 0;
    }

    private static async Task<int> RunNamedScriptAsync(string nameOrPath, bool concise)
    {
        var path = ResolveScript(nameOrPath);
        var source = await File.ReadAllTextAsync(path);
        var result = await RunScriptAsync(source, Path.GetFileNameWithoutExtension(path));
        foreach (var line in result.Logs) Console.WriteLine(line);
        if (concise && result.Result.ValueKind == JsonValueKind.Object
            && result.Result.TryGetProperty("toolCount", out var toolCount)
            && result.Result.TryGetProperty("attempt", out var attempt))
        {
            Console.WriteLine($"Codex reloaded dgSpy in {attempt.GetInt32()} attempt(s); {toolCount.GetInt32()} tools ready.");
        }
        else if (result.Result.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            Console.WriteLine(result.Result.GetRawText());
        }
        return 0;
    }

    private static async Task<int> RunAllAsync()
    {
        Directory.CreateDirectory(UserScriptDirectory);
        var scripts = Directory.EnumerateFiles(UserScriptDirectory, "*.js").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        if (scripts.Length == 0) return Fail($"No user scripts found in {UserScriptDirectory}.");
        foreach (var script in scripts)
        {
            Console.WriteLine($"[{Path.GetFileName(script)}]");
            await RunNamedScriptAsync(script, concise: false);
        }
        return 0;
    }

    private static int ListScripts()
    {
        Directory.CreateDirectory(UserScriptDirectory);
        var codexVersion = UserscriptMetadata.CurrentCodexVersion();
        Console.WriteLine($"Codex: {codexVersion}");
        Console.WriteLine($"User scripts ({UserScriptDirectory}):");
        PrintScripts(UserScriptDirectory, showAutoload: true, codexVersion);
        Console.WriteLine($"Bundled scripts ({BundledScriptDirectory}):");
        PrintScripts(BundledScriptDirectory, showAutoload: false, codexVersion: null);
        return 0;
    }

    private static void PrintScripts(string directory, bool showAutoload, string? codexVersion)
    {
        var files = Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*.js").OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : [];
        if (files.Length == 0) Console.WriteLine("  (none)");
        else foreach (var file in files)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var suffix = showAutoload && AutoloadScripts.IsEnabled(name) ? " [autoload]" : string.Empty;
            if (codexVersion is not null)
            {
                try { suffix += $" [{UserscriptMetadata.Parse(File.ReadAllText(file)).Compatibility(codexVersion).Status}]"; }
                catch (FormatException) { suffix += " [metadata_invalid]"; }
            }
            Console.WriteLine($"  {name}{suffix}");
        }
    }

    internal static string ResolveScript(string nameOrPath)
    {
        var candidates = new List<string>();
        if (Path.IsPathFullyQualified(nameOrPath) || nameOrPath.Contains(Path.DirectorySeparatorChar) || nameOrPath.Contains(Path.AltDirectorySeparatorChar))
            candidates.Add(Path.GetFullPath(nameOrPath));
        else
        {
            var fileName = nameOrPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ? nameOrPath : nameOrPath + ".js";
            candidates.Add(Path.Combine(UserScriptDirectory, fileName));
            candidates.Add(Path.Combine(BundledScriptDirectory, fileName));
        }
        return candidates.FirstOrDefault(File.Exists)
            ?? throw new FileNotFoundException($"Script '{nameOrPath}' was not found. Run 'claudex-yourself list' to see available scripts.");
    }

    internal static async Task<ScriptResult> RunScriptAsync(string source, string scriptName)
    {
        var sourceLiteral = JsonSerializer.Serialize(source);
        var nameLiteral = JsonSerializer.Serialize(scriptName);
        var expression = $$"""
            (async () => {
              const bridge = window.electronBridge;
              if (!bridge || typeof bridge.sendMessageFromView !== 'function') throw new Error('Codex preload bridge is unavailable');
              const logs = [];
              const request = (method, params, timeoutMs = 30000) => new Promise((resolve, reject) => {
                const id = 'claudex-' + crypto.randomUUID();
                const timeout = setTimeout(() => {
                  window.removeEventListener('message', listener);
                  reject(new Error(method + ' timed out'));
                }, timeoutMs);
                const listener = event => {
                  const envelope = event.data;
                  if (envelope?.type !== 'mcp-response' || envelope?.message?.id !== id) return;
                  clearTimeout(timeout);
                  window.removeEventListener('message', listener);
                  if (envelope.message.error) reject(new Error(envelope.message.error.message || JSON.stringify(envelope.message.error)));
                  else resolve(envelope.message.result);
                };
                window.addEventListener('message', listener);
                bridge.sendMessageFromView({
                  type: 'mcp-request', hostId: 'local', request: { id, method, ...(params === undefined ? {} : { params }) },
                  priority: 'interactive', source: 'claudex-yourself', timeoutMs, expiresAtMs: Date.now() + timeoutMs
                }).catch(error => {
                  clearTimeout(timeout);
                  window.removeEventListener('message', listener);
                  reject(error);
                });
              });
              const format = value => typeof value === 'string' ? value : JSON.stringify(value);
              const claudex = Object.freeze({
                request,
                sleep: milliseconds => new Promise(resolve => setTimeout(resolve, milliseconds)),
                log: (...values) => logs.push(values.map(format).join(' ')),
                scriptName: {{nameLiteral}}
              });
              const execute = new Function('claudex', '"use strict"; return (async () => {\n' + {{sourceLiteral}} + '\n})();');
              const result = await execute(claudex);
              return JSON.stringify({ logs, result: result === undefined ? null : result });
            })()
            """;

        var value = await RendererDevTools.EvaluateStringAsync(expression, TimeSpan.FromSeconds(120));
        using var document = JsonDocument.Parse(value);
        var logs = document.RootElement.GetProperty("logs").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        return new ScriptResult(logs, document.RootElement.GetProperty("result").Clone());
    }

    private static int InstallShortcut()
    {
        if (!OperatingSystem.IsWindows()) return Fail("install-shortcut is currently Windows-only; macOS can launch the published executable directly.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the current executable.");
        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Codex (controlled).lnk");
        var codexExecutable = FindCodexExecutable();
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows shortcut support is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = executable;
        shortcut.Arguments = "launch";
        shortcut.WorkingDirectory = Path.GetDirectoryName(executable)!;
        shortcut.IconLocation = $"{codexExecutable},0";
        shortcut.Description = "Launch Codex with the local development control endpoint";
        shortcut.Save();
        Console.WriteLine($"Created {shortcutPath}");
        return 0;
    }

    private static int ConfigureCodex()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the current executable.");
        var codex = ResolveCommand("codex") ?? throw new InvalidOperationException("The Codex CLI was not found on PATH.");
        using var process = Process.Start(new ProcessStartInfo(codex)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            ArgumentList = { "mcp", "add", "claudex-yourself", "--", executable, "mcp" }
        }) ?? throw new InvalidOperationException("Could not start the Codex CLI.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new InvalidOperationException($"Codex MCP configuration failed: {error}".Trim());
        Console.Write(output);
        Console.WriteLine("Configured claudex-yourself for Codex. Run 'claudex-yourself reload' once to load it without restarting Codex.");
        return 0;
    }

    internal static void StartReloadWorker()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the current executable.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add("reload-worker");
        Process.Start(start)?.Dispose();
    }

    private static void StartAutoloadWorker()
    {
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Cannot resolve the current executable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add("autoload-worker");
        Process.Start(start)?.Dispose();
    }

    private static int SetAutoload(string name, string value)
    {
        var enabled = value.ToLowerInvariant() switch
        {
            "on" or "true" or "enable" or "enabled" => true,
            "off" or "false" or "disable" or "disabled" => false,
            _ => throw new ArgumentException("autoload state must be on or off.")
        };
        var result = AutoloadScripts.Set(name, enabled);
        Console.WriteLine($"Autoload {(result.Enabled ? "enabled" : "disabled")} for {result.Name}.");
        return 0;
    }

    private static async Task<int> ReloadWorkerAsync()
    {
        Directory.CreateDirectory(StateDirectory);
        var statusPath = Path.Combine(StateDirectory, "reload-status.json");
        await Task.Delay(750);
        object status;
        try
        {
            var source = await File.ReadAllTextAsync(ResolveScript("reload-mcp"));
            var result = await RunScriptAsync(source, "reload-mcp");
            status = new { state = "completed", completed_at_utc = DateTime.UtcNow, logs = result.Logs, result = result.Result };
        }
        catch (Exception exception)
        {
            status = new { state = "failed", completed_at_utc = DateTime.UtcNow, error = exception.Message };
        }
        await File.WriteAllTextAsync(statusPath, JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static string? ResolveCommand(string name)
    {
        var extensions = OperatingSystem.IsWindows() ? new[] { ".exe", ".cmd", ".bat", "" } : new[] { "" };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            foreach (var extension in extensions)
            {
                var candidate = Path.Combine(directory.Trim(), name + extension);
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }

    private static int SelfTest()
    {
        Directory.CreateDirectory(UserScriptDirectory);
        if (DevToolsListUri.Port != DevToolsPort) throw new InvalidOperationException("DevTools endpoint configuration failed.");
        var environment = BuildWindowsEnvironmentBlock(["Z=value", "a=value"]);
        if (environment != "a=value\0Z=value\0\0") throw new InvalidOperationException("Windows environment block construction failed.");
        const string metadataSmoke = "// ==ClaudexUserScript==\n// @name Test\n// @id test\n// @version 1.0.0\n// @description Test script.\n// @run-at renderer-ready\n// @platform windows, macos\n// @codex-tested 1.2.3\n// @grant codex-request\n// ==/ClaudexUserScript==\nreturn true;";
        var parsed = UserscriptMetadata.Parse(metadataSmoke);
        if (parsed.Id != "test" || !parsed.TestedCodexVersions.Contains("1.2.3")) throw new InvalidOperationException("Userscript metadata parsing failed.");
        if (!UserscriptMetadata.AddTestedVersion(metadataSmoke, "2.0.0").Contains("@codex-tested  2.0.0")) throw new InvalidOperationException("Userscript metadata update failed.");
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS()) Console.WriteLine("Warning: controlled launch is unsupported on this OS.");
        Console.WriteLine("Self-test passed.");
        return 0;
    }

    private static async Task<IReadOnlyList<DevToolsTarget>> ReadTargetsAsync(HttpClient client)
    {
        try
        {
            await using var stream = await client.GetStreamAsync(DevToolsListUri);
            return await JsonSerializer.DeserializeAsync<List<DevToolsTarget>>(stream) ?? [];
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException("The Codex DevTools endpoint is not running. Launch Codex using claudex-yourself.", exception);
        }
    }

    private static DevToolsTarget? SelectCodexPage(IEnumerable<DevToolsTarget> targets) => targets.FirstOrDefault(target =>
        string.Equals(target.Type, "page", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(target.WebSocketDebuggerUrl)
        && !target.Url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase));

    private static async Task<string> ReceiveMessageAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close) throw new InvalidOperationException("Codex closed the DevTools connection.");
            output.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(output.ToArray());
        }
    }

    private static bool IsCodexRunning() => new[] { "ChatGPT", "Codex" }.SelectMany(Process.GetProcessesByName).Any(process =>
    {
        try { return process.MainModule?.FileName?.Contains("Codex", StringComparison.OrdinalIgnoreCase) == true; }
        catch { return true; }
        finally { process.Dispose(); }
    });

    private static int ActivateRunningCodex()
    {
        if (!OperatingSystem.IsWindows())
            return Fail("Codex is already running, but foreground activation is currently Windows-only.");

        var package = FindCodexPackage();
        var activationManager = (IApplicationActivationManager)new ApplicationActivationManager();
        ThrowForHResult(
            activationManager.ActivateApplication($"{package.FamilyName}!App", null, ActivateOptions.None, out _),
            "activate the running Codex application");
        Console.WriteLine("Activated the running Codex application.");
        return 0;
    }

    private static string FindCodexExecutable()
    {
        var package = FindCodexPackage();
        var executable = Path.Combine(package.InstallLocation, "app", "ChatGPT.exe");
        return File.Exists(executable) ? executable : throw new FileNotFoundException("The Codex desktop executable was not found.", executable);
    }

    private static CodexPackage FindCodexPackage()
    {
        using var powershell = Process.Start(new ProcessStartInfo("powershell.exe",
            "-NoProfile -NonInteractive -Command \"$p = Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1; if ($p) { Write-Output ($p.PackageFullName + '|' + $p.PackageFamilyName + '|' + $p.InstallLocation) }\"")
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not query the installed Codex package.");
        var output = powershell.StandardOutput.ReadToEnd().Trim();
        var error = powershell.StandardError.ReadToEnd().Trim();
        powershell.WaitForExit();
        if (powershell.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) throw new InvalidOperationException($"Could not locate the installed Codex package. {error}".Trim());
        var parts = output.Split('|', 3);
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException($"Unexpected Codex package information: {output}");
        return new CodexPackage(parts[0], parts[1], parts[2]);
    }

    private static int Help()
    {
        Console.WriteLine("claudex-yourself launch");
        Console.WriteLine("claudex-yourself status");
        Console.WriteLine("claudex-yourself list");
        Console.WriteLine("claudex-yourself run <name-or-path>");
        Console.WriteLine("claudex-yourself run-all");
        Console.WriteLine("claudex-yourself dev <userscript-path> [--autoload]");
        Console.WriteLine("claudex-yourself reload");
        Console.WriteLine("claudex-yourself autoload <script-name> on|off");
        Console.WriteLine("claudex-yourself configure codex");
        Console.WriteLine("claudex-yourself mcp");
        Console.WriteLine("claudex-yourself install-shortcut");
        return 0;
    }

    private static int Fail(string message) { Console.Error.WriteLine($"claudex-yourself: {message}"); return 1; }

    internal sealed record ScriptResult(IReadOnlyList<string> Logs, JsonElement Result);
    private sealed record DevToolsTarget(
        [property: System.Text.Json.Serialization.JsonPropertyName("type")] string Type,
        [property: System.Text.Json.Serialization.JsonPropertyName("title")] string Title,
        [property: System.Text.Json.Serialization.JsonPropertyName("url")] string Url,
        [property: System.Text.Json.Serialization.JsonPropertyName("webSocketDebuggerUrl")] string WebSocketDebuggerUrl);
    private sealed record CodexPackage(string FullName, string FamilyName, string InstallLocation);

    [Flags] private enum ActivateOptions : uint { None = 0 }
    [Flags] private enum ThreadAccess : uint { SuspendResume = 0x0002 }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeThreadHandle OpenThread(ThreadAccess desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(SafeThreadHandle thread);

    private sealed class SafeThreadHandle : Microsoft.Win32.SafeHandles.SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeThreadHandle() : base(true) { }
        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
    [ComImport, Guid("F27C3930-8029-4AD1-94E3-3DBA417810C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPackageDebugSettings
    {
        [PreserveSig] int EnableDebugging([MarshalAs(UnmanagedType.LPWStr)] string packageFullName, [MarshalAs(UnmanagedType.LPWStr)] string? debuggerCommandLine, IntPtr environment);
        [PreserveSig] int DisableDebugging([MarshalAs(UnmanagedType.LPWStr)] string packageFullName);
    }
    [ComImport, Guid("B1AEC16F-2383-4852-B0E9-8F0B1DC66B4D")] private class PackageDebugSettings;
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, ActivateOptions options, out uint processId);
    }
    [ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] private class ApplicationActivationManager;
}
