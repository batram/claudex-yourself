param(
    [Parameter(Mandatory=$true)][ValidateSet('inspect','stage','install','register-staged','detach')][string]$Action,
    [string]$PackagePath,
    [string]$Launcher,
    [switch]$FinishTargetShutdown,
    [string]$ExpectedUserSid,
    [string]$FailurePath
)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
try {
    if ($ExpectedUserSid -and [Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne $ExpectedUserSid) {
        throw 'Approve the update using the same Windows account. Installing for a different administrator account is not supported.'
    }
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
    # Retry only the packaged-service elevation requirement, never generic deployment failures.
    # The normal controller retains ownership of quit, status, and unelevated relaunch.
    if (-not $ExpectedUserSid -and $Action -in @('stage', 'install', 'register-staged') -and
        ($_.Exception.Message -match '0x80073D28' -or $_.Exception.HResult -eq -2147009240)) {
        $resultPath = [IO.Path]::GetTempFileName()
        try {
            $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
            $helperLiteral = $PSCommandPath.Replace("'", "''")
            $packageLiteral = $PackagePath.Replace("'", "''")
            $resultLiteral = $resultPath.Replace("'", "''")
            $shutdownArgument = if ($FinishTargetShutdown) { ' -FinishTargetShutdown' } else { '' }
            $command = "& '$helperLiteral' -Action '$Action' -PackagePath '$packageLiteral' -ExpectedUserSid '$sid' -FailurePath '$resultLiteral.error'$shutdownArgument | Out-File -LiteralPath '$resultLiteral' -Encoding utf8; exit `$LASTEXITCODE"
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
            $process = Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -Verb RunAs -WindowStyle Hidden -Wait -PassThru -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encoded)
            $result = Get-Content -LiteralPath $resultPath -Raw
            if ($process.ExitCode -ne 0) {
                if (Test-Path -LiteralPath ($resultPath + '.error')) { $result = Get-Content -LiteralPath ($resultPath + '.error') -Raw }
                throw "Administrator package deployment failed: $result"
            }
            if ([string]::IsNullOrWhiteSpace($result)) { throw 'Administrator package deployment returned no result.' }
            $result
            exit 0
        } catch {
            if ($_.Exception.NativeErrorCode -eq 1223 -or $_.Exception.InnerException.NativeErrorCode -eq 1223) {
                [Console]::Error.WriteLine('Administrator approval was cancelled. The update was not completed.')
            } else {
                [Console]::Error.WriteLine($_.Exception.Message)
            }
            exit 1
        } finally {
            Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath ($resultPath + '.error') -Force -ErrorAction SilentlyContinue
        }
    }
    if ($FailurePath) { [IO.File]::WriteAllText($FailurePath, $_.Exception.Message) }
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
