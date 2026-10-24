# Inertia props and SSR caching

TechLandierDistributed uses one shared `IInertiaCacheStore` for both cached props and SSR HTML.
The store is Redis-backed in replicated deployments and falls back to Inertia's per-process memory
store when `CACHE_REDIS_URL` is not configured (tests/local single-process).

## What is cached

| Cache | Inertia key | Tags | Default lifetime |
|---|---|---|---|
| Props | `inertia:prop:{cacheKey}` | Custom tags + `inertia:props` | Per prop, e.g. 5 min fresh/1 min stale for notifications; 1–5 min for storefront rails; 60 s/30 s for sales KPIs |
| SSR HTML | `inertia:ssr:{sha256(pageJson)}` | `inertia:ssr` | 300 s fresh / 60 s stale (configured in `InertiaSetup`) |

The cache never authorizes: ownership, roles and stock are checked in services on every request.
Cached props are UI data only. Per-user notification cache keys include the user id.

## Redis key layout

The prefix is `{Replicas:Namespace}:cache:{Cache:Version}:`:

```text
TechLandierDistributed:production:cache:v1:inertia:prop:notifications:{userId}
TechLandierDistributed:production:cache:v1:inertia:ssr:{hash}
TechLandierDistributed:production:cache:v1:tag:catalog
TechLandierDistributed:production:cache:v1:inertia:prop:...:tags   # reverse index (JSON tags)
```

- `REPLICA_NAMESPACE` must include the sample/environment so samples can share Redis safely.
- `CACHE_VERSION` is a deployment value, not the assembly version. Bump it when prop shapes change.
  MainApp and Worker must use the same value.
- Cache Redis is separate from critical Redis. A cache outage degrades a page to a DB/SSR read; it
  does not break authentication, limits or realtime.

## Store algorithm

`lib/TechLandier.Caching/RedisInertiaCacheStore.cs` implements:

- **Get** — one `GET`; missing or any Redis failure is a miss.
- **Set** — read the reverse tag index, remove the entry from old tag sets, `SET` the value and the
  reverse index with `Expiration`, then add the entry to each tag set and extend the tag-set TTL.
- **Tag TTL** — a small Lua script sets the larger of the current PTTL and `expiration + 1 min`.
  `EXPIRE ... GT` would treat a new key's missing TTL as infinite and leak tag sets forever.
- **Remove** — read the reverse index, remove the entry from its tag sets, then `UNLINK` the value
  and reverse index.
- **RemoveByTag** — `SSCAN` the tag set in pages, verify each member's reverse index still contains
  the tag (an overwrite may have changed it), remove it from the other tag sets, `UNLINK` the value
  and reverse index, then delete the tag set.
- **Max entry** — `Cache:MaxEntryBytes` (default 1 MiB) skips oversized values. A skipped value
  also removes an older entry so stale HTML is not served.
- **Fail-open** — Redis exceptions are logged at most once every 30 s and swallowed. `CacheRedisHealthCheck`
  reports **Degraded**, which remains HTTP 200 for HAProxy.

Tag invalidation is not transactional. A loader that started before an invalidation can reinsert a
stale value for its TTL. Keep fresh windows short for mutable data, never authorize from cached
props, and treat the TTL as the correctness bound. The tests exercise overwrite tag cleanup and
cross-store visibility.

## Invalidator adapters

Core services depend on the narrow port:

```csharp
public interface IPropCacheInvalidator
{
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default);
}
```

- MainApp: `InertiaPropCacheInvalidator(IInertiaCache)` — also clears Inertia's own accounting.
- Worker: `RedisPropCacheInvalidator(IInertiaCacheStore)` — a job or cron in another process
  invalidates the same shared store without loading Inertia.
- `REPLICAS_ENABLED=false`/no cache URL: Worker gets `NoOpPropCacheInvalidator` because there is no
  shared store to clear.

Write paths invalidate only the tags they affect (catalog/home/product/sales/notifications), so a
product edit does not flush every page.

## SSR cache specifics

- The SSR key is a function of the page JSON, including component, props, URL, asset version and
  flash/errors. A deploy changes the Vite manifest hash, so old renders are not reused.
- `Preferred` mode falls back to client rendering on failure; a cache hit does not consume an SSR
  slot or trip a circuit.
- Remote SSR is trusted input. Keep the pool on a private network; `/render` costs CPU and
  `/shutdown` exits the process.
- Each URL has its own slots/circuit. MainApp sets `MaxActive=2`, `MaxQueued=8`,
  `FailureThreshold=2`, timeout 2 s. A production proxy with active `/health` checks is preferable
  to direct URLs; direct URLs only learn from render failures.
- Do not enable CSP nonces without a cache-key strategy; cached HTML would carry another request's
  nonce.
- Per-user SSR pages make one entry per user. This sample server-renders only Home, products Index/
  Show and Deals; account/admin pages render client-side. Keep avatars on a stable public URL in
  production (`S3_PUBLIC_URL`) so page JSON does not change on every request.
- Filtered or paged product listings disable the SSR cache for that response
  (`WithSsrCache(TimeSpan.Zero)`), so query permutations do not multiply entries. The cache still
  helps the canonical Home, first product page and detail pages.

## Configuration

| Setting | Default | Meaning |
|---|---|---|
| `CACHE_REDIS_URL` | empty | Shared store. Required when `REPLICAS_ENABLED=true`; empty = Inertia memory store only in the single-process fallback. |
| `Cache:Version` / `CACHE_VERSION` | `v1` | Key namespace; same in web and worker. |
| `Cache:MaxEntryBytes` | `1048576` | Per-entry guard. |
| `Inertia:SsrCacheSeconds` / `SSR_CACHE_SECONDS` | `300` | SSR fresh window. |
| `Inertia:SsrStaleSeconds` / `SSR_STALE_SECONDS` | `60` | SSR stale window. |
| `CACHE_REDIS_MAXMEMORY` | `256mb` | Compose cache Redis budget. |
| `CACHE_REDIS_POLICY` | `volatile-lru` | Compose eviction policy. |

## Tests and acceptance

Run `dotnet test tests/TechLandierDistributed.Tests` with `REDIS_URL` set for the real-Redis tests;
without it they return early. The suite covers:

- two entries tagged `a,b`/`a`, removing `a` clears both;
- overwriting with different tags does not delete through the old tag;
- tag sets created for new keys have a TTL (the `GT` trap);
- two store instances see the same values and invalidation;
- oversized values are not cached;
- MainApp registers the Redis store when `CACHE_REDIS_URL` is set;
- MainApp and Worker invalidator adapters remove by tag;
- the stock-alert Redis gate fires once across two instances and resets above threshold.

For a Compose acceptance run, warm a page on two MainApp replicas, mutate on one, and reload on the
other. Stop cache Redis and verify pages still render with `/health` 200/Degraded; stop critical
Redis and verify `/health` 503.
