# Samples overhaul report

All five samples are overhauled: Inkwell, TechLandier, TechLandierDistributed, CarParker and CarParkerReplicas.
Research behind the UI changes is in [UI_UX_RESEARCH.md](UI_UX_RESEARCH.md). The impeccable critique ran two rounds
per app (the plan said three; reduced to two at the owner's request on 2026-11-16).

## At a glance

| App | Public req/s | Signed-in req/s | Signed-in p50 | Idle DB work | Idle worker CPU |
|---|---:|---:|---:|---:|---:|
| Inkwell | 314 → 889 | 302 → 828 | 24 → 7.5 ms | 340 → 1 EF commands/min | 15.6 → 1.5 s/min |
| TechLandier | 301 → 603 | 219 → 343 | 34 → 21 ms | 144.5 → 110.5 ops/min | 2.35 → 1.65 s/min |
| CarParker | 230 → 350 | 144 → 183 | 53 → 42 ms | 241.5 → 128 Mongo ops/min | 2.25 → 1.84 s/min |

The two copies (TechLandierDistributed, CarParkerReplicas) carry the same code paths and were verified by build, tests
and, for TechLandierDistributed, the full compose stack; they were not benchmarked separately.

Across all apps:

- Every sample now runs all background work on Runwick. The custom database outboxes and their pollers are gone,
  and that is most of the idle saving.
- Every page has a boot skeleton, non-blocking fonts, one toaster, and a phone layout for customers and staff (drawers,
  bottom bars, sticky action bars, tables that turn into cards, 44 px targets).
- Every sample has branded responsive emails with plain-text twins and a Development `/dev/emails` preview. The
  store and parking apps also have QuestPDF receipts generated on request.
- Security: CSP with nonces and the usual headers, Runwick refuses its sample login outside Development, rate limits on
  writes, and signed upload sizes with magic-byte checks. Bugs fixed along the way: a realtime role-claim mismatch,
  a guest support ticket able to attach to another customer's order, and the HAProxy scheme.
- Each benchmark caught one regression before it shipped: a correlated subquery in the Inkwell inbox, a `Once` expiry
  that defeated the TechLandier SSR cache, and a k6 script broken by the CSP nonce. All three are fixed.

Still open (details in the CarParker section): a few CarParker staff screens still print UTC, some phone touch targets are under 44 px,
CarParker has no forgot-password flow, and the Runwick 0.1.3 start-up schedule budget (250 ms) needs an upstream fix.

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

## TechLandierDistributed

The TechLandier overhaul, ported by hand. The copy had drifted before the overhaul: its own paging shape,
Redis-backed cache and rate limits, and a split into web (`MainApp`), `Worker`, `Realtime` and `Dashboard` hosts
over a shared `TechLandier.Core`. Those differences were kept.

- Jobs: the custom outbox and its tables are gone (`DropLegacyJobs` migration). The web role only stages Runwick jobs
  in the same transaction as the business write; the Worker owns every handler, pool and the scheduler. The realtime
  role never stages jobs.
- Cache: `RedisInertiaCacheStore` pipelines its commands. A set or remove reads the old tags once and sends the rest in
  one `IBatch`; tag invalidation is two round trips per scan page instead of several per member.
- Paging: admin care and deliveries moved to numbered pages (`NumberedPage<T>`), the other lists keep `{ data, page,
  hasMore }`.
- Dashboard: refuses the sample login outside Development, rate-limits sign-in, checks the logout `Origin`, and now
  applies the trusted forwarded headers so those checks see the browser's scheme and address behind HAProxy (found by
  the Codex review). Compose defaults moved off the sample login.

Verified on the full compose stack (`--env-file .env.example`: 3 web replicas, 2 workers, 2 realtime, PgDog with
two read replicas, Redis, HAProxy on 8185). Store, account and admin pages render at phone width with no console
errors or horizontal overflow, the Redis cache fills with prop, SSR and tag keys, crons run on the Worker, and a sign-up
staged its verification email through PgDog, which the Worker picked up. Build: 0 warnings. Tests: 95 pass.

The stack run found one bug in both TechLandier samples: the CSP sent `upgrade-insecure-requests` on every
non-Development response, so on a plain-HTTP origin the post-login redirect was upgraded to an `https://` port nothing
served. The directive is now sent only when the request arrived over HTTPS (directly or via a trusted
`X-Forwarded-Proto`).

## CarParker

MongoDB 8 replica set in Docker on the same machine; the Runwick store is SQLite. Benchmark runs use a Release publish,
throwaway `RUNWICK_ADMIN_*` credentials and the SSR sidecar on its usual port.

### Load (k6, 8 VUs, 30 s)

| Run | Base req/s | After req/s | Base p50 / p95 | After p50 / p95 | App CPU base → after |
|---|---:|---:|---:|---:|---:|
| Public pages (home, lots, lot, JSON and partial reloads) | 230 | 350 | 32 / 54 ms | 21 / 38 ms | 37 % → 25 % |
| Signed-in pages (account, bookings, lot, guard, admin) | 144 | 183 | 53 / 91 ms | 42 / 80 ms | 35 % → 28 % |

Per-route p50, public: home 37 → 23 ms, lots 37 → 23 ms, lot 26 → 12 ms, lots JSON 28 → 22 ms. Signed-in: lot 33 → 17 ms,
lots 45 → 29 ms, guard console 50 → 33 ms, admin overview 61 → 42 ms, admin analytics JSON 19 → 11 ms, admin bookings
73 → 58 ms. Account pages held level (59 → 59 ms p50 for the JSON visit): they now carry upcoming and past bookings,
refund state and the overstay card. Mongo queries per signed-in request fell from 4.65 to 4.14, mostly from the
30-second principal cache.

The run found one tooling bug: the k6 auth script matched the page `<script>` tag by exact attributes, and the CSP nonce
added in the security pass broke the match. The regex now accepts any attribute order.

### Idle and workers

| Measure | Base | After |
|---|---:|---:|
| Mongo operations per minute, workers on, no traffic | 241.5 | 128 |
| Worker CPU per minute, idle | 2.25 s | 1.84 s |
| App CPU at rest | 0.54 % | 0.24 % |
| Working set, idle (avg) | 121 MB | 123 MB |

The custom Mongo outbox and its poller are gone; jobs go through Runwick, and the no-show and overstay sweeps update in
batches with projections over 12 new indexes. What remains at idle is Runwick's own schedule and lease work.

### Profiler (signed-in load, 20 s)

| Measure | Base | After |
|---|---:|---:|
| Live managed heap after load (gcdump) | 11.2 MB | 12.1 MB |
| Working set after load | 174 MB | 173 MB |

About the same memory at 27 % more throughput; the one new large entry in the heap is the parsed Vite manifest. Thread
time is dominated by thread-pool waits, as in the other samples; nothing in application code stands out.

### What changed

- Shell: boot skeleton, preloaded fonts, one toaster, a navigation skeleton on tap; focus moves to the new page heading
  and its title is announced after each visit.
- Phone: store drawer, guard and admin bottom bars, sticky Reserve bar with live price, table rows that become tappable
  cards, 44 px targets, the admin bay inspector scrolls into view.
- Booking: the bay map shows availability for the chosen window, lots run on their own clock (DST-safe weekly windows,
  lot time in every view, email and PDF), refund tiers shown before paying and cancelling, Spot Cover off by default.
- Correctness: bookings serialize on the spot inside the Mongo transaction, so two overlapping reservations cannot both
  commit; check-in, checkout, cancel and the sweeps agree on when a bay is free.
- Inertia: Deferred analytics, paged bookings and payments, optimistic preference and alert toggles, Inertia `Link` with
  hover prefetch for internal navigation.
- Email and PDF: branded responsive emails with plain-text twins, a new booking confirmation, a `/dev/emails` preview,
  QuestPDF booking and refund receipts generated on request.
- Security: CSP with nonces, HSTS over HTTPS, the realtime staff group fixed (it checked the wrong role claim), signed
  upload sizes with magic-byte checks, rate limits on booking, resend and logout.
- Critique: two impeccable rounds (26/40, then 25/40 on a stricter source pass covering guard, admin and dark mode),
  snapshots in `samples/CarParker/.impeccable/critique/`. PRODUCT.md and DESIGN.md record the system.
- Left for later: some admin tables and chart labels still print UTC; pagination and plate chips are under 44 px on
  phones; no forgot-password flow; no-show and overstay emails. Single-node mode keeps a best-effort 30 s booking lease;
  replica mode serializes in the transaction.
- Upstream note: Runwick 0.1.3 seeds schedules under a fixed 250 ms budget, which a cold start can exceed and crash the
  host. CarParker warms NodaTime and Cronos before start-up to stay inside it.

## CarParkerReplicas

The CarParker overhaul and both critique rounds, ported as patches (the client app and features match CarParker file
for file; only the project folder names differ). The replica-specific parts were kept: Mongo replica set,
Runwick on its own PostgreSQL store, Redis-backed cache and rate limits, HAProxy in front of several web replicas.

- Booking serialization runs inside the Mongo transaction, so overlapping bookings cannot both commit across replicas;
  the single-node lease fallback in CarParker is not used here.
- HAProxy sent `X-Forwarded-Proto: http` to the app whatever the client used, so secure cookies broke behind TLS
  (found by the Codex review). The header now comes from `PUBLIC_SCHEME` (default `https`, `http` in `.env.example`).
- The principal cache sets its expiry before the load and stores the result only if the user's generation is
  unchanged and the entry has not already expired, so a slow load cannot store a stale principal.
- Build: 0 warnings. Tests: 138 pass, with Postgres 18 for the Runwick store. `Health_endpoint_is_healthy` sometimes
  returns 503 under full-suite load while Runwick reports not ready; it passes alone and predates the overhaul.
- Not re-benchmarked separately: the request path is the CarParker code plus Redis, and the CarParker numbers above
  apply to it.
