param(
    [Parameter(Mandatory = $true)][string]$Query,
    [int]$MaxResults = 10,
    [string]$AssemblyPath
)

$ErrorActionPreference = 'Stop'
$OutputEncoding = [Console]::OutputEncoding = [Text.UTF8Encoding]::new()
if (-not $AssemblyPath) {
    $AssemblyPath = Join-Path $PSScriptRoot '..\bin\Release\net10.0\claudex-yourself.dll'
}
$argumentsObject = @{ query = $Query; max_results = $MaxResults }
$requestObject = @{
    jsonrpc = '2.0'
    id = 1
    method = 'tools/call'
    params = @{ name = 'search_renderer_sources'; arguments = $argumentsObject }
}
$requestJson = $requestObject | ConvertTo-Json -Depth 8 -Compress
$response = $requestJson | & dotnet $AssemblyPath mcp | ConvertFrom-Json
if ($response.error) { throw ($response.error | ConvertTo-Json -Depth 8) }
if ($response.result.isError) { throw $response.result.content[0].text }
$response.result.structuredContent | ConvertTo-Json -Depth 12
