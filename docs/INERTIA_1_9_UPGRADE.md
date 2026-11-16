# Inertia.Net 1.9.2 upgrade: Mongo pagination, typed pages and routes

All five samples moved from **Inertia.Net 1.7.0 to 1.9.2** on 2026-11-16. The two Mongo samples finally use the
official `Inertia.Net.MongoDB` pagination helpers, and every sample now generates and consumes typed page props,
shared props, flash data and named routes. The upgrade also removes the hand-written scope factories from cached
prop loaders and replaces the custom cookie-auth redirect events with `UseInertiaRedirects()`.

## Packages

Every sample pins `Inertia.Net`, `Inertia.Net.Live.SignalR` and `Inertia.Net.Testing` to **1.9.2**.

- TechLandier and Inkwell keep `Inertia.Net.EntityFrameworkCore` 1.9.2 and align their EF Core packages to
  **10.0.15**, the floor required by the package on `net10.0`.
- CarParker and CarParkerReplicas add `Inertia.Net.MongoDB` 1.9.2 and move `MongoDB.Driver` to **3.13.0**
  (the helper package's floor).
- CarParkerReplicas and TechLandierDistributed align Data Protection, SignalR Redis and cryptography packages to
  **10.0.15**.

## MongoDB pagination (1.9.0)

`Inertia.Net.MongoDB` adds `ToPageAsync` and `ToCursorPageAsync` for `collection.Find(...)`:

- `ToPageAsync` replaces `Skip`/`Limit` and counts the filter with `CountDocuments`, so `Page<T>` carries
  `data`, `currentPage`, `perPage`, `total`, `lastPage`, `from`, `to`, `hasMore` and `pageName`.
- The sorted find is required. The samples use their existing sort plus a unique `Id` tie-breaker so pages never
  overlap.
- `ToCursorPageAsync` is available for keyset flows; the CarParker flows are numbered pagers, so they use
  `ToPageAsync`.

| Sample | Converted paging |
| --- | --- |
| CarParker | Lots grid (`LotsService.QueryPageAsync` returns `Page<LotCard>` and the route binds `PageRequest`); guard incidents; admin bookings, sessions, refunds and users; insurance policies and claims |
| CarParkerReplicas | Same conversions, kept in sync with CarParker |
| Inkwell | Already on `ToCursorPageAsync`/`Pagination.PaginateAsync` from 1.6.3; no Mongo code |
| TechLandier | Already on `ToPageAsync` from 1.6.3; only package alignment |
| TechLandierDistributed | `Core` keeps its Inertia-free `Paged<T>` helper; MainApp only adds page records and generated types |

The lots grid keeps its in-memory occupancy-ranking branch (no stable Mongo sort without an aggregation), but it
now returns the same `Page<LotCard>` value so the frontend has one page contract. `LotsPage` was deleted.

## Typed pages, shared props and routes (1.9.0)

Every sample sets:

```csharp
o.TypeScript.OutputPath = "ClientApp/types/inertia.d.ts";
o.TypeScript.RoutesOutputPath = "ClientApp/types/routes.ts";
o.TypeScript.AddGeneratedPages().SharedProps<SharedPageProps>().FlashData<Flash>();
```

- **Page records** replace the component string on public flows: CarParker `HomePage`, `LotsIndexPage`,
  `LotShowPage`, `AboutPage`; Inkwell `FeedIndexPage`, `UsersIndexPage`; TechLandier and TechLandierDistributed
  `ProductsPage`, `DealsPage`. `InertiaProp` members use `[InertiaPropType]` so `Scroll` props are typed as
  `Page<T>`/`CursorPage<T>`/`Paged<T>` rather than `unknown`.
- **Shared props** are explicit records (`SharedPageProps`) because the shared `notifications` prop is a scoped
  loader whose value type cannot be inferred from the conditional factory. `auth` uses the typed
  `Share<T>` overload and returns `SharedAuthDto` instead of an anonymous object.
- **Flash data** is a C# record per sample (`Flash(Toast? Toast)` or Inkwell's `success/error/info`).
- **Typed routes** come from `WithName(...)` on the Inertia endpoints. Layouts now use `routes.*` for navigation,
  search and account links (`routes.lots.index.url()`, `routes.products.index.url()`, `routes.posts.index.url()`,
  `routes.logout()`, ...). Unnamed operational endpoints (Runwick, health, error) are skipped by the generator.
- Generated files are committed. Regenerate them with `dotnet run --project <app> -- --inertia-types`; run it in CI
  and fail when `git diff --exit-code ClientApp/types` reports a stale file.

## Scoped services in loaders (1.9.0)

`Prop<TService, T>((service, ct) => ...)` replaces the `IServiceScopeFactory` captured in shared prop factories:

- During a render the loader receives the request's instance.
- A `.Parallel()` loader gets its own scope.
- A stale-while-revalidate refresh runs in a new scope after the response, so it cannot capture a disposed request.

Converted loaders:

- CarParker / CarParkerReplicas / TechLandier / TechLandierDistributed shared `notifications`:
  `Prop<AlertsService, NotificationSummary>`.
- Inkwell shared `notifications`: `Prop<NotificationsService, NotificationSummary>`.
- Inkwell shared `auth`: `Prop<AppDb, SharedAuthDto>`; `SharedAuth.LoadAsync` now takes an `AppDb` instead of an
  `IServiceProvider`.

The helper overloads are ambiguous for `async` lambdas (both `Task<T>` and `ValueTask<T>` match), so each one uses a
small local `async Task<T>` method before passing it to `Prop<TService, T>`.

## Cookie authentication and rate limiting (1.9.0)

All four cookie-auth samples call `o.UseInertiaRedirects()`. Inertia visits now follow login/verify redirects like
browser navigations (302 for GET; 303 after POST/PUT/PATCH/DELETE), while fetch and Precognition requests keep the
events configured by the handler. This replaced the hand-written `OnRedirectToLogin`/`OnRedirectToAccessDenied`
wrappers. Inkwell adds the call to `ConfigureApplicationCookie`.

The existing `OnRejected` handlers stay: they flash a `Toast` object that the shared client toaster reads, while
`InertiaRateLimiting.OnRejected()` writes a string under a flash key. The built-in helper is documented in the
upgrade notes for apps that use the default flash shape.

## Other 1.9 behavior absorbed

- **1.9.1**: Mongo/EF `ToPageAsync` skips the count when the page already proves the total (first page with no more
  rows, or a last page with rows). The Redis tag-set expiry and async TempData fixes do not affect these samples.
- **1.9.2**: DevTools refuses proxied requests without an `Authorize` callback, cursor keys must be finite, SSR
  `HttpClient` logs are quieter, JSON pages over 32 KB stream in 32 KB pieces, and buffers over 1 MB are not kept in
  the shared array pool. No sample code changes were needed.

## Deliberately not adopted

- **`Inertia.Net.Redis`**: CarParkerReplicas and TechLandierDistributed keep their own `IInertiaCacheStore`
  implementations and tests because the key/tag layout and Lua tag-set TTL are part of those samples' distributed
  cache documentation. The built-in package is a drop-in alternative for a new deployment.
- **`liveReload` for scroll/merge props**: the storefronts keep their raw `/realtime` hubs for spot pulses and
  toasts, while dashboards, badges and inboxes already use server `Live(...)` props on `/inertia-live`.
- **Server head tags**: pages own their titles through the existing `PageTitle`/`useDocumentTitle` components, so no
  `.WithHead(...)` was added.
- **`dotnet new inertia --auth`**: the samples already have their own auth flows and demo users.

## Verification

| Check | Result |
| --- | --- |
| `dotnet build` all five solutions | pass, 0 warnings |
| CarParker `dotnet test` (Mongo replica set on 27017) | **78/78** |
| CarParkerReplicas `dotnet test` (replica set Mongo; Redis tests skip) | **95/95** |
| Inkwell `dotnet test` (SQLite) | **119/119** |
| TechLandier `dotnet test` (Postgres on 5433 via `INERTIA_TEST_CONNECTION`) | **53/53** |
| TechLandierDistributed `dotnet test` | not run in this session (requires the full PgDog/critical Redis/cache Redis topology); the solution builds |
| CarParker / CarParkerReplicas `svelte-check` | pass, 0 errors |
| Inkwell `tsc --noEmit` | pass |
| TechLandier / TechLandierDistributed generated `routes.ts` + `inertia.d.ts` | pass with `tsc`; the full `vue-tsc` run is blocked by this shell's Bun shim resolution for `.vue` SFCs |
| Vite client builds (all five frontends) | pass |
| `dotnet run -- --inertia-types` (CarParker, Inkwell, TechLandier, TechLandierDistributed) | pass; generated files are committed |

## Follow-up

- Add `dotnet run -- --inertia-types` to each sample's CI check and fail on a diff in `ClientApp/types`.
- Consider `Inertia.Net.Redis` for a new replica deployment; the custom stores can then be deleted with their
  Redis-specific tests.
- `liveReload` fits the storefront lot/product pulses once those hubs publish a stable public live event alongside
  the existing raw `SpotChanged`/`LotPulse` messages.
