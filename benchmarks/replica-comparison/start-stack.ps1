param(
    [Parameter(Mandatory = $true)][ValidateSet('CarParker', 'CarParkerReplicas', 'TechLandier', 'TechLandierDistributed')][string]$Variant,
    [int]$SsrCacheSeconds = 300,
    [int]$SsrStaleSeconds = 60
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifacts = Join-Path $root 'benchmarks\artifacts\perf'
$stateDir = Join-Path $PSScriptRoot 'state'
$logDir = Join-Path $PSScriptRoot 'logs'
New-Item -ItemType Directory -Force -Path $stateDir, $logDir | Out-Null
$statePath = Join-Path $stateDir "$Variant.json"

function Stop-Tracked([string]$path) {
    if (-not (Test-Path $path)) { return }
    $old = Get-Content $path -Raw | ConvertFrom-Json
    $pids = @()
    if ($old.appPids) { $pids += $old.appPids }
    if ($old.ssrPids) { $pids += $old.ssrPids }
    if ($old.auxPids) { $pids += $old.auxPids }
    foreach ($pidValue in $pids) {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$pidValue" -ErrorAction SilentlyContinue |
            ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
        Stop-Process -Id $pidValue -Force -ErrorAction SilentlyContinue
    }
    if ($old.proxy) { docker rm -f $old.proxy 2>$null | Out-Null }
    Remove-Item $path -Force -ErrorAction SilentlyContinue
}
Stop-Tracked $statePath

# Stray Bun processes from a previous interrupted run.
Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^bun(\.exe)?$' -and $_.CommandLine -match 'benchmarks\\artifacts\\perf\\.*\\ssr[/\\]ssr\.js' } |
    ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }

$baseEnv = @{
    ASPNETCORE_ENVIRONMENT = 'Production'
    DOTNET_gcServer = '0'
    Logging__LogLevel__Default = 'Warning'
    Logging__LogLevel__Microsoft = 'Warning'
    Logging__LogLevel__System = 'Warning'
    APP_BASE_URL = 'http://127.0.0.1:8199'
    ANALYTICS_SALT = 'bench'
    CRON_ENABLED = 'false'
    JOBS_ENABLED = 'false'
    AUTH_SECURE_COOKIES = 'false'
    Inertia__SsrCacheSeconds = "$SsrCacheSeconds"
    Inertia__SsrStaleSeconds = "$SsrStaleSeconds"
}

function Start-App([string]$dll, [string]$dir, [int]$port, [string]$name, [hashtable]$extra) {
    $envMap = $baseEnv.Clone()
    $envMap.ASPNETCORE_URLS = "http://127.0.0.1:$port"
    $envMap.DATA_DIR = (Join-Path $dir "storage-$name")
    foreach ($key in $extra.Keys) { $envMap[$key] = $extra[$key] }
    $out = Join-Path $logDir "$Variant-$name.out.log"
    $err = Join-Path $logDir "$Variant-$name.err.log"
    $p = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory $dir -Environment $envMap `
        -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
    return $p.Id
}

function Wait-Health([int]$port, [string]$path = '/health', [int]$seconds = 150) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 750
        try {
            $r = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port$path" -TimeoutSec 3
            if ($r.StatusCode -eq 200) { return $true }
        } catch { }
    }
    return $false
}

function Assert-Healthy([int]$port, [string]$label, [string]$path = '/health') {
    if (-not (Wait-Health $port $path)) {
        Write-Output "$label did not become healthy"
        Get-ChildItem $logDir -Filter "$Variant-$label*.log" -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Output "--- $($_.Name) ---"
            Get-Content $_.FullName -Tail 30
        }
        exit 1
    }
}

$state = [ordered]@{
    variant = $Variant
    startedAt = (Get-Date).ToString('o')
    baseUrl = ''
    appPorts = @()
    appPids = @()
    ssrPids = @()
    auxPids = @()
    proxy = $null
    mongo = $null
    redis = $null
    cacheRedis = $null
    pgPrimary = $null
    pgReplicas = @()
    pgdog = $false
    containers = @()
}

switch ($Variant) {
    'CarParker' {
        $dir = Join-Path $artifacts 'CarParker'
        $extra = @{
            DATABASE_URL = 'mongodb://localhost:27018/?replicaSet=rs0&directConnection=true'
            DATABASE_NAME = 'carparker_bench'
            MONGO_MODE = 'replica'
            APP_BASE_URL = 'http://127.0.0.1:5199'
        }
        $state.appPids = @(Start-App 'CarParker.dll' $dir 5199 'app' $extra)
        Assert-Healthy 5199 'app'
        $state.baseUrl = 'http://127.0.0.1:5199'
        $state.appPorts = @(5199)
        $state.mongo = 'carparkerreplicas-dev-mongo-1'
        $state.containers = @($state.mongo)
    }
    'CarParkerReplicas' {
        docker exec carparkerreplicas-dev-redis-1 redis-cli FLUSHALL | Out-Null
        $dir = Join-Path $artifacts 'CarParkerReplicas'
        $appPorts = @(8201, 8202, 8203)
        $ssrPorts = @(13801, 13802, 13803)
        for ($i = 0; $i -lt 3; $i++) {
            $extra = @{
                DATABASE_URL = 'mongodb://localhost:27018/?replicaSet=rs0&directConnection=true'
                DATABASE_NAME = 'carparker_bench'
                MONGO_MODE = 'replica'
                REDIS_URL = 'redis://localhost:6388'
                REPLICA_NAMESPACE = 'CarParkerReplicas:perf'
                CACHE_VERSION = 'v1'
                Replicas__Enabled = 'true'
                Database__InitializeOnStartup = 'false'
                Inertia__SsrBasePort = "$($ssrPorts[$i])"
                APP_BASE_URL = 'http://127.0.0.1:8199'
            }
            $state.appPids += Start-App 'CarParkerReplicas.dll' $dir $appPorts[$i] "app$($i+1)" $extra
        }
        for ($i = 0; $i -lt 3; $i++) { Assert-Healthy $appPorts[$i] "app$($i+1)" }
        $proxy = 'perf-cp-proxy'
        docker rm -f $proxy 2>$null | Out-Null
        docker run -d --name $proxy -p 8199:8080 --add-host=host.docker.internal:host-gateway `
            -v "$PSScriptRoot\haproxy-cp.cfg:/usr/local/etc/haproxy/haproxy.cfg:ro" haproxy:3.2-alpine | Out-Null
        Assert-Healthy 8199 'proxy' '/alive'
        $state.proxy = $proxy
        $state.baseUrl = 'http://127.0.0.1:8199'
        $state.appPorts = @(8201, 8202, 8203)
        $state.mongo = 'carparkerreplicas-dev-mongo-1'
        $state.redis = 'carparkerreplicas-dev-redis-1'
        $state.containers = @($state.mongo, $state.redis, $proxy)
    }
    'TechLandier' {
        $dir = Join-Path $artifacts 'TechLandier'
        $extra = @{
            DATABASE_URL = 'Host=localhost;Port=5442;Database=techlandier;Username=techlandier;Password=local-dev-password;Maximum Pool Size=20'
            APP_BASE_URL = 'http://127.0.0.1:5100'
        }
        $state.appPids = @(Start-App 'TechLandier.dll' $dir 5100 'app' $extra)
        Assert-Healthy 5100 'app'
        $state.baseUrl = 'http://127.0.0.1:5100'
        $state.appPorts = @(5100)
        $state.pgPrimary = $true
        $state.containers = @('techlandier-distributed-dev-postgres-primary-1')
    }
    'TechLandierDistributed' {
        docker exec techlandier-distributed-dev-redis-1 redis-cli FLUSHALL | Out-Null
        docker exec techlandier-distributed-dev-cache-1 redis-cli FLUSHALL | Out-Null
        $dir = Join-Path $artifacts 'TechLandierDistributed\MainApp'
        $workerDir = Join-Path $artifacts 'TechLandierDistributed\Worker'
        $realtimeDir = Join-Path $artifacts 'TechLandierDistributed\Realtime'
        $common = @{
            DATABASE_URL = 'Host=localhost;Port=5443;Database=techlandier;Username=techlandier;Password=local-dev-password;Options=-c pgdog.role=primary;Maximum Pool Size=10'
            DATABASE_READ_URL = 'Host=localhost;Port=5443;Database=techlandier;Username=techlandier;Password=local-dev-password;Options=-c pgdog.role=replica;Maximum Pool Size=5'
            DATABASE_MIGRATE_URL = 'Host=localhost;Port=5442;Database=techlandier;Username=techlandier;Password=local-dev-password;Maximum Pool Size=5'
            DATABASE_MIGRATE_ON_STARTUP = 'false'
            REDIS_URL = 'redis://localhost:6389'
            CACHE_REDIS_URL = 'redis://localhost:6390'
            REPLICA_NAMESPACE = 'TechLandierDistributed:perf'
            CACHE_VERSION = 'v1'
            REPLICAS_ENABLED = 'true'
            INERTIA__SSR = 'true'
            INERTIA__SSRURLS__0 = 'http://127.0.0.1:13901'
            INERTIA__SSRURLS__1 = 'http://127.0.0.1:13902'
            INERTIA__SSRURLS__2 = 'http://127.0.0.1:13903'
            INERTIA__SSRTIMEOUTSECONDS = '2'
            APP_BASE_URL = 'http://127.0.0.1:8185'
            JOBS_ENABLED = 'false'
            CRON_ENABLED = 'false'
        }
        for ($i = 1; $i -le 3; $i++) {
            $state.appPids += Start-App 'TechLandierDistributed.MainApp.dll' $dir (8400 + $i) "main$i" $common
        }
        for ($i = 1; $i -le 3; $i++) { Assert-Healthy (8400 + $i) "main$i" }
        $workerEnv = $common.Clone()
        $workerEnv.JOBS_ENABLED = 'true'
        $workerEnv.CRON_ENABLED = 'false'
        $workerEnv.INERTIA__SSR = 'false'
        $state.auxPids += Start-App 'TechLandierDistributed.Worker.dll' $workerDir 0 'worker' $workerEnv
        $realtimeEnv = $common.Clone()
        $realtimeEnv.INERTIA__SSR = 'false'
        $state.auxPids += Start-App 'TechLandierDistributed.Realtime.dll' $realtimeDir 8404 'realtime' $realtimeEnv
        Assert-Healthy 8404 'realtime'
        for ($i = 0; $i -lt 3; $i++) {
            $ssrEnv = @{
                INERTIA_SSR_HOST = '127.0.0.1'
                INERTIA_SSR_PORT = "$(13901 + $i)"
                NODE_ENV = 'production'
            }
            $out = Join-Path $logDir "$Variant-ssr$($i+1).out.log"
            $err = Join-Path $logDir "$Variant-ssr$($i+1).err.log"
            $p = Start-Process -FilePath 'bun' -ArgumentList 'ssr/ssr.js' -WorkingDirectory $dir -Environment $ssrEnv `
                -RedirectStandardOutput $out -RedirectStandardError $err -PassThru
            $state.ssrPids += $p.Id
        }
        for ($i = 0; $i -lt 3; $i++) {
            $deadline = (Get-Date).AddSeconds(60)
            $ok = $false
            while ((Get-Date) -lt $deadline) {
                Start-Sleep -Milliseconds 500
                try { $r = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$(13901 + $i)/health" -TimeoutSec 3; if ($r.StatusCode -eq 200) { $ok = $true; break } } catch { }
            }
            if (-not $ok) { Write-Output "ssr$($i+1) did not become healthy"; exit 1 }
        }
        $proxy = 'perf-tld-proxy'
        docker rm -f $proxy 2>$null | Out-Null
        docker run -d --name $proxy -p 8185:8080 --add-host=host.docker.internal:host-gateway `
            -v "$PSScriptRoot\haproxy-tld.cfg:/usr/local/etc/haproxy/haproxy.cfg:ro" haproxy:3.2-alpine | Out-Null
        Assert-Healthy 8185 'proxy' '/alive'
        $state.proxy = $proxy
        $state.baseUrl = 'http://127.0.0.1:8185'
        $state.appPorts = @(8401, 8402, 8403)
        $state.pgPrimary = $true
        $state.pgReplicas = @('techlandier-distributed-dev-postgres-replica-1-1', 'techlandier-distributed-dev-postgres-replica-2-1')
        $state.pgdog = $true
        $state.redis = 'techlandier-distributed-dev-redis-1'
        $state.cacheRedis = 'techlandier-distributed-dev-cache-1'
        $state.containers = @('techlandier-distributed-dev-postgres-primary-1', 'techlandier-distributed-dev-postgres-replica-1-1', 'techlandier-distributed-dev-postgres-replica-2-1', 'techlandier-distributed-dev-pgdog-1', 'techlandier-distributed-dev-redis-1', 'techlandier-distributed-dev-cache-1', $proxy)
    }
}

$state | ConvertTo-Json -Depth 6 | Set-Content -Path $statePath -Encoding utf8
Write-Output "Started $Variant at $($state.baseUrl) (state: $statePath)"
