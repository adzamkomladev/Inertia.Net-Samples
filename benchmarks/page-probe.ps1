param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [string]$Version = '1.5.1-opt',
    [int]$Requests = 40,
    [int]$Port = 0,
    [string]$Only = '',
    [int]$SsrCacheSeconds = 300,
    [int]$SsrStaleSeconds = 60
)

$ErrorActionPreference = 'Stop'
trap { Write-Host "TRAP: $_"; Write-Host $_.ScriptStackTrace; exit 1 }
$root = Split-Path -Parent $PSScriptRoot
$script:ResultsDir = Join-Path $PSScriptRoot 'results'
$script:ArtifactResults = Join-Path $PSScriptRoot 'artifacts/results'
New-Item -ItemType Directory -Force -Path $script:ResultsDir, $script:ArtifactResults | Out-Null

$sample = @{
    inkwell = @{
        Dir = 'samples/Inkwell/src/Inkwell'; Dll = 'Inkwell.dll'; Port = 5100
        AdminEmail = 'inertia.demo42.00000@yopmail.com'
        CustomerEmail = 'inertia.demo42.00001@yopmail.com'
    }
    carparker = @{
        Dir = 'samples/CarParker/src/CarParker'; Dll = 'CarParker.dll'; Port = 5199
        AdminEmail = 'inertia.demo42.carparker.admin@yopmail.com'
        CustomerEmail = 'inertia.demo42.carparker.customer@yopmail.com'
    }
    techlandier = @{
        Dir = 'samples/TechLandier/src/TechLandier'; Dll = 'TechLandier.dll'; Port = 5100
        AdminEmail = 'inertia.demo42.techlandier.admin@yopmail.com'
        CustomerEmail = 'inertia.demo42.techlandier.customer@yopmail.com'
    }
}[$App]
if ($Port -eq 0) { $Port = $sample.Port }
$script:Base = "http://127.0.0.1:$Port"
$artifactDir = Join-Path $PSScriptRoot "artifacts/$($App.Substring(0,1).ToUpper() + $App.Substring(1))-$Version"
$dll = Join-Path $artifactDir $sample.Dll
if (-not (Test-Path $dll)) { throw "Missing $dll; publish the app at version $Version first." }
$workdir = Join-Path $root $sample.Dir
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$name = "$App-$Version-page-probe-$stamp"
$appLog = Join-Path $script:ArtifactResults "$name.app.log"
$appErr = Join-Path $script:ArtifactResults "$name.app.err.log"
$out = Join-Path $script:ResultsDir "$name.json"
$script:LogicalCpus = [Environment]::ProcessorCount
$script:JsonCache = @{}

function Stop-App([int]$processId) {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$processId" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $processId -Force -ErrorAction SilentlyContinue
}

function Wait-Healthy([int]$processId, [int]$healthPort) {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (-not (Get-Process -Id $processId -ErrorAction SilentlyContinue)) { throw "App exited during startup; see $appLog" }
        try {
            if ((Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$healthPort/health" -TimeoutSec 3).StatusCode -eq 200) { return }
        } catch { }
        Start-Sleep -Milliseconds 750
    }
    throw "App did not become healthy; see $appLog"
}

$script:Handlers = @{}
function New-Client([bool]$useCookies = $true) {
    $handler = [System.Net.Http.HttpClientHandler]::new()
    $handler.UseCookies = $useCookies
    if ($useCookies) { $handler.CookieContainer = [System.Net.CookieContainer]::new() }
    $handler.AllowAutoRedirect = $false
    $client = [System.Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(60)
    $script:Handlers[$client.GetHashCode()] = $handler
    return $client
}

function Connect-Client([string]$email) {
    $client = New-Client
    $login = Send-Request $client '/login' 'html'
    $page = Get-EmbeddedPage $login.Body
    if (-not $page) { throw 'Could not parse the login page JSON.' }
    if (-not $script:AssetVersion) { $script:AssetVersion = $page.version }

    $cookies = $script:Handlers[$client.GetHashCode()].CookieContainer.GetCookies([Uri]$script:Base)
    if (-not $cookies['XSRF-TOKEN']) { throw 'XSRF-TOKEN cookie was not issued by /login.' }
    $xsrf = [Uri]::UnescapeDataString($cookies['XSRF-TOKEN'].Value)

    $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Post, "$script:Base/login")
    $req.Headers.Add('X-Inertia', 'true')
    $req.Headers.Add('X-Inertia-Version', $script:AssetVersion)
    $req.Headers.Add('X-XSRF-TOKEN', $xsrf)
    $req.Headers.Add('X-Requested-With', 'XMLHttpRequest')
    $req.Content = [System.Net.Http.StringContent]::new(
        (@{ email = $email; password = 'Password@12345'; remember = $true } | ConvertTo-Json),
        [System.Text.Encoding]::UTF8, 'application/json')
    $post = $client.SendAsync($req).Result
    if ([int]$post.StatusCode -ge 400) { throw "Login failed for ${email}: $([int]$post.StatusCode)" }

    # Production marks the auth cookie Secure; copy it without Secure for local HTTP.
    $setCookieValues = $null
    if ($post.Headers.TryGetValues('Set-Cookie', [ref]$setCookieValues)) {
        foreach ($header in $setCookieValues) {
            $pair = ($header -split ';')[0]
            $eq = $pair.IndexOf('=')
            if ($eq -gt 0) {
                $cookie = [System.Net.Cookie]::new($pair.Substring(0, $eq), $pair.Substring($eq + 1), '/', '127.0.0.1')
                $cookie.Secure = $false
                $cookie.HttpOnly = $true
                $script:Handlers[$client.GetHashCode()].CookieContainer.Add($cookie)
            }
        }
    }
    return $client
}

function Send-Request(
    [System.Net.Http.HttpClient]$client,
    [string]$pagePath,
    [string]$kind = 'html',
    [string]$version = '',
    [string]$component = '',
    [string]$partial = ''
) {
    $req = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::Get, "$script:Base$pagePath")
    if ($kind -eq 'json' -or $kind -eq 'partial' -or $kind -eq 'deferred') {
        $req.Headers.Add('X-Inertia', 'true')
        if ($version) { $req.Headers.Add('X-Inertia-Version', $version) }
        $req.Headers.Add('Accept', 'application/json')
        $req.Headers.Add('X-Requested-With', 'XMLHttpRequest')
        if ($component -and ($kind -eq 'partial' -or $kind -eq 'deferred')) {
            $req.Headers.Add('X-Inertia-Partial-Component', $component)
        }
        if ($partial) { $req.Headers.Add('X-Inertia-Partial-Data', $partial) }
    } else {
        $req.Headers.Add('Accept', 'text/html')
    }

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $res = $client.SendAsync($req).Result
    $body = $res.Content.ReadAsStringAsync().Result
    $sw.Stop()
    return [pscustomobject]@{
        Status = [int]$res.StatusCode
        Body = $body
        Ms = [math]::Round($sw.Elapsed.TotalMilliseconds, 3)
        Bytes = [System.Text.Encoding]::UTF8.GetByteCount($body)
        Headers = $res.Headers
    }
}

function Get-EmbeddedPage($html) {
    $m = [regex]::Match($html, '<script data-page="app" type="application/json">([\s\S]*?)</script>')
    if (-not $m.Success) { return $null }
    return $m.Groups[1].Value | ConvertFrom-Json
}

function Get-PageJson([System.Net.Http.HttpClient]$client, [string]$pagePath, [string]$version) {
    $key = "$($client.GetHashCode())|$pagePath"
    if ($script:JsonCache.ContainsKey($key)) { return $script:JsonCache[$key] }
    $r = Send-Request $client $pagePath 'json' $version
    if ($r.Status -ne 200) {
        Write-Host "  resolve $pagePath returned $($r.Status) version=[$version] body=$($r.Body.Substring(0, [Math]::Min(200, $r.Body.Length)))"
    }
    $page = if ($r.Status -eq 200 -and $r.Body) { $r.Body | ConvertFrom-Json } else { $null }
    $script:JsonCache[$key] = $page
    return $page
}

function Find-Prop($obj, [string]$propPath) {
    $cur = $obj
    foreach ($token in $propPath.Split('.')) {
        if ($null -eq $cur) { return $null }
        if ($token -match '^([^\[]+)\[(\d+)\]$') {
            $cur = $cur.($matches[1])
            if ($null -eq $cur) { return $null }
            $idx = [int]$matches[2]
            if ($cur.Count -le $idx) { return $null }
            $cur = $cur[$idx]
        } else {
            $cur = $cur.$token
        }
    }
    return $cur
}

function Get-Shape($value) {
    if ($null -eq $value) { return [ordered]@{ type = 'null' } }
    if ($value -is [System.Array]) {
        $keys = @()
        if ($value.Count -gt 0 -and $value[0] -is [pscustomobject]) { $keys = @($value[0].PSObject.Properties.Name) }
        return [ordered]@{ type = 'array'; count = $value.Count; itemKeys = $keys }
    }
    if ($value -is [pscustomobject]) {
        $names = $value.PSObject.Properties.Name
        return [ordered]@{ type = 'object'; keys = if ($null -eq $names) { @() } else { @($names) } }
    }
    if ($value -is [string]) { return [ordered]@{ type = 'string' } }
    if ($value -is [bool]) { return [ordered]@{ type = 'boolean' } }
    return [ordered]@{ type = $value.GetType().Name }
}

function Get-PageProps($page) {
    $props = @()
    if ($null -eq $page -or $null -eq $page.props) { return $props }
    foreach ($p in $page.props.PSObject.Properties) {
        $shape = Get-Shape $p.Value
        $props += [ordered]@{ name = $p.Name; type = $shape['type']; keys = $shape['keys']; itemKeys = $shape['itemKeys']; count = $shape['count'] }
    }
    return $props
}

function Get-AppProcesses {
    $app = Get-Process -Id $script:AppPid -ErrorAction SilentlyContinue
    if (-not $app) { return $null }
    if (-not $script:SsrPid) {
        $child = Get-CimInstance Win32_Process -Filter "ParentProcessId=$($script:AppPid)" -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } | Select-Object -First 1
        if ($child) { $script:SsrPid = $child.ProcessId }
    }
    $ssr = if ($script:SsrPid) { Get-Process -Id $script:SsrPid -ErrorAction SilentlyContinue } else { $null }
    return [pscustomobject]@{
        AppRss = [math]::Round($app.WorkingSet64 / 1MB, 2)
        AppPrivate = [math]::Round($app.PrivateMemorySize64 / 1MB, 2)
        AppCpuMs = $app.TotalProcessorTime.TotalMilliseconds
        SsrRss = if ($ssr) { [math]::Round($ssr.WorkingSet64 / 1MB, 2) } else { $null }
        SsrPrivate = if ($ssr) { [math]::Round($ssr.PrivateMemorySize64 / 1MB, 2) } else { $null }
        SsrCpuMs = if ($ssr) { $ssr.TotalProcessorTime.TotalMilliseconds } else { $null }
    }
}

function Get-DbCounter {
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

function Get-EfOffset {
    if ($App -eq 'carparker') { return 0 }
    return (Get-Item $appLog).Length
}

function Count-EfCommands([long]$offset) {
    if ($App -eq 'carparker') { return 0 }
    $stream = [System.IO.File]::Open($appLog, 'Open', 'Read', 'ReadWrite')
    try {
        $stream.Seek($offset, 'Begin') | Out-Null
        $reader = [System.IO.StreamReader]::new($stream)
        try { return ([regex]::Matches($reader.ReadToEnd(), 'Executed DbCommand')).Count } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Measure-Mode(
    [System.Net.Http.HttpClient]$client,
    [string]$mode,
    [string]$pagePath,
    [string]$version,
    [string]$component,
    [string]$partial
) {
    $kind = if ($mode -eq 'html') { 'html' } elseif ($mode -eq 'json') { 'json' } elseif ($mode -eq 'partial') { 'partial' } else { 'deferred' }
    # A warm request is excluded from the measured percentiles and DB delta.
    $null = Send-Request $client $pagePath $kind $version $component $partial
    Start-Sleep -Milliseconds 200

    $dbBefore = if ($App -eq 'carparker') { Get-DbCounter } else { Get-EfOffset }
    $procBefore = Get-AppProcesses
    $times = [System.Collections.Generic.List[double]]::new()
    $statuses = [System.Collections.Generic.HashSet[string]]::new()
    $bytes = 0
    $errors = 0
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $Requests; $i++) {
        $r = Send-Request $client $pagePath $kind $version $component $partial
        $times.Add($r.Ms)
        $null = $statuses.Add([string]$r.Status)
        $bytes += $r.Bytes
        if ($r.Status -ge 400 -or $r.Status -eq 0) { $errors++ }
    }
    $sw.Stop()
    Start-Sleep -Milliseconds 500
    $dbAfter = if ($App -eq 'carparker') { Get-DbCounter } else { Count-EfCommands $dbBefore }
    $procAfter = Get-AppProcesses

    $sorted = @($times | Sort-Object)
    function Pct([double]$p) {
        if ($sorted.Count -eq 0) { return $null }
        $idx = [math]::Ceiling($p * $sorted.Count) - 1
        if ($idx -lt 0) { $idx = 0 }
        if ($idx -ge $sorted.Count) { $idx = $sorted.Count - 1 }
        return [math]::Round($sorted[$idx], 2)
    }
    $dbOps = if ($App -eq 'carparker') { $dbAfter - $dbBefore } else { $dbAfter }
    $elapsed = $sw.Elapsed.TotalSeconds
    $appCpuPct = $null
    $ssrCpuPct = $null
    if ($procBefore -and $procAfter -and $elapsed -gt 0) {
        $appCpuPct = [math]::Round((($procAfter.AppCpuMs - $procBefore.AppCpuMs) / 1000) / $elapsed / $script:LogicalCpus * 100, 2)
        if ($null -ne $procBefore.SsrCpuMs -and $null -ne $procAfter.SsrCpuMs) {
            $ssrCpuPct = [math]::Round((($procAfter.SsrCpuMs - $procBefore.SsrCpuMs) / 1000) / $elapsed / $script:LogicalCpus * 100, 2)
        }
    }
    return [ordered]@{
        mode = $mode
        requests = $Requests
        statuses = @($statuses) -join ','
        errors = $errors
        elapsedSeconds = [math]::Round($elapsed, 2)
        rps = [math]::Round($Requests / $elapsed, 1)
        p50Ms = Pct 0.50
        p95Ms = Pct 0.95
        p99Ms = Pct 0.99
        avgMs = [math]::Round(($times | Measure-Object -Average).Average, 2)
        minMs = if ($sorted.Count) { $sorted[0] } else { $null }
        maxMs = if ($sorted.Count) { $sorted[$sorted.Count - 1] } else { $null }
        avgBytes = [math]::Round($bytes / $Requests, 0)
        dbOps = $dbOps
        dbOpsPerRequest = [math]::Round($dbOps / $Requests, 2)
        appRssMb = if ($procAfter) { $procAfter.AppRss } else { $null }
        appRssDeltaMb = if ($procBefore -and $procAfter) { [math]::Round($procAfter.AppRss - $procBefore.AppRss, 2) } else { $null }
        appCpuPct = $appCpuPct
        ssrRssMb = if ($procAfter) { $procAfter.SsrRss } else { $null }
        ssrRssDeltaMb = if ($procBefore -and $procAfter -and $null -ne $procBefore.SsrRss -and $null -ne $procAfter.SsrRss) { [math]::Round($procAfter.SsrRss - $procBefore.SsrRss, 2) } else { $null }
        ssrCpuPct = $ssrCpuPct
    }
}

function Get-Ids {
    $ids = @{}
    if ($App -eq 'inkwell') {
        $posts = Get-PageJson $script:CustomerClient '/posts' $script:AssetVersion
        $users = Get-PageJson $script:CustomerClient '/users' $script:AssetVersion
        $ids.postId = Find-Prop $posts 'props.posts.data[0].id'
        $ids.userId = Find-Prop $users 'props.users.data[0].id'
        $adminId = Find-Prop $posts 'props.auth.user.id'
        $myPost = @(Find-Prop $posts 'props.posts.data') | Where-Object { $_.author.id -eq $adminId } | Select-Object -First 1
        if ($myPost) { $ids.myPostId = $myPost.id }
    } elseif ($App -eq 'carparker') {
        $lots = Get-PageJson $script:CustomerClient '/lots' $script:AssetVersion
        $bookings = Get-PageJson $script:CustomerClient '/account/bookings' $script:AssetVersion
        $adminBookings = Get-PageJson $script:AdminClient '/admin/bookings' $script:AssetVersion
        $sessions = Get-PageJson $script:AdminClient '/admin/sessions' $script:AssetVersion
        $insurance = Get-PageJson $script:AdminClient '/admin/insurance' $script:AssetVersion
        $users = Get-PageJson $script:AdminClient '/admin/users' $script:AssetVersion
        $ids.lotId = Find-Prop $lots 'props.lots.data[0].id'
        $ids.bookingId = Find-Prop $bookings 'props.bookings[0].id'
        if (-not $ids.bookingId) { $ids.bookingId = Find-Prop $adminBookings 'props.bookings[0].id' }
        $ids.adminBookingId = Find-Prop $adminBookings 'props.bookings[0].id'
        $ids.sessionId = Find-Prop $sessions 'props.openSessions[0].id'
        if (-not $ids.sessionId) { $ids.sessionId = Find-Prop $sessions 'props.exitedSessions[0].id' }
        $ids.claimId = Find-Prop $insurance 'props.claims[0].id'
        $ids.userId = Find-Prop $users 'props.users[0].id'
    } else {
        $products = Get-PageJson $script:CustomerClient '/products' $script:AssetVersion
        $accountOrders = Get-PageJson $script:CustomerClient '/account/orders' $script:AssetVersion
        $adminOrders = Get-PageJson $script:AdminClient '/admin/orders' $script:AssetVersion
        $warranties = Get-PageJson $script:CustomerClient '/account/care/warranties' $script:AssetVersion
        $insurance = Get-PageJson $script:CustomerClient '/account/care/insurance' $script:AssetVersion
        $repairs = Get-PageJson $script:CustomerClient '/account/care/repairs' $script:AssetVersion
        $support = Get-PageJson $script:CustomerClient '/account/support' $script:AssetVersion
        $adminCare = Get-PageJson $script:AdminClient '/admin/care' $script:AssetVersion
        $adminCredit = Get-PageJson $script:AdminClient '/admin/credit' $script:AssetVersion
        $adminSupport = Get-PageJson $script:AdminClient '/admin/support' $script:AssetVersion
        $ids.productSlug = Find-Prop $products 'props.products.data[0].slug'
        $ids.productId = Find-Prop $products 'props.products.data[0].id'
        $ids.orderNumber = Find-Prop $accountOrders 'props.orders[0].number'
        if (-not $ids.orderNumber) { $ids.orderNumber = Find-Prop $adminOrders 'props.orders.data[0].number' }
        $ids.adminOrderNumber = Find-Prop $adminOrders 'props.orders.data[0].number'
        $ids.warrantyId = Find-Prop $warranties 'props.warranties[0].id'
        $ids.policyId = Find-Prop $insurance 'props.policies[0].id'
        $ids.repairId = Find-Prop $repairs 'props.repairs[0].id'
        $ids.ticketNumber = Find-Prop $support 'props.tickets[0].number'
        $ids.adminTicketNumber = Find-Prop $adminSupport 'props.tickets.data[0].number'
        $ids.careWarrantyId = Find-Prop $adminCare 'props.warrantyClaims[0].id'
        $ids.careInsuranceId = Find-Prop $adminCare 'props.insuranceClaims[0].id'
        $ids.careRepairId = Find-Prop $adminCare 'props.repairs[0].id'
        if (-not $ids.careRepairId) { $ids.careRepairId = $ids.repairId }
        $ids.planId = Find-Prop $adminCredit 'props.plans.data[0].id'
    }
    return $ids
}

function Get-PageSpecs($ids) {
    $pages = @()
    switch ($App) {
        'inkwell' {
            $pages += @{ Name = 'login'; Path = '/login'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'register'; Path = '/register'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'feed'; Path = '/posts'; Auth = 'none'; Modes = @('html', 'json', 'partial'); Partial = 'posts' }
            $pages += @{ Name = 'feed_page2'; Path = '/posts?page=2'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'post'; Path = '/posts/{postId}'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'post_comments'; Path = '/posts/{postId}/comments'; Auth = 'none'; Modes = @('html') }
            $pages += @{ Name = 'users'; Path = '/users'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'profile'; Path = '/users/{userId}'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'feed_auth'; Path = '/feed'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'bookmarks'; Path = '/bookmarks'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'me_posts'; Path = '/me/posts'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'me_archives'; Path = '/me/archives'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'analytics'; Path = '/analytics'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'post_analytics'; Path = '/posts/{myPostId}/analytics'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'settings'; Path = '/settings'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'notifications'; Path = '/notifications'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'users_auth'; Path = '/users'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'profile_auth'; Path = '/users/{userId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'post_edit'; Path = '/posts/{myPostId}/edit'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'post_create'; Path = '/posts/create'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_team'; Path = '/admin/team'; Auth = 'admin'; Modes = @('html', 'json') }
        }
        'carparker' {
            $pages += @{ Name = 'login'; Path = '/login'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'register'; Path = '/register'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'home'; Path = '/'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'lots'; Path = '/lots'; Auth = 'none'; Modes = @('html', 'json', 'partial'); Partial = 'lots' }
            $pages += @{ Name = 'lot'; Path = '/lots/{lotId}'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'about'; Path = '/about'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'policies'; Path = '/policies'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'account'; Path = '/account'; Auth = 'customer'; Modes = @('html', 'json') }
            $pages += @{ Name = 'account_bookings'; Path = '/account/bookings'; Auth = 'customer'; Modes = @('html', 'json') }
            $pages += @{ Name = 'account_booking'; Path = '/account/bookings/{bookingId}'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_payments'; Path = '/account/payments'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_insurance'; Path = '/account/insurance'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_alerts'; Path = '/account/alerts'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_profile'; Path = '/account/profile'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_preferences'; Path = '/account/preferences'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'lot_book'; Path = '/lots/{lotId}/book'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'guard'; Path = '/guard'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'guard_board'; Path = '/guard/board'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'guard_incidents'; Path = '/guard/incidents'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin'; Path = '/admin'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'admin_lots'; Path = '/admin/lots'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'admin_lot'; Path = '/admin/lots/{lotId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_bookings'; Path = '/admin/bookings'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_booking'; Path = '/admin/bookings/{adminBookingId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_sessions'; Path = '/admin/sessions'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_refunds'; Path = '/admin/refunds'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_insurance'; Path = '/admin/insurance'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_claim'; Path = '/admin/claims/{claimId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_incidents'; Path = '/admin/incidents'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_users'; Path = '/admin/users'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_user'; Path = '/admin/users/{userId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_analytics'; Path = '/admin/analytics'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'admin_team'; Path = '/admin/team'; Auth = 'admin'; Modes = @('html') }
        }
        'techlandier' {
            $pages += @{ Name = 'login'; Path = '/login'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'register'; Path = '/register'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'home'; Path = '/'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'products'; Path = '/products'; Auth = 'none'; Modes = @('html', 'json', 'partial'); Partial = 'products' }
            $pages += @{ Name = 'product'; Path = '/products/{productSlug}'; Auth = 'none'; Modes = @('html', 'json') }
            $pages += @{ Name = 'product_auth'; Path = '/products/{productSlug}'; Auth = 'customer'; Modes = @('html', 'json') }
            $pages += @{ Name = 'deals'; Path = '/deals'; Auth = 'none'; Modes = @('html') }
            $pages += @{ Name = 'about'; Path = '/about'; Auth = 'none'; Modes = @('html') }
            $pages += @{ Name = 'policy'; Path = '/support/policy'; Auth = 'none'; Modes = @('html') }
            $pages += @{ Name = 'order_track'; Path = '/orders/{orderNumber}'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account'; Path = '/account'; Auth = 'customer'; Modes = @('html', 'json') }
            $pages += @{ Name = 'account_orders'; Path = '/account/orders'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_order'; Path = '/account/orders/{orderNumber}'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_credit'; Path = '/account/credit'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_warranties'; Path = '/account/care/warranties'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_insurance'; Path = '/account/care/insurance'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_repairs'; Path = '/account/care/repairs'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_repair'; Path = '/account/care/repairs/{repairId}'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_support'; Path = '/account/support'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_alerts'; Path = '/account/alerts'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_profile'; Path = '/account/profile'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'account_preferences'; Path = '/account/preferences'; Auth = 'customer'; Modes = @('html') }
            $pages += @{ Name = 'buy'; Path = '/buy/{productId}'; Auth = 'customer'; Modes = @('html', 'json') }
            $pages += @{ Name = 'admin'; Path = '/admin'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'admin_analytics'; Path = '/admin/analytics'; Auth = 'admin'; Modes = @('html', 'json') }
            $pages += @{ Name = 'admin_catalog'; Path = '/admin/catalog'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_orders'; Path = '/admin/orders'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_order'; Path = '/admin/orders/{adminOrderNumber}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_deliveries'; Path = '/admin/deliveries'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_credit'; Path = '/admin/credit'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_plan'; Path = '/admin/credit/{planId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_care'; Path = '/admin/care'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_care_warranty'; Path = '/admin/care/warranty-claims/{careWarrantyId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_care_insurance'; Path = '/admin/care/insurance-claims/{careInsuranceId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_care_repair'; Path = '/admin/care/repairs/{careRepairId}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_support'; Path = '/admin/support'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_support_ticket'; Path = '/admin/support/{adminTicketNumber}'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_reviews'; Path = '/admin/reviews'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_settings'; Path = '/admin/settings'; Auth = 'admin'; Modes = @('html') }
            $pages += @{ Name = 'admin_team'; Path = '/admin/team'; Auth = 'admin'; Modes = @('html') }
        }
    }
    return $pages
}

function Resolve-PagePath([string]$template, $ids) {
    $missing = $false
    $result = [regex]::Replace($template, '\{(\w+)\}', {
        param($m)
        $key = $m.Groups[1].Value
        $value = $ids[$key]
        if ($null -eq $value -or "$value" -eq '') { $script:MissingId = $true; return '' }
        return [Uri]::EscapeDataString([string]$value)
    })
    if ($script:MissingId) { return $null }
    return $result
}

# --- app lifecycle -------------------------------------------------------------------------------
Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-App $_.OwningProcess }
Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = $script:Base
$env:JOBS_ENABLED = 'false'
$env:CRON_ENABLED = 'false'
$env:DOTNET_gcServer = '0'
$env:Logging__LogLevel__Default = 'Warning'
$env:Logging__LogLevel__Microsoft = 'Warning'
$env:Logging__LogLevel__System = 'Warning'
if ($App -eq 'inkwell' -or $App -eq 'techlandier') {
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', 'Information')
} else {
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', 'Warning')
}
$env:Inertia__SsrCacheSeconds = "$SsrCacheSeconds"
$env:Inertia__SsrStaleSeconds = "$SsrStaleSeconds"

Write-Host "Starting $App $Version on $Port ..."
$p = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory $workdir `
    -RedirectStandardOutput $appLog -RedirectStandardError $appErr -PassThru
$script:AppPid = $p.Id
$script:SsrPid = $null
try {
    Wait-Healthy $script:AppPid $Port
    Write-Host "Healthy (pid $($script:AppPid))"

    $anonClient = New-Client
    $adminClient = Connect-Client $sample.AdminEmail
    $customerClient = if ($sample.CustomerEmail -ne $sample.AdminEmail -and ($App -eq 'carparker' -or $App -eq 'techlandier')) {
        Connect-Client $sample.CustomerEmail
    } else {
        $adminClient
    }
    $script:AdminClient = $adminClient
    $script:CustomerClient = $customerClient

    $adminCheckPath = if ($App -eq 'inkwell') { '/settings' } else { '/admin' }
    $adminCheck = Send-Request $adminClient $adminCheckPath 'html' $script:AssetVersion
    if ($adminCheck.Status -ne 200) { throw "Admin check failed: $($adminCheck.Status)" }
    if ($customerClient -ne $adminClient) {
        $customerCheck = Send-Request $customerClient '/account' 'html' $script:AssetVersion
        if ($customerCheck.Status -ne 200) { throw "Customer check failed: $($customerCheck.Status)" }
    }

    # Warm shared props (auth + notifications + cached layout) before per-page measurements.
    $null = Send-Request $adminClient $adminCheckPath 'html' $script:AssetVersion
    if ($customerClient -ne $adminClient) { $null = Send-Request $customerClient '/account' 'html' $script:AssetVersion }

    $ids = Get-Ids
    $ids.GetEnumerator() | Sort-Object Name | ForEach-Object { Write-Host "id $($_.Key)=$($_.Value)" }

    $pages = Get-PageSpecs $ids
    if ($Only) { $pages = @($pages | Where-Object { $_.Name -match $Only }) }

    $pageResults = @()
    $flatRows = @()
    foreach ($spec in $pages) {
        $script:MissingId = $false
        $pagePath = Resolve-PagePath $spec.Path $ids
        if (-not $pagePath) {
            Write-Host "skip $($spec.Name): missing id for $($spec.Path)"
            continue
        }
        $client = switch ($spec.Auth) {
            'none' { $anonClient }
            'customer' { $customerClient }
            default { $adminClient }
        }
        Write-Host "page $($spec.Name) $pagePath ($($spec.Auth))"

        $warm = Send-Request $client $pagePath 'html' $script:AssetVersion
        if ($warm.Status -ne 200) {
            Write-Host "  status $($warm.Status); recording only"
            $pageResults += [ordered]@{
                name = $spec.Name; path = $pagePath; auth = $spec.Auth; status = $warm.Status
                component = $null; props = @(); sharedProps = @(); deferredProps = $null
                mergeProps = $null; modes = @()
            }
            continue
        }
        $page = Get-EmbeddedPage $warm.Body
        if (-not $page) {
            Write-Host "  no Inertia page JSON"
            $pageResults += [ordered]@{
                name = $spec.Name; path = $pagePath; auth = $spec.Auth; status = $warm.Status
                component = $null; props = @(); sharedProps = @(); deferredProps = $null
                mergeProps = $null; modes = @()
            }
            continue
        }

        $deferredNames = @()
        if ($page.deferredProps) {
            foreach ($g in $page.deferredProps.PSObject.Properties) { $deferredNames += @($g.Value) }
        }
        $modes = @($spec.Modes)
        if ($deferredNames.Count -gt 0 -and $modes -notcontains 'deferred') { $modes += 'deferred' }

        $modeResults = @()
        foreach ($mode in $modes) {
            $partial = ''
            if ($mode -eq 'partial') { $partial = $spec.Partial }
            if ($mode -eq 'deferred') { $partial = ($deferredNames -join ',') }
            if ($mode -eq 'partial' -and -not $partial) { continue }
            if ($mode -eq 'deferred' -and -not $partial) { continue }
            $m = Measure-Mode $client $mode $pagePath $script:AssetVersion $page.component $partial
            $modeResults += $m
            $flatRows += [ordered]@{
                page = $spec.Name; path = $pagePath; auth = $spec.Auth; component = $page.component
                mode = $m.mode; statuses = $m.statuses; requests = $m.requests; rps = $m.rps
                p50Ms = $m.p50Ms; p95Ms = $m.p95Ms; p99Ms = $m.p99Ms; avgMs = $m.avgMs
                avgBytes = $m.avgBytes; dbOpsPerRequest = $m.dbOpsPerRequest
                appRssMb = $m.appRssMb; appRssDeltaMb = $m.appRssDeltaMb; appCpuPct = $m.appCpuPct
                ssrRssMb = $m.ssrRssMb; ssrRssDeltaMb = $m.ssrRssDeltaMb; ssrCpuPct = $m.ssrCpuPct
            }
            Write-Host ("  {0,-8} p50={1,7} p95={2,7} p99={3,7} ops={4,5} rss={5,7} ssr={6,7}" -f `
                $m.mode, $m.p50Ms, $m.p95Ms, $m.p99Ms, $m.dbOpsPerRequest, $m.appRssMb, $m.ssrRssMb)
        }

        $pageResults += [ordered]@{
            name = $spec.Name
            path = $pagePath
            auth = $spec.Auth
            status = $warm.Status
            component = $page.component
            props = Get-PageProps $page
            sharedProps = @($page.sharedProps)
            deferredProps = $page.deferredProps
            mergeProps = $page.mergeProps
            scrollProps = $page.scrollProps
            onceProps = $page.onceProps
            modes = $modeResults
        }
    }

    $result = [ordered]@{
        app = $App
        version = $Version
        assetVersion = $script:AssetVersion
        kind = 'page-probe'
        requestsPerMode = $Requests
        measuredAt = (Get-Date).ToString('o')
        environment = [ordered]@{
            ssrCacheSeconds = $SsrCacheSeconds
            ssrStaleSeconds = $SsrStaleSeconds
            logicalProcessors = $script:LogicalCpus
        }
        pages = $pageResults
        rows = $flatRows
        ids = $ids
        logs = [ordered]@{ app = $appLog }
    }
    $result | ConvertTo-Json -Depth 12 | Set-Content -Path $out -Encoding utf8
    Write-Host "Wrote $out"
}
finally {
    Stop-App $script:AppPid
    Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue
}
