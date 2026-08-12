param([string]$Runtime)

$ErrorActionPreference = 'Stop'
$runtimeWasExplicit = $PSBoundParameters.ContainsKey('Runtime')

if (-not $Runtime) {
    if ($IsMacOS) {
        $Runtime = if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
    }
    else {
        $Runtime = 'win-x64'
    }
}

$output = if ($runtimeWasExplicit) { Join-Path $PSScriptRoot "bin\$Runtime" } else { Join-Path $PSScriptRoot 'bin' }
dotnet publish "$PSScriptRoot\ClaudexYourself.csproj" -c Release -r $Runtime --self-contained false -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Built $output"
