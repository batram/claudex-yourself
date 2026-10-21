using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using System.Text;

namespace ClaudexYourself;

internal static class ControllerReload
{
    internal sealed record Instance(int Pid, long Started, string Executable, string BuildId, string AssemblyPath);
    internal static string BuildId => typeof(Program).Module.ModuleVersionId.ToString();
    private static string Registry => Path.Combine(Program.StateDirectory, "mcp-processes");
    private static string StatusPath => Path.Combine(Program.StateDirectory, "reload-status.json");
    internal static void StartWorker()
    {
        if (!OperatingSystem.IsWindows()) { BackgroundWorker.Start("reload-worker").Dispose(); return; }
        var executable = Environment.ProcessPath!;
        var command = new StringBuilder("\"" + executable + "\" ");
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            command.Append('"').Append(typeof(Program).Assembly.Location).Append("\" ");
        command.Append("reload-worker");
        // Codex's Windows job forbids breakaway and kills descendants on transport
        // shutdown. WMI creates the hidden worker outside that job and its pipes.
        Directory.CreateDirectory(Program.StateDirectory);
        var script = Path.Combine(Program.StateDirectory, "start-reload-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(script, "$ErrorActionPreference = 'Stop'\n" +
            "$startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow = [uint16]0 }\n" +
            "$result = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = '" + command.ToString().Replace("'", "''") + "'; ProcessStartupInformation = $startup }\n" +
            "if ($result.ReturnValue -ne 0) { throw ('Detached reload launch failed: ' + $result.ReturnValue) }\n");
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-File", script }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start reload launcher");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) { process.Kill(); throw new TimeoutException("Detached reload launch timed out"); }
            if (process.ExitCode != 0) throw new InvalidOperationException(error.GetAwaiter().GetResult());
            output.GetAwaiter().GetResult();
        }
        finally { File.Delete(script); }
    }
    internal static Instance Current => new(Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
        Environment.ProcessPath!, BuildId, typeof(Program).Assembly.Location);

    internal static void Register()
    {
        Directory.CreateDirectory(Registry);
        var path = Path.Combine(Registry, Environment.ProcessId + ".json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(Current));
        File.Move(path + ".tmp", path, true);
    }

    internal static bool IsAlive(Instance instance)
    {
        try
        {
            using var process = Process.GetProcessById(instance.Pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().Ticks == instance.Started &&
                string.Equals(process.MainModule?.FileName, instance.Executable, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch (ArgumentException) { return false; }
        catch (InvalidOperationException) { return false; }
    }

    internal static Instance[] Instances()
    {
        if (!Directory.Exists(Registry)) return [];
        return Directory.GetFiles(Registry, "*.json").Select(path => JsonSerializer.Deserialize<Instance>(File.ReadAllText(path))!)
            .Where(IsAlive).ToArray();
    }

    internal static string DiskBuildId(string assembly)
    {
        using var stream = File.OpenRead(assembly);
        using var pe = new PEReader(stream);
        var metadata = pe.GetMetadataReader();
        return metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString();
    }

    internal static bool Verified(Instance[] before, Instance[] after, string executable, string expected) =>
        before.Length > 0 && before.All(old => !IsAlive(old)) && after.Length >= before.Length &&
        after.All(item => item.BuildId == expected && item.Executable == executable &&
            !before.Any(old => old.Pid == item.Pid && old.Started == item.Started));

    private static void Status(object value)
    {
        File.WriteAllText(StatusPath + ".tmp", JsonSerializer.Serialize(value));
        File.Move(StatusPath + ".tmp", StatusPath, true);
    }

    internal static async Task<int> RunAsync()
    {
        Directory.CreateDirectory(Program.StateDirectory);
        using var lease = new FileStream(Path.Combine(Program.StateDirectory, "reload.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        var operationId = Guid.NewGuid().ToString("N");
        try
        {
            var executable = Environment.ProcessPath!;
            var expected = DiskBuildId(typeof(Program).Assembly.Location);
            if (expected != BuildId) throw new InvalidOperationException("Reload worker does not match the build on disk. Retry after publication finishes.");
            var before = Instances().Where(item => item.Executable == executable && item.AssemblyPath == typeof(Program).Assembly.Location).ToArray();
            if (before.Length == 0) throw new InvalidOperationException("No registered MCP processes for this installation. Restart legacy MCP processes once before using verified reload.");
            Status(new { state = "restarting", operationId, expectedBuildId = expected, before });
            // Allow the scheduling tool response to reach its caller before closing stdio.
            await Task.Delay(1500);
            foreach (var instance in before)
            {
                if (!IsAlive(instance)) continue;
                using var process = Process.GetProcessById(instance.Pid);
                process.Kill();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            // Always use the bundled reload protocol, never a per-user script override.
            var source = await File.ReadAllTextAsync(Path.Combine(Program.BundledScriptDirectory, "reload-mcp.js"));
            var result = await Program.RunScriptAsync(source, "reload-mcp");
            var deadline = DateTime.UtcNow.AddSeconds(30);
            Instance[] after;
            do
            {
                after = Instances().Where(item => item.Executable == executable && item.AssemblyPath == typeof(Program).Assembly.Location).ToArray();
                if (Verified(before, after, executable, expected))
                {
                    if (DiskBuildId(typeof(Program).Assembly.Location) != expected)
                        throw new InvalidOperationException("Published build changed during reload. Retry after publication finishes.");
                    Status(new { state = "completed", operationId, expectedBuildId = expected, before, after, result = result.Result });
                    return 0;
                }
                await Task.Delay(250);
            } while (DateTime.UtcNow < deadline);
            throw new InvalidOperationException("MCP reload did not replace every controller with the expected build within 30 seconds.");
        }
        catch (Exception error)
        {
            Status(new { state = "failed", operationId, error = error.Message });
            return 1;
        }
    }
}
