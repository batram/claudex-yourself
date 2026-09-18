param([string]$Scenario)
$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
if ($Scenario) {
    function Add-AppxPackage {
        [CmdletBinding()]
        param([string]$Path, [switch]$Stage, [switch]$Register, [switch]$DisableDevelopmentMode, [switch]$ForceTargetApplicationShutdown)
        if ($Scenario -eq 'ordinary-error') { throw '0x80070005 ordinary failure' }
        throw '0x80073D28 packaged service requires elevation'
    }
    function Start-Process {
        param($FilePath, $Verb, $WindowStyle, [switch]$Wait, [switch]$PassThru, $ArgumentList)
        if ($Verb -ne 'RunAs' -or $WindowStyle -ne 'Hidden' -or -not $Wait) { throw 'Invalid elevation launch' }
        if ($Scenario -eq 'cancel') { throw [ComponentModel.Win32Exception]::new(1223) }
        $command = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($ArgumentList[-1]))
        $preamble = @'
function Add-AppxPackage {
    [CmdletBinding()]
    param([string]$Path, [switch]$Stage, [switch]$Register, [switch]$DisableDevelopmentMode, [switch]$ForceTargetApplicationShutdown)
    if ($Path -ne "C:\test\package with space's.msix") { throw 'Package path quoting failed' }
}
'@
        if ($Scenario -eq 'elevated-error') { $preamble = "function Add-AppxPackage { throw '0x80073D02 still in use' }" }
        if ($Scenario -eq 'wrong-user') { $command = $command.Replace([Security.Principal.WindowsIdentity]::GetCurrent().User.Value, 'S-1-0-0') }
        $scratch = Join-Path ([IO.Path]::GetTempPath()) ([Guid]::NewGuid().ToString() + '.ps1')
        try {
            [IO.File]::WriteAllText($scratch, $preamble + [Environment]::NewLine + $command)
            & $FilePath -NoProfile -File $scratch
            return [pscustomobject]@{ ExitCode = $LASTEXITCODE }
        } finally { Remove-Item -LiteralPath $scratch -Force }
    }
    $operation = if ($Scenario -in @('install', 'register-staged')) { $Scenario } else { 'stage' }
    & (Join-Path $PSScriptRoot '../update/WindowsPackage.ps1') -Action $operation -PackagePath "C:\test\package with space's.msix"
    exit $LASTEXITCODE
}
foreach ($case in @('stage', 'install', 'register-staged', 'cancel', 'ordinary-error', 'elevated-error', 'wrong-user')) {
    $capture = [IO.Path]::GetTempFileName()
    try {
        & powershell -NoProfile -File $PSCommandPath -Scenario $case 2> $capture | Out-Null
        $code = $LASTEXITCODE
        $detail = Get-Content -LiteralPath $capture -Raw
        if ($case -in @('stage', 'install', 'register-staged')) {
            if ($code -ne 0) { throw "$case failed: $detail" }
        } else {
            $expected = switch ($case) {
                'cancel' { 'approval was cancelled' }
                'ordinary-error' { '0x80070005' }
                'elevated-error' { '0x80073D02' }
                'wrong-user' { 'same Windows account' }
            }
            if ($code -eq 0 -or $detail -notmatch $expected) { throw "$case did not preserve failure: $detail" }
        }
    } finally { Remove-Item -LiteralPath $capture -Force }
}
'Windows elevation checks passed (7 scenarios; mocked deployment and UAC).'
