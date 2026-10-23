param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [Parameter(Mandatory = $true)][string]$Version,
    [ValidateSet('rps', 'vus', 'ramp')][string]$Profile = 'rps',
    [int]$Rps = 200,
    [int]$Vus = 8,
    [string]$Duration = '45s',
    [string]$Warmup = '12s',
    [int]$Port = 0,
    [int]$SsrCacheSeconds = 300,
    [int]$SsrStaleSeconds = 60,
    [string]$Routes = '',
    [string]$Label = ''
)

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
$workdir = Join-Path $root $sample.Dir
$k6 = Join-Path $env:LOCALAPPDATA 'bench/k6-v1.3.0-windows-amd64/k6.exe'
$counters = Join-Path $env:USERPROFILE '.dotnet/tools/dotnet-counters.exe'
$resultsDir = Join-Path $PSScriptRoot 'results'
$artifactResults = Join-Path $PSScriptRoot 'artifacts/results'
New-Item -ItemType Directory -Force -Path $resultsDir, $artifactResults | Out-Null

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$name = if ($Label) { "$App-$Version-$Label-$stamp" } else { "$App-$Version-$Profile-$stamp" }
$appLog = Join-Path $artifactResults "$name.app.log"
$appErr = Join-Path $artifactResults "$name.app.err.log"
$warmupJson = Join-Path $artifactResults "$name.warmup.json"
$loadJson = Join-Path $artifactResults "$name.load.json"
$processJson = Join-Path $artifactResults "$name.process.json"
$countersCsv = Join-Path $artifactResults "$name.counters.csv"
$countersOut = Join-Path $artifactResults "$name.counters.log"
$summaryJson = Join-Path $resultsDir "$name.json"

function Stop-App([int]$processId) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$processId" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

function Wait-Healthy([int]$processId, [int]$port) {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $processId -ErrorAction SilentlyContinue)) { throw "App exited during startup; see $appLog" }
        try {
            $r = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/health" -TimeoutSec 3
            if ($r.StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 750
    }
    throw "App did not become healthy; see $appLog"
}

function Get-DbCounters {
    if ($App -eq 'techlandier') {
        $env:PGPASSWORD = 'local-dev-password'
        $line = & psql -h localhost -p 5433 -U techlandier -d techlandier -t -A -c "select xact_commit, tup_returned, blks_hit, blks_read from pg_stat_database where datname='techlandier'"
        $parts = $line -split '\|'
        return [ordered]@{ xactCommit = [long]$parts[0]; tuplesReturned = [long]$parts[1]; blocksHit = [long]$parts[2]; blocksRead = [long]$parts[3] }
    }
    if ($App -eq 'carparker') {
        $json = docker exec carparker-mongo-1 mongosh --quiet --eval 'const o=db.serverStatus().opcounters; JSON.stringify({query:Number(o.query),command:Number(o.command),getmore:Number(o.getmore),insert:Number(o.insert),update:Number(o.update),delete:Number(o.delete)})'
        return ($json | ConvertFrom-Json)
    }
    return $null
}

function Import-RuntimeCounters([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $rows = Import-Csv $path
    function Values([string]$name) {
        @($rows | Where-Object { $_.'Counter Name' -eq $name } | ForEach-Object { [double]$_.'Mean/Increment' })
    }
    function Avg([string]$name) {
        $v = Values $name
        if ($v.Count -eq 0) { return $null }
        [math]::Round(($v | Measure-Object -Average).Average, 3)
    }
    function Max([string]$name) {
        $v = Values $name
        if ($v.Count -eq 0) { return $null }
        [math]::Round(($v | Measure-Object -Maximum).Maximum, 3)
    }
    function Sum([string]$name) {
        $v = Values $name
        if ($v.Count -eq 0) { return $null }
        [math]::Round(($v | Measure-Object -Sum).Sum, 3)
    }
    function InertiaRate([string]$pattern) {
        $v = @($rows | Where-Object { $_.'Counter Name' -like $pattern -and $_.'Counter Type' -eq 'Rate' } | ForEach-Object { [double]$_.'Mean/Increment' })
        if ($v.Count -eq 0) { return 0 }
        [math]::Round(($v | Measure-Object -Sum).Sum, 2)
    }
    function InertiaP95([string]$name) {
        $v = @($rows | Where-Object { $_.'Counter Name' -like "$name*Percentile=95*" } | ForEach-Object { [double]$_.'Mean/Increment' })
        if ($v.Count -eq 0) { return $null }
        [math]::Round(($v | Measure-Object -Average).Average, 4)
    }
    [ordered]@{
        cpuPctAvg = Avg 'CPU Usage (%)'
        workingSetMbAvg = Avg 'Working Set (MB)'
        workingSetMbMax = Max 'Working Set (MB)'
        gcHeapMbMax = Max 'GC Heap Size (MB)'
        allocRateMbPerSecAvg = if ($null -ne (Avg 'Allocation Rate (B / 1 sec)')) { [math]::Round((Avg 'Allocation Rate (B / 1 sec)') / 1MB, 2) } else { $null }
        gen0PerSecAvg = Avg 'Gen 0 GC Count (Count / 1 sec)'
        gen1PerSecAvg = Avg 'Gen 1 GC Count (Count / 1 sec)'
        gen2PerSecAvg = Avg 'Gen 2 GC Count (Count / 1 sec)'
        timeInGcPctAvg = Avg '% Time in GC since last GC (%)'
        threadPoolThreadsMax = Max 'ThreadPool Thread Count'
        threadPoolQueueMax = Max 'ThreadPool Queue Length'
        exceptionsPerSecAvg = Avg 'Exception Count (Count / 1 sec)'
        ssrOutcomesCache = InertiaRate 'inertia.ssr.outcomes*reason=cache*'
        ssrOutcomesRendered = InertiaRate 'inertia.ssr.outcomes*reason=ok*'
        ssrOutcomesAll = InertiaRate 'inertia.ssr.outcomes*'
        ssrCacheHitPct = $null
        pageDurationP95S = InertiaP95 'inertia.page.duration'
        ssrDurationP95S = InertiaP95 'inertia.ssr.duration'
        pageSizeP95By = InertiaP95 'inertia.page.size'
    }
}

# Stop anything already listening on the port / stray SSR processes from a previous run.
Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-App $_.OwningProcess }
Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:JOBS_ENABLED = 'false'
$env:CRON_ENABLED = 'false'
$env:DOTNET_gcServer = '0'
$env:Logging__LogLevel__Default = 'Warning'
$env:Logging__LogLevel__Microsoft = 'Warning'
$env:Logging__LogLevel__System = 'Warning'
$env:Inertia__SsrCacheSeconds = "$SsrCacheSeconds"
$env:Inertia__SsrStaleSeconds = "$SsrStaleSeconds"
$env:ROUTES = $Routes

Write-Host "Starting $App $Version on $Port ..."
$p = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory $workdir `
    -RedirectStandardOutput $appLog -RedirectStandardError $appErr -PassThru
try {
    Wait-Healthy $p.Id $Port
    Write-Host "Healthy (pid $($p.Id)); warming up $Warmup ..."

    $env:BASE_URL = "http://127.0.0.1:$Port"
    $env:APP = $App
    $env:PROFILE = 'vus'
    $env:VUS = '4'
    $env:DURATION = $Warmup
    $env:SUMMARY_PATH = $warmupJson
    & $k6 run (Join-Path $PSScriptRoot 'load.js') *> (Join-Path $artifactResults "$name.warmup.log")
    if ($LASTEXITCODE -ne 0) { throw "Warmup k6 failed with exit code $LASTEXITCODE" }

    $measureSeconds = [int]([TimeSpan]::Parse(($Duration -replace 's$', '') + ':00:00').TotalSeconds)
    if ($Duration -match '^(\d+)s$') { $measureSeconds = [int]$matches[1] }
    $sampleSeconds = $measureSeconds + 8

    Write-Host "Sampling processes and counters for $sampleSeconds s ..."
    $sampler = Start-Process -FilePath 'pwsh' -ArgumentList @(
        '-NoProfile', '-File', (Join-Path $PSScriptRoot 'sample-process.ps1'),
        '-AppPid', $p.Id, '-DurationSeconds', $sampleSeconds, '-OutputPath', $processJson
    ) -PassThru -RedirectStandardOutput (Join-Path $artifactResults "$name.sampler.log") -RedirectStandardError (Join-Path $artifactResults "$name.sampler.err")

    $counterArgs = @(
        'collect', '--process-id', $p.Id,
        '--counters', 'System.Runtime[working-set,gc-heap-size,alloc-rate,gen-0-gc-count,gen-1-gc-count,gen-2-gc-count,time-in-gc,threadpool-thread-count,threadpool-queue-length,exception-count,cpu-usage],Inertia.Net',
        '--format', 'csv', '--output', $countersCsv, '--duration', "00:00:$sampleSeconds"
    )
    $counterProc = Start-Process -FilePath $counters -ArgumentList $counterArgs -PassThru `
        -RedirectStandardOutput $countersOut -RedirectStandardError (Join-Path $artifactResults "$name.counters.err")

    $dbBefore = Get-DbCounters
    Start-Sleep -Seconds 2
    Write-Host "Running k6 $Profile (rps=$Rps vus=$Vus duration=$Duration) ..."
    $env:PROFILE = $Profile
    $env:RPS = "$Rps"
    $env:VUS = "$Vus"
    $env:DURATION = $Duration
    $env:SUMMARY_PATH = $loadJson
    & $k6 run (Join-Path $PSScriptRoot 'load.js') *> (Join-Path $artifactResults "$name.load.log")
    $k6Exit = $LASTEXITCODE
    $dbAfter = Get-DbCounters

    Write-Host "Waiting for samplers ..."
    $sampler.WaitForExit()
    $counterProc.WaitForExit()
    Start-Sleep -Milliseconds 500

    $load = Get-Content $loadJson -Raw | ConvertFrom-Json
    $process = if (Test-Path $processJson) { Get-Content $processJson -Raw | ConvertFrom-Json } else { $null }
    $runtime = Import-RuntimeCounters $countersCsv
    $db = $null
    if ($null -ne $dbBefore -and $null -ne $dbAfter) {
        $seconds = $measureSeconds
        if ($App -eq 'techlandier') {
            $db = [ordered]@{
                xactCommitDelta = $dbAfter.xactCommit - $dbBefore.xactCommit
                statementsPerSec = [math]::Round(($dbAfter.xactCommit - $dbBefore.xactCommit) / $seconds, 1)
                tuplesReturnedDelta = $dbAfter.tuplesReturned - $dbBefore.tuplesReturned
                tuplesPerSec = [math]::Round(($dbAfter.tuplesReturned - $dbBefore.tuplesReturned) / $seconds, 1)
                blocksHitDelta = $dbAfter.blocksHit - $dbBefore.blocksHit
                blocksReadDelta = $dbAfter.blocksRead - $dbBefore.blocksRead
            }
        } else {
            $db = [ordered]@{
                queryDelta = $dbAfter.query - $dbBefore.query
                commandDelta = $dbAfter.command - $dbBefore.command
                queriesPerSec = [math]::Round(($dbAfter.query - $dbBefore.query) / $seconds, 1)
                commandsPerSec = [math]::Round(($dbAfter.command - $dbBefore.command) / $seconds, 1)
                insertsDelta = $dbAfter.insert - $dbBefore.insert
                updatesDelta = $dbAfter.update - $dbBefore.update
                getMoreDelta = $dbAfter.getmore - $dbBefore.getmore
            }
        }
    }
    if ($runtime -and $runtime.ssrOutcomesAll -gt 0) {
        $runtime.ssrCacheHitPct = [math]::Round(100 * $runtime.ssrOutcomesCache / $runtime.ssrOutcomesAll, 1)
    }

    $result = [ordered]@{
        app = $App
        version = $Version
        label = $Label
        profile = $Profile
        rpsTarget = if ($Profile -eq 'rps') { $Rps } else { $null }
        vus = if ($Profile -ne 'rps') { $Vus } else { $null }
        duration = $Duration
        ssrCacheSeconds = $SsrCacheSeconds
        ssrStaleSeconds = $SsrStaleSeconds
        routesFilter = $Routes
        measuredAt = (Get-Date).ToString('o')
        k6Exit = $k6Exit
        load = $load
        process = $process
        runtime = $runtime
        db = $db
        logs = [ordered]@{ app = $appLog; counters = $countersCsv }
    }
    $result | ConvertTo-Json -Depth 10 | Set-Content -Path $summaryJson -Encoding utf8
    Write-Host "Wrote $summaryJson"
    $result | ConvertTo-Json -Depth 4
}
finally {
    Stop-App $p.Id
    Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue
}
