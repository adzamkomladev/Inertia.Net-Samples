# Performance optimization plan — Inkwell, CarParker, TechLandier

**Status:** planning handoff. Do not implement the earlier chat version; the corrections in
[Decisions](#decisions-from-review) supersede it.

**Baseline:** Inertia.Net 1.5.1 upgrade commit `ea0b3d5`. The measurements and raw data referenced
here were produced before any of the changes below.

**Scope:** `Inkwell` (React/SQLite), `CarParker` (Svelte/MongoDB), `TechLandier` (Vue/PostgreSQL).
The three replica samples are out of scope and remain on 1.4.0.

---

## 1. Decisions from review

| Item | Original suggestion | Final decision for this plan |
| --- | --- | --- |
| Shared `notifications` cache | 15 s fresh / 30 s stale | **5 minutes fresh**, 1 minute stale. Tag invalidation and write-through make the long TTL safe. |
| Inkwell shared `auth` cache | 15–30 s | **3 minutes fresh**, 1 minute stale, per-user tag. Invalidate on profile, theme, email verification and role changes. |
| CarParker shared `auth` | Cache the auth prop | **Do not cache the prop.** It is built from cookie claims and does zero DB queries. The DB cost is the cookie `ValidatePrincipal` lookup; see §5.3. |
| TechLandier shared `auth` | Cache the auth prop | **Do not cache the prop.** Same reason as CarParker. The cookie validation lookup is the only query. |
| CarParker/TechLandier auth validation | Not discussed | Optional: cache `AuthService.FindAsync` for **60 s** (recommended) with invalidation on profile/role/email changes. 3 minutes is possible but delays deleted-user/role-change enforcement. |
| Analytics caching | Add more | **Do not change.** Inkwell analytics is already 0 DB ops and 4.8 ms on the full deferred load; TechLandier admin analytics is 1 op and ~8 ms. |

The two long TTLs are deliberate: both caches are invalidated on the writes that matter and both
values are UI-only. They are not authorization decisions.

---

## 2. Why this plan, in one paragraph

The 1.5.1 measurements show the Inertia adapter is not the bottleneck: SSR was a 100% cache hit,
the managed Bun process sat at ~0.04% CPU while cached, and render allocations were flat at the
application-page level. The cost is **data loading**. Anonymous requests are cheap (Inkwell 3 EF
commands/request, CarParker ~1.25 Mongo finds/request, TechLandier ~2.06 SQL statements/request),
but signed-in pages add three shared queries (cookie validation + unread count + latest
notifications), and several dashboards still run 5–9 independent queries in sequence. The pages
that already use `Defer + Parallel + Cache` are the fastest pages in the suite.

---

## 3. Performance research and analytics

### 3.1 Environment and method

| Item | Value |
| --- | --- |
| Host | Windows 11 10.0.26300, Intel Core i5-8265U (4 cores / 8 threads), 16 GB |
| Runtime | .NET SDK 10.0.402, ASP.NET Core 10.0.13 |
| Load generator | k6 v1.3.0, read-only mixed HTML / Inertia JSON / partial reloads |
| Counters | `dotnet-counters` (`System.Runtime`, `Inertia.Net`), 250 ms process sampler, DB counters |
| App mode | Release publish, `ASPNETCORE_ENVIRONMENT=Production`, server GC off, jobs and cron off |
| Data | Inkwell 5,000 users / 40,000 posts; CarParker 5 lots / 144 spots / 30 reservations / 13 sessions / 3 refunds; TechLandier 10 categories / 121 products / 400 orders / 31 sales-stat days |
| Load runs | 10 s warmup, 30 s measured, 4 alternating rounds (CarParker 150 req/s used 6 rounds) |
| Auth probe | 1 signed-in demo user, 10 requests per page after one warm-up request; exact DB operation counts |

The host is a laptop running the app, databases, k6 and the counters together. Absolute numbers are
not capacity guarantees; compare only same-host deltas.

### 3.2 Anonymous load: 150 requests/second

Medians. RPS is delivered rate; the target was 150/s.

| Sample | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % | Alloc MB/s |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.4.0 | 149.8 | 3.56 | 7.89 | 10.32 | 165.0 | 11.0 | 24.0 |
| Inkwell | 1.5.1 | 149.8 | 3.45 | 7.72 | 10.40 | 165.2 | 10.9 | 23.9 |
| CarParker | 1.4.0 | 149.8 | 11.23 | 19.09 | 25.66 | 136.3 | 16.5 | 26.3 |
| CarParker | 1.5.1 | 149.7 | 12.19 | 22.88 | 30.10 | 134.1 | 17.9 | 26.2 |
| TechLandier | 1.4.0 | 149.9 | 5.22 | 10.51 | 13.14 | 165.9 | 11.9 | 10.6 |
| TechLandier | 1.5.1 | 149.9 | 5.19 | 9.35 | 11.43 | 165.8 | 11.9 | 10.6 |

CarParker’s low-load p95 regression is real and is not caused by the new cache code on its public
routes; do not add public-page caching that could make it worse. TechLandier improved; Inkwell is
flat.

### 3.3 Anonymous load: 8 virtual users (saturation)

| Sample | Version | RPS | p50 ms | p95 ms | p99 ms | App RSS MB | App CPU % |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Inkwell | 1.4.0 | 505.8 | 14.13 | 26.42 | 34.16 | 169.7 | 53.1 |
| Inkwell | 1.5.1 | 510.2 | 13.89 | 26.16 | 34.31 | 170.9 | 54.0 |
| CarParker | 1.4.0 | 262.3 | 28.19 | 45.03 | 55.83 | 138.1 | 36.5 |
| CarParker | 1.5.1 | 261.5 | 28.23 | 45.58 | 55.66 | 137.7 | 36.1 |
| TechLandier | 1.4.0 | 413.1 | 16.88 | 29.98 | 39.42 | 173.4 | 42.4 |
| TechLandier | 1.5.1 | 422.3 | 16.71 | 28.78 | 36.68 | 174.0 | 42.1 |

Saturation is essentially flat; memory and CPU are unchanged. This is why further adapter-level
tuning is not the priority.

### 3.4 Public per-route p50 at 150 requests/second (1.5.1)

| Sample | Route | p50 ms | p95 ms |
| --- | --- | ---: | ---: |
| Inkwell | feed HTML | 5.56 | 9.20 |
| Inkwell | feed JSON | 2.46 | 4.32 |
| Inkwell | post HTML | 4.71 | 7.07 |
| Inkwell | post JSON | 1.70 | 3.00 |
| CarParker | home HTML | 13.39 | 22.57 |
| CarParker | lots HTML | 13.29 | 22.95 |
| CarParker | lot detail HTML | **15.38** | 25.03 |
| CarParker | lots JSON | 8.60 | 15.99 |
| TechLandier | home HTML | 5.66 | 8.99 |
| TechLandier | products HTML | **6.48** | 10.45 |
| TechLandier | product HTML | 6.07 | 9.89 |
| TechLandier | home JSON | 2.39 | 3.59 |
| TechLandier | products JSON | 3.01 | 4.85 |

CarParker’s lot detail page is the slowest public HTML route and reads the same spots twice.
TechLandier’s product list is the slowest of its public HTML routes, but the probe shows only ~1 DB
operation after warm caches; its cost is SSR/HTML, not data, so caching it is a low priority.

### 3.5 Authenticated page probe (1.5.1)

One demo user, 10 requests per page, warm cache, single-user latency. **Ops/request is the reliable
number; p50 is not directly comparable to the 150 req/s load runs.**

| Sample | Page | p50 ms | DB ops/request | Page-specific ops |
| --- | --- | ---: | ---: | --- |
| Inkwell | `/posts` HTML | 16.98 | **6.0** | 3 shared + 3 feed |
| Inkwell | `/posts` JSON | 13.56 | 6.0 | 3 shared + 3 feed |
| Inkwell | `/analytics` shell | 12.80 | 3.0 | 3 shared |
| Inkwell | `/analytics` full deferred | **4.78** | **0.0** | already cached |
| Inkwell | `/settings` | 9.70 | 4.0 | 3 shared + 1 |
| Inkwell | `/notifications` | 11.77 | 4.0 | 3 shared + 1 |
| Inkwell | `/users` | 10.73 | 4.0 | 3 shared + 1 |
| CarParker | `/` | 19.09 | 4.0 | 3 shared + 1 |
| CarParker | `/lots` | 16.50 | 5.0 | 3 shared + 2 |
| CarParker | `/account` | **25.10** | **11.0** | 3 shared + ~9 sequential |
| CarParker | `/guard` | **24.28** | **9.1** | 3 shared + ~5–6 sequential |
| CarParker | `/admin` | 25.80 | 6.2 | 3 shared + overview |
| CarParker | `/admin/analytics` | 11.15 | 4.0 | mostly shared |
| TechLandier | `/` | 15.84 | 4.0 | 3 shared + 1 |
| TechLandier | `/products` | 15.06 | 4.0 | 3 shared + 1 |
| TechLandier | `/products/{slug}` | 18.10 | **5.0** | 3 shared + detail + canReview |
| TechLandier | `/account` | **22.14** | **8.0** | 3 shared + 5 widgets |
| TechLandier | `/admin` | 11.62 | 3.0 | 3 shared + cached KPIs |
| TechLandier | `/admin/analytics` | **8.74** | **1.0** | already cached |

“3 shared” = cookie validation + unread notification count + latest notifications (Inkwell’s version
is shared auth + the two notification queries; it does not do a separate cookie validation query).
The two notification queries are exactly the ones P0 removes.

### 3.6 Database and component counters

| Signal | 1.4.0 | 1.5.1 |
| --- | ---: | ---: |
| Inkwell EF commands, anonymous `/posts` | 3/request | 3/request |
| CarParker Mongo finds at 150 req/s | 187.4/s | 187.4/s |
| TechLandier SQL statements at 150 req/s | 309.1/s | 310.3/s |
| SSR cache hit rate, steady state | 100% | 100% |
| Bun CPU with SSR cache hits | ~0.04% | ~0.04% |
| Bun RSS, SSR cache off, 40–80 req/s | 139–169 MB | 137–170 MB |
| Bun CPU, SSR cache off | 3.1–10.8% | 3.8–10.7% |
| Page-level render allocation | unchanged | unchanged |

SSR cache-expiry stress (2 s TTL, 250 req/s) showed 1.5.1’s coalescing: CarParker rendered 30 times
instead of 77–131, TechLandier 45 instead of 137. That work is already done; this plan is about the
remaining DB/round-trip cost.

### 3.7 Raw data

- `docs/INERTIA_1_5_UPGRADE.md` — full 1.4.0 vs 1.5.1 report.
- `benchmarks/README.md` — harness description.
- `benchmarks/results/*-m-*.json` — anonymous load summaries.
- `benchmarks/results/*-idle-*.json` — idle memory.
- `benchmarks/results/*-auth-probe-*.json` — signed-in page probe.
- `benchmarks/auth-probe.ps1` — the signed-in probe source.

---

## 4. P0 — Shared notifications cache (all three samples)

### Why

Measured 2 DB operations on every authenticated page. Removing them is the largest cross-app
reduction and requires no schema or UI change.

### Policy

| Setting | Value |
| --- | --- |
| Key | `notifications:{userId}` |
| Fresh | **5 minutes** |
| Stale | 1 minute |
| Tag | `notifications:{userId}` |
| Value | the existing `NotificationSummary` / `NotificationSummary` rows |
| Invalidate on | notification created, notification read, mark-all-read, alert created/read, staff alert |
| Write-through | after mark-all-read, optionally `SetPropAsync` with `unread = 0` and the items marked read |

Five minutes is safe because writes invalidate; the UI also receives SignalR notifications, so a
missing badge refresh is not the only update path. If any creation path cannot be made to invalidate,
drop that path to a 60 s TTL before shipping.

### Implementation

1. Add a small `NotificationsCache` static class per app:
   - Inkwell: `Features/Notifications/NotificationsCache.cs`
   - CarParker: `Features/Alerts/AlertsCache.cs`
   - TechLandier: `Features/Notifications/NotificationsCache.cs`
2. In each `InertiaSetup`, change the shared prop from a plain `Prop(...)` to a per-user cached prop:
   ```csharp
   o.Share("notifications", ctx =>
   {
       if (ctx.User.UserId() is not { } id) return Prop((object?)null);
       return Prop(async ct =>
       {
           await using var scope = ctx.RequestServices.CreateAsyncScope();
           return await scope.ServiceProvider.GetRequiredService<NotificationsService>().SummaryAsync(id, ct);
       })
       .Cache($"notifications:{id}", NotificationsCache.FreshFor, NotificationsCache.StaleFor,
           [NotificationsCache.Tag(id)]);
   });
   ```
   Keep the existing service scopes; the cache stores plain data, not scoped services.
3. Invalidate in the service methods that create or change notifications. Put one helper in each
   service and call it from every create/read path:
   - Inkwell `NotificationsService`: `SummaryAsync`, `MarkAllReadAsync`, and the notification
     creation/queue handlers that insert rows.
   - CarParker `AlertsService`: `CreateAsync`, `CreateManyAsync`, `NotifyStaffAsync`,
     `MarkReadAsync`, `MarkAllReadAsync`.
   - TechLandier `AlertsService`: `CreateAsync`/creation helpers, `MarkReadAsync`,
     `MarkAllReadAsync`.
4. After mark-all-read, use `IInertiaCache.SetPropAsync` to write the zero-unread summary instead of
   only removing the tag, so the next render is an instant hit. The summary’s item list must be
   updated to `ReadAt != null`; if that is inconvenient, invalidate instead and accept one refill.
5. Add a test per app:
   - Seed/create a notification, load a page that renders the shared prop, create a second
     notification, load again and assert the new unread count/list.
   - Mark all read, load again, assert zero.
   - Confirm the cache key is per user (two users do not share an entry).

### Expected measured effect (ops/request)

| Sample | Page | Now | After |
| --- | --- | ---: | ---: |
| Inkwell | `/posts` | 6.0 | 4.0 |
| Inkwell | `/settings`, `/users` | 4.0 | 2.0 |
| CarParker | `/` | 4.0 | 2.0 |
| CarParker | `/lots` | 5.0 | 3.0 |
| CarParker | `/account` | 11.0 | 9.0 |
| CarParker | `/guard` | 9.1 | 7.1 |
| TechLandier | `/` | 4.0 | 2.0 |
| TechLandier | `/products` | 4.0 | 2.0 |
| TechLandier | `/products/{slug}` | 5.0 | 3.0 |
| TechLandier | `/account` | 8.0 | 6.0 |
| TechLandier | `/admin` | 3.0 | 1.0 |

No claim is made about p50 until the after-run; the load test is anonymous and cannot show this.

### Risks

- A creation path that does not invalidate can leave a stale badge for up to 5 minutes. Test every
  path; keep the 1-minute stale window as a bound.
- Do not cache the notification **list page** (`/notifications`); it is a dedicated page with its
  own pagination and read actions. Cache only the shared summary.

---

## 5. P1 — CarParker authenticated dashboards

MongoDB’s driver is thread-safe; `Task.WhenAll` is safe. No EF `DbContext` rule applies here.

### 5.1 `/account` — `BookingsService.AccountAsync`

Current sequence (~9 page queries): reservations → next occurrence → occurrence IDs → open session
→ paid payments → spot labels → all lot IDs → lots → unread alerts. Measured 11 total ops, 25 ms p50.

Target stages:

1. confirmed reservations (must stay first).
2. in parallel: next occurrence, occurrence IDs, paid payments, all lot IDs, unread count.
3. in parallel: open session (needs occurrence IDs), spot labels (needs next/open session), lots
   (needs lot IDs).
4. compose.

Watch the null paths: `nextReservation` may be null, and an empty `occurrenceIds` list must not
produce an `$in: []` query that scans. Use the existing guards.

### 5.2 `/guard` and `/guard/board`

- `GuardService.ConsoleAsync`: active lots → chosen lot → parallel spots, reservations, open
  sessions → queue after reservations. Measured 9.1 total ops, 24 ms p50.
- `GuardService.BoardAsync`: active lots → parallel open/exited sessions → spot label lookup.
- These are latency-sensitive gate pages; target roughly 3 stages instead of 5–6.

### 5.3 Auth validation (optional, security review first)

CarParker and TechLandier build the `auth` prop from claims (zero queries). The DB hit is
`AuthHttp.ValidatePrincipal` → `AuthService.FindAsync` on every authenticated request.

Options:

| Option | TTL | Trade-off |
| --- | ---: | --- |
| Leave as-is | — | Safest; one indexed lookup per request |
| Cache `FindAsync` result | 60 s | Recommended; worst-case 60 s delay for deletion/role change |
| Cache `FindAsync` result | 3 min | Matches Inkwell’s auth TTL; larger security window |

If caching, invalidate on profile/role/verification changes. Do **not** cache the `auth` Inertia
prop for CarParker/TechLandier; it adds a cache read with no query saved.

### 5.4 `/admin/bookings/{id}`

`AdminService.BookingAsync` reads reservation → `ViewsAsync` → user. Start the user lookup in
parallel with `ViewsAsync`; both depend only on the reservation.

### Tests and measurement

- Existing `BookingFlowTests`, `SessionFlowTests`, `IncidentTests`, `Guard` tests.
- Add a test that account data is unchanged after the parallel rewrite (compare the DTO).
- `benchmarks/auth-probe.ps1` before/after: total ops stay 9–11, p50 is the metric. Expect lower
  p50 on `/account` and `/guard`; do not claim a specific percentage until measured.

---

## 6. P2 — TechLandier page shaping

### 6.1 `/account`

Currently five parallel props (`stats`, `recentOrders`, `nextInstallment`, `openDeliveries`,
`unreadAlerts`) all block the first response. Measured 8 ops, 22.1 ms — the slowest TechLandier authenticated
page in the probe.

Change:

- Eager: `stats`, `recentOrders`.
- Deferred: `nextInstallment` (group `installment`), `openDeliveries` (group `deliveries`),
  `unreadAlerts` (group `alerts`).
- Add the same loading treatment used by the existing deferred pages (skeleton/empty state) in
  `ClientApp/pages/account/Index.vue` and its components.
- Keep each loader on its own `IDbContextFactory`; the deferred requests still run in parallel after
  paint.

First-response page ops go from 5 to 2; with P0 the shared total drops from 8 to 3. Total work is
unchanged, but TTFB/first paint improves.

### 6.2 `/products/{slug}`

`Store.Show` eagerly awaits `CanReviewAsync` in an `Always(...)` value. Measured 5 ops, ~18 ms.
Move it to its own deferred prop in the existing `reviews` group:

```csharp
["canReview"] = Defer(async c => ctx.User.UserIdOrNull() is { } uid && await store.CanReviewAsync(product.Id, uid, c), "reviews"),
```

Update the Vue page type and treat the value as `boolean | null` until the deferred group arrives.
With P0, eager product-page ops go from 5 to 2 (validation + detail).

### 6.3 `/buy/{id}`

Four sequential queries: product → zones → settings → credit state. `product` is needed for the
404 check. Defer `creditState` into group `credit`; the BNPL selector can show its disabled/loading
state until it arrives. This removes one query from TTFB. Caching `zones`/`settings` is optional and
lower priority; they change rarely and could use the existing `catalog`/settings tags.

### 6.4 Do not touch

- `/admin/analytics`: 1 op, ~8 ms, already cached.
- Home/category/rail caches: already 100% hits; more TTL only increases staleness.
- Product list: ~1 DB op after warm; the remaining 6.5 ms is SSR/HTML, not data.

---

## 7. P2 — Inkwell shared auth and feed/profile round trips

### 7.1 Shared `auth` — 3 minutes

Inkwell loads the user row on every authenticated page (`SharedAuth.LoadAsync`). Measured 1 op on
every signed-in page (and 6 total ops on `/posts`).

Add:

```csharp
o.Share("auth", ctx =>
{
    if (ctx.User.UserId() is not { } id) return Prop(new SharedAuthDto(null));
    return Prop(async ct => await SharedAuth.LoadAsync(ctx, ct))
        .Cache($"auth:{id}", TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(1), [AuthCache.Tag(id)]);
});
```

`AuthCache.Tag(id) => $"auth:{id}"`. Invalidate in:

- `SettingsService.UpdateProfileAsync` and `UpdatePreferencesAsync` (name, avatar, theme),
- email verification confirmation,
- admin/team role changes,
- any other place that changes `DisplayName`, `AvatarUrl`, `EmailConfirmed`, `IsAdmin` or `Theme`.

This is UI data only; authorization continues to use claims. With P0, the signed-in feed drops from
6 to 3 ops (feed queries only) and settings/users/notifications drop from 4 to 1.

### 7.2 Feed and profile round trips

- `SocialService.RankedFeedAsync`: after the snapshot page returns ids, start the card query and a
  new “reposter rows” query concurrently. Merge reposters into the cards after both complete. This
  removes one sequential stage from `/feed`.
- `SocialService.TimelineAsync`: same for cards + reposter user lookup.
- `SocialService.UserProfile`: the route already has the user id, so start `ProfileAsync` and
  `TimelineAsync` together. If the profile is null, return 404 and discard the timeline. Accept the
  wasted timeline query on 404s, or keep the current order if that path is hot.

All of these use the request `AppDb`; do not run them concurrently on the same context. Use
`IDbContextFactory<AppDb>` for the parallel pair, or load the related rows in one query.

### 7.3 Do not touch

Inkwell `/analytics`: 0 DB ops and 4.8 ms on the full deferred load. The existing
`Defer + Parallel + Cache + tags` implementation is the model to copy, not a target for more caching.

---

## 8. P3 — CarParker lot page duplicate query

`Lots.ShowAsync` does `FindActiveAsync → CardsAsync (spot aggregate) → SpotsAsync (full spots)`.
`BookPageAsync` does the same. The spots are read twice.

Change:

1. Fetch `lot` and `spots` in parallel (both depend only on the route id).
2. Compute the card from the already-loaded spots (add a `CardsFromSpots`/`LotCard` helper).
3. Keep `tariff` and `calendar` in memory.

Measured public `lot_html`: 15.4 ms p50, the slowest public HTML route; JSON `/lots` is 8.6 ms.
This removes one query and one round trip without any cache staleness.

Tests: `LotListingTests`, `BookingFlowTests`; verify free/occupied counts and the booking page.

---

## 9. P3 — Optional admin defers

- CarParker `/admin`: the KPI row and the lots table are one response. Defer the lots cards into a
  `lots` group so the KPI row paints first. Measured 6.2 ops, 25.8 ms; admin traffic is low, so do
  this only if the admin console feels slow in use.
- TechLandier `/admin`: 3 ops, 13.1 ms; no change needed.

---

## 10. Implementation order

| Step | Change | Why now |
| --- | --- | --- |
| 1 | P0 notifications cache + invalidation + tests | Largest cross-app reduction; no UI changes |
| 2 | P1 CarParker `/account`, `/guard`, `/guard/board` | Slowest authenticated pages; Mongo-safe |
| 3 | P2 Inkwell shared auth + feed/profile parallel | Removes the remaining shared query and one feed stage |
| 4 | P2 TechLandier `/account` defers + `canReview` + `creditState` | Improves the slowest TL page and product TTFB |
| 5 | P3 CarParker lot page duplicate query | Public-page win with no staleness risk |
| 6 | P3 optional admin defers | Only if measured/observed need |

Each step should be a separate commit so it can be reverted independently.

---

## 11. Verification and acceptance

### Per step

```powershell
# Query counts and single-user page latency
pwsh benchmarks/auth-probe.ps1 -App inkwell -Version 1.5.1 -Requests 10
pwsh benchmarks/auth-probe.ps1 -App carparker -Version 1.5.1 -Requests 10
pwsh benchmarks/auth-probe.ps1 -App techlandier -Version 1.5.1 -Requests 10

# Anonymous load regression check
pwsh benchmarks/matrix.ps1 -App inkwell
pwsh benchmarks/matrix.ps1 -App carparker
pwsh benchmarks/matrix.ps1 -App techlandier
```

### Acceptance criteria

| Change | Must show |
| --- | --- |
| P0 notifications | Exactly 2 fewer ops/request on every authenticated page in the table; no anonymous p95 regression; create/read invalidation tests pass; two-user isolation test passes |
| P1 CarParker account/guard | Same DTO/UI; lower p50 on the auth probe; no increase in anonymous load p95; existing booking/session/guard tests pass |
| P2 Inkwell auth | Auth is cached per user; invalidates after profile/theme/verification/role changes; feed ops fall from 6 to 4 after P0 and 3 after auth |
| P2 Inkwell feed/profile | Same ordering and reposter attribution; one fewer sequential stage; feed/profile tests pass |
| P2 TechLandier account | First response has 2 page props instead of 5; deferred groups render after paint; no layout shift/error in Playwright |
| P2 TechLandier product | `canReview` arrives with the reviews group; review form still appears for eligible users; product page ops fall by 1 eager + 2 shared |
| P2 TechLandier buy | BNPL selector works after `creditState` arrives; no checkout regression |
| P3 CarParker lot page | Same free/occupied counts; one fewer DB query; public p50 does not regress |

### Standard checks

```powershell
cd samples/Inkwell;      dotnet test tests/Inkwell.Tests
cd samples/CarParker;    dotnet test tests/CarParker.Tests
cd samples/TechLandier;  dotnet test tests/TechLandier.Tests   # needs the disposable Postgres at :55432
cd samples/<app>/src/<app>; bun run typecheck
```

Playwright: sign in as each demo user, open the changed pages at desktop and 390 px, exercise the
changed controls, inspect console/network errors, and verify the notification badge after
create/read/mark-all-read.

---

## 12. Risks and guardrails

| Risk | Guardrail |
| --- | --- |
| 5-minute notification staleness | Invalidate on every create/read path; SignalR is an independent update path; 1-minute stale bound |
| 3-minute Inkwell auth staleness | Invalidate on profile/theme/verification/role changes; this is UI data, never authorization |
| CP/TL auth validation cache delays role/deletion enforcement | Prefer 60 s, not 3 min; invalidate on role/profile changes; leave as-is if security review is uncomfortable |
| EF `DbContext` concurrency | Never `Task.WhenAll` on the same context; use `IDbContextFactory` or separate scopes |
| Mongo concurrency | Safe to parallelize; still avoid `$in: []` and unbounded queries |
| Deferred prop layout shift | Add skeletons/empty states; keep above-the-fold data eager |
| CarParker public p95 already regressed in 1.5.1 | Do not add public-page caching without a measured win; monitor the matrix |
| Cache stampede on first load | 1.5.1 coalesces concurrent misses; no extra work needed |
| Over-caching | Do not change Inkwell analytics or TechLandier admin analytics; they are already 0–1 ops and 5–7 ms |

---

## 13. Open decisions for the next session

1. **CP/TL auth validation cache:** 60 s recommended; 3 min only if the security window is accepted.
2. **Notification write-through:** use `SetPropAsync` on mark-all-read (best UX) or just invalidate
   (simpler). Decide during implementation.
3. **TechLandier product detail cache:** not in this plan; revisit only if product-page traffic is
   high and the duplicate `DetailAsync` query shows up in a profile.
4. **Product list cache:** not recommended now; the cost is SSR, not DB.
5. **Auth probe/results:** `benchmarks/auth-probe.ps1` and
   `benchmarks/results/*-auth-probe-*.json` are committed with this plan. Use the same probe for the
   before/after runs.

---

## 14. File map for the next session

| Concern | Files |
| --- | --- |
| Shared props / Inertia options | `samples/*/src/*/Infrastructure/Setup/InertiaSetup.cs` |
| Inkwell notifications | `Features/Notifications/NotificationsService.cs`, `Notifications.cs` |
| CarParker alerts | `Features/Alerts/AlertsService.cs`, `Features/Bookings/Bookings.cs` |
| TechLandier alerts | `Features/Notifications/AlertsService.cs` |
| Inkwell auth | `Infrastructure/Shared.cs`, `Features/Settings/SettingsService.cs` |
| CarParker account | `Features/Bookings/BookingsService.cs` (`AccountAsync`) |
| CarParker guard | `Features/Guard/GuardService.cs` (`ConsoleAsync`, `BoardAsync`) |
| CarParker lots | `Features/Lots/Lots.cs`, `Features/Lots/LotsService.cs` |
| TechLandier account | `Features/Account/Account.cs`, `Features/Account/AccountService.cs` |
| TechLandier product/buy | `Features/Store/Store.cs`, `Features/Orders/Checkout.cs` |
| Inkwell feed/profile | `Features/Social/SocialService.cs` |
| Inkwell analytics (reference only) | `Features/Analytics/AnalyticsFeature.cs` |
| Harness | `benchmarks/README.md`, `benchmarks/run.ps1`, `benchmarks/matrix.ps1`, `benchmarks/auth-probe.ps1` |

---

## 15. Bottom line

Start with P0 and P1. They remove 2 shared queries from every signed-in page and shorten the two
slowest authenticated pages in CarParker, with no schema, no UI redesign and no public-page
staleness. Then do the Inkwell auth/feed work and the TechLandier deferred widgets. The measured
data says this is where the remaining time is; the adapter, SSR and render paths are already in good
shape.
