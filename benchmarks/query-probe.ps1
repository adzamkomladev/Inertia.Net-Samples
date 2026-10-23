param(
    [Parameter(Mandatory = $true)][string]$Version,
    [int]$Port = 5100,
    [int]$Requests = 200
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $PSScriptRoot "artifacts/Inkwell-$Version/Inkwell.dll"
$workdir = Join-Path $root 'samples/Inkwell/src/Inkwell'
$name = "inkwell-$Version-queryprobe-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$log = Join-Path $PSScriptRoot "artifacts/results/$name.log"
$out = Join-Path $PSScriptRoot "results/$name.json"

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:JOBS_ENABLED = 'false'
$env:CRON_ENABLED = 'false'
$env:DOTNET_gcServer = '0'
$env:Logging__LogLevel__Default = 'Warning'
$env:Logging__LogLevel__Microsoft_EntityFrameworkCore_Database_Command = 'Information'
# Configuration keys use __; the dotted env form above is ignored by the host, so set it explicitly.
[Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', 'Information')

Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    ForEach-Object { Stop-Process -Id $_.OwningProcess -Force -ErrorAction SilentlyContinue }
Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue

$p = Start-Process -FilePath 'dotnet' -ArgumentList $dll -WorkingDirectory $workdir `
    -RedirectStandardOutput $log -RedirectStandardError ($log + '.err') -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(120)
    while ([DateTime]::UtcNow -lt $deadline) {
        try {
            if ((Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$Port/health" -TimeoutSec 3).StatusCode -eq 200) { break }
        } catch { }
        Start-Sleep -Milliseconds 750
    }
    $client = [System.Net.Http.HttpClient]::new()
    for ($i = 0; $i -lt 20; $i++) { $null = $client.GetStringAsync("http://127.0.0.1:$Port/posts").Result }
    Start-Sleep -Seconds 1
    $offset = (Get-Item $log).Length
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt $Requests; $i++) { $null = $client.GetStringAsync("http://127.0.0.1:$Port/posts").Result }
    $sw.Stop()
    Start-Sleep -Seconds 1
    $stream = [System.IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    $stream.Seek($offset, 'Begin') | Out-Null
    $reader = [System.IO.StreamReader]::new($stream)
    $text = $reader.ReadToEnd()
    $reader.Dispose()
    $commands = ([regex]::Matches($text, 'Executed DbCommand')).Count
    [ordered]@{
        app = 'inkwell'; version = $Version; kind = 'query-probe'; requests = $Requests
        elapsedSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 3)
        dbCommands = $commands
        commandsPerRequest = [math]::Round($commands / $Requests, 3)
        requestsPerSecond = [math]::Round($Requests / $sw.Elapsed.TotalSeconds, 1)
        estimatedQueriesPerSecond = [math]::Round($commands / $sw.Elapsed.TotalSeconds, 1)
    } | ConvertTo-Json -Depth 5 | Set-Content -Path $out -Encoding utf8
    Write-Host "Wrote $out"
} finally {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command', $null)
}
