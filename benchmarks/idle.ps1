param(
    [Parameter(Mandatory = $true)][ValidateSet('inkwell', 'carparker', 'techlandier')][string]$App,
    [Parameter(Mandatory = $true)][string]$Version,
    [int]$Port = 0,
    [int]$SsrCacheSeconds = 300
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
$workdir = Join-Path $root $sample.Dir
$name = "$App-$Version-idle-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$out = Join-Path $PSScriptRoot "results/$name.json"
$process = Join-Path $PSScriptRoot "artifacts/results/$name.process.json"
$log = Join-Path $PSScriptRoot "artifacts/results/$name.app.log"

$env:ASPNETCORE_ENVIRONMENT = 'Production'
$env:ASPNETCORE_URLS = "http://127.0.0.1:$Port"
$env:JOBS_ENABLED = 'false'
$env:CRON_ENABLED = 'false'
$env:DOTNET_gcServer = '0'
$env:Logging__LogLevel__Default = 'Warning'
$env:Inertia__SsrCacheSeconds = "$SsrCacheSeconds"

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
    Start-Sleep -Seconds 5
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'sample-process.ps1') -AppPid $p.Id -DurationSeconds 10 -OutputPath $process | Out-Null
    $data = Get-Content $process -Raw | ConvertFrom-Json
    [ordered]@{
        app = $App; version = $Version; kind = 'idle'; measuredAt = (Get-Date).ToString('o')
        ssrCacheSeconds = $SsrCacheSeconds; process = $data
    } | ConvertTo-Json -Depth 10 | Set-Content -Path $out -Encoding utf8
    Write-Host "Wrote $out"
} finally {
    Get-CimInstance Win32_Process -Filter "ParentProcessId=$($p.Id)" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }
    Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    Get-Process bun -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\.bun\*' } | Stop-Process -Force -ErrorAction SilentlyContinue
}
