# Performance playbook adoption

The samples were audited against the load-testing playbook written for QuizDesk (staged load tests on
production-like limits, then the fixes it found). Most of the playbook was already in place from the
overhaul (Runwick-staged jobs, tagged prop caches with invalidation after writes, batched sweeps over
indexes, claims with rowcount checks, per-lot booking serialization). This pass closes the remaining gaps,
sample by sample. Every change is verified by the per-sample test suites.

## Package versions

- Inertia.Net 1.9.2 → **1.9.5** in all five samples (1.9.3 is the framework's own performance pass, 1.9.4
  skips named mounts without a TypeScript output path, 1.9.5 is a security/performance review; all build
  and test with no code changes).
- Runwick 0.1.3 → **0.1.4** (internal hardening; the dashboard mount message is the only surface change).
  The 250 ms schedule-seeding budget the overhaul report works around is unchanged in 0.1.4, so the
  NodaTime/Cronos warm-up stays.

## The playbook's checklist, per sample

### Return the next page from the POST (all five)

A write whose landing page is known now renders that page directly from the POST instead of redirecting and
paying a second full visit. `InertiaOptions.UrlResolver` is wired to a per-request page-url override
(`Infrastructure/PageUrl.cs`), so the page object's `url` is the GET address the browser would have visited
and history, prop-reload keys and later visits stay correct. Converted flows:

| Sample | Flow | Before | After |
| --- | --- | --- | --- |
| Inkwell | login (no returnUrl) | 303 → GET /posts | feed rendered inline at `/posts` |
| Inkwell | register | 303 → GET /email/verify | `Auth/VerifyEmail` inline |
| Inkwell | logout | 303 → GET /login | `Auth/Login` inline |
| Inkwell | publish / update post | 303 → GET /posts/{id} | `Posts/Show` inline |
| TechLandier (+Distributed) | login (customer) | 303 → GET /products | `products/Index` inline |
| TechLandier (+Distributed) | register, logout | 303 → GET … | inline |
| TechLandier (+Distributed) | checkout | 303 → GET /orders/{n}?t= → 302 → GET /account/orders/{n} | `account/Order` rendered inline at `/account/orders/{n}` — one request instead of three |
| CarParker (+Replicas) | login (customer) | 303 → GET /lots | `lots/Index` inline |
| CarParker (+Replicas) | register, logout | 303 → GET … | inline |
| CarParker (+Replicas) | booking create | 303 → GET /account/bookings/{id} | `account/Booking` inline |

Plain (non-Inertia) form posts keep the redirects, so non-JS fallbacks behave exactly as before, and error
outcomes keep the mapped `Back().WithErrors` responses. Because the cookie handler writes the session cookie
but never puts the principal on the request, every sample's sign-in now sets `HttpContext.User` (and sign-out
clears it) — the shared `auth` prop and the antiforgery token the render re-issues both read it. The render
re-issues the script-readable `XSRF-TOKEN` cookie for the new identity (`PageUrl.RefreshXsrf`): the token the
browser holds was minted for the previous identity, and the follow-up page load that used to refresh it no
longer happens.

### Throttle "something changed, reload" broadcasts

- Inkwell: every like/comment/view used to publish an analytics reload per write. The first event of a burst
  still publishes at once; the rest folds into one trailing `events.analytics-reload` job (unique while it is
  queued), so a burst costs two reload waves instead of one per write.
- TechLandier (+Distributed): the admins pulse (per order move, stock alert, escalation) got the same
  treatment through a trailing `events.pulse-reload` job; the raw SignalR push still carries the toast wording.
- CarParker (+Replicas): the storefront pulse debounce on the client is now randomized 400–1200 ms
  (playbook: jitter coordinated reloads so every console does not reload on the same tick); the staff
  consoles' 200 ms badge throttle and client-side coalescing were already in place.
- Trailing jobs rate-limit their dispatches in-process (playbook: skip the dispatch call you know is a
  duplicate); a Runwick uniqueness refusal keeps the in-process mark, any other dispatch failure forgets it,
  and a mark expires after 10 s so a job that never ran only delays the next fold by a window.

### No write that changes nothing useful

- Inkwell: `SeenPosts` upserts ran on every post GET, comment and like; they now run only when the row is
  missing or older than a 10-minute staleness window, and the profile flag moves only when the row actually
  changed. Repost counts were a correlated `COUNT` per card row and per realtime delivery; they now ride a
  denormalized `Posts.RepostCount` bumped in the same transaction as the repost row (migration backfills).
- CarParker (+Replicas): `UpdateLotAsync` skips the realtime fan-out and cache invalidation when the update
  matched but changed nothing ("Lot unchanged."); `OverrideSpotAsync` refuses to rewrite an already-blocked
  spot and treats `ModifiedCount == 0` as "changed while you were editing it"; `ToggleVerifyAsync` is now a
  conditional-update claim (filter on the state it read) instead of a read-then-write.
- TechLandier (+Distributed): `SettlePaymentAsync` is a claim (`UPDATE … WHERE Status = Pending` + rowcount)
  so two admins settling the same payment agree on one.

### Rate limits for shared addresses

Per-address windows are sized for a whole room behind one NAT (the playbook's ×100 of a single user's
budget): `auth` 5–10/min → 500–1,000/min, `register` → 1,000/min (precognition 6,000/min), `password-reset`
→ 500/min, and the guest fallbacks of `uploads`/`views`/`writes` → 3,000–12,000/min. Per-identity limits
remain the abuse control. For anonymous sign-ins, where no identity exists, all three single-app samples
add a per-account guard in the auth service: 30 attempts per 5 minutes per submitted address (in-process;
CarParkerReplicas holds one window per replica and the per-address limiter still bounds the room).

### Every hot query on an index / pools against max_connections

- TechLandier (+Distributed): `docs/DEPLOY.md` gained a "Connection pool sizing" section — sum the app pool,
  the Runwick worker connections and health/migrations against the database's `max_connections`, size the cap
  from the peak "in use" of the heaviest test plus headroom.
- CarParker: the production compose `DATABASE_URL` now carries `maxPoolSize=50`; CarParkerReplicas sizes the
  Runwick Postgres store against `max_connections`.

### Heartbeat and offline indicator (all five)

Every frontend now has a heartbeat that surfaces failed background calls: a `GET /ping` (204, unnamed so the
route generator skips it) probed every 20 s, the timer reset after every completed Inertia visit (that request
already proved the session is alive), and an offline pill appears when the browser reports itself offline or
the last ping fails. React (`use-offline` + `OfflineBanner`), Vue (`useOffline` composable mounted from
`app.ts`), Svelte (`lib/offline.svelte.ts` + `OfflineBanner.svelte`).

## What the playbook already found in place

One commit per request everywhere (jobs staged in the write's transaction), read-mostly data cached with tags
and invalidated after every write, hot queries on indexes (no cross-table `OR` in request paths), batched
sweeps with `SKIP LOCKED` claims and re-run-safe markers, no per-entity timer jobs, booking/check-in
serialization through transactions and conditional updates, live partial reloads that load only what they
show, and Precognition windows separated from submit windows.

## Verification

Per sample: `dotnet test` (Inkwell 121, TechLandier 90, CarParker 126, TechLandierDistributed and
CarParkerReplicas as reported below), plus frontend typecheck and build (`bun run typecheck`, `bun run
build`) for the samples whose client changed.
