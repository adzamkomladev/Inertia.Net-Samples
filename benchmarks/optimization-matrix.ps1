param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [string]$Before = '1.5.1',
    [string]$After = '1.5.1-opt',
    [int]$Rounds = 2,
    [int]$AuthRounds = 1,
    [int]$WarmupSeconds = 10,
    [int]$MeasureSeconds = 30,
    [switch]$Auth,
    [switch]$Ssr0
)

$ErrorActionPreference = 'Stop'
$run = Join-Path $PSScriptRoot 'run.ps1'
$duration = "$($MeasureSeconds)s"
$warmup = "$($WarmupSeconds)s"
$versions = @($Before, $After)

function Invoke-Run([string]$version, [string]$profile, [int]$rps, [int]$vus, [int]$ssrCache, [string]$label, [bool]$auth) {
    Write-Host "=== $App $version $label ==="
    $runArgs = @(
        '-NoProfile', '-File', $run, '-App', $App, '-Version', $version, '-Profile', $profile,
        '-Duration', $duration, '-Warmup', $warmup, '-SsrCacheSeconds', $ssrCache, '-SsrStaleSeconds', '60',
        '-Label', $label
    )
    if ($profile -eq 'rps') { $runArgs += @('-Rps', $rps) } else { $runArgs += @('-Vus', $vus) }
    if ($auth) { $runArgs += '-Auth' }
    & pwsh @runArgs *> (Join-Path $PSScriptRoot "artifacts/results/$App-$version-$label.matrix.log")
    if ($LASTEXITCODE -ne 0) { throw "run.ps1 failed for $App $version $label" }
}

foreach ($round in 1..$Rounds) {
    foreach ($version in $versions) {
        Invoke-Run $version 'rps' 150 0 300 "opt-rps-$round" $false
        Invoke-Run $version 'vus' 0 8 300 "opt-vus-$round" $false
    }
}

if ($Auth) {
    foreach ($round in 1..$AuthRounds) {
        foreach ($version in $versions) {
            Invoke-Run $version 'rps' 100 0 300 "opt-auth-rps-$round" $true
            Invoke-Run $version 'vus' 0 8 300 "opt-auth-vus-$round" $true
        }
    }
}

if ($Ssr0) {
    foreach ($version in $versions) {
        Invoke-Run $version 'rps' 80 0 0 'opt-ssr0' $false
    }
}

Write-Host "Optimization matrix complete for $App."
