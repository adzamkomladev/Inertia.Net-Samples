# Inertia.Net 1.5.0 / 1.5.1 upgrade and measurements

Inkwell (React/SQLite), CarParker (Svelte/MongoDB) and TechLandier (Vue/PostgreSQL) moved from
**Inertia.Net 1.4.0 to 1.5.1** on 2026-10-23. Unlike the earlier version-only rounds, this one adopts the
1.5.0 cache lifecycle APIs and measures the result: request throughput and percentiles, process and SSR
memory, CPU and GC, database operations and cache outcomes, before and after.

The three replica samples (`ReactSqliteReplicasBlog`, `SvelteMongoReplicasBlog`,
`VuePgReplicasAdvBlog`) remain on 1.4.0.

## What 1.5.0 and 1.5.1 add

1.5.0 is the feature release: cached props and SSR renders can be invalidated, replaced, served stale while
they refresh, and kept in a store of the app's choice. 1.5.1 is the allocation/coalescing pass: it reduces
temporary cache serialization buffers and prop wrapper allocations and merges concurrent cache misses per
key and app instance. No public API was removed; apps that use nothing new behave as in 1.4.0.

The parts adopted here:

| API | Use |
| --- | --- |
| `InertiaProp.Cache(key, duration, staleFor, tags)` | Dashboard props stay fresh for 60 s, keep serving for another 30-120 s while one request refreshes, and carry tags |
| `IInertiaCache.RemoveByTagAsync` | Domain mutations clear only the props they changed |
| `Ssr.Cache.StaleFor` | An expired server render is served while one request re-renders it |
| `Inertia.Net` 1.5.1 cache-miss coalescing | Concurrent misses for the same key share one fill |
| `IInertiaCache.SetPropAsync` | Not used: no page needs to write a computed value outside its loader |

`IInertiaCacheStore`, cross-instance shared stores, CSP nonces and generated TypeScript declarations were
not needed by these samples.

## Per-sample adoption

| Sample | Tagged props | Invalidation points |
| --- | --- | --- |
| Inkwell | `analytics:user:{id}` and `analytics:post:{id}` on the overview summary/series/top posts and post analytics | Post create, update and delete (`PostsService`) |
| CarParker | `analytics` on KPIs, series, top lots and refund mix | Lot create/edit, spot override, refund approve/pay/reject, booking create/pay/cancel, check-in/out, force-close and overstay resolution |
| TechLandier | `catalog` on categories and related products, `home` on the two rails, `sales` on admin KPIs/charts/stock, `product:{id}` on related products | Product/category create/edit, restock, checkout, order status/refund, payment settlement and installment payment |

Stale windows:

| Sample | Prop fresh / stale | SSR fresh / stale (defaults) |
| --- | --- | --- |
| Inkwell | 60 s / 120 s | 300 s / 60 s |
| CarParker | 60 s / 30 s | 300 s / 60 s |
| TechLandier | 60 s / 30 s (rails 60/60, catalog 5 min/5 min, related 2 min/2 min) | 300 s / 60 s |

The SSR windows are configurable with `Inertia:SsrCacheSeconds` and the new `Inertia:SsrStaleSeconds`.
Views, likes and comments keep the dashboard's existing eventual-consistency window; structural changes
(new, edited or deleted posts, catalog edits, bookings, payments and refunds) invalidate immediately.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build` for all three solutions (Release) | pass, 0 warnings |
| Inkwell tests | 106 passed (includes a new end-to-end cache-invalidation test) |
| CarParker tests | 66 passed |
| TechLandier tests | 33 passed (includes a tagged-cache invalidation test) |
| `bun run typecheck` (Inkwell, CarParker, TechLandier) | pass, 0 errors |
| Playwright browse (desktop + 390 px) | 0 console errors; Inkwell create-post → analytics 8 → 9 posts without waiting for TTL; CarParker and TechLandier admin analytics render; TechLandier catalog and mobile storefront render |

The TechLandier opt-in `InertiaPerformanceTests` had an old assertion for a `posts` prop on the products
page; it now asserts `products`. The test is skipped unless `INERTIA_BENCHMARK_OUTPUT` is set.

## Benchmark method

Environment: Windows 11 10.0.26300, Intel Core i5-8265U (4 cores / 8 threads), 16 GB RAM, .NET SDK
10.0.402, ASP.NET Core 10.0.13. The app, k6, databases and `dotnet-counters` share the laptop, so the
absolute values are not a capacity guarantee; only same-host before/after comparisons are meaningful.

For each sample:

- Published Release build, `ASPNETCORE_ENVIRONMENT=Production`, server GC off, jobs and cron off.
- Same demo database for both versions: Inkwell 5,000 users / 40,000 posts (the sample's documented
  demo seed), CarParker's 5 lots / 144 spots / 30 reservations / 13 sessions / 3 refunds, TechLandier's
  10 categories / 121 products / 400 orders / 31 sales-stat days. No additional seed was run for the
  benchmark.
- k6 v1.3.0, read-only mixed workload: full HTML, Inertia JSON and partial reloads; no login and no writes.
- 10 s warmup, 30 s measured, two to six alternating rounds. Tables show medians; cache-stress runs also
  show the range because they are noisy.
- `dotnet-counters` sampled `System.Runtime` and the `Inertia.Net` meter; a 250 ms sampler recorded the
  dotnet process and its managed Bun SSR child.
- Database counters: `pg_stat_database` (PostgreSQL), `serverStatus().opcounters` (MongoDB) and EF Core
  command logs (SQLite). Counters are sampled before and after the measured window.
- The SSR cache default (300 s) is used for the steady-state runs. The stress runs shorten it to 2 s so
  expiry is frequent, with and without `StaleFor`.

The harness is committed under [`benchmarks/`](../benchmarks/README.md); raw summaries are in
[`benchmarks/results/`](../benchmarks/results/).

## Steady state: 150 requests/second

Medians of 4 rounds (CarParker 6 rounds because its first pair was noisy). RPS is the delivered rate;
the target was 150/s in every run.

| Sample | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s | GC pause % |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.4.0 | 149.8 | 3.56 | 7.89 | 10.32 | 165.0 | 11.0 | 24.0 | 1.03 |
| Inkwell | 1.5.1 | 149.8 | 3.45 | 7.72 | 10.40 | 165.2 | 10.9 | 23.9 | 1.04 |
| CarParker | 1.4.0 | 149.8 | 11.23 | 19.09 | 25.66 | 136.3 | 16.5 | 26.3 | 2.28 |
| CarParker | 1.5.1 | 149.7 | 12.19 | 22.88 | 30.10 | 134.1 | 17.9 | 26.2 | 2.75 |
| TechLandier | 1.4.0 | 149.9 | 5.22 | 10.51 | 13.14 | 165.9 | 11.9 | 10.6 | 0.29 |
| TechLandier | 1.5.1 | 149.9 | 5.19 | 9.35 | 11.43 | 165.8 | 11.9 | 10.6 | 0.12 |

- **Inkwell:** flat (p95 −2%, p99 within 1%, RSS and allocation unchanged).
- **CarParker:** p95 +20% and p99 +17% at this low load, with identical request rate and query rate.
  Saturation and all component counters are unchanged; the extra cost is per-request work in the new
  adapter, not database or SSR load. This is the one regression found and is reported rather than hidden.
- **TechLandier:** p95 −11%, p99 −13%; RSS, CPU and allocation unchanged.

## Saturation: 8 virtual users

Medians of 4 rounds. This is the throughput ceiling of each app on this laptop.

| Sample | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s | GC pause % |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.4.0 | 505.8 | 14.13 | 26.42 | 34.16 | 169.7 | 53.1 | 81.1 | 14.14 |
| Inkwell | 1.5.1 | 510.2 | 13.89 | 26.16 | 34.31 | 170.9 | 54.0 | 81.7 | 13.68 |
| CarParker | 1.4.0 | 262.3 | 28.19 | 45.03 | 55.83 | 138.1 | 36.5 | 45.9 | 9.82 |
| CarParker | 1.5.1 | 261.5 | 28.23 | 45.58 | 55.66 | 137.7 | 36.1 | 45.8 | 10.45 |
| TechLandier | 1.4.0 | 413.1 | 16.88 | 29.98 | 39.42 | 173.4 | 42.4 | 29.1 | 4.72 |
| TechLandier | 1.5.1 | 422.3 | 16.71 | 28.78 | 36.68 | 174.0 | 42.1 | 29.7 | 4.25 |

Changes: Inkwell +0.9% requests/s, CarParker −0.3%, TechLandier +2.2%. P95 moves −1.0%, +1.2% and
−4.0%. RSS moves within ±1%. There is no material throughput or memory change at saturation; the
adapter is a small part of a page that also serializes application data and runs database work.

## Cache expiry stress: 2 s fresh window, 250 requests/second

This isolates the new cache behavior. The same page keys expire every two seconds, so without coalescing
or a stale window each expiry lets several concurrent requests render the same page again.

| Sample | Version | SSR renders* | p95 ms (range) | p99 ms (range) | App RSS MB |
| --- | --- | ---: | ---: | ---: | ---: |
| Inkwell | 1.4.0 | 65 | 10.1 | 14.5 | 170.4 |
| Inkwell | 1.5.1, stale 60 s | 45 | 9.8 | 12.9 | 171.0 |
| CarParker | 1.4.0 | 77–131 (median 131) | 130–845 (median 666) | 186–884 (median 884) | 153.1 |
| CarParker | 1.5.1, stale 0 | 30 | 233 | 319 | — |
| CarParker | 1.5.1, stale 60 s | 30 | 87–545 (median 185) | 134–746 (median 254) | 146.3 |
| TechLandier | 1.4.0 | 137 | 17.1 | 76.3 | 177.5 |
| TechLandier | 1.5.1, stale 0 | 30 | 25.0 | 73.5 | — |
| TechLandier | 1.5.1, stale 60 s | 45 | 15.9 | 35.2 | 174.0 |

\* The `Inertia.Net` SSR outcome counter over the measured window; lower is better because it means the
cache absorbed the burst.

The deterministic result is the render count: 1.5.1 coalesces concurrent misses, so CarParker rendered
**30 times instead of 77–131**, and TechLandier **45 instead of 137**, even when `StaleFor` is zero.
`StaleFor` then removes the latency of the remaining refreshes: TechLandier's p99 fell from 73.5 ms
(stale 0) to 35.2 ms, and CarParker's median p95 from 233 ms (stale 0) to 185 ms. The CarParker latency
ranges remain wide because 250 req/s is close to its ~262 req/s ceiling; the render count is the stable
signal.

## Components

### Managed Bun SSR process, cache off

At 80 req/s for Inkwell and CarParker, and 40 req/s for TechLandier (80 overloads its SSR queue), so
every public HTML visit really renders in Bun.

| Sample | Version | SSR renders | SSR CPU % | SSR RSS MB | App CPU % | p95 ms | p99 ms |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.4.0 | 1,303 | 3.10 | 156.6 | 8.0 | 18.2 | 23.6 |
| Inkwell | 1.5.1 | 1,308 | 3.78 | 156.3 | 7.6 | 18.3 | 24.0 |
| CarParker | 1.4.0 | 1,738 | 5.35 | 139.2 | 12.4 | 34.5 | 61.6 |
| CarParker | 1.5.1 | 1,689 | 4.38 | 137.3 | 11.1 | 34.5 | 49.9 |
| TechLandier | 1.4.0 | 538 | 5.43 | 156.4 | 6.5 | 38.9 | 89.0 |
| TechLandier | 1.5.1 | 552 | 5.15 | 153.9 | 6.0 | 36.5 | 44.5 |

The Bun process is essentially flat in memory and CPU; CarParker and TechLandier show a better tail,
consistent with the render allocation changes. With the SSR cache on (steady state) the Bun process is
idle at about 0.04% CPU and 90 MB RSS, because every HTML response is a cache hit.

### Database and cache counters

| Sample | Version | DB ops/s at 150 req/s | Cache signal |
| --- | --- | ---: | --- |
| Inkwell | 1.4.0 | 3 EF commands/request → ~373/s at 124 req/s | SSR cache 100% hits in steady state |
| Inkwell | 1.5.1 | 3 EF commands/request → ~362/s at 121 req/s | SSR cache 100% hits |
| CarParker | 1.4.0 | 187.4 Mongo finds/s | SSR cache 100% hits |
| CarParker | 1.5.1 | 187.4 Mongo finds/s | SSR cache 100% hits |
| TechLandier | 1.4.0 | 309.1 SQL statements/s | SSR cache 100% hits |
| TechLandier | 1.5.1 | 310.3 SQL statements/s | SSR cache 100% hits |

Cache tags and stale metadata add no database work: query rates are identical within 0.5%. The L1/L2
cache hit indicators are the SSR outcome counter (`reason=cache`) and, for props, the absence of the
loader queries when a dashboard is warm. Inkwell's new test and the Playwright run both proved the
invalidation path: after creating a post, the analytics page showed 9 posts immediately instead of the
cached 8.

### Idle memory

10 s after a healthy start, no load:

| Sample | 1.4.0 app RSS | 1.5.1 app RSS | 1.4.0 SSR RSS | 1.5.1 SSR RSS |
| --- | ---: | ---: | ---: | ---: |
| Inkwell | 139.1 MB | 139.9 MB | 70.5 MB | 70.4 MB |
| CarParker | 103.1 MB | 113.0 MB | 66.9 MB | 66.9 MB |
| TechLandier | 133.1 MB | 137.0 MB | 72.3 MB | 55.3 MB* |

\* The TechLandier idle snapshot caught the Bun process before it had fully warmed; the loaded runs put
it at 154–170 MB in both versions. The dotnet idle deltas are within the startup/JIT variance of a single
10 s snapshot; the loaded medians (±1%) are the more reliable memory comparison.

## Render microbenchmarks

The samples' opt-in `InertiaPerformanceTests` run five 500-request rounds per mode through the real app
host, with a canned SSR response. Median of rounds 2–5, Inkwell:

| Mode | 1.4.0 allocated B/req | 1.5.1 allocated B/req | 1.4.0 p50 ms | 1.5.1 p50 ms |
| --- | ---: | ---: | ---: | ---: |
| HTML + Vite tags | 108,640 | 109,528 | 4.27 | 4.59 |
| JSON, 20 props | 106,840 | 106,872 | 3.71 | 3.66 |
| Partial reload | 106,194 | 106,209 | 3.79 | 3.62 |
| SSR parser | 168,864 | 168,834 | 4.00 | 4.02 |

The adapter's own 1.5.1 saving (release notes: 2,760 → 2,152 B JSON and 1,592 → 984 B partial) is below
the noise floor here because a real page's props, Vite tags and the test host allocate 100–200 KB per
request. The other two samples show the same pattern. The allocation win is real at the adapter layer but
does not move these content-heavy pages; the request-level results above are the better guide.

## Reproduce

```sh
# build 1.4.0 and 1.5.1 artifacts as the Dockerfile does (publish + copy ssr/ and wwwroot/build)
pwsh benchmarks/matrix.ps1 -App inkwell
pwsh benchmarks/idle.ps1 -App inkwell -Version 1.4.0
pwsh benchmarks/idle.ps1 -App inkwell -Version 1.5.1
pwsh benchmarks/query-probe.ps1 -Version 1.5.1
```

See [`benchmarks/README.md`](../benchmarks/README.md) for prerequisites, the k6 workload and every
environment variable. The raw JSON summaries retain the per-route percentiles, process samples,
`dotnet-counters` aggregates and database deltas used in these tables.

## Bottom line

- 1.5.1 is safe at 1.4.0's throughput and memory on all three samples, and improves TechLandier's
  low-load tail (p95 −11%, p99 −13%) and its saturation tail (p99 −7%).
- CarParker shows a real low-load p95 regression (+20%) with unchanged DB/SSR/CPU totals. Worth an
  upstream issue if it reproduces elsewhere; it does not affect its saturation ceiling.
- The 1.5.0 cache lifecycle is the bigger operational win: dashboards stay correct after writes instead
  of waiting for a TTL, and cache expiry no longer causes a render stampede. Under the synthetic 2 s TTL,
  coalescing cut SSR renders by 31–77% and `StaleFor` cut the remaining tail.
- Process memory does not decrease, matching the 1.5.1 release note; the allocation reduction is visible
  at the adapter layer, not at the page level of these apps.

## Follow-up

The performance work planned after this upgrade (shared notification/auth caches, dashboard query
parallelization, deferred TechLandier widgets and the CarParker single-spot read) is measured in
[`PERFORMANCE_OPTIMIZATION_RESULTS.md`](PERFORMANCE_OPTIMIZATION_RESULTS.md).
