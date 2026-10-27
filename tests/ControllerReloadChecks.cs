namespace ClaudexYourself;

internal static class ControllerReloadChecks
{
    internal static void Run()
    {
        static void Assert(bool value, string message) { if (!value) throw new Exception("Reload check failed: " + message); }
        var current = ControllerReload.Current;
        var old = current with { Pid = int.MaxValue, Started = 1, BuildId = "old" };
        var replacement = current with { Pid = int.MaxValue - 1, Started = 2 };
        Assert(ControllerReload.DiskBuildId(typeof(Program).Assembly.Location) == ControllerReload.BuildId, "disk identity matches loaded module");
        Assert(ControllerReload.IsAlive(current), "current process identity");
        Assert(!ControllerReload.IsAlive(current with { Started = 1 }), "PID reuse is rejected");
        if (OperatingSystem.IsWindows())
            Assert(!ControllerReload.IsAlive(current with { Pid = 4, Started = 1 }), "a protected system process reusing a stale PID cannot block reload");
        Assert(!ControllerReload.Verified([current], [replacement], current.Executable, current.BuildId), "live old process prevents success");
        Assert(!ControllerReload.Verified([old], [], current.Executable, current.BuildId), "missing replacement prevents success");
        Assert(!ControllerReload.Verified([old], [replacement with { BuildId = "old" }], current.Executable, current.BuildId), "old build prevents success even at same version");
        Assert(!ControllerReload.Verified([old], [replacement with { Executable = "elsewhere" }], current.Executable, current.BuildId), "wrong installation prevents success");
        Assert(!ControllerReload.Verified([old, old with { Pid = int.MaxValue - 2 }], [replacement], current.Executable, current.BuildId), "every old connection needs replacement");
        Assert(ControllerReload.Verified([old], [replacement], current.Executable, current.BuildId), "matching new build accepted");
        Console.WriteLine("Controller reload identity checks passed.");
    }
}
