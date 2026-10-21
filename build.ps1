param([string]$Runtime)

$ErrorActionPreference = 'Stop'
$hostRuntime = if ($IsMacOS) { if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' } } elseif ($IsLinux) { if ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') { 'linux-arm64' } else { 'linux-x64' } } else { 'win-x64' }

if (-not $Runtime) {
    $Runtime = $hostRuntime
}

$output = if ($Runtime -eq $hostRuntime) { Join-Path $PSScriptRoot 'bin' } else { Join-Path $PSScriptRoot "artifacts\$Runtime" }
$staging = Join-Path $PSScriptRoot ('artifacts\publish-' + [Guid]::NewGuid().ToString('N'))
dotnet publish "$PSScriptRoot\ClaudexYourself.csproj" -c Release -r $Runtime --self-contained false -o $staging
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# Loaded Windows binaries cannot be overwritten, but can be renamed. Preserve
# the old build for rollback; verified MCP reload then loads the new files.
$backup = Join-Path $PSScriptRoot ('artifacts\previous-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $output, $backup -Force | Out-Null
foreach ($artifact in Get-ChildItem -LiteralPath $staging -File) {
    $target = Join-Path $output $artifact.Name
    if (Test-Path -LiteralPath $target) { Move-Item -LiteralPath $target -Destination (Join-Path $backup $artifact.Name) }
    Copy-Item -LiteralPath $artifact.FullName -Destination $target
}
foreach ($folder in Get-ChildItem -LiteralPath $staging -Directory) {
    Copy-Item -LiteralPath $folder.FullName -Destination $output -Recurse -Force
}

Write-Host "Built $output"
