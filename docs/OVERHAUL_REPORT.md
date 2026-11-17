# Samples overhaul report

Work in progress: each sample's section is added when its overhaul lands. Research behind the UI changes is in
[UI_UX_RESEARCH.md](UI_UX_RESEARCH.md). The impeccable critique ran two rounds per app (the plan said three; reduced to
two at the owner's request on 2026-11-16).

How the numbers were taken: Release builds published with `benchmarks/publish.ps1` (`base` = the commit before the
overhaul, `after` = the overhauled code), run in Production mode on the same Windows machine (8 logical cores) with no
dev servers running, jobs and cron off. k6 v1.3.0, 8 virtual users for 30 s after a 10 s warm-up (`benchmarks/run.ps1`).
Profiles come from `benchmarks/profile.ps1`: a 20 s `dotnet-trace` thread-time sample under the signed-in load, then a
`dotnet-gcdump` once the load settles. Raw results are in `benchmarks/results/`.

## Inkwell

### Load (k6, 8 VUs, 30 s, base and after run back to back)

| Run | Base req/s | After req/s | Base p50 / p95 | After p50 / p95 | App CPU base → after |
|---|---:|---:|---:|---:|---:|
| Public pages (feed, essay, partial reloads) | 314 | 889 | 22 / 45 ms | 7.4 / 16 ms | 45 % → 35 % |
| Signed-in pages (feed, analytics, settings, notifications, team) | 302 | 828 | 24 / 45 ms | 7.5 / 20 ms | 48 % → 42 % |

Per-route p50, signed-in: feed HTML 30 → 10 ms, essay 22 → 6 ms, analytics JSON 13 → 3 ms, settings 23 → 4 ms,
notifications 23 → 6 ms, people directory 28 → 8 ms, team 40 → 19 ms. Allocation per request held at about 0.15 MB,
so the higher allocation rate under load is the higher throughput, not heavier requests. (The first `base` runs, taken
while other work loaded the machine, read 227 and 141 req/s; they are superseded by the `*-rerun` files.)

A regression caught by this run: the grouped notification inbox first used correlated subqueries for the actor and
essay title, and the inbox p50 rose to 84 ms. Rewritten as primary-key left joins, it fell to 6 ms (run
`inkwell-after-preinboxfix-auth-vus8-*` kept for the record).

### Idle and workers

| Measure | Base | After |
|---|---:|---:|
| EF commands per minute with workers on, no traffic | 340 | 1 |
| Worker CPU per minute, idle | 15.6 s | 1.5 s |
| Working set, idle worker | 175 MB | 164 MB |
| App CPU at rest | 0.9 % | 0.2 % |

The custom `Jobs` outbox polled SQLite constantly; with everything on Runwick, an idle Inkwell makes one database
command a minute.

### Profiler (signed-in load, 20 s)

| Measure | Base | After |
|---|---:|---:|
| Requests served in the window | 6,282 | 13,997 |
| Request pipeline share of sampled thread time | 16.8 % | 14.0 % |
| Live managed heap after load (gcdump) | 16.2 MB / 174k objects | 15.3 MB / 163k objects |
| Working set after load | 215 MB | 207 MB |

The after build serves more than twice the requests in a smaller share of thread time. Remaining hot spots are SQLite
reads (`SqliteDataReader.NextResult`) and pipe reads from the SSR sidecar; nothing in application code stands out.
Thread time is wall clock, so idle thread-pool waits dominate both profiles.
