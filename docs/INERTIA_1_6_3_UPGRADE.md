# Inertia.Net 1.6.3 upgrade: EF Core pagination

All five samples moved from **Inertia.Net 1.6.0 to 1.6.3** on 2026-11-04. The headline additions are the
[pagination helpers](https://github.com/adzamkomladev/Inertia.Net#pagination) from 1.6.2 —
`PageRequest`/`CursorRequest` binding, `Page<T>`/`CursorPage<T>` values, `Scroll(...)` integration and the new
optional `Inertia.Net.EntityFrameworkCore` package (`ToPageAsync`, `ToCursorPageAsync`) — plus 1.6.3's stricter
rules for cached prop loaders (`staleFor` values refresh on a bounded background worker after the response is
gone, so background loaders must not capture request-scoped services or `HttpContext`).

## Packages

Every sample bumps `Inertia.Net`, `Inertia.Net.Live.SignalR` and `Inertia.Net.Testing` to 1.6.3. The two
EF-backed monoliths additionally add `Inertia.Net.EntityFrameworkCore` 1.6.3 and align their EF Core
packages to 10.0.14 (the new package requires it):

- TechLandier (PostgreSQL/Npgsql)
- Inkwell (SQLite)

The Mongo samples (CarParker, CarParkerReplicas) keep their hand-rolled `Skip/Limit` paging: the new helpers
page `IQueryable<T>` or delegate data sources, and the Mongo driver's LINQ is not covered by the EF package.
TechLandierDistributed keeps its own `Paged<T>` helper in `TechLandier.Core` — `Core` must not reference
Inertia.Net (MainApp applies `ScrollMetadata` explicitly instead).

## Error pages and remembered URLs (1.6.1)

`AddInertiaErrorPages()`/`UseInertiaErrorPages()` and `State.RememberPreviousUrl` are available, but the
samples already render their own `Error` component through each sample's existing 403/404/500 path, so nothing
changed there.

## Pagination adoption, per sample

| Sample | What changed |
| --- | --- |
| TechLandier | Every numbered list moves to `ToPageAsync`: routes bind `PageRequest page` and services clamp `page with { PerPage = ... }`. Storefront search/deals and the admin catalog/credit/reviews/support pagers count totals (`data`, `currentPage`, `total`, `lastPage`, `from`, `to`); the live admin orders board and the alerts inbox use `countTotal: false` (forward-only pages). The storefront rails keep working with `InfiniteScroll` because `Page<T>` supplies its own `Scroll` metadata. The hand-rolled `Paged<T>`/`ToPagedAsync` helper is deleted. The admin catalog pager now shows totals and `page x of y`, support shows `page x of y`, and the notifications inbox page (`account/Alerts`) additionally became a live prop on the owner's channel |
| Inkwell | Every cursor flow moves to `ToCursorPageAsync`: latest feed (`r => r.CreatedAt`, tie-breaker `r => r.Id`, descending), social timelines (repost-aware `(At, Id)` descending), people directory (`u => u.Id` ascending) and comment pages. FTS5 search keeps offset paging (bm25 rank has no keyset) through the core delegate paginator `Pagination.PaginateAsync`. Routes bind `CursorRequest`; the old `Paging.Encode/TryDecode` cursor codec, `CardPage`/`UserPage`/`FeedPage`/`CommentPageData` records and the per-row `CommentDto.Cursor` property are deleted. The deferred `comments` prop is now a `CursorPage` object (`data`/`nextCursor`/`hasMore`), which the comment drawer consumes instead of the length-50 heuristic; `GET /posts/{id}/comments` answers `{ data, nextCursor, hasMore }` |
| CarParker / CarParkerReplicas | Packages only (Mongo paging stays hand-rolled, as above) |
| TechLandierDistributed | Packages only; `Core` stays Inertia-free as mandated |

A malformed or foreign cursor now loads the first page (1.6.2+ behavior; Debug-logged, values are not exposed)
instead of answering 400 — noted in `docs/CONTRACT.md`.

## Cached prop loaders must survive the background refresh worker (1.6.3)

`staleFor` refreshes moved off response completion into a bounded background worker. Loaders captured in the
1.6.0 code used **`ctx.RequestServices`** or request-scoped services, which are disposed once the request ends.
Every shared/prop loader affected now resolves `IServiceScopeFactory` (a singleton) at share time and creates
its own scope inside the loader; TechLandier/Inkwell queries already used `IDbContextFactory<AppDb>` where they
run parallel, so no other loader needed restructuring:

- `InertiaSetup.` shared `notifications` prop: TechLandier, CarParker, CarParkerReplicas, Distributed MainApp.
- Inkwell shared `auth` prop: `SharedAuth.LoadAsync(ctx)` became `LoadAsync(IServiceProvider, Guid id)` and the
  share factory passes the id instead of `HttpContext`.

## Other 1.6.3 behavior absorbed

- Worker/Realtime of TechLandierDistributed now call `AddInertia()` next to `AddInertiaLive()`: the live
  publisher's singleton resolves an internal `InertiaPageWriter` that only `AddInertia()` registers. Neither
  role renders pages, so no SSR or Vite configuration belongs there.
- Validation error keys honor JSON aliases; endpoints that use `.WithInertiaValidation()` are unaffected.

## Bugs found and fixed while validating

- The live-props hub authorizers of CarParker and CarParkerReplicas used `ClaimsPrincipal.IsInRole(...)` for
  the `staff` channel, but the samples' roles are a **custom claim** (`role`), which `IsInRole` never matches
  (the `RequireClaim(AuthHelpers.RoleClaim, ...)` policies are what authorize the pages). The authorizers now
  read the same claim, so the staff channel actually authorizes admin/guard connections.
- The WebSocket live-props tests built their cookie header from the login response only. TechLandier's factory
  follows redirects in-process, so the session cookie (written on the POST hop) was missing. The TechLandier
  factory now exposes each client's cookie jar (`AppFactory.CookieHeader(client)`) and the test uses it; the
  CarParker suites already worked because their clients disable redirects.
- Two TechLandier live tests had drift: the customer socket connected with a bogus cookie string, and two
  tests in one fixture signed up the same demo email. Both fixed; the alerts inbox page now declares the
  `Notification` live listener the test expected (it kept losing updates while open).

## Verification

| Check | Result |
| --- | --- |
| `dotnet build` all five solutions (`-warnaserror` where mandated) | pass |
| TechLandier `dotnet test` (disposable Postgres on 55432) | **pass, 48/48** — 5 new numbered-pagination tests (storefront pages, totals/lastPage, admin catalog pager, live orders partial reload, alerts inbox) |
| Inkwell `dotnet test` (SQLite) | **pass, 120/120** — 2 new cursor-pagination tests (feed + people directory `LoadNextPageAsync`, no overlap between pages) |
| CarParker `dotnet test` (disposable replica-set Mongo on 27017) | **pass, 78/78** |
| CarParkerReplicas `dotnet test` (Mongo + `REDIS_URL`) | **pass, 95/95** |
| TechLandierDistributed `dotnet test` (Postgres on 55432 + Redis) | **pass, 55/55** |
| Frontends changed by the upgrade (TechLandier, Inkwell): `bun run typecheck` + `bun run build` | pass |
| Frontends untouched (CarParker, CarParkerReplicas, Distributed MainApp) | no changes |

## Follow-up

- The CarParker samples showcase Mongo paging; if a delegate paginator for Mongo driver queries is wanted,
  `Pagination.PaginateAsync` (core) accepts `fetch(skip, take, ct)` delegates and would fit the lot list.
- Once the Distributed `Core` contract allows a tiny dependency-import surface, `ToPageAsync` could replace
  `Core`'s `Paged<T>` too — it needs Inertia.Net types, so it waits for that decision.
