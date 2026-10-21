# Inertia.Net 1.4.0 feature adoption

All six samples and their test projects moved from Inertia.Net and Inertia.Net.Testing **1.3.0 → 1.4.0** on 2026-10-21. Unlike the earlier rounds, this one also adopts the 1.4 features rather than only re-validating the version: cached props, the SSR output cache, Inertia DevTools, Vite prefetch, the development page-file check and the new single antiforgery middleware. No new manual benchmark run was made for this upgrade, so there are no before/after numbers here; the [1.1.0](INERTIA_1_1_UPGRADE.md) and [1.2.0](INERTIA_1_2_UPGRADE.md) records stay the measured baseline for request cost, and the [memory research](MEMORY_AND_DB_RESEARCH.md) keeps the container numbers.

## What every sample adopted

- **Cached props.** Deferred props on the analytics pages (the heaviest aggregates: KPIs, chart series, top posts, referrers) now call `.Cache(key, AnalyticsTtl)`. Results are stored as JSON in `IMemoryCache` (`AddInertia()` wires memory cache) and a cache hit skips both the deferred callback and the query it wraps. All keys are per user (`analytics:{uid}:{window}:{prop}` and `analytics-post:{uid}:{id}:{window}:{prop}`), so anonymous-vs-signed-in and between-user leakage are impossible by construction; values expire after 60 seconds (`AnalyticsTtl = TimeSpan.FromSeconds(60)`), which keeps staleness within what the pages already tolerate being deferred.
- **SSR output cache.** `o.Ssr.Cache.Duration` is set from `Inertia:SsrCacheSeconds` (default 300, a negative value fails startup). Identical first-visit page JSON reuses the last server render instead of re-contacting Node/Bun. Anonymous visitors share one entry per URL and per replica (the cache is in-process); every later Inertia visit still fetches fresh JSON, so only the initial HTML is cached. The sample-specific renderers still run as before.
- **Inertia DevTools.** `services.AddInertiaDevTools()` plus `app.MapInertiaDevTools()` serve the `/_inertia/devtools/entries/{id}` protocol for the browser extension. Recording happens in Development only; redaction of secrets still applies, and nothing outside Development is recorded.
- **Vite prefetch.** `o.Vite.Prefetch = VitePrefetch.Waterfall` prefetches the code-split chunks the next navigation will import.
- **Page-file check.** `o.Pages.Directory = "ClientApp/pages"` makes `Render` throw in Development with the closest match when a component has no page file (a no-op outside Development).
- **Antiforgery.** `UseInertiaAntiforgeryCookie()` and the hand-written unsafe-method middleware are replaced by one `app.UseInertiaAntiforgery(...)` placed after `UseAuthorization`. `CookieRefresh = XsrfCookieRefresh.Lazy` keeps a token that still validates, so page loads no longer cost a Set-Cookie and its no-store; `ShouldValidate = ctx => !ctx.Request.Path.StartsWithSegments("/realtime")` keeps SignalR negotiate POSTs flowing.
- **Page helper.** The bare create-post shells use `MapInertia("/posts/create", "Posts/Create")` instead of a `MapGet` that only rendered a page.
- **Transport.** `AddResponseCompression(o => o.EnableForHttps = true)` + `UseResponseCompression()` compress page JSON/HTML five to ten times, and `/build/assets` responses get `Cache-Control: public, max-age=31536000, immutable` (the Vite build inventories those files in every HTML, so an outdated cached asset always hits the immutable path).

## Per-sample differences

| Sample | Deferred props cached | Antiforgery notes |
| --- | --- | --- |
| ReactSqliteBlog | 5 (summary, series, topPosts, post summary, post series) | `OnRejected` preserves the old UX: Inertia requests get `Back().WithFlash("error", …)`, plain POSTs get 400 |
| ReactSqliteReplicasBlog | 5 (same set) | same `OnRejected` |
| SvelteMongoBlog | 6 (kpis, series, topPosts, post kpis, post series, post referrers) | defaults (419) |
| SvelteMongoReplicasBlog | 6 (same set) | defaults (419) |
| VuePgBlog | 6 (same set) | defaults (419) |
| VuePgReplicasAdvBlog | 6 (same set) | `CookieRefresh = Lazy` only; no realtime hub in the app |

The two React apps kept their custom rejection handler and the Svelte/Vue apps kept the default 419 so their existing integration assertions (expired-session flash text, 419 status) keep passing unchanged. The Svelte apps' analytics deferred props were already built in `AnalyticsService`, so `.Cache()` was added there; the React apps' props were built when assembling the page, and the advanced Vue sample caches in its `AnalyticsEndpoints`.

## Performance-test adjustments

`InertiaPerformanceTests` opt-in harnesses overwrite Inertia options at startup, and three of them carry an `ssr` mode that exercises the SSR response parser (ReactSqliteBlog, SvelteMongoBlog, VuePgBlog). Now that the samples ship an SSR cache, those harnesses turn it off (`o.Ssr.Cache.Duration = TimeSpan.Zero;`) so the benchmark keeps measuring the parser instead of a cache hit. ReactSqliteReplicasBlog, SvelteMongoReplicasBlog and VuePgReplicasAdvBlog have no `ssr` mode in their harnesses (their benchmarks cover html/json/partial only), so no change was needed there.

## Not adopted

- **CSP nonces** — none of the samples ship a Content-Security-Policy header, so there is nothing for nonces to relax.
- **TypeScript declarations output** — the samples don't consume generated prop types; keeping their existing `Props` module structure.
- **External-redirect 409 responses** — the 1.4 default behavior change (external redirects on Inertia requests answer with 409 + `X-Inertia-Location`) requires no work: no sample redirects to an absolute external URL from an Inertia request.
