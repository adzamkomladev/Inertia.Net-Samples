param(
    [Parameter(Mandatory = $true)][ValidateSet('CarParker', 'CarParkerReplicas', 'TechLandier', 'TechLandierDistributed')][string]$Variant,
    [Parameter(Mandatory = $true)][string]$Name,
    [switch]$Auth,
    [ValidateSet('rps', 'vus')][string]$Profile = 'rps',
    [int]$Rps = 100,
    [int]$Vus = 8,
    [string]$Duration = '30s',
    [string]$Routes = '',
    [switch]$ViaProxy,
    [switch]$NoCounters
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$statePath = Join-Path $PSScriptRoot "state\$Variant.json"
if (-not (Test-Path $statePath)) { throw "No state for $Variant; start the stack first." }
$state = Get-Content $statePath -Raw | ConvertFrom-Json
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $PSScriptRoot "results\$Variant\$Name-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$k6 = Join-Path $env:LOCALAPPDATA 'bench\k6-v1.3.0-windows-amd64\k6.exe'
if (-not (Test-Path $k6)) { throw "k6 not found at $k6" }
$countersExe = Join-Path $env:USERPROFILE '.dotnet\tools\dotnet-counters.exe'
$gcdumpExe = Join-Path $env:USERPROFILE '.dotnet\tools\dotnet-gcdump.exe'
$appName = if ($Variant -like 'TechLandier*') { 'techlandier' } else { 'carparker' }
$durationSeconds = if ($Duration -match '^(\d+)s$') { [int]$matches[1] } else { 30 }
$sampleSeconds = $durationSeconds + 8

function Get-ChildPids([int]$parent, [string]$namePattern) {
    return @(Get-CimInstance Win32_Process -Filter "ParentProcessId=$parent" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match $namePattern } | ForEach-Object { [int]$_.ProcessId })
}

$ssrPids = @()
if ($state.ssrPids) { $ssrPids = @($state.ssrPids | ForEach-Object { [int]$_ }) }
else {
    foreach ($pidValue in $state.appPids) {
        $ssrPids += Get-ChildPids ([int]$pidValue) '^bun(\.exe)?$'
    }
}
$appPids = @($state.appPids | ForEach-Object { [int]$_ })
$auxPids = @()
if ($state.auxPids) { $auxPids = @($state.auxPids | ForEach-Object { [int]$_ }) }

$specPath = Join-Path $outDir 'pids.json'
[ordered]@{ appPids = $appPids; ssrPids = $ssrPids; auxPids = $auxPids } | ConvertTo-Json | Set-Content $specPath -Encoding utf8

Write-Output "Measuring $Variant / $Name ($Profile rps=$Rps vus=$Vus duration=$Duration auth=$([bool]$Auth))"
Write-Output "apps=$($appPids -join ',') ssr=$($ssrPids -join ',') aux=$($auxPids -join ',')"

# --- snapshots -------------------------------------------------------------
$before = Join-Path $outDir 'before.json'
$after = Join-Path $outDir 'after.json'

# --- process sampler -------------------------------------------------------
$samplerOut = Join-Path $outDir 'process.json'
$samplerErr = Join-Path $outDir 'process.err.log'
$sampler = Start-Process -FilePath 'pwsh' -ArgumentList @(
    '-NoProfile', '-File', (Join-Path $PSScriptRoot 'sample-stack.ps1'),
    '-SpecPath', $specPath, '-DurationSeconds', "$sampleSeconds", '-OutputPath', $samplerOut
) -PassThru -RedirectStandardOutput (Join-Path $outDir 'process.out.log') -RedirectStandardError $samplerErr

# --- dotnet-counters -------------------------------------------------------
$counterProcs = @()
if (-not $NoCounters) {
    foreach ($pidValue in ($appPids + $auxPids)) {
        $csv = Join-Path $outDir "counters-$pidValue.csv"
        $err = Join-Path $outDir "counters-$pidValue.err.log"
        $args = @(
            'collect', '--process-id', "$pidValue",
            '--counters', 'System.Runtime,Microsoft.AspNetCore.Hosting,Inertia.Net',
            '--format', 'csv', '--output', $csv,
            '--duration', "00:00:$sampleSeconds"
        )
        $counterProcs += Start-Process -FilePath $countersExe -ArgumentList $args -PassThru `
            -RedirectStandardOutput (Join-Path $outDir "counters-$pidValue.out.log") -RedirectStandardError $err
    }
}
Start-Sleep -Milliseconds 1500
& (Join-Path $PSScriptRoot 'snapshot.ps1') -StatePath $statePath -OutputPath $before

# --- k6 --------------------------------------------------------------------
$summaryPath = Join-Path $outDir 'k6.json'
$k6Log = Join-Path $outDir 'k6.log'
$baseUrls = if ($ViaProxy -or -not $state.appPorts) { @($state.baseUrl) } else { @($state.appPorts | ForEach-Object { "http://127.0.0.1:$($_)" }) }
$k6Env = @{
    BASE_URL = $baseUrls[0]
    BASE_URLS = ($baseUrls -join ',')
    APP = $appName
    PROFILE = $Profile
    RPS = "$Rps"
    VUS = "$Vus"
    DURATION = $Duration
    SUMMARY_PATH = $summaryPath
}
if ($Routes) { $k6Env.ROUTES = $Routes }
$script = if ($Auth) { Join-Path $root 'benchmarks\auth-load.js' } else { Join-Path $root 'benchmarks\load.js' }
$k6Proc = Start-Process -FilePath $k6 -ArgumentList @('run', $script) -Environment $k6Env -PassThru `
    -RedirectStandardOutput $k6Log -RedirectStandardError (Join-Path $outDir 'k6.err.log')
$k6Proc.WaitForExit()
$k6Exit = $k6Proc.ExitCode
& (Join-Path $PSScriptRoot 'snapshot.ps1') -StatePath $statePath -OutputPath $after

# Let the last counter sample land, then stop the collectors.
Start-Sleep -Seconds 2
foreach ($p in $counterProcs) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
$sampler.WaitForExit()

# --- parse counters --------------------------------------------------------
function Read-CounterFile([string]$path) {
    if (-not (Test-Path $path)) { return $null }
    $rows = Import-Csv $path
    if (-not $rows) { return $null }
    function Values([string]$pattern) {
        @($rows | Where-Object { $_.'Counter Name' -like $pattern } | ForEach-Object { [double]$_.'Mean/Increment' })
    }
    function Avg([string]$pattern) {
        $v = Values $pattern
        if ($v.Count -eq 0) { return $null }
        [math]::Round(($v | Measure-Object -Average).Average, 4)
    }
    function Max([string]$pattern) {
        $v = Values $pattern
        if ($v.Count -eq 0) { return $null }
        [math]::Round(($v | Measure-Object -Maximum).Maximum, 4)
    }
    function Sum([string]$pattern) {
        $v = Values $pattern
        if ($v.Count -eq 0) { return $null }
        [math]::Round(($v | Measure-Object -Sum).Sum, 4)
    }
    function Last([string]$pattern) {
        $matched = @($rows | Where-Object { $_.'Counter Name' -like $pattern })
        if ($matched.Count -eq 0) { return $null }
        return [double]$matched[$matched.Count - 1].'Mean/Increment'
    }

    $heapMb = 0.0
    foreach ($gen in @('gen0', 'gen1', 'gen2', 'loh', 'poh')) {
        $v = Last "dotnet.gc.last_collection.heap.size*generation=$gen*"
        if ($null -ne $v) { $heapMb += $v / 1MB }
    }
    $committedMb = Last 'dotnet.gc.last_collection.memory.committed_size*'
    $ssrCache = Sum 'inertia.ssr.outcomes*reason=cache*'
    $ssrRendered = Sum 'inertia.ssr.outcomes*reason=rendered*'
    if ($null -eq $ssrRendered) { $ssrRendered = Sum 'inertia.ssr.outcomes*reason=ok*' }
    if ($null -eq $ssrRendered) { $ssrRendered = 0 }
    if ($null -eq $ssrCache) { $ssrCache = 0 }
    $ssrAll = Sum 'inertia.ssr.outcomes*'
    $pageP50 = Avg 'inertia.page.duration*Percentile=50*'
    $pageP95 = Avg 'inertia.page.duration*Percentile=95*'
    $pageP99 = Avg 'inertia.page.duration*Percentile=99*'
    $props = [ordered]@{}
    foreach ($row in ($rows | Where-Object { $_.'Counter Name' -like 'inertia.props*' })) {
        $key = $row.'Counter Name'
        if (-not $props.Contains($key)) { $props[$key] = $null }
    }
    foreach ($key in @($props.Keys)) {
        $props[$key] = Sum $key
    }
    return [ordered]@{
        allocRateMbPerSec = if ($null -ne (Avg 'dotnet.gc.heap.total_allocated*')) { [math]::Round((Avg 'dotnet.gc.heap.total_allocated*') / 1MB, 2) } else { $null }
        workingSetMb = if ($null -ne (Last 'dotnet.process.memory.working_set*')) { [math]::Round((Last 'dotnet.process.memory.working_set*') / 1MB, 2) } else { $null }
        heapMb = [math]::Round($heapMb, 2)
        committedMb = if ($null -ne $committedMb) { [math]::Round($committedMb / 1MB, 2) } else { $null }
        gcPausePct = if ($null -ne (Avg 'dotnet.gc.pause.time*')) { [math]::Round((Avg 'dotnet.gc.pause.time*') * 100, 2) } else { $null }
        gen0PerSec = Avg 'dotnet.gc.collections*generation=gen0*'
        gen1PerSec = Avg 'dotnet.gc.collections*generation=gen1*'
        gen2PerSec = Avg 'dotnet.gc.collections*generation=gen2*'
        threadPoolMax = Max 'dotnet.thread_pool.thread.count*'
        threadPoolQueueMax = Max 'dotnet.thread_pool.queue.length*'
        exceptionsPerSec = if ($null -ne (Avg 'dotnet.exceptions*')) { Avg 'dotnet.exceptions*' } else { 0 }
        ssrCacheHits = $ssrCache
        ssrRenders = $ssrRendered
        ssrOutcomes = $ssrAll
        ssrCacheHitPct = if ($ssrAll -gt 0) { [math]::Round(100 * $ssrCache / $ssrAll, 1) } else { $null }
        pageP50Ms = if ($null -ne $pageP50) { [math]::Round($pageP50 * 1000, 2) } else { $null }
        pageP95Ms = if ($null -ne $pageP95) { [math]::Round($pageP95 * 1000, 2) } else { $null }
        pageP99Ms = if ($null -ne $pageP99) { [math]::Round($pageP99 * 1000, 2) } else { $null }
        props = $props
    }
}

$runtimeProcesses = @()
foreach ($pidValue in ($appPids + $auxPids)) {
    $summary = Read-CounterFile (Join-Path $outDir "counters-$pidValue.csv")
    if ($summary) {
        $runtimeProcesses += [ordered]@{ pid = $pidValue; role = if ($appPids -contains $pidValue) { 'app' } else { 'aux' }; values = $summary }
    }
}
$appRuntime = @($runtimeProcesses | Where-Object { $_.role -eq 'app' })
$runtimeAggregate = [ordered]@{
    allocRateMbPerSec = [math]::Round((($appRuntime | ForEach-Object { $_.values.allocRateMbPerSec } | Measure-Object -Sum).Sum), 2)
    heapMb = [math]::Round((($appRuntime | ForEach-Object { $_.values.heapMb } | Measure-Object -Sum).Sum), 2)
    committedMb = [math]::Round((($appRuntime | ForEach-Object { $_.values.committedMb } | Measure-Object -Sum).Sum), 2)
    workingSetMb = [math]::Round((($appRuntime | ForEach-Object { $_.values.workingSetMb } | Measure-Object -Sum).Sum), 2)
    gcPausePct = [math]::Round((($appRuntime | ForEach-Object { $_.values.gcPausePct } | Measure-Object -Average).Average), 2)
    gen0PerSec = [math]::Round((($appRuntime | ForEach-Object { $_.values.gen0PerSec } | Measure-Object -Sum).Sum), 2)
    gen1PerSec = [math]::Round((($appRuntime | ForEach-Object { $_.values.gen1PerSec } | Measure-Object -Sum).Sum), 2)
    gen2PerSec = [math]::Round((($appRuntime | ForEach-Object { $_.values.gen2PerSec } | Measure-Object -Sum).Sum), 2)
    threadPoolMax = (($appRuntime | ForEach-Object { $_.values.threadPoolMax } | Measure-Object -Maximum).Maximum)
    ssrCacheHits = [math]::Round((($appRuntime | ForEach-Object { $_.values.ssrCacheHits } | Measure-Object -Sum).Sum), 2)
    ssrRenders = [math]::Round((($appRuntime | ForEach-Object { $_.values.ssrRenders } | Measure-Object -Sum).Sum), 2)
    ssrCacheHitPct = $null
    pageP50Ms = [math]::Round((($appRuntime | ForEach-Object { $_.values.pageP50Ms } | Measure-Object -Average).Average), 2)
    pageP95Ms = [math]::Round((($appRuntime | ForEach-Object { $_.values.pageP95Ms } | Measure-Object -Average).Average), 2)
    pageP99Ms = [math]::Round((($appRuntime | ForEach-Object { $_.values.pageP99Ms } | Measure-Object -Average).Average), 2)
}
if ($runtimeAggregate.ssrCacheHits + $runtimeAggregate.ssrRenders -gt 0) {
    $runtimeAggregate.ssrCacheHitPct = [math]::Round(100 * $runtimeAggregate.ssrCacheHits / ($runtimeAggregate.ssrCacheHits + $runtimeAggregate.ssrRenders), 1)
}

# --- parse process sampler -------------------------------------------------
$process = if (Test-Path $samplerOut) { Get-Content $samplerOut -Raw | ConvertFrom-Json } else { $null }
function Summarize-Group($group) {
    if (-not $group -or -not $group.processes -or $group.processes.Count -eq 0) { return $null }
    $ws = @($group.processes | ForEach-Object { $_.workingSetMb.avg })
    $wsMax = @($group.processes | ForEach-Object { $_.workingSetMb.max })
    $priv = @($group.processes | ForEach-Object { $_.privateMb.avg })
    $cpu = @($group.processes | ForEach-Object { $_.cpuPctAvg })
    return [ordered]@{
        processes = $group.processes
        count = $group.processes.Count
        workingSetMbAvg = [math]::Round(($ws | Measure-Object -Sum).Sum, 2)
        workingSetMbMax = [math]::Round(($wsMax | Measure-Object -Sum).Sum, 2)
        privateMbAvg = [math]::Round(($priv | Measure-Object -Sum).Sum, 2)
        cpuPctAvg = [math]::Round(($cpu | Measure-Object -Sum).Sum, 2)
        threadsLast = [math]::Round(($group.processes | ForEach-Object { $_.threadsLast } | Measure-Object -Sum).Sum, 0)
    }
}
$processSummary = $null
if ($process) {
    $processSummary = [ordered]@{
        app = Summarize-Group ($process.groups | Where-Object { $_.role -eq 'app' })
        ssr = Summarize-Group ($process.groups | Where-Object { $_.role -eq 'ssr' })
        aux = Summarize-Group ($process.groups | Where-Object { $_.role -eq 'aux' })
    }
}

# --- deltas ----------------------------------------------------------------
$beforeObj = Get-Content $before -Raw | ConvertFrom-Json
$afterObj = Get-Content $after -Raw | ConvertFrom-Json
function Safe([object]$value) {
    if ($null -eq $value) { return 0 }
    if ($value -is [pscustomobject] -and $null -ne $value.PSObject.Properties['low']) {
        return [double]$value.low + ([double]$value.high * 4294967296)
    }
    return [double]$value
}

$db = [ordered]@{ mongo = $null; postgres = @(); pgdog = $null }
if ($beforeObj.mongo -and $afterObj.mongo) {
    $b = $beforeObj.mongo.opcounters; $a = $afterObj.mongo.opcounters
    $bwt = $beforeObj.mongo.wiredTiger; $awt = $afterObj.mongo.wiredTiger
    $db.mongo = [ordered]@{
        queryDelta = Safe($a.query) - Safe($b.query)
        commandDelta = Safe($a.command) - Safe($b.command)
        getMoreDelta = Safe($a.getmore) - Safe($b.getmore)
        insertDelta = Safe($a.insert) - Safe($b.insert)
        updateDelta = Safe($a.update) - Safe($b.update)
        deleteDelta = Safe($a.delete) - Safe($b.delete)
        pagesRequestedDelta = Safe($awt.pagesRequestedFromCache) - Safe($bwt.pagesRequestedFromCache)
        pagesReadDelta = Safe($awt.pagesReadIntoCache) - Safe($bwt.pagesReadIntoCache)
        cacheBytesReadDelta = Safe($awt.cacheBytesReadInto) - Safe($bwt.cacheBytesReadInto)
        cacheBytesWrittenDelta = Safe($awt.cacheBytesWrittenFrom) - Safe($bwt.cacheBytesWrittenFrom)
        cacheBytesInCache = Safe($awt.cacheBytesInCache)
        cacheMaxBytes = Safe($awt.cacheMaxBytes)
        cacheHitPct = if ((Safe($awt.pagesRequestedFromCache) - Safe($bwt.pagesRequestedFromCache)) -gt 0) {
            [math]::Round(100 * (1 - ((Safe($awt.pagesReadIntoCache) - Safe($bwt.pagesReadIntoCache)) / (Safe($awt.pagesRequestedFromCache) - Safe($bwt.pagesRequestedFromCache)))), 2)
        } else { $null }
    }
}
if ($beforeObj.postgres -and $afterObj.postgres) {
    $labels = @($beforeObj.postgres | ForEach-Object { $_.label })
    foreach ($label in $labels) {
        $bNode = $beforeObj.postgres | Where-Object { $_.label -eq $label }
        $aNode = $afterObj.postgres | Where-Object { $_.label -eq $label }
        $bDb = $bNode.databases | Where-Object { $_.datname -eq 'techlandier' }
        $aDb = $aNode.databases | Where-Object { $_.datname -eq 'techlandier' }
        if (-not $bDb -or -not $aDb) { continue }
        $bT = $bNode.userTables; $aT = $aNode.userTables
        $hits = Safe($aDb.blksHit) - Safe($bDb.blksHit)
        $reads = Safe($aDb.blksRead) - Safe($bDb.blksRead)
        $db.postgres += [ordered]@{
            label = $label
            xactCommitDelta = Safe($aDb.xactCommit) - Safe($bDb.xactCommit)
            xactRollbackDelta = Safe($aDb.xactRollback) - Safe($bDb.xactRollback)
            blksHitDelta = $hits
            blksReadDelta = $reads
            blockCacheHitPct = if ($hits + $reads -gt 0) { [math]::Round(100 * $hits / ($hits + $reads), 2) } else { $null }
            tupReturnedDelta = Safe($aDb.tupReturned) - Safe($bDb.tupReturned)
            tupFetchedDelta = Safe($aDb.tupFetched) - Safe($bDb.tupFetched)
            tupInsertedDelta = Safe($aDb.tupInserted) - Safe($bDb.tupInserted)
            tupUpdatedDelta = Safe($aDb.tupUpdated) - Safe($bDb.tupUpdated)
            tupDeletedDelta = Safe($aDb.tupDeleted) - Safe($bDb.tupDeleted)
            seqScanDelta = Safe($aT.seqScan) - Safe($bT.seqScan)
            idxScanDelta = Safe($aT.idxScan) - Safe($bT.idxScan)
            nDeadTup = Safe($aT.nDeadTup)
        }
    }
}
if ($beforeObj.pgdog -and $afterObj.pgdog) {
    $db.pgdog = [ordered]@{
        totalReadsDelta = Safe($afterObj.pgdog.total_reads) - Safe($beforeObj.pgdog.total_reads)
        totalWritesDelta = Safe($afterObj.pgdog.total_writes) - Safe($beforeObj.pgdog.total_writes)
        totalXactDelta = Safe($afterObj.pgdog.total_xact_count) - Safe($beforeObj.pgdog.total_xact_count)
        totalQueryDelta = Safe($afterObj.pgdog.total_query_count) - Safe($beforeObj.pgdog.total_query_count)
        clients = Safe($afterObj.pgdog.clients)
        clWaiting = Safe($afterObj.pgdog.cl_waiting)
        errors = Safe($afterObj.pgdog.errors)
    }
}

function Redis-Delta($b, $a) {
    if (-not $b -or -not $a) { return $null }
    $commands = [ordered]@{}
    $names = @($b.commandStats.PSObject.Properties.Name) + @($a.commandStats.PSObject.Properties.Name)
    foreach ($name in ($names | Select-Object -Unique)) {
        $bc = if ($b.commandStats.$name) { Safe($b.commandStats.$name.calls) } else { 0 }
        $ac = if ($a.commandStats.$name) { Safe($a.commandStats.$name.calls) } else { 0 }
        if ($ac - $bc -gt 0) { $commands[$name] = $ac - $bc }
    }
    $hits = Safe($a.keyspaceHits) - Safe($b.keyspaceHits)
    $misses = Safe($a.keyspaceMisses) - Safe($b.keyspaceMisses)
    return [ordered]@{
        commandDelta = Safe($a.totalCommandsProcessed) - Safe($b.totalCommandsProcessed)
        keyspaceHitsDelta = $hits
        keyspaceMissesDelta = $misses
        hitRatePct = if ($hits + $misses -gt 0) { [math]::Round(100 * $hits / ($hits + $misses), 2) } else { $null }
        expiredKeysDelta = Safe($a.expiredKeys) - Safe($b.expiredKeys)
        evictedKeysDelta = Safe($a.evictedKeys) - Safe($b.evictedKeys)
        usedMemoryMb = if ($a.usedMemory) { [math]::Round([double]$a.usedMemory / 1MB, 2) } else { $null }
        usedMemoryPeakMb = if ($a.usedMemoryPeak) { [math]::Round([double]$a.usedMemoryPeak / 1MB, 2) } else { $null }
        dbSize = if ($a.dbSize) { [int]$a.dbSize } else { $null }
        commands = $commands
    }
}
$cache = [ordered]@{ redis = $null; cacheRedis = $null }
if ($beforeObj.redis -and $afterObj.redis) { $cache.redis = Redis-Delta $beforeObj.redis $afterObj.redis }
if ($beforeObj.cacheRedis -and $afterObj.cacheRedis) { $cache.cacheRedis = Redis-Delta $beforeObj.cacheRedis $afterObj.cacheRedis }

$load = Get-Content $summaryPath -Raw | ConvertFrom-Json
$requests = if ($load.requests) { [double]$load.requests } else { 0 }
$dbPerRequest = $null
if ($db.mongo -and $requests -gt 0) {
    $dbPerRequest = [ordered]@{
        mongoOpsPerRequest = [math]::Round((Safe($db.mongo.queryDelta) + Safe($db.mongo.commandDelta) + Safe($db.mongo.getMoreDelta) + Safe($db.mongo.insertDelta) + Safe($db.mongo.updateDelta) + Safe($db.mongo.deleteDelta)) / $requests, 3)
        mongoQueriesPerRequest = [math]::Round((Safe($db.mongo.queryDelta) + Safe($db.mongo.getMoreDelta)) / $requests, 3)
    }
}
if ($db.postgres -and $requests -gt 0) {
    $primary = $db.postgres | Where-Object { $_.label -eq 'primary' }
    if ($primary) {
        $dbPerRequest = [ordered]@{
            pgXactsPerRequest = [math]::Round($primary.xactCommitDelta / $requests, 3)
            pgTuplesFetchedPerRequest = [math]::Round($primary.tupFetchedDelta / $requests, 3)
            pgTuplesReturnedPerRequest = [math]::Round($primary.tupReturnedDelta / $requests, 3)
        }
    }
}

$result = [ordered]@{
    kind = 'replica-comparison-phase'
    variant = $Variant
    name = $Name
    authenticated = [bool]$Auth
    profile = $Profile
    rpsTarget = if ($Profile -eq 'rps') { $Rps } else { $null }
    vus = if ($Profile -eq 'vus') { $Vus } else { $null }
    duration = $Duration
    baseUrls = $baseUrls
    measuredAt = (Get-Date).ToString('o')
    k6Exit = $k6Exit
    load = $load
    process = $processSummary
    runtime = [ordered]@{ processes = $runtimeProcesses; aggregate = $runtimeAggregate }
    db = $db
    dbPerRequest = $dbPerRequest
    cache = $cache
    docker = [ordered]@{ before = $beforeObj.docker; after = $afterObj.docker }
    files = [ordered]@{
        before = $before; after = $after; k6 = $summaryPath; process = $samplerOut
        counters = @($runtimeProcesses | ForEach-Object { Join-Path $outDir "counters-$($_.pid).csv" })
    }
}
$resultPath = Join-Path $outDir 'result.json'
$result | ConvertTo-Json -Depth 16 | Set-Content $resultPath -Encoding utf8
Write-Output "Wrote $resultPath"
Write-Output ("requests={0} rps={1:N1} p50={2:N1} p95={3:N1} p99={4:N1} failed={5:P2} alloc={6:N1}MB/s heap={7:N1}MB ssrCache={8}" -f `
    $load.requests, $load.rps, $load.latencyMs.p50, $load.latencyMs.p95, $load.latencyMs.p99, $load.failed, `
    $runtimeAggregate.allocRateMbPerSec, $runtimeAggregate.heapMb, $runtimeAggregate.ssrCacheHitPct)
