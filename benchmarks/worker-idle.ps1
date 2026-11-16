param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [Parameter(Mandatory = $true)][string]$Version,
    [int]$Seconds = 120,
    [int]$Port = 0,
    [switch]$Trace
)

# Idle cost of the background machinery: the app runs with the job worker and cron ON and receives no traffic.
# Records process CPU/memory plus the database work the workers do on their own (EF command log for every app,
# pg_stat_database for PostgreSQL, serverStatus opcounters for MongoDB).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sample = @{
    inkwell = @{ Dir = 'samples/Inkwell/src/Inkwell'; Dll = 'Inkwell.dll'; Port = 5100 }
    carparker = @{ Dir = 'samples/CarParker/src/CarParker'; Dll = 'CarParker.dll'; Port = 5199 }
    techlandier = @{ Dir = 'samples/TechLandier/src/TechLandier'; Dll = 'TechLandier.dll'; Port = 5100 }
}[$App]
if ($Port -eq 0) { $Port = $sample.Port }
$name = $App.Substring(0, 1).ToUpper() + $App.Substring(1)
$dll = Join-Path $PSScriptRoot "artifacts/$name-$Version/$($sample.Dll)"
if (-not (Test-Path $dll)) { throw "Missing $dll" }
$out = Join-Path $PSScriptRoot 'artifacts/results'
New-Item -ItemType Directory -Force -Path $out, (Join-Path $PSScriptRoot 'results') | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$log = Join-Path $out "$App-$Version-worker-idle-$stamp.log"

function Get-DbOps {
    if ($App -eq 'techlandier') {
        $env:PGPASSWORD = 'local-dev-password'
        return [long](& psql -h localhost -p 5433 -U techlandier -d techlandier -t -A -c "select xact_commit + xact_rollback from pg_stat_database where datname='techlandier'")
    }
    if ($App -eq 'carparker') {
        $json = docker exec carparker-mongo-1 mongosh --quiet --eval 'const o=db.serverStatus().opcounters; JSON.stringify(o)' | ConvertFrom-Json
        return [long]($json.query + $json.command + $json.getmore + $json.insert + $json.update + $json.delete)
    }
    return $null
}

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:JOBS_ENABLED = 'true'
$env:CRON_ENABLED = 'true'
$env:DOTNET_gcServer = '0'
$env:Inertia__SsrBasePort = '13814'
$env:Logging__LogLevel__Default = 'Warning'
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', 'Information')

$p = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory (Join-Path $root $sample.Dir) `
    -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru -NoNewWindow
try {
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        try { if ((Invoke-WebRequest -UseBasicParsing "http://127.0.0.1:$Port/health" -TimeoutSec 3).StatusCode -eq 200) { break } } catch { }
        Start-Sleep -Milliseconds 750
    }
    Start-Sleep -Seconds 10 # let startup work settle
    $startLines = (Get-Content $log | Select-String 'Executed DbCommand').Count
    $dbStart = Get-DbOps
    $proc = Get-Process -Id $p.Id
    $cpuStart = $proc.TotalProcessorTime.TotalMilliseconds
    $ws = @()
    $traceFile = $null
    if ($Trace) {
        # CPU sampling for the whole idle window; open the .nettrace in PerfView/VS or `dotnet-trace report`.
        $traceFile = Join-Path $out "$App-$Version-worker-idle-$stamp.nettrace"
        $tracer = Start-Process -FilePath (Join-Path $env:USERPROFILE '.dotnet/tools/dotnet-trace.exe') -PassThru -NoNewWindow `
            -ArgumentList @('collect', '-p', $p.Id, '--profile', 'dotnet-sampled-thread-time,database', '--duration', ([TimeSpan]::FromSeconds($Seconds).ToString('hh\:mm\:ss')), '-o', $traceFile) `
            -RedirectStandardOutput "$traceFile.log"
    }
    $end = (Get-Date).AddSeconds($Seconds)
    while ((Get-Date) -lt $end) { $ws += (Get-Process -Id $p.Id).WorkingSet64 / 1MB; Start-Sleep -Seconds 2 }
    $proc.Refresh()
    $cpuMs = $proc.TotalProcessorTime.TotalMilliseconds - $cpuStart
    $efCommands = (Get-Content $log | Select-String 'Executed DbCommand').Count - $startLines
    $dbEnd = Get-DbOps
    $result = [ordered]@{
        app = $App; version = $Version; kind = 'worker-idle'; seconds = $Seconds
        efCommandsPerMinute = [math]::Round($efCommands * 60 / $Seconds, 1)
        dbOpsPerMinute = if ($null -ne $dbStart) { [math]::Round(($dbEnd - $dbStart) * 60 / $Seconds, 1) } else { $null }
        cpuMsPerMinute = [math]::Round($cpuMs * 60 / $Seconds, 1)
        workingSetMbAvg = [math]::Round(($ws | Measure-Object -Average).Average, 1)
        workingSetMbMax = [math]::Round(($ws | Measure-Object -Maximum).Maximum, 1)
        log = $log
        trace = $traceFile
    }
    if ($Trace) { $tracer.WaitForExit(30000) | Out-Null }
    $json = $result | ConvertTo-Json
    $json | Set-Content (Join-Path $PSScriptRoot "results/$App-$Version-worker-idle-$stamp.json")
    $json
} finally {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" -ErrorAction SilentlyContinue |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
}
