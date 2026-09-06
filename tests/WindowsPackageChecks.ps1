$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
# Intercept the deployment boundary: these checks never install or close an application.
$deploymentCalls = [Collections.Generic.List[object]]::new()
function Add-AppxPackage {
    [CmdletBinding()]
    param([string]$Path, [switch]$Stage, [switch]$Register, [switch]$DisableDevelopmentMode,
        [switch]$ForceTargetApplicationShutdown)
    $deploymentCalls.Add(@{} + $PSBoundParameters)
}
$helper = Join-Path $PSScriptRoot '..\update\WindowsPackage.ps1'
foreach ($action in @('install', 'register-staged')) {
    foreach ($recover in @($false, $true)) {
        $result = & $helper -Action $action -PackagePath 'C:\test\verified-package' -FinishTargetShutdown:$recover | ConvertFrom-Json
        $captured = $deploymentCalls[$deploymentCalls.Count - 1]
        if (-not $result.installed -or $captured.Path -ne 'C:\test\verified-package') { throw 'Installation arguments were lost.' }
        if ([bool]$captured.ForceTargetApplicationShutdown -ne $recover) { throw 'Target shutdown must require explicit recovery.' }
        if ([bool]$captured.Register -ne ($action -eq 'register-staged')) { throw 'Wrong registration mode.' }
        if ($action -eq 'register-staged' -and -not $captured.DisableDevelopmentMode) { throw 'Staged registration must preserve production mode.' }
    }
}
$result = & $helper -Action stage -PackagePath 'C:\test\verified-package' -FinishTargetShutdown | ConvertFrom-Json
$captured = $deploymentCalls[$deploymentCalls.Count - 1]
if (-not $result.staged -or -not $captured.Stage -or $captured.ContainsKey('ForceTargetApplicationShutdown')) { throw 'Staging must never shut down the app.' }
'Windows package checks passed (5 deployment scenarios; no packages changed).'
