param(
    [Parameter(Mandatory = $true)][int]$AppPid,
    [Parameter(Mandatory = $true)][int]$DurationSeconds,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [int]$IntervalMs = 250
)

$ErrorActionPreference = 'Stop'
$logical = [Environment]::ProcessorCount
$appSamples = [System.Collections.Generic.List[object]]::new()
$ssrSamples = [System.Collections.Generic.List[object]]::new()

function Get-ChildPid([int]$parent) {
    $child = Get-CimInstance Win32_Process -Filter "ParentProcessId=$parent" -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^(bun|node)(\.exe)?$' } |
        Select-Object -First 1
    return $child.ProcessId
}

$ssrPid = Get-ChildPid $AppPid
$appProc = Get-Process -Id $AppPid -ErrorAction Stop
$appCpu0 = $appProc.TotalProcessorTime.TotalMilliseconds
$appAt0 = [DateTime]::UtcNow
$ssrCpu0 = $null
$ssrAt0 = $null
if ($ssrPid) {
    $ssrProc = Get-Process -Id $ssrPid -ErrorAction SilentlyContinue
    if ($ssrProc) {
        $ssrCpu0 = $ssrProc.TotalProcessorTime.TotalMilliseconds
        $ssrAt0 = [DateTime]::UtcNow
    }
}

$deadline = [DateTime]::UtcNow.AddSeconds($DurationSeconds)
while ([DateTime]::UtcNow -lt $deadline) {
    $now = [DateTime]::UtcNow
    try {
        $p = Get-Process -Id $AppPid -ErrorAction Stop
        $appSamples.Add([pscustomobject]@{
            at = $now.ToString('o')
            workingSetMb = [math]::Round($p.WorkingSet64 / 1MB, 2)
            privateMb = [math]::Round($p.PrivateMemorySize64 / 1MB, 2)
            cpuMs = [math]::Round($p.TotalProcessorTime.TotalMilliseconds, 1)
            threads = $p.Threads.Count
            handles = $p.HandleCount
        })
    } catch { break }

    if ($ssrPid) {
        try {
            $s = Get-Process -Id $ssrPid -ErrorAction Stop
            $ssrSamples.Add([pscustomobject]@{
                at = $now.ToString('o')
                workingSetMb = [math]::Round($s.WorkingSet64 / 1MB, 2)
                privateMb = [math]::Round($s.PrivateMemorySize64 / 1MB, 2)
                cpuMs = [math]::Round($s.TotalProcessorTime.TotalMilliseconds, 1)
                threads = $s.Threads.Count
            })
        } catch { $ssrPid = $null }
    }
    Start-Sleep -Milliseconds $IntervalMs
}

function Summarize($samples, $cpu0, $at0, [int]$cpuCount) {
    if (-not $samples -or $samples.Count -eq 0) { return $null }
    $ws = $samples | ForEach-Object { $_.workingSetMb }
    $priv = $samples | ForEach-Object { $_.privateMb }
    $last = $samples[$samples.Count - 1]
    $seconds = ([DateTime]::Parse($last.at) - $at0).TotalSeconds
    $cpuPct = if ($null -ne $cpu0 -and $seconds -gt 0) {
        (($last.cpuMs - $cpu0) / 1000) / $seconds / $cpuCount * 100
    } else { $null }
    [pscustomobject]@{
        samples = $samples.Count
        workingSetMb = [pscustomobject]@{
            min = [math]::Round(($ws | Measure-Object -Minimum).Minimum, 2)
            avg = [math]::Round(($ws | Measure-Object -Average).Average, 2)
            max = [math]::Round(($ws | Measure-Object -Maximum).Maximum, 2)
            last = [math]::Round($last.workingSetMb, 2)
        }
        privateMb = [pscustomobject]@{
            min = [math]::Round(($priv | Measure-Object -Minimum).Minimum, 2)
            avg = [math]::Round(($priv | Measure-Object -Average).Average, 2)
            max = [math]::Round(($priv | Measure-Object -Maximum).Maximum, 2)
            last = [math]::Round($last.privateMb, 2)
        }
        cpuPctAvg = if ($null -ne $cpuPct) { [math]::Round($cpuPct, 2) } else { $null }
        threadsLast = $last.threads
    }
}

$result = [pscustomobject]@{
    appPid = $AppPid
    ssrPid = $ssrPid
    intervalMs = $IntervalMs
    durationSeconds = $DurationSeconds
    app = Summarize $appSamples $appCpu0 $appAt0 $logical
    ssr = if ($ssrPid) { Summarize $ssrSamples $ssrCpu0 $ssrAt0 $logical } else { $null }
    logicalProcessors = $logical
}
$result | ConvertTo-Json -Depth 6 | Set-Content -Path $OutputPath -Encoding utf8
