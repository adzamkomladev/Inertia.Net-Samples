param(
    [Parameter(Mandatory = $true)][ValidateSet('CarParker', 'CarParkerReplicas', 'TechLandier', 'TechLandierDistributed')][string]$Variant,
    [Parameter(Mandatory = $true)][string]$Tag,
    [switch]$IncludeAux
)

$ErrorActionPreference = 'Stop'
$statePath = Join-Path $PSScriptRoot "state\$Variant.json"
if (-not (Test-Path $statePath)) { throw "No state for $Variant" }
$state = Get-Content $statePath -Raw | ConvertFrom-Json
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$outDir = Join-Path $PSScriptRoot "results\$Variant\profile-$Tag-$stamp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null
$gcdumpExe = Join-Path $env:USERPROFILE '.dotnet\tools\dotnet-gcdump.exe'
$targets = @()
foreach ($pidValue in $state.appPids) { $targets += [ordered]@{ pid = [int]$pidValue; role = 'app' } }
if ($IncludeAux -and $state.auxPids) {
    foreach ($pidValue in $state.auxPids) { $targets += [ordered]@{ pid = [int]$pidValue; role = 'aux' } }
}

$results = @()
foreach ($target in $targets) {
    $proc = Get-Process -Id $target.pid -ErrorAction SilentlyContinue
    if (-not $proc) { continue }
    $file = Join-Path $outDir "$($target.role)-$($target.pid).gcdump"
    $report = Join-Path $outDir "$($target.role)-$($target.pid).txt"
    $collectLog = Join-Path $outDir "$($target.role)-$($target.pid).collect.log"
    & $gcdumpExe collect --process-id $target.pid -o $file *> $collectLog
    if (-not (Test-Path $file)) { continue }
    & $gcdumpExe report $file *> $report
    $lines = Get-Content $report -ErrorAction SilentlyContinue
    $heapBytes = $null
    $heapObjects = $null
    foreach ($line in $lines) {
        if ($line -match '^\s*([0-9,]+)\s+GC Heap bytes') { $heapBytes = [long]($matches[1] -replace ',', '') }
        if ($line -match '^\s*([0-9,]+)\s+GC Heap objects') { $heapObjects = [long]($matches[1] -replace ',', '') }
    }
    $top = @()
    $inTable = $false
    foreach ($line in $lines) {
        if ($line -match '^\s*Object Bytes\s+Count\s+Type') { $inTable = $true; continue }
        if (-not $inTable) { continue }
        if ($line -notmatch '^\s*([0-9,]+)\s+([0-9,]+)\s+(.+?)\s{2,}\[(.+)\]$') { continue }
        $top += [ordered]@{
            bytes = [long]($matches[1] -replace ',', '')
            count = [long]($matches[2] -replace ',', '')
            type = $matches[3].Trim()
            assembly = $matches[4]
        }
        if ($top.Count -ge 25) { break }
    }
    $results += [ordered]@{
        pid = $target.pid
        role = $target.role
        gcdump = $file
        report = $report
        gcdumpBytes = (Get-Item $file).Length
        heapBytes = $heapBytes
        heapObjects = $heapObjects
        workingSetMb = [math]::Round($proc.WorkingSet64 / 1MB, 2)
        privateMb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
        topTypes = $top
    }
}

$profile = [ordered]@{
    variant = $Variant
    tag = $Tag
    measuredAt = (Get-Date).ToString('o')
    processes = $results
}
$jsonPath = Join-Path $outDir 'profile.json'
$profile | ConvertTo-Json -Depth 10 | Set-Content $jsonPath -Encoding utf8
Write-Output "Wrote $jsonPath"
foreach ($r in $results) {
    Write-Output ("pid={0} role={1} heap={2:N2}MB objects={3} ws={4:N1}MB gcdump={5:N1}MB" -f $r.pid, $r.role, ($r.heapBytes / 1MB), $r.heapObjects, $r.workingSetMb, ($r.gcdumpBytes / 1MB))
}
