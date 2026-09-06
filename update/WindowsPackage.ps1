param(
    [Parameter(Mandatory=$true)][ValidateSet('inspect','stage','install','register-staged','detach')][string]$Action,
    [string]$PackagePath,
    [string]$Launcher,
    [switch]$FinishTargetShutdown
)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
try {
    switch ($Action) {
        'inspect' {
            $package = Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1
            if (-not $package) { throw 'OpenAI.Codex is not installed for this Windows user.' }
            [ordered]@{ fullName=$package.PackageFullName; familyName=$package.PackageFamilyName; version=$package.Version.ToString(); publisher=$package.Publisher; architecture=$package.Architecture.ToString().ToLowerInvariant(); status=$package.Status.ToString(); installLocation=$package.InstallLocation } | ConvertTo-Json -Compress
        }
        'stage' {
            # Windows validates the MSIX signature; staging does not close or replace the running app.
            Add-AppxPackage -Stage -Path $PackagePath -ErrorAction Stop
            '{"staged":true}'
        }
        'install' {
            # Only a package-in-use retry after the controller's graceful quit/exit check opts in.
            # Windows tracks package activity beyond processes with GetPackageFamilyName identity.
            Add-AppxPackage -Path $PackagePath -ForceTargetApplicationShutdown:$FinishTargetShutdown -ErrorAction Stop
            '{"installed":true}'
        }
        'register-staged' {
            # This is the exact manifest already staged by Windows in its protected package directory.
            Add-AppxPackage -Register -DisableDevelopmentMode -Path $PackagePath -ForceTargetApplicationShutdown:$FinishTargetShutdown -ErrorAction Stop
            '{"installed":true}'
        }
        'detach' {
            if (-not [IO.Path]::IsPathRooted($Launcher) -or $Launcher.Contains('"') -or -not (Test-Path -LiteralPath $Launcher -PathType Leaf)) { throw 'Invalid updater executable.' }
            # WMI creates this process outside Codex's package/job lifetime. It must survive app quit.
            $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{ ShowWindow=[uint16]0 }
            $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine=('"' + $Launcher + '" update-worker'); ProcessStartupInformation=$startup }
            if ($created.ReturnValue -ne 0) { throw "Could not detach updater (Win32_Process.Create returned $($created.ReturnValue))." }
            @{ processId=$created.ProcessId } | ConvertTo-Json -Compress
        }
    }
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
