param(
    [Parameter(Mandatory = $true)][ValidateSet('CarParker', 'CarParkerReplicas', 'TechLandier', 'TechLandierDistributed')][string]$Variant
)

$ErrorActionPreference = 'Stop'
$statePath = Join-Path $PSScriptRoot "state\$Variant.json"
if (-not (Test-Path $statePath)) {
    Write-Output "No state for $Variant"
    exit 0
}
$state = Get-Content $statePath -Raw | ConvertFrom-Json
$pids = @()
if ($state.appPids) { $pids += $state.appPids }
if ($state.ssrPids) { $pids += $state.ssrPids }
if ($state.auxPids) { $pids += $state.auxPids }
foreach ($pidValue in $pids) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$pidValue" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue
}
if ($state.proxy) { docker rm -f $state.proxy 2>$null | Out-Null }
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^bun(\.exe)?$' -and $_.CommandLine -match 'benchmarks\\artifacts\\perf\\.*\\ssr[/\\]ssr\.js' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
Remove-Item $statePath -Force -ErrorAction SilentlyContinue
Write-Output "Stopped $Variant"
