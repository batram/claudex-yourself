param(
    [ValidateSet('check','download')][string]$Action = 'check',
    [switch]$InPackage,
    [string]$ResultPath
)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

# StoreContext operates on the calling package, not an arbitrary product ID.
# Keep this helper short-lived: it must exit before the controller checks for quit.
if (-not $InPackage) {
    $resultFile = Join-Path ([IO.Path]::GetTempPath()) ('claudex-store-' + [guid]::NewGuid().ToString('N') + '.json')
    try {
        $package = Get-AppxPackage -Name OpenAI.Codex | Sort-Object Version -Descending | Select-Object -First 1
        if (-not $package -or $package.PackageFamilyName -ne 'OpenAI.Codex_2p2nqsd0c76g0') { throw 'Stable Codex package not found.' }
        $manifest = Get-AppxPackageManifest $package
        $appId = @($manifest.Package.Applications.Application | Where-Object { $_.Executable.Replace('/', '\') -eq 'app\ChatGPT.exe' })[0].Id
        if (-not $appId) { throw 'Codex desktop application identity not found.' }
        $guiHost = Join-Path $env:SystemRoot 'System32\wscript.exe'
        if (-not (Test-Path -LiteralPath $guiHost)) { throw 'Windows Script Host is unavailable; the Store helper cannot run without flashing a console.' }
        $hostScript = Join-Path $PSScriptRoot 'WindowsStoreHost.vbs'
        $childArguments = '//B //NoLogo "' + $hostScript + '" "' + (Join-Path $PSHOME 'powershell.exe') + '" "' + $PSCommandPath + '" ' + $Action + ' "' + $resultFile + '"'
        Invoke-CommandInDesktopPackage -PackageFamilyName $package.PackageFamilyName -AppId $appId -Command $guiHost -Args $childArguments -PreventBreakaway
        $timeout = if ($Action -eq 'check') { 45 } else { 1140 }
        $deadline = [DateTime]::UtcNow.AddSeconds($timeout)
        while (-not (Test-Path -LiteralPath $resultFile)) {
            if ([DateTime]::UtcNow -ge $deadline) { throw 'The packaged Store helper timed out. No installation was requested.' }
            Start-Sleep -Milliseconds 200
        }
        $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json
        if ($result.error) { throw $result.error }
        $result | ConvertTo-Json -Depth 8 -Compress
    } catch {
        [Console]::Error.WriteLine($_.Exception.Message)
        exit 1
    } finally {
        Remove-Item -LiteralPath $resultFile -Force -ErrorAction SilentlyContinue
    }
    exit 0
}

function Wait-StoreOperation($Operation, [Type[]]$Types, [int]$TimeoutSeconds) {
    $interfaceName = if ($Types.Count -eq 1) { 'IAsyncOperation`1' } else { 'IAsyncOperationWithProgress`2' }
    $method = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
        $_.Name -eq 'AsTask' -and $_.IsGenericMethod -and $_.GetGenericArguments().Count -eq $Types.Count -and
        $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq $interfaceName
    } | Select-Object -First 1
    $task = $method.MakeGenericMethod($Types).Invoke($null, @($Operation))
    if (-not $task.Wait([TimeSpan]::FromSeconds($TimeoutSeconds))) {
        $Operation.Cancel()
        throw 'Microsoft Store operation timed out. No installation was requested.'
    }
    return $task.Result
}

try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime
    $package = [Windows.ApplicationModel.Package,Windows.ApplicationModel,ContentType=WindowsRuntime]::Current
    if ($package.Id.FamilyName -ne 'OpenAI.Codex_2p2nqsd0c76g0') { throw 'Store helper is not running with stable Codex package identity.' }
    $context = [Windows.Services.Store.StoreContext,Windows.Services.Store,ContentType=WindowsRuntime]::GetDefault()
    $updates = @(Wait-StoreOperation $context.GetAppAndOptionalStorePackageUpdatesAsync() @([System.Collections.Generic.IReadOnlyList[Windows.Services.Store.StorePackageUpdate]]) 30 |
        Where-Object { $_.Package.Id.FamilyName -eq $package.Id.FamilyName })
    $result = [ordered]@{ hasUpdate=($updates.Count -gt 0); canSilentlyDownload=$context.CanSilentlyDownloadStorePackageUpdates; completed=$false; overallState=$null }
    if ($Action -eq 'download' -and $result.hasUpdate) {
        if (-not $result.canSilentlyDownload) { throw 'Microsoft Store silent downloads are disabled or unavailable on this network. Enable automatic Store updates or use a direct download.' }
        $typedUpdates = [System.Collections.Generic.List[Windows.Services.Store.StorePackageUpdate]]::new()
        foreach ($update in $updates) { $typedUpdates.Add($update) }
        # Download only. The external controller still owns graceful quit and registration.
        $download = Wait-StoreOperation ($context.TrySilentDownloadStorePackageUpdatesAsync($typedUpdates)) @([Windows.Services.Store.StorePackageUpdateResult], [Windows.Services.Store.StorePackageUpdateStatus]) 1080
        $result.overallState = $download.OverallState.ToString()
        $result.completed = $result.overallState -eq 'Completed'
        if (-not $result.completed) { throw ('Microsoft Store download failed: ' + $result.overallState) }
    }
} catch {
    $result = @{ error=$_.Exception.ToString() }
}
# Atomic publication prevents the parent reading a partial result.
$temporary = $ResultPath + '.tmp'
[IO.File]::WriteAllText($temporary, ($result | ConvertTo-Json -Depth 8 -Compress), [Text.UTF8Encoding]::new($false))
[IO.File]::Move($temporary, $ResultPath)
