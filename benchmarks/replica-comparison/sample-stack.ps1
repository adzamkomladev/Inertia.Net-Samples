param(
    [int[]]$AppPids = @(),
    [int[]]$SsrPids = @(),
    [int[]]$AuxPids = @(),
    [string]$SpecPath = '',
    [Parameter(Mandatory = $true)][int]$DurationSeconds,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [int]$IntervalMs = 250
)

$ErrorActionPreference = 'Stop'
if ($SpecPath) {
    $spec = Get-Content $SpecPath -Raw | ConvertFrom-Json
    $AppPids = @($spec.appPids)
    $SsrPids = @($spec.ssrPids)
    $AuxPids = @($spec.auxPids)
}
$logical = [Environment]::ProcessorCount
$groups = @(
    @{ role = 'app'; pids = $AppPids },
    @{ role = 'ssr'; pids = $SsrPids },
    @{ role = 'aux'; pids = $AuxPids }
)

$samples = @{}
$firstCpu = @{}
$firstAt = [DateTime]::UtcNow
foreach ($group in $groups) {
    foreach ($pidValue in $group.pids) {
        if ($pidValue -le 0) { continue }
        $proc = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
        if ($proc) {
            $samples["$pidValue"] = [System.Collections.Generic.List[object]]::new()
            $firstCpu["$pidValue"] = $proc.TotalProcessorTime.TotalMilliseconds
        }
    }
}

$deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    $now = [DateTime]::UtcNow
    foreach ($group in $groups) {
        foreach ($pidValue in $group.pids) {
            $key = "$pidValue"
            if (-not $samples.ContainsKey($key)) { continue }
            $proc = Get-Process -Id $pidValue -ErrorAction SilentlyContinue
            if (-not $proc) { continue }
            $samples[$key].Add([pscustomobject]@{
                at = $now.ToString('o')
                workingSetMb = [math]::Round($proc.WorkingSet64 / 1MB, 2)
                privateMb = [math]::Round($proc.PrivateMemorySize64 / 1MB, 2)
                cpuMs = [math]::Round($proc.TotalProcessorTime.TotalMilliseconds, 1)
                threads = $proc.Threads.Count
                handles = $proc.HandleCount
            })
        }
    }
    Start-Sleep -Milliseconds $IntervalMs
}

$result = [ordered]@{
    intervalMs = $IntervalMs
    durationSeconds = $DurationSeconds
    logicalProcessors = $logical
    groups = @()
}
foreach ($group in $groups) {
    $processes = @()
    foreach ($pidValue in $group.pids) {
        $key = "$pidValue"
        if (-not $samples.ContainsKey($key) -or $samples[$key].Count -eq 0) { continue }
        $rows = $samples[$key]
        $last = $rows[$rows.Count - 1]
        $seconds = ([DateTime]::Parse($last.at) - $firstAt).TotalSeconds
        $cpuPct = if ($seconds -gt 0) { (($last.cpuMs - $firstCpu[$key]) / 1000) / $seconds / $logical * 100 } else { $null }
        $ws = $rows | ForEach-Object { $_.workingSetMb }
        $priv = $rows | ForEach-Object { $_.privateMb }
        $processes += [ordered]@{
            pid = [int]$pidValue
            samples = $rows.Count
            workingSetMb = [ordered]@{
                min = [math]::Round(($ws | Measure-Object -Minimum).Minimum, 2)
                avg = [math]::Round(($ws | Measure-Object -Average).Average, 2)
                max = [math]::Round(($ws | Measure-Object -Maximum).Maximum, 2)
                last = [math]::Round($last.workingSetMb, 2)
            }
            privateMb = [ordered]@{
                min = [math]::Round(($priv | Measure-Object -Minimum).Minimum, 2)
                avg = [math]::Round(($priv | Measure-Object -Average).Average, 2)
                max = [math]::Round(($priv | Measure-Object -Maximum).Maximum, 2)
                last = [math]::Round($last.privateMb, 2)
            }
            cpuPctAvg = if ($null -ne $cpuPct) { [math]::Round($cpuPct, 2) } else { $null }
            threadsLast = $last.threads
            handlesLast = $last.handles
        }
    }
    $result.groups += [ordered]@{ role = $group.role; processes = $processes }
}
$result | ConvertTo-Json -Depth 8 | Set-Content -Path $OutputPath -Encoding utf8
