param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [Parameter(Mandatory = $true)][string]$Version,
    [int]$Vus = 8,
    [int]$Seconds = 20,
    [int]$Port = 0,
    [switch]$Auth
)

# Thread-time and memory profile of a published build under k6 load: a dotnet-trace thread-time sample taken during the load (wall clock, not pure CPU), a
# dotnet-gcdump of the managed heap once the load has settled, and the top-N hot methods and heap types as text.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sample = @{
    inkwell = @{ Dir = 'samples/Inkwell/src/Inkwell'; Dll = 'Inkwell.dll'; Port = 5100 }
    carparker = @{ Dir = 'samples/CarParker/src/CarParker'; Dll = 'CarParker.dll'; Port = 5199 }
    techlandier = @{ Dir = 'samples/TechLandier/src/TechLandier'; Dll = 'TechLandier.dll'; Port = 5100 }
}[$App]
if ($Port -eq 0) { $Port = $sample.Port }
$artifactDir = Join-Path $PSScriptRoot "artifacts/$($App.Substring(0,1).ToUpper() + $App.Substring(1))-$Version"
$dll = Join-Path $artifactDir $sample.Dll
if (-not (Test-Path $dll)) { throw "Missing $dll; publish the app at version $Version first." }
$tools = Join-Path $env:USERPROFILE '.dotnet/tools'
$k6 = Join-Path $env:LOCALAPPDATA 'bench/k6-v1.3.0-windows-amd64/k6.exe'
$out = Join-Path $PSScriptRoot 'artifacts/results'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$kind = if ($Auth) { 'auth' } else { 'pub' }
$name = "$App-$Version-profile-$kind-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$trace = Join-Path $out "$name.nettrace"
$gcdump = Join-Path $out "$name.gcdump"
$loadScript = if ($Auth) { Join-Path $PSScriptRoot 'auth-load.js' } else { Join-Path $PSScriptRoot 'load.js' }

Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue | ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }
$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:JOBS_ENABLED = 'false'
$env:CRON_ENABLED = 'false'
$env:DOTNET_gcServer = '0'
$env:Logging__LogLevel__Default = 'Warning'
$env:Inertia__SsrBasePort = if ($env:BENCH_SSR_PORT) { $env:BENCH_SSR_PORT } else { '13814' }

$p = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory (Join-Path $root $sample.Dir) `
    -RedirectStandardOutput (Join-Path $out "$name.app.log") -RedirectStandardError (Join-Path $out "$name.app.err.log") -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ($true) {
        try { if ((Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$Port/health" -TimeoutSec 3).StatusCode -eq 200) { break } } catch { }
        if ([DateTime]::UtcNow -gt $deadline) { throw 'App did not become healthy' }
        Start-Sleep -Milliseconds 750
    }
    $env:BASE_URL = "http://127.0.0.1:$Port"; $env:APP = $App; $env:PROFILE = 'vus'; $env:VUS = '4'; $env:DURATION = '10s'
    $env:SUMMARY_PATH = Join-Path $out "$name.warmup.json"
    & $k6 run $loadScript *> $null
    if ($LASTEXITCODE -ne 0) { throw "Warmup k6 failed ($LASTEXITCODE)" }

    $tracer = Start-Process -FilePath (Join-Path $tools 'dotnet-trace.exe') -PassThru -NoNewWindow `
        -ArgumentList @('collect', '--process-id', $p.Id, '--profile', 'dotnet-common,dotnet-sampled-thread-time', '--duration', [TimeSpan]::FromSeconds($Seconds + 2).ToString('hh\:mm\:ss'), '--output', $trace) `
        -RedirectStandardOutput (Join-Path $out "$name.trace.log")
    Start-Sleep -Seconds 2
    $env:VUS = "$Vus"; $env:DURATION = "${Seconds}s"; $env:SUMMARY_PATH = Join-Path $out "$name.load.json"
    & $k6 run $loadScript *> (Join-Path $out "$name.load.log")
    if ($LASTEXITCODE -ne 0) { throw "Load k6 failed ($LASTEXITCODE); see $name.load.log" }
    $tracer.WaitForExit()
    if ($tracer.ExitCode -ne 0 -or -not (Test-Path $trace)) { throw "dotnet-trace failed ($($tracer.ExitCode)); see $name.trace.log" }

    Start-Sleep -Seconds 5
    & (Join-Path $tools 'dotnet-gcdump.exe') collect --process-id $p.Id --output $gcdump *> (Join-Path $out "$name.gcdump.log")
    if ($LASTEXITCODE -ne 0) { throw "dotnet-gcdump failed ($LASTEXITCODE); see $name.gcdump.log" }
    $wsMb = [math]::Round((Get-Process -Id $p.Id).WorkingSet64 / 1MB, 1)

    $top = & (Join-Path $tools 'dotnet-trace.exe') report $trace topN -n 20 --inclusive 2>&1
    $topExclusive = & (Join-Path $tools 'dotnet-trace.exe') report $trace topN -n 20 2>&1
    $heap = & (Join-Path $tools 'dotnet-gcdump.exe') report $gcdump 2>&1 | Select-Object -First 30
    $report = Join-Path $PSScriptRoot "results/$name.txt"
    @("# $name", "workingSetMbAfterLoad: $wsMb", '', '## Sampled thread time, top 20 (inclusive; wall clock, so idle waits such as WaitForSignal rank high)', $top, '', '## Sampled thread time, top 20 (exclusive)', $topExclusive, '', '## Heap (gcdump, largest types)', $heap) |
        Out-File -FilePath $report -Encoding utf8
    Write-Host "Wrote $report"
}
finally {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
