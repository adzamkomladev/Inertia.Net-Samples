param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [int]$WarmupSeconds = 10,
    [int]$MeasureSeconds = 30,
    [string]$Label = 'matrix'
)

$ErrorActionPreference = 'Stop'
$script = Join-Path $PSScriptRoot 'run.ps1'
$duration = "$($MeasureSeconds)s"
$warmup = "$($WarmupSeconds)s"

function Invoke-Run([string]$version, [string]$profile, [int]$rps, [int]$vus, [int]$ssrCache, [int]$ssrStale, [string]$name) {
    Write-Host "=== $App $version $name ==="
    $args = @(
        '-NoProfile', '-File', $script, '-App', $App, '-Version', $version, '-Profile', $profile,
        '-Duration', $duration, '-Warmup', $warmup, '-SsrCacheSeconds', $ssrCache, '-SsrStaleSeconds', $ssrStale,
        '-Label', $name
    )
    if ($profile -eq 'rps') { $args += @('-Rps', $rps) } else { $args += @('-Vus', $vus) }
    & pwsh @args *> (Join-Path $PSScriptRoot "artifacts/results/$App-$version-$name.matrix.log")
    if ($LASTEXITCODE -ne 0) { throw "run.ps1 failed for $App $version $name" }
}

# Two alternating rounds per version for the two steady-state profiles, one for the component probes.
foreach ($round in 1, 2) {
    foreach ($version in '1.4.0', '1.5.1') {
        Invoke-Run $version 'rps' 150 0 300 60 "$Label-rps-$round"
        Invoke-Run $version 'vus' 0 8 300 60 "$Label-vus-$round"
    }
}
foreach ($version in '1.4.0', '1.5.1') {
    Invoke-Run $version 'rps' 80 0 0 60 "$Label-ssr0"
    Invoke-Run $version 'rps' 250 0 2 60 "$Label-ttl2"
}
Write-Host "Matrix complete for $App."
