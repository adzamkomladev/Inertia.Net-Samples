# Benchmark harness — Inertia.Net 1.5.1 upgrade

Reproducible before/after measurements for Inkwell (React/SQLite), CarParker (Svelte/MongoDB) and
TechLandier (Vue/PostgreSQL). It runs a published Release build with the job worker and cron off,
drives read-only traffic with k6, and records the app, the managed Bun SSR process, the .NET
runtime counters and the database counters.

The measured results and conclusions are in
[`docs/INERTIA_1_5_UPGRADE.md`](../docs/INERTIA_1_5_UPGRADE.md). Raw summaries are under
[`results/`](results/).

## What is measured

| Layer | Source |
| --- | --- |
| Requests/s, p50/p90/p95/p99/max, per-route latency | k6 `load.js` summary |
| .NET working set, private bytes, CPU, threads | `sample-process.ps1` (`Get-Process` samples every 250 ms) |
| GC heap, allocation rate, gen-0/1/2 collections, time in GC, thread pool | `dotnet-counters` (`System.Runtime`) |
| Managed Bun SSR process RSS, private bytes, CPU | `sample-process.ps1` (child process of the app) |
| SSR cache hits/renders and Inertia page/SSR duration | `dotnet-counters` (`Inertia.Net` meter) |
| PostgreSQL statements/s and tuples/s | `pg_stat_database` deltas |
| MongoDB find/command ops/s | `serverStatus().opcounters` deltas |
| SQLite commands/request | EF Core command log probe (`query-probe.ps1`) |
| Idle RSS | `idle.ps1` |

## Workload

`load.js` picks a weighted mix of full HTML visits, Inertia JSON visits and Inertia partial
reloads. It reads the asset version, page component, a post/lot/product id and the list page from
the app's own first responses, so no ids or seeds are hard-coded. It never writes and never logs in.

| App | HTML | Inertia JSON | Partial reload |
| --- | --- | --- | --- |
| Inkwell | feed, page 2, post | feed, post | feed `posts` |
| CarParker | home, lots, lot | lots | lots `lots` |
| TechLandier | home, products, product | home, products | products `products` |

Built-in profiles:

- `rps` — constant arrival rate, e.g. 150/s, for a fixed duration.
- `vus` — constant VUs, e.g. 8, for maximum sustained throughput.
- `ramp` — ramp to a peak VU count (not used in the report).

## Prerequisites

- .NET 10 SDK and the sample's database (SQLite is embedded; start the Compose files for Mongo/Postgres).
- `k6` v1.3.0 (the scripts use the `k6` CLI; adjust `$k6` in `run.ps1` to your path).
- `dotnet-counters` (`dotnet tool install --global dotnet-counters`).
- `psql` for TechLandier and Docker for CarParker's `mongosh` counters.

## Usage

Build the two versions into `artifacts/{App}-{Version}` (publish, then copy the built `ssr/` and
`wwwroot/build` beside the DLL, exactly as the Dockerfile does). Then:

```powershell
# One measured run: 10 s warmup, 30 s at 150 req/s
pwsh benchmarks/run.ps1 -App inkwell -Version 1.5.1 -Profile rps -Rps 150 -Duration 30s -Warmup 10s

# Saturation: 8 VUs
pwsh benchmarks/run.ps1 -App inkwell -Version 1.5.1 -Profile vus -Vus 8 -Duration 30s

# Component probe: SSR cache off (every HTML visit renders in Bun)
pwsh benchmarks/run.ps1 -App inkwell -Version 1.5.1 -Profile rps -Rps 80 -SsrCacheSeconds 0

# Cache-expiry stress: 2 s fresh window, stale-while-revalidate on 1.5.1
pwsh benchmarks/run.ps1 -App inkwell -Version 1.5.1 -Profile rps -Rps 250 -SsrCacheSeconds 2 -SsrStaleSeconds 60

# Full alternating matrix for one app (two rounds per version)
pwsh benchmarks/matrix.ps1 -App inkwell

# Idle memory for both versions
pwsh benchmarks/idle.ps1 -App inkwell -Version 1.4.0
pwsh benchmarks/idle.ps1 -App inkwell -Version 1.5.1

# SQLite commands/request (Inkwell only)
pwsh benchmarks/query-probe.ps1 -Version 1.5.1
```

`run.ps1` writes a combined JSON to `results/` and keeps app logs, k6 logs, process samples and
the dotnet-counters CSV under `artifacts/results/` (git-ignored). Every run starts from a healthy
app, warms up, samples the processes and counters for the measured window, and stops the app and
its SSR child at the end.

## Notes

- The suite targets a local demo database. It does not seed; seeding is a one-time sample step
  (`--seed-demo` at the sample's documented demo size). The 78k/500k capacity seed is not used.
- Run one sample at a time: the single-app samples share ports and the managed SSR process uses
  the same base port.
- Results are comparable only on the same host, database contents, cache settings, duration and
  profile. The report uses medians of two alternating rounds and gives ranges for noisy cache
  stress runs.
- `dotnet-counters` and the 250 ms process sampler run for both versions, so their observer cost
  is part of both sides of the comparison.
