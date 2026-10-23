param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [string]$Version = '1.5.1',
    [int]$Requests = 10,
    [int]$Port = 0
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sample = @{
    inkwell = @{
        Dir = 'samples/Inkwell/src/Inkwell'; Dll = 'Inkwell.dll'; Port = 5100
        Email = 'inertia.demo42.00000@yopmail.com'; Pages = @(
            @{ Name = 'feed_html'; Path = '/posts'; Kind = 'html' },
            @{ Name = 'feed_json'; Path = '/posts'; Kind = 'json' },
            @{ Name = 'analytics_shell'; Path = '/analytics'; Kind = 'html' },
            @{ Name = 'analytics_full'; Path = '/analytics'; Kind = 'json'; Component = 'Analytics/Index'; Partial = 'summary,series,topPosts' },
            @{ Name = 'settings'; Path = '/settings'; Kind = 'html' },
            @{ Name = 'notifications'; Path = '/notifications'; Kind = 'html' },
            @{ Name = 'users'; Path = '/users'; Kind = 'html' }
        )
    }
    carparker = @{
        Dir = 'samples/CarParker/src/CarParker'; Dll = 'CarParker.dll'; Port = 5199
        Email = 'inertia.demo42.carparker.admin@yopmail.com'; Pages = @(
            @{ Name = 'home'; Path = '/'; Kind = 'html' },
            @{ Name = 'lots'; Path = '/lots'; Kind = 'html' },
            @{ Name = 'account'; Path = '/account'; Kind = 'html' },
            @{ Name = 'guard'; Path = '/guard'; Kind = 'html' },
            @{ Name = 'admin'; Path = '/admin'; Kind = 'html' },
            @{ Name = 'admin_analytics'; Path = '/admin/analytics'; Kind = 'json'; Component = 'admin/Analytics'; Partial = 'kpis,series,topLots,refundMix' }
        )
    }
    techlandier = @{
        Dir = 'samples/TechLandier/src/TechLandier'; Dll = 'TechLandier.dll'; Port = 5100
        Email = 'inertia.demo42.techlandier.admin@yopmail.com'; Pages = @(
            @{ Name = 'home'; Path = '/'; Kind = 'html' },
            @{ Name = 'products'; Path = '/products'; Kind = 'html' },
            @{ Name = 'product'; Path = '/products/browser-verified-device'; Kind = 'html' },
            @{ Name = 'account'; Path = '/account'; Kind = 'html' },
            @{ Name = 'admin'; Path = '/admin'; Kind = 'html' },
            @{ Name = 'admin_analytics'; Path = '/admin/analytics'; Kind = 'json'; Component = 'admin/Analytics'; Partial = 'kpis,series,topProducts,categoryShare' }
        )
    }
}[$App]
if ($Port -eq 0) { $Port = $sample.Port }
$artifactDir = Join-Path $PSScriptRoot "artifacts/$($App.Substring(0,1).ToUpper() + $App.Substring(1))-$Version"
$dll = Join-Path $artifactDir $sample.Dll
$workdir = Join-Path $root $sample.Dir
$name = "$App-$Version-auth-probe-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$log = Join-Path $PSScriptRoot "artifacts/results/$name.app.log"
$out = Join-Path $PSScriptRoot "results/$name.json"
$base = "http://127.0.0.1:$Port"

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = $base
$env:JOBS_ENABLED = 'false'
$env:CRON_ENABLED = 'false'
$env:DOTNET_gcServer = '0'
$env:Logging__LogLevel__Default = 'Warning'
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', 'Information')

Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue

function Db-Ops {
    if ($App -eq 'techlandier') {
        $env:PGPASSWORD = 'local-dev-password'
        $line = & psql -h localhost -p 5433 -U techlandier -d techlandier -t -A -c "select xact_commit from pg_stat_database where datname='techlandier'"
        return [long]$line
    }
    if ($App -eq 'carparker') {
        $json = docker exec carparker-mongo-1 mongosh --quiet --eval 'const o=db.serverStatus().opcounters; Number(o.query)+Number(o.command)+Number(o.getmore)+Number(o.insert)+Number(o.update)+Number(o.delete)'
        return [long]$json
    }
    return -1
}

function Count-DbCommands([long]$offset) {
    if ($App -eq 'carparker') { return $null }
    $stream = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($offset, 'Begin') | Out-Null
        $reader = [System.IO.StreamReader]::new($stream)
        try { return ([regex]::Matches($reader.ReadToEnd(), 'Executed DbCommand')).Count } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.UseCookies = $true
$handler.CookieContainer = [System.Net.CookieContainer]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(30)

function Get-Page([string]$path, [string]$kind, [string]$component, [string]$partial, [string]$version) {
    $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$base$path")
    if ($kind -eq 'json') {
        $req.Headers.Add('X-Inertia', 'true')
        $req.Headers.Add('X-Inertia-Version', $version)
        $req.Headers.Add('Accept', 'application/json')
        if ($component) { $req.Headers.Add('X-Inertia-Partial-Component', $component) }
        if ($partial) { $req.Headers.Add('X-Inertia-Partial-Data', $partial) }
    } else {
        $req.Headers.Add('Accept', 'text/html')
    }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $res = $client.SendAsync($req).Result
    $body = $res.Content.ReadAsStringAsync().Result
    $sw.Stop()
    return [pscustomobject]@{ Status = [int]$res.StatusCode; Body = $body; Ms = $sw.Elapsed.TotalMilliseconds }
}

$p = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory $workdir `
    -RedirectStandardOutput $log -RedirectStandardError ($log + '.err') -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ([DateTime]::UtcNow -lt $deadline) {
        try { if ((Invoke-WebRequest -UseBasicParsing -Uri "$base/health" -TimeoutSec 3).StatusCode -eq 200) { break } } catch { }
        Start-Sleep -Milliseconds 750
    }

    # Login: read the page for the antiforgery cookie and asset version.
    $login = Get-Page '/login' 'html' '' '' ''
    $page = [regex]::Match($login.Body, '<script data-page="app" type="application/json">([\s\S]*?)</script>').Groups[1].Value | ConvertFrom-Json
    $version = $page.version
    $cookies = $handler.CookieContainer.GetCookies([Uri]$base)
    $xsrf = [Uri]::UnescapeDataString($cookies['XSRF-TOKEN'].Value)

    $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$base/login")
    $req.Headers.Add('X-Inertia', 'true')
    $req.Headers.Add('X-Inertia-Version', $version)
    $req.Headers.Add('X-XSRF-TOKEN', $xsrf)
    $req.Headers.Add('X-Requested-With', 'XMLHttpRequest')
    $req.Content = [System.Net.Http.StringContent]::new((@{ email = $sample.Email; password = 'Password@12345'; remember = $true } | ConvertTo-Json), [System.Text.Encoding]::UTF8, 'application/json')
    $loginPost = $client.SendAsync($req).Result
    if ([int]$loginPost.StatusCode -ge 400) { throw "Login failed: $([int]$loginPost.StatusCode)" }

    # Production marks the auth cookie Secure. Browsers accept it on http://127.0.0.1; HttpClient does not,
    # so add a non-Secure copy for this local probe only.
    $setCookieValues = $null
    if ($loginPost.Headers.TryGetValues('Set-Cookie', [ref]$setCookieValues)) {
        foreach ($header in $setCookieValues) {
            $pair = ($header -split ';')[0]
            $eq = $pair.IndexOf('=')
            if ($eq -gt 0) {
                $cookieName = $pair.Substring(0, $eq)
                $cookieValue = $pair.Substring($eq + 1)
                $cookie = [System.Net.Cookie]::new($cookieName, $cookieValue, '/', '127.0.0.1')
                $cookie.Secure = $false
                $cookie.HttpOnly = $true
                $handler.CookieContainer.Add($cookie)
            }
        }
    }

    $rows = @()
    foreach ($pg in $sample.Pages) {
        # Warm the page and its caches once, then measure a batch.
        $null = Get-Page $pg.Path $pg.Kind $pg.Component $pg.Partial $version
        Start-Sleep -Milliseconds 300
        $before = if ($App -eq 'carparker') { Db-Ops } else { (Get-Item $log).Length }
        $opsBefore = if ($App -eq 'carparker') { $before } else { 0 }
        $times = @()
        $statuses = @()
        for ($i = 0; $i -lt $Requests; $i++) {
            $r = Get-Page $pg.Path $pg.Kind $pg.Component $pg.Partial $version
            $times += $r.Ms
            $statuses += $r.Status
        }
        Start-Sleep -Milliseconds 500
        $ops = if ($App -eq 'carparker') { (Db-Ops) - $opsBefore } else { Count-DbCommands $before }
        $perReq = if ($null -ne $ops) { [math]::Round($ops / $Requests, 2) } else { $null }
        $sorted = $times | Sort-Object
        $rows += [ordered]@{
            page = $pg.Name
            path = $pg.Path
            kind = $pg.Kind
            status = ($statuses | Select-Object -Unique) -join ','
            avgMs = [math]::Round(($times | Measure-Object -Average).Average, 2)
            p50Ms = [math]::Round($sorted[[int][math]::Floor($sorted.Count * 0.5)], 2)
            p95Ms = [math]::Round($sorted[[int][math]::Floor($sorted.Count * 0.95)], 2)
            dbOpsPerRequest = $perReq
        }
    }

    [ordered]@{
        app = $App; version = $Version; kind = 'auth-probe'; requestsPerPage = $Requests
        measuredAt = (Get-Date).ToString('o'); pages = $rows
    } | ConvertTo-Json -Depth 6 | Set-Content -Path $out -Encoding utf8
    $rows | Format-Table -AutoSize
    Write-Host "Wrote $out"
} finally {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', $null)
}
