param(
    [Parameter(Mandatory = $true)][string]$StatePath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$state = Get-Content $StatePath -Raw | ConvertFrom-Json
$snapshot = [ordered]@{
    at = (Get-Date).ToString('o')
    mongo = $null
    redis = $null
    cacheRedis = $null
    postgres = @()
    pgdog = $null
    docker = @()
}

function Invoke-Docker([string[]]$arguments) {
    $output = & docker @arguments 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return ($output -join "`n")
}

if ($state.mongo) {
    $js = @'
const s = db.serverStatus();
const wt = s.wiredTiger && s.wiredTiger.cache ? s.wiredTiger.cache : {};
const num = (v) => v === null || v === undefined ? null : Number(v);
const o = s.opcounters;
const out = {
  opcounters: {
    insert: num(o.insert), query: num(o.query), update: num(o.update),
    delete: num(o.delete), getmore: num(o.getmore), command: num(o.command)
  },
  opcountersRepl: s.opcountersRepl ? {
    insert: num(s.opcountersRepl.insert), update: num(s.opcountersRepl.update),
    delete: num(s.opcountersRepl.delete), command: num(s.opcountersRepl.command)
  } : null,
  mem: s.mem,
  connections: s.connections,
  uptime: num(s.uptime),
  wiredTiger: {
    cacheBytesInCache: num(wt["bytes currently in the cache"]),
    cacheMaxBytes: num(wt["maximum bytes configured"]),
    cacheBytesReadInto: num(wt["bytes read into cache"]),
    cacheBytesWrittenFrom: num(wt["bytes written from cache"]),
    pagesRequestedFromCache: num(wt["pages requested from the cache"]),
    pagesReadIntoCache: num(wt["pages read into cache"])
  },
  db: db.stats()
};
print(JSON.stringify(out));
'@
    $raw = Invoke-Docker @('exec', $state.mongo, 'mongosh', '--quiet', '--eval', $js)
    if ($raw) { $snapshot.mongo = $raw | ConvertFrom-Json }
}

function Read-Redis([string]$container) {
    if (-not $container) { return $null }
    $info = Invoke-Docker @('exec', $container, 'redis-cli', 'INFO')
    if (-not $info) { return $null }
    $values = [ordered]@{}
    foreach ($line in ($info -split "`r?`n")) {
        if ($line -match '^([^:#]+):(.+)$') { $values[$matches[1]] = $matches[2] }
    }
    $commandStats = [ordered]@{}
    $cmdInfo = Invoke-Docker @('exec', $container, 'redis-cli', 'INFO', 'commandstats')
    if ($cmdInfo) {
        foreach ($line in ($cmdInfo -split "`r?`n")) {
            if ($line -match '^cmdstat_([^:]+):calls=([0-9]+),usec=([0-9]+)') {
                $commandStats[$matches[1]] = [ordered]@{ calls = [long]$matches[2]; usec = [long]$matches[3] }
            }
        }
    }
    return [ordered]@{
        usedMemory = $values['used_memory']
        usedMemoryHuman = $values['used_memory_human']
        usedMemoryPeak = $values['used_memory_peak']
        maxMemory = $values['maxmemory']
        maxMemoryPolicy = $values['maxmemory_policy']
        totalCommandsProcessed = [long]$values['total_commands_processed']
        totalConnectionsReceived = [long]$values['total_connections_received']
        keyspaceHits = [long]$values['keyspace_hits']
        keyspaceMisses = [long]$values['keyspace_misses']
        expiredKeys = [long]$values['expired_keys']
        evictedKeys = [long]$values['evicted_keys']
        instantaneousOpsPerSec = [long]$values['instantaneous_ops_per_sec']
        keyspace = $values['keyspace'] ?? ''
        dbSize = (Invoke-Docker @('exec', $container, 'redis-cli', 'DBSIZE'))
        commandStats = $commandStats
    }
}

if ($state.redis) { $snapshot.redis = Read-Redis $state.redis }
if ($state.cacheRedis) { $snapshot.cacheRedis = Read-Redis $state.cacheRedis }

function Read-Postgres([string]$label, [string]$container, [int]$port) {
    $query = @'
select datname, xact_commit, xact_rollback, blks_read, blks_hit, tup_returned, tup_fetched, tup_inserted, tup_updated, tup_deleted, conflicts, temp_files, temp_bytes, deadlocks from pg_stat_database order by datname;
'@
    $tableQuery = @'
select coalesce(sum(seq_scan),0), coalesce(sum(seq_tup_read),0), coalesce(sum(idx_scan),0), coalesce(sum(idx_tup_fetch),0), coalesce(sum(n_tup_ins),0), coalesce(sum(n_tup_upd),0), coalesce(sum(n_tup_del),0), coalesce(sum(n_live_tup),0), coalesce(sum(n_dead_tup),0) from pg_stat_user_tables;
'@
    $bgQuery = @'
select coalesce(sum(checkpoints_timed),0), coalesce(sum(checkpoints_req),0), coalesce(sum(buffers_checkpoint),0), coalesce(sum(buffers_clean),0), coalesce(sum(buffers_backend),0), coalesce(sum(buffers_alloc),0) from pg_stat_bgwriter;
'@
    $env:PGPASSWORD = 'local-dev-password'
    if ($container) {
        $db = Invoke-Docker @('exec', $container, 'psql', '-U', 'techlandier', '-d', 'techlandier', '-t', '-A', '-F', '|', '-c', $query)
        $tables = Invoke-Docker @('exec', $container, 'psql', '-U', 'techlandier', '-d', 'techlandier', '-t', '-A', '-F', '|', '-c', $tableQuery)
        $bg = Invoke-Docker @('exec', $container, 'psql', '-U', 'techlandier', '-d', 'techlandier', '-t', '-A', '-F', '|', '-c', $bgQuery)
    } else {
        $db = & psql -h localhost -p $port -U techlandier -d techlandier -t -A -F '|' -c $query 2>$null
        $tables = & psql -h localhost -p $port -U techlandier -d techlandier -t -A -F '|' -c $tableQuery 2>$null
        $bg = & psql -h localhost -p $port -U techlandier -d techlandier -t -A -F '|' -c $bgQuery 2>$null
    }
    if (-not $db) { return $null }
    $dbRows = @()
    foreach ($line in ($db -split "`r?`n")) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $p = $line -split '\|'
        if ($p.Count -lt 14) { continue }
        $dbRows += [ordered]@{
            datname = $p[0]; xactCommit = [long]$p[1]; xactRollback = [long]$p[2]
            blksRead = [long]$p[3]; blksHit = [long]$p[4]; tupReturned = [long]$p[5]; tupFetched = [long]$p[6]
            tupInserted = [long]$p[7]; tupUpdated = [long]$p[8]; tupDeleted = [long]$p[9]
            conflicts = [long]$p[10]; tempFiles = [long]$p[11]; tempBytes = [long]$p[12]; deadlocks = [long]$p[13]
        }
    }
    $tp = ($tables -split '\|')
    $bp = ($bg -split '\|')
    return [ordered]@{
        label = $label
        databases = $dbRows
        userTables = if ($tp.Count -ge 9) { [ordered]@{
            seqScan = [long]$tp[0]; seqTupRead = [long]$tp[1]; idxScan = [long]$tp[2]; idxTupFetch = [long]$tp[3]
            nTupIns = [long]$tp[4]; nTupUpd = [long]$tp[5]; nTupDel = [long]$tp[6]; nLiveTup = [long]$tp[7]; nDeadTup = [long]$tp[8]
        } } else { $null }
        bgwriter = if ($bp.Count -ge 6) { [ordered]@{
            checkpointsTimed = [long]$bp[0]; checkpointsReq = [long]$bp[1]; buffersCheckpoint = [long]$bp[2]
            buffersClean = [long]$bp[3]; buffersBackend = [long]$bp[4]; buffersAlloc = [long]$bp[5]
        } } else { $null }
    }
}

if ($state.pgPrimary) { $snapshot.postgres += Read-Postgres 'primary' $null 5442 }
if ($state.pgReplicas) {
    $index = 1
    foreach ($container in $state.pgReplicas) {
        $snapshot.postgres += Read-Postgres "replica-$index" $container 0
        $index++
    }
}

if ($state.pgdog) {
    try {
        $metrics = (Invoke-WebRequest -UseBasicParsing -Uri 'http://127.0.0.1:9090/metrics' -TimeoutSec 5).Content
        $wanted = @('total_reads', 'total_writes', 'total_xact_count', 'total_query_count', 'clients', 'cl_waiting', 'errors', 'total_prepared_statements')
        $values = [ordered]@{}
        foreach ($line in ($metrics -split "`r?`n")) {
            if ($line.StartsWith('#')) { continue }
            foreach ($name in $wanted) {
                if ($line -match "^$name(?:\{[^}]*\})?\s+([0-9.eE+-]+)$") { $values[$name] = [double]$matches[1] }
                if ($line -match "^pgdog_$name(?:\{[^}]*\})?\s+([0-9.eE+-]+)$") { $values[$name] = [double]$matches[1] }
            }
        }
        $snapshot.pgdog = $values
    } catch { $snapshot.pgdog = $null }
}

if ($state.containers -and $state.containers.Count -gt 0) {
    $raw = & docker stats --no-stream --format '{{json .}}' @($state.containers) 2>$null
    foreach ($line in $raw) {
        if (-not $line) { continue }
        try { $snapshot.docker += ($line | ConvertFrom-Json) } catch { }
    }
}

$snapshot | ConvertTo-Json -Depth 12 | Set-Content -Path $OutputPath -Encoding utf8
