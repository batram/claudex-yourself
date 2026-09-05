param(
    [string]$AssemblyPath = (Join-Path $PSScriptRoot '..\bin\claudex-yourself.dll'),
    [switch]$LiveRenderer
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()

$requestLines = @(
    '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18"}}',
    '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
)
if ($LiveRenderer) {
    $requestLines += '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"inspect_renderer","arguments":{"selector":"body","max_results":1}}}'
    $requestLines += '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"evaluate_renderer","arguments":{"expression":"({ title: document.title })"}}}'
    $requestLines += '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"get_userscript_runtime_status","arguments":{}}}'
    $requestLines += '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"capture_renderer","arguments":{"selector":"body"}}}'
    $requestLines += '{"jsonrpc":"2.0","id":7,"method":"tools/call","params":{"name":"interact_renderer","arguments":{"action":"press_key","key":"Shift"}}}'
    $requestLines += '{"jsonrpc":"2.0","id":8,"method":"tools/call","params":{"name":"search_renderer_sources","arguments":{"query":"Usage remaining","max_results":5}}}'
}

$processInfo = [Diagnostics.ProcessStartInfo]::new('dotnet')
$processInfo.UseShellExecute = $false
$processInfo.RedirectStandardInput = $true
$processInfo.RedirectStandardOutput = $true
$processInfo.RedirectStandardError = $true
$processInfo.Arguments = '"' + $AssemblyPath + '" mcp'
$serverProcess = [Diagnostics.Process]::Start($processInfo)
foreach ($requestLine in $requestLines) { $serverProcess.StandardInput.WriteLine($requestLine) }
$serverProcess.StandardInput.Close()
$responseItems = @()
while (-not $serverProcess.StandardOutput.EndOfStream) {
    $responseItems += $serverProcess.StandardOutput.ReadLine() | ConvertFrom-Json
}
$standardError = $serverProcess.StandardError.ReadToEnd()
$serverProcess.WaitForExit()
if ($serverProcess.ExitCode -ne 0) { throw "MCP server exited $($serverProcess.ExitCode): $standardError" }
if ($responseItems.Count -ne $requestLines.Count) { throw "Expected $($requestLines.Count) responses; received $($responseItems.Count)." }
if ($responseItems | Where-Object error) { throw 'MCP smoke test returned an error response.' }
$expectedIds = (1..$requestLines.Count) -join ','
if (($responseItems.id -join ',') -ne $expectedIds) { throw "Expected response IDs $expectedIds; received $($responseItems.id -join ',')." }

$toolNames = @($responseItems[1].result.tools | ForEach-Object name)
$requiredTools = @('inspect_renderer', 'interact_renderer', 'capture_renderer', 'evaluate_renderer', 'get_userscript_runtime_status', 'search_renderer_sources', 'check_codex_update', 'get_codex_update_status', 'install_codex_update')
foreach ($toolName in $requiredTools) {
    if ($toolName -notin $toolNames) { throw "Missing MCP tool: $toolName" }
}
if ($LiveRenderer) {
    foreach ($responseItem in $responseItems[2..7]) {
        if ($responseItem.result.isError) { throw $responseItem.result.content[0].text }
    }
    $capturePath = $responseItems[5].result.structuredContent.path
    if (-not (Test-Path -LiteralPath $capturePath)) { throw "Renderer capture was not created: $capturePath" }
    Write-Host "Renderer capture: $capturePath"
}

Write-Host "MCP protocol smoke passed ($($toolNames.Count) tools)."
