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

## TechLandier

Postgres 18 in Docker on the same machine. Benchmark runs set throwaway `RUNWICK_ADMIN_*` credentials, because the
overhauled app now refuses to start outside Development with the sample dashboard login.

### Load (k6, 8 VUs, 30 s, base and after run back to back: `*-final-*`)

| Run | Base req/s | After req/s | Base p50 / p95 | After p50 / p95 | App CPU base → after |
|---|---:|---:|---:|---:|---:|
| Public pages (home, list, product, JSON and partial reloads) | 301 | 603 | 24 / 42 ms | 11 / 23 ms | 41 % → 22 % |
| Signed-in pages (account, orders, product, checkout, admin) | 219 | 343 | 34 / 58 ms | 21 / 42 ms | 38 % → 24 % |

Per-route p50, public: home 24 → 8 ms, products 30 → 14 ms, product 25 → 10 ms, home JSON 14 → 6 ms. Signed-in:
account 37 → 20 ms, checkout 41 → 26 ms, admin overview 27 → 10 ms, admin analytics JSON 15 → 9 ms, admin catalog
47 → 34 ms, admin orders 35 → 28 ms. Twice the traffic at about half the app CPU.

A regression caught by this run: the shared `categories` prop was first sent with `Once(..., 10 minutes)`. The expiry
writes a moving `expiresAt` timestamp into every page object, so no two responses matched and the SSR render cache
never hit. The product page alone went from 16 ms to 51 ms p50 with the SSR process busy (`*-rerun-*` and
`*-prodonly-*` files). Without the expiry it serves 473 req/s at 7 ms p50, against 229 req/s at 16 ms on base
(`*-prodonly2-*`).

Rows read per request went up (signed-in: about 480 → 2,500 tuples). Order lists now show plan progress
("On plan · 2 of 6 paid") through a per-row count of paid installments. On the seeded data the installments table is
273 rows (4 pages), so Postgres scans it instead of using `IX_Installments_PlanId_Number`. This is cheap at this size and
the index takes over as the table grows. Latency fell on every route regardless.

### Idle and workers

| Measure | Base | After |
|---|---:|---:|
| EF commands per minute, workers on, no traffic | 6.5 | 1 |
| Database operations per minute, idle | 144.5 | 110.5 |
| Worker CPU per minute, idle | 2.35 s | 1.65 s |
| Working set, idle worker (avg) | 187 MB | 162 MB |
| App CPU at rest | 0.9 % | 0.1 % |

The custom `Jobs` outbox and its poller are gone; jobs are staged in the same Postgres transaction as the business
write (Runwick.Postgres `StageAsync`), and crons update in batches. The remaining idle operations are Runwick's own
lease and schedule checks.

### Profiler (signed-in load, 20 s)

| Measure | Base | After |
|---|---:|---:|
| Live managed heap after load (gcdump) | 20.1 MB | 20.3 MB |
| Working set after load | 205 MB | 187 MB |
| Request pipeline share of sampled thread time | 20.3 % | 19.2 % |

Same heap at far higher throughput. The first after profile, taken with the SSR cache broken, showed a 56 MB heap and
9 % of thread time blocked in synchronous pipe reads to the SSR process; both went away with the fix.

### What changed

- Shell: inline skeleton in `app.html` for client-rendered pages, cached template, non-blocking fonts, one toaster,
  a navigation skeleton shown on the tap.
- Phone: store drawer, admin drawer with bottom quick-nav, sticky buy and checkout bars with live totals, tables that
  become cards below `md`, 44 px touch targets.
- Inertia: lazy `Live` props on the admin board, `Once` categories, `EncryptHistory` on signed-in pages, deferred
  account widgets, paginated deliveries and care desk.
- Money: one plan language across store and admin, insurance and plan fee shown before checkout, client rounding that
  matches the server, confirm-before-pay on installments.
- Email and PDF: branded responsive emails with plain-text twins and unsubscribe, a `/dev/emails` preview, QuestPDF
  receipts and installment statements generated on request.
- Security: CSP with nonces, HSTS and friends; Runwick refuses default credentials; guest support rate-limited and
  no longer able to attach to other customers' orders; signed upload size and magic-byte checks; write rate limits.
- Critique: two impeccable rounds (25/40 then 25/40 on a deeper pass, P0 1 → 0), snapshots in
  `samples/TechLandier/.impeccable/critique/`. PRODUCT.md and DESIGN.md record the system.
