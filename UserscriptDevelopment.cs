using System.Text.Json;

namespace ClaudexYourself;

internal static class UserscriptDevelopment
{
    public static async Task<int> RunAsync(string[] arguments)
    {
        var pathArgument = arguments.FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        if (pathArgument is null) throw new ArgumentException("dev requires a userscript path.");
        var sourcePath = Path.GetFullPath(pathArgument);
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Userscript source was not found.", sourcePath);
        var enableAutoload = arguments.Contains("--autoload", StringComparer.OrdinalIgnoreCase);

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        try
        {
            await PublishAndRunAsync(sourcePath, enableAutoload);
            Console.WriteLine($"Watching {sourcePath}");
            Console.WriteLine("Press Ctrl+C to stop.");

            using var watcher = new FileSystemWatcher(Path.GetDirectoryName(sourcePath)!, Path.GetFileName(sourcePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            using var changed = new SemaphoreSlim(0, 1);
            void Signal(object? _, FileSystemEventArgs __) { if (changed.CurrentCount == 0) changed.Release(); }
            watcher.Changed += Signal;
            watcher.Created += Signal;
            watcher.Renamed += (_, __) => { if (changed.CurrentCount == 0) changed.Release(); };

            while (!cancellation.IsCancellationRequested)
            {
                await changed.WaitAsync(cancellation.Token);
                await Task.Delay(150, cancellation.Token);
                while (changed.CurrentCount > 0) await changed.WaitAsync(cancellation.Token);
                try { await PublishAndRunAsync(sourcePath, enableAutoload); }
                catch (Exception exception) { Console.Error.WriteLine($"[{DateTime.Now:T}] reload failed: {exception.Message}"); }
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        finally { Console.CancelKeyPress -= cancel; }
        return 0;
    }

    internal static async Task<object> PublishAndRunAsync(string sourcePath, bool enableAutoload)
    {
        var source = await ReadStableAsync(sourcePath);
        var metadata = UserscriptMetadata.Parse(source);
        var sourceName = Path.GetFileNameWithoutExtension(sourcePath);
        if (!metadata.Id.Equals(sourceName, StringComparison.OrdinalIgnoreCase))
            throw new FormatException($"Userscript @id '{metadata.Id}' must match filename '{sourceName}'.");
        var compatibility = metadata.Compatibility(UserscriptMetadata.CurrentCodexVersion());
        if (!compatibility.PlatformSupported) throw new PlatformNotSupportedException($"Userscript '{metadata.Id}' does not support this platform.");

        Directory.CreateDirectory(Program.UserScriptDirectory);
        var installedPath = Path.Combine(Program.UserScriptDirectory, metadata.Id + ".js");
        var sameFile = Path.GetFullPath(installedPath).Equals(sourcePath, StringComparison.OrdinalIgnoreCase);
        var previous = !sameFile && File.Exists(installedPath) ? await File.ReadAllTextAsync(installedPath) : null;
        if (!sameFile) await WriteAtomicAsync(installedPath, source);
        try
        {
            var result = await Program.RunScriptAsync(source, metadata.Id);
            if (enableAutoload) AutoloadScripts.Set(metadata.Id, true);
            foreach (var log in result.Logs) Console.WriteLine(log);
            Console.WriteLine($"[{DateTime.Now:T}] reloaded {metadata.Id}: {result.Result.GetRawText()}");
            return new { published = !sameFile, metadata.Id, autoload = AutoloadScripts.IsEnabled(metadata.Id), compatibility, logs = result.Logs, result = result.Result };
        }
        catch
        {
            if (!sameFile)
            {
                if (previous is null) File.Delete(installedPath);
                else await WriteAtomicAsync(installedPath, previous);
            }
            throw;
        }
    }

    private static async Task<string> ReadStableAsync(string path)
    {
        IOException? lastError = null;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try { return await File.ReadAllTextAsync(path); }
            catch (IOException exception) { lastError = exception; await Task.Delay(50); }
        }
        throw lastError ?? new IOException("Could not read userscript source.");
    }

    private static async Task WriteAtomicAsync(string path, string content)
    {
        var temporary = path + $".{Environment.ProcessId}.tmp";
        await File.WriteAllTextAsync(temporary, content);
        File.Move(temporary, path, overwrite: true);
    }
}
