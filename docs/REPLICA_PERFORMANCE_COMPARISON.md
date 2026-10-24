# Replica performance comparison - CarParker vs CarParkerReplicas and TechLandier vs TechLandierDistributed

Measured 2026-10-24 on the same host, one sample at a time, in this order:

1. CarParker (single process)
2. CarParkerReplicas (three app replicas + shared Redis + HAProxy)
3. TechLandier (single process)
4. TechLandierDistributed (three web replicas + worker + realtime + three SSR servers + PgDog + PostgreSQL primary/two replicas + two Redis instances)

The raw summary is in [`benchmarks/replica-comparison/summary.md`](../benchmarks/replica-comparison/summary.md).
Every phase's JSON, counter CSV, process samples, DB/cache snapshots and gcdump is under
`benchmarks/replica-comparison/results/`.

## 1. Headline result

Replication did **not** increase single-host throughput. On this 4-core laptop, every replica topology was
slower and heavier than its single-process baseline, while still meeting the fixed low-load targets with zero
HTTP failures and zero dropped iterations.

| Sample | Phase | Baseline RPS | Replica RPS | Delta | Baseline p95 | Replica p95 | Baseline p99 | Replica p99 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | public, 8 VUs | 280.0 | 223.3 | -20% | 43.4 | 54.9 | 53.4 | 69.7 |
| CarParker | signed-in, 8 VUs | 165.0 | 143.2 | -13% | 78.1 | 85.9 | 97.7 | 120.1 |
| TechLandier | public, 8 VUs | 428.8 | 227.3 | -47% | 28.7 | 51.7 | 37.5 | 81.1 |
| TechLandier | signed-in, 8 VUs | 257.5 | 165.7 | -36% | 52.6 | 88.4 | 70.6 | 123.8 |

| Sample | Total system memory, baseline | Total system memory, replica stack | Multiplier |
| --- | ---: | ---: | ---: |
| CarParker | 516.9 MB | 1013.6 MB | 1.96x |
| TechLandier | 452.1 MB | 1396.8 MB | 3.09x |

At fixed low load both replica stacks kept up:

| Sample | Phase | Baseline p50 / p95 / p99 | Replica p50 / p95 / p99 |
| --- | --- | ---: | ---: |
| CarParker | public, 100 req/s | 9.1 / 15.7 / 26.3 ms | 13.2 / 27.9 / 45.1 ms |
| CarParker | signed-in, 40 req/s | 16.3 / 30.6 / 48.8 ms | 18.8 / 37.5 / 59.8 ms |
| TechLandier | public, 100 req/s | 6.2 / 11.2 / 22.5 ms | 10.5 / 36.8 / 97.9 ms |
| TechLandier | signed-in, 40 req/s | 11.2 / 23.7 / 43.7 ms | 13.7 / 30.1 / 2056.1 ms |

The TechLandierDistributed signed-in p99 is a real but localized outlier: `/account` p99 was 2071 ms and
`/account` JSON p95 was 2054 ms, while their medians were 12.0 ms and 7.8 ms. It is consistent with occasional
PgDog/Npgsql connection-establishment stalls, not general database slowness.

## 2. Method and environment

| Item | Value |
| --- | --- |
| Host | Windows 11, Intel Core i5-8265U (4 cores / 8 threads), 16 GB |
| Runtime | .NET SDK 10.0.402, ASP.NET Core 10 |
| Load generator | native k6 1.3.0 |
| Bun | 1.3.14 |
| App mode | Release publish, `ASPNETCORE_ENVIRONMENT=Production`, workstation GC, jobs and cron off |
| Data | CarParker demo seed: 5 lots, 30 reservations, sessions/payments/refunds/claims; TechLandier demo seed: 120 products, 400 orders, 4 users, 10 categories |
| Metrics | k6, `dotnet-counters` 1 s CSV, 250 ms process sampler, `dotnet-gcdump`, Mongo `serverStatus`, PostgreSQL `pg_stat_database`/`pg_stat_user_tables`, Redis `INFO`/`COMMANDSTATS`, Docker stats |
| Load | read-only public mix and signed-in admin/customer mix; 10 s warmup, 30 s measured per phase |
| Phases | public 100 req/s, public 8 VUs, signed-in 40 req/s, signed-in 8 VUs, public with SSR cache off |

### What was actually compared

- **CarParker vs CarParkerReplicas.** Both used the same `carparker_bench` MongoDB database on the same
  single-node replica set. CarParkerReplicas ran three app processes, each with its own Bun SSR child, plus
  shared Redis and HAProxy. This isolates the app-tier replication and shared-cache effect. The production
  sample's three-member MongoDB set was not used for the performance run because the app processes ran on the
  host; the Mongo durability topology is unchanged from the sample.
- **TechLandier vs TechLandierDistributed.** Both used the same `techlandier` PostgreSQL database. The
  distributed run used the real PgDog + primary + two streaming read replicas, two Redis instances, three
  MainApp replicas, three standalone SSR servers, one Worker and one Realtime process. The sample's Compose
  default is two Workers and two Realtime replicas; one of each was used because they are idle in this
  read-only web workload and would only add host contention.
- App-tier measurements used direct round-robin across the replica ports. The HAProxy path was measured
  separately because Docker Desktop's Windows hairpin NAT is very expensive (see section 7). The single-process
  baselines were also measured directly.
- Every measured phase had zero HTTP failures, 100% checks and zero dropped iterations.

### Harness accommodations (not app behavior changes)

- CarParker and CarParkerReplicas now read `Inertia:SsrBasePort` (default 13714) so three host-run replicas can
  each own a Bun process without colliding on port 13714. Containers keep the default and are unaffected.
- The built CarParkerReplicas SSR bundle in the benchmark artifact was patched to honor `INERTIA_SSR_PORT`.
  The TechLandierDistributed bundle already honors it.
- `benchmarks/load.js` and `benchmarks/auth-load.js` accept `BASE_URLS` for round-robin across replicas.
- `benchmarks/auth-load.js` now accepts the Scroll object shape for `bookings`/`orders` instead of assuming a
  bare array. The measured routes are unchanged.

### Caveats

- All roles ran on one 4-core host with Docker Desktop. Absolute numbers are not a capacity guarantee.
- One measured round per phase. The docs' existing harness uses medians of two alternating rounds; the
  direction here is supported by four independent phases per pair, but the tails are noisy.
- Public TechLandier load increments product views (the default k6 user-agent is used). The signed-in load
  uses `k6-benchmark-bot`, which skips view writes.
- Mongo `opcounters` include command/session and connection-pool overhead. PostgreSQL transaction counts through
  PgDog transaction pooling count statements/transactions, not HTTP requests. Treat the DB ops/request numbers
  as directional; a per-page probe is the exact tool.
- TechLandierDistributed's first warmup was a cold start: 51.8 req/s, p99 2051 ms and 0% SSR cache. A second
  warmup reached 189.9 req/s, p99 69.2 ms and 100% SSR cache. The measured phases below use the warmed stack.

## 3. CarParker vs CarParkerReplicas

### 3.1 Load

| Phase | Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Public 100 req/s | CarParker | 99.9 | 9.1 | 15.7 | 26.3 | 3,007 | 0 | 0% |
| Public 100 req/s | CarParkerReplicas | 99.8 | 13.2 | 27.9 | 45.1 | 3,006 | 0 | 0% |
| Public 8 VUs | CarParker | 280.0 | 26.6 | 43.4 | 53.4 | 8,426 | 0 | 0% |
| Public 8 VUs | CarParkerReplicas | 223.3 | 33.3 | 54.9 | 69.7 | 6,735 | 0 | 0% |
| Signed-in 40 req/s | CarParker | 40.0 | 16.3 | 30.6 | 48.8 | 1,215 | 0 | 0% |
| Signed-in 40 req/s | CarParkerReplicas | 39.7 | 18.8 | 37.5 | 59.8 | 1,216 | 0 | 0% |
| Signed-in 8 VUs | CarParker | 165.0 | 47.5 | 78.1 | 97.7 | 5,041 | 0 | 0% |
| Signed-in 8 VUs | CarParkerReplicas | 143.2 | 53.4 | 85.9 | 120.1 | 4,376 | 0 | 0% |

Per-route medians at 100 req/s:

| Route | Base p50 | Replica p50 | Base p95 | Replica p95 | Base p99 | Replica p99 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| home_html | 10.5 | 15.9 | 17.4 | 31.5 | 27.4 | 48.2 |
| lots_html | 10.7 | 15.7 | 17.9 | 31.4 | 33.8 | 49.5 |
| lot_html | 6.1 | 10.0 | 10.1 | 20.1 | 19.8 | 34.3 |
| lots_json | 7.8 | 9.5 | 12.5 | 22.9 | 22.6 | 38.8 |
| lots_partial | 7.8 | 9.5 | 13.4 | 20.6 | 20.4 | 42.8 |

The replica penalty is already visible on every public route at the same fixed arrival rate. It is not a
throughput-only effect.

### 3.2 Memory, allocations and GC

These are from the public 8-VU phase (the saturation point).

| Metric | CarParker | CarParkerReplicas |
| --- | ---: | ---: |
| App working set | 149.2 MB | 446.5 MB (3 x ~149 MB) |
| App CPU (8 logical CPUs) | 27.67% | 27.12% |
| SSR working set | 79.5 MB | 225.5 MB (3 x ~75 MB) |
| SSR CPU | 0.04% | 0.12% |
| Allocation rate | 49.6 MB/s | 42.7 MB/s |
| Managed heap | 18.6 MB | 46.3 MB total (3 x ~15 MB) |
| Committed GC memory | 22.0 MB | 61.7 MB |
| Time in GC | 8.86% | 2.23% |
| Gen 0 / Gen 1 / Gen 2 per second | 8.47 / 4.74 / 0.09 | 10.92 / 1.65 / 0.43 |
| Total system memory (host processes + Docker DB/cache/proxy) | 516.9 MB | 1013.6 MB |

Managed heap from `gcdump` after load:

| Process | Heap | Objects | Working set |
| --- | ---: | ---: | ---: |
| CarParker app | 8.24 MB | 106,537 | 143.4 MB |
| CarParkerReplicas app-1 | 8.65 MB | 108,579 | 149.7 MB |
| CarParkerReplicas app-2 | 8.50 MB | 108,573 | 150.5 MB |
| CarParkerReplicas app-3 | 8.12 MB | 106,813 | 147.4 MB |
| **Replica total** | **25.27 MB** | **323,965** | **447.5 MB** |

The only notable cache-specific managed allocation is `StackExchange.Redis.RawResult[]`: four arrays of
229,400 bytes, about **917 KB per replica process**, absent in the single-process baseline. Everything else is
runtime/DI/routing/EF-type-map overhead. There is no leak signal; the heaps stabilize at 8-9 MB per process.

GC pause is *lower* in the replica build because each process has a much smaller heap and they collect
independently. Total allocation is slightly lower at saturation and slightly higher at 100 req/s. Replication
does not reduce allocation per request; it redistributes it across three heaps.

### 3.3 DB and cache

| Phase | Variant | Mongo finds/request | Mongo WT cache hit | Redis commands/request | Redis hit rate | Redis memory | Redis keys |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Public 100 req/s | CarParker | 7.13 | 100% | - | - | - | - |
| Public 100 req/s | CarParkerReplicas | 25.99 | 100% | 3.32 | 98.3% | 2.14 MB | 19 |
| Public 8 VUs | CarParker | 3.79 | 100% | - | - | - | - |
| Public 8 VUs | CarParkerReplicas | 12.85 | 100% | 2.19 | 99.0% | 2.14 MB | 19 |
| Signed-in 40 req/s | CarParker | 30.95 | 100% | - | - | - | - |
| Signed-in 40 req/s | CarParkerReplicas | 75.90 | 100% | 13.44 | 99.1% | 2.14 MB | 19 |
| Signed-in 8 VUs | CarParker | 12.17 | 100% | - | - | - | - |
| Signed-in 8 VUs | CarParkerReplicas | 25.73 | 100% | 5.00 | 99.4% | 2.14 MB | 19 |

The replica build's higher Mongo ops/request is directional only. `opcounters` includes per-connection and
session overhead, and three driver pools plus the job/cron-disabled but still present background machinery
inflate it. The HTTP-level result is the trustworthy one: the replica app is slower per request, and the shared
Redis cache itself is healthy (99%+ hit rate, about 2 MB).

### 3.4 SSR cache off

Every public HTML request renders in Bun; `Inertia:SsrCacheSeconds=0`.

| Variant | RPS | p50 ms | p95 ms | p99 ms | SSR CPU | SSR cache | Dropped |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| CarParker | 58.7 | 14.5 | 24.4 | 35.6 | 3.66% | 0% | 0 |
| CarParkerReplicas | 56.8 | 17.5 | 43.3 | 61.0 | 5.97% | 0% | 0 |

With the cache on, the same 8-VU public run was 280.0 / 223.3 req/s with 100% cache hits. The SSR cache is
the single largest performance lever in this sample.

### 3.5 HAProxy control

CarParkerReplicas, public 8 VUs:

| Path | RPS | p50 ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| Direct round-robin to app ports | 223.3 | 33.3 | 54.9 | 69.7 |
| Through Dockerized HAProxy | 112.9 | 65.9 | 115.1 | 153.5 |

The proxy path halves throughput and adds about 33 ms to p50 on this Windows/Docker Desktop host. That is a
hairpin-NAT/container artifact, not an app result, which is why the comparison above uses direct app-tier
round-robin. A production Linux HAProxy does not pay this cost.

## 4. TechLandier vs TechLandierDistributed

### 4.1 Load

| Phase | Variant | RPS | p50 ms | p95 ms | p99 ms | Requests | Dropped | Failed |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Public 100 req/s | TechLandier | 100.0 | 6.2 | 11.2 | 22.5 | 3,008 | 0 | 0% |
| Public 100 req/s | TechLandierDistributed | 99.9 | 10.5 | 36.8 | 97.9 | 3,009 | 0 | 0% |
| Public 8 VUs | TechLandier | 428.8 | 16.1 | 28.7 | 37.5 | 12,903 | 0 | 0% |
| Public 8 VUs | TechLandierDistributed | 227.3 | 27.8 | 51.7 | 81.1 | 6,888 | 0 | 0% |
| Signed-in 40 req/s | TechLandier | 40.0 | 11.2 | 23.7 | 43.7 | 1,216 | 0 | 0% |
| Signed-in 40 req/s | TechLandierDistributed | 39.9 | 13.7 | 30.1 | 2056.1 | 1,216 | 0 | 0% |
| Signed-in 8 VUs | TechLandier | 257.5 | 28.4 | 52.6 | 70.6 | 7,819 | 0 | 0% |
| Signed-in 8 VUs | TechLandierDistributed | 165.7 | 41.1 | 88.4 | 123.8 | 5,044 | 0 | 0% |

Per-route medians at 100 req/s:

| Route | Base p50 | Dist p50 | Base p95 | Dist p95 | Base p99 | Dist p99 |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| home_html | 5.8 | 11.4 | 11.2 | 44.6 | 22.3 | 87.1 |
| products_html | 7.1 | 12.8 | 15.1 | 40.6 | 27.2 | 147.2 |
| product_html | 7.2 | 10.7 | 10.5 | 33.9 | 23.2 | 86.7 |
| home_json | 2.7 | 5.2 | 3.9 | 22.5 | 9.3 | 83.1 |
| products_json | 4.0 | 6.3 | 9.3 | 21.9 | 14.7 | 68.0 |
| products_partial | 4.0 | 6.6 | 8.2 | 29.1 | 17.4 | 84.9 |

The distributed tail is the biggest regression in the suite: every public route's p95 roughly triples at the
same fixed load. The `account` outlier at signed-in 40 req/s is consistent with a PgDog/Npgsql connection
stall; the route medians are otherwise in the 5-23 ms range.

### 4.2 Memory, allocations and GC

Public 8-VU phase:

| Metric | TechLandier | TechLandierDistributed |
| --- | ---: | ---: |
| App working set | 188.1 MB | 581.9 MB (3 x ~194 MB) |
| Worker + Realtime working set | - | 224.9 MB |
| App CPU (8 logical CPUs) | 30.41% | 21.42% |
| SSR working set | 117.5 MB | 248.9 MB (3 x ~83 MB) |
| SSR CPU | 0.07% | 0.33% |
| Allocation rate | 30.2 MB/s | 24.2 MB/s |
| Managed heap | 19.2 MB | 91.3 MB total (3 web + worker + realtime) |
| Time in GC | 3.69% | 0.80% |
| Total system memory (host processes + Docker DB/cache/pooler/proxy) | 452.1 MB | 1396.8 MB |

Managed heap from `gcdump` after load:

| Process | Heap | Objects | Working set |
| --- | ---: | ---: | ---: |
| TechLandier app | 14.98 MB | 230,163 | 189.0 MB |
| TechLandierDistributed mainapp-1 | 15.91 MB | 232,345 | 185.0 MB |
| TechLandierDistributed mainapp-2 | 15.83 MB | 230,997 | 187.6 MB |
| TechLandierDistributed mainapp-3 | 15.41 MB | 225,557 | 191.1 MB |
| TechLandierDistributed worker | 6.03 MB | 78,698 | 110.4 MB |
| TechLandierDistributed realtime | 7.00 MB | 91,737 | 116.6 MB |
| **Distributed total** | **60.18 MB** | **859,334** | **790.7 MB** |

Each web process carries the same roughly 0.9 MB of `StackExchange.Redis.RawResult[]` buffers as the CarParker
replicas. The extra processes, not a larger per-process heap, account for the memory increase.

### 4.3 DB and cache

| Phase | Variant | PG xacts/request | PG tuples fetched/request | Cache Redis commands/request | Cache hit rate | Cache Redis memory | Critical Redis hit rate |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Public 100 req/s | TechLandier | 6.487 | 44.81 | - | - | - | - |
| Public 100 req/s | TechLandierDistributed | 32.944 | 149.34 | 4.62 | 99.7% | 2.11 MB | 50.0% |
| Public 8 VUs | TechLandier | 3.496 | 18.65 | - | - | - | - |
| Public 8 VUs | TechLandierDistributed | 14.700 | 65.93 | 4.03 | 99.9% | 2.19 MB | 50.0% |
| Signed-in 40 req/s | TechLandier | 42.410 | 219.23 | - | - | - | - |
| Signed-in 40 req/s | TechLandierDistributed | 85.391 | 390.33 | 24.02 | 99.9% | 2.08 MB | 55.0% |
| Signed-in 8 VUs | TechLandier | 11.375 | 50.32 | - | - | - | - |
| Signed-in 8 VUs | TechLandierDistributed | 22.676 | 112.95 | 6.94 | 99.9% | 2.06 MB | 58.3% |

PostgreSQL block-cache hit is 99.8-100% for both, and the Inertia cache Redis is at 99.9% hit rate with about
2 MB of data and 18-24 keys. The critical Redis (Data Protection, rate limits, backplane) hits about 50-58%,
which is expected for a mixed key space. As with CarParker, the DB xact/tuple counts are directional: PgDog's
transaction pool and the extra role health checks change the accounting, so use HTTP latency for the primary
comparison.

The distributed sample also runs three PostgreSQL servers, PgDog, two Redis servers and HAProxy. Their Docker
memory at saturation was approximately:

| Container | Memory |
| --- | ---: |
| PostgreSQL primary | 134.4 MiB |
| PostgreSQL replica 1 | 47.6 MiB |
| PostgreSQL replica 2 | 47.2 MiB |
| PgDog | 22.2 MiB |
| Critical Redis | 6.6 MiB |
| Cache Redis | 6.5 MiB |
| HAProxy | 76.7 MiB |
| **Docker total** | **341.1 MiB** |

### 4.4 SSR cache off

| Variant | RPS target | RPS delivered | p50 ms | p95 ms | p99 ms | SSR CPU | SSR cache | Dropped |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| TechLandier | 60 | 54.4 | 28.3 | 76.5 | 128.6 | 9.10% | 0% | 0 |
| TechLandierDistributed | 30 | 29.2 | 25.8 | 80.3 | 161.9 | 7.07% | 0% | 0 |

The distributed stack could not sustain the 60 req/s cache-off rate on this host; 30 req/s was the stable run.
Even at half the arrival rate its p95 is slightly worse than the single process at 60 req/s. With the cache on,
the same distributed stack delivered 227.3 req/s at 8 VUs with 99.9% cache hits.

## 5. What the numbers mean

1. **Replication is not a single-host optimization.** Both replica samples are built to scale across hosts,
   survive process/node loss and separate concerns. On one 4-core laptop they add processes, network hops and
   duplicate runtimes without adding CPU or memory. The single-process baselines win on throughput and latency;
   the replica stacks win on availability and operational isolation, which this benchmark does not measure.
2. **TechLandier distribution is the heavier trade-off on this host.** CarParkerReplicas loses 13-20% of
   saturation throughput; TechLandierDistributed loses 36-47%. The distributed sample adds PgDog, two read
   replicas, two Redis instances, a proxy, a worker, a realtime tier and a standalone SSR pool. On localhost
   those hops cost more than the read scale saves.
3. **Low fixed load is safe.** At 100 public req/s and 40 signed-in req/s, every stack kept up with zero
   failures and zero drops. The latency penalty is visible but small for CarParker; TechLandierDistributed has
   a worse tail and one localized `/account` ~2 s stall that deserves follow-up.
4. **The SSR cache is the dominant lever.** Turning it off raises CarParker p50 from 9-13 ms to 14-18 ms,
   TechLandier p50 from 6 ms to 28 ms, and collapses the distributed public capacity. Keep the cache on.
5. **Memory scales with process count, not with a leak.** Each .NET web process stabilizes around 8-16 MB of
   managed heap and 150-190 MB working set. The three-replica CarParker stack uses about 1.0 GB total; the
   distributed TechLandier stack uses about 1.4 GB. Roughly 0.9 MB per process is Redis result buffers.
6. **GC behaves better per process.** Time in GC falls from 8.86% to 2.23% (CarParker) and from 3.69% to 0.80%
   (TechLandier) because each replica has a smaller heap. Total allocation is similar or lower. This is a real
   benefit of process isolation, even though it does not translate into higher single-host throughput.
7. **The caches are working.** Inertia SSR cache hit is 100% in every warmed run; shared Redis prop/SSR cache
   hit is 99.0-99.9%; MongoDB WiredTiger and PostgreSQL block caches are at 100%. The DB ops/request increase
   is measurement overhead and extra connection pools, not cache failure.
8. **Watch PgDog/Npgsql connection startup.** The distributed stack's first warmup had p99 2051 ms and 0% SSR
   cache; after a second warmup it was p99 69 ms and 100% cache. The `/account` p99 2071 ms at 40 req/s points
   at the same area: occasional connection establishment/checkout stalls. Pre-warming pools, checking PgDog
   `SHOW POOLS`/`maxwait`, and reviewing Npgsql connect timeouts and per-role pool sizes are the first follow-ups.

## 6. Recommendations

1. Do not use these single-host numbers to size production. Re-run on separate hosts (or at least separate
   CPU/memory budgets) to measure real horizontal scaling.
2. Keep the Inertia SSR cache enabled. If SSR must be disabled for freshness, scale the SSR pool and add an
   SSR-aware proxy with active health checks.
3. Investigate the TechLandierDistributed `/account` tail: PgDog pool warm-up, Npgsql `Maximum Pool Size`,
   connect timeout, and health-check overlap. The medians are good; the tail is not.
4. Budget memory per replica: about 150 MB for the .NET process, 75-85 MB for its Bun SSR process (or 3 x 83 MB
   for the standalone pool), plus shared Redis/DB/pooler/proxy. CarParkerReplicas is about 1 GB at three
   replicas; TechLandierDistributed is about 1.4 GB at this role count.
5. Use the page probe from `benchmarks/page-probe.ps1` (or an equivalent per-page probe) if exact DB
   operations per page are needed. The load-level `opcounters`/`pg_stat_database` numbers include connection,
   session, health-check and pooler overhead.
6. On Windows, do not benchmark through Dockerized HAProxy. It halves throughput and adds ~33 ms to p50. Use a
   host proxy or a Linux host for proxy-path measurements.

## 7. Reproduce

The harness is in `benchmarks/replica-comparison/`:

```powershell
# Infrastructure
docker compose -f samples/CarParkerReplicas/docker-compose.dev.yml up -d
docker compose -f samples/TechLandierDistributed/docker-compose.dev.yml up -d

# One phase (stack must already be running)
pwsh benchmarks/replica-comparison/start-stack.ps1 -Variant CarParker
pwsh benchmarks/replica-comparison/measure.ps1 -Variant CarParker -Name public-vus8 -Profile vus -Vus 8 -Duration 30s
pwsh benchmarks/replica-comparison/profile.ps1 -Variant CarParker -Tag loaded
pwsh benchmarks/replica-comparison/stop-stack.ps1 -Variant CarParker

# SSR cache off
pwsh benchmarks/replica-comparison/start-stack.ps1 -Variant CarParker -SsrCacheSeconds 0 -SsrStaleSeconds 0
pwsh benchmarks/replica-comparison/measure.ps1 -Variant CarParker -Name public-ssr0-rps60 -Profile rps -Rps 60 -Duration 30s

# Combined Markdown summary
python benchmarks/replica-comparison/summarize.py > benchmarks/replica-comparison/summary.md
```

Raw result directories contain `result.json`, `k6.json`, `counters-<pid>.csv`, `process.json`,
`before.json`, `after.json` and the phase's logs. The memory profile directories contain the `.gcdump` files
and their type reports.

## 8. Bottom line

The replica and distributed samples do what they are designed to do - they replicate correctly, share cache
and state, and answer every request with zero failures - but they are **not faster on a single laptop**. The
single-process baselines remain the right choice for raw single-host throughput. The replica stacks trade
13-47% of saturation throughput and roughly 2-3x memory for horizontal scale, failure isolation and independent
role scaling, which need multiple hosts to show their value. The strongest measured optimizations are still the
SSR cache, the shared Inertia cache and keeping per-process heaps small; all three were confirmed here.
