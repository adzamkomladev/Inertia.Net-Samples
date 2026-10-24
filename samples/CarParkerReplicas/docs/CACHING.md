# Inertia cache in CarParkerReplicas

Inertia.Net exposes one cache abstraction, `IInertiaCacheStore`, for **both** cached props and the SSR response
cache. The single-process default (`MemoryInertiaCacheStore`) is wrong here: a write on replica A would only
invalidate A's memory, so B could serve stale props until the TTL expires. This sample registers
`RedisInertiaCacheStore` whenever `REDIS_URL` is present. Without Redis (single app, tests) the memory store stays
in place.

The cache is not a source of truth. Cached props never authorize a request, and a Redis outage must degrade to
database reads and SSR renders, not fail the page.

## Registration

`Infrastructure/Setup/InertiaSetup.cs` replaces the default `IInertiaCacheStore` with a singleton when
`ReplicaSetup.HasRedis(config)` is true:

```csharp
builder.Services.Replace(ServiceDescriptor.Singleton<IInertiaCacheStore>(sp =>
    new RedisInertiaCacheStore(
        sp.GetRequiredService<IConnectionMultiplexer>(),
        RedisInertiaCacheOptions.FromConfiguration(config, ReplicaSetup.Namespace(builder)),
        sp.GetRequiredService<ILogger<RedisInertiaCacheStore>>())));
```

The `IConnectionMultiplexer` is registered once by `ReplicaSetup` (also used for Data Protection and the SignalR
backplane). Inertia.Net writes the value bytes and their fresh/stale timestamps itself; the store only keeps each
entry for `InertiaCacheEntryOptions.Expiration` (fresh + stale).

## Key layout

```text
{namespace}:cache:{version}:inertia:prop:{key}
{namespace}:cache:{version}:inertia:ssr:{sha256}
{namespace}:cache:{version}:tags:{entryKey}                # reverse index (JSON tag array)
{namespace}:cache:{version}:tag:{tag}                      # set of entry keys carrying the tag
```

Examples:

```text
CarParkerReplicas:production:cache:v1:inertia:prop:notifications:42
CarParkerReplicas:production:cache:v1:inertia:ssr:0f3c...
CarParkerReplicas:production:cache:v1:tag:analytics
CarParkerReplicas:production:cache:v1:tags:CarParkerReplicas:production:cache:v1:inertia:prop:notifications:42
```

- `{namespace}` is `Replicas:Namespace` (default `CarParkerReplicas:{environment}`).
- `{version}` is `Cache:Version` / `CACHE_VERSION`, default `v1`. It is a deploy value, not an assembly version.
  Keep it identical on every replica. Bump it when cached prop shapes change; old keys expire naturally.
- `InertiaCacheKey.ToString()` supplies `inertia:prop:{key}` or `inertia:ssr:{hash}`. A lookup builds no string
  twice; `RedisCacheKeys.Entry` appends it to the prefix.
- The reverse index is per entry and contains the tags that entry currently carries, including
  `inertia:props` / `inertia:ssr` added by Inertia.Net.

## Write path

`SetAsync`:

1. Reads the old reverse index.
2. Removes the entry from every old tag set. This covers overwriting the same key with a different tag list.
3. `SET entry value EX expiration`.
4. `SET reverseIndex [tags] EX expiration`.
5. For each tag: `SADD tagSet entry`, then extend the tag set's TTL with the Lua script below.

The tag set TTL is `expiration + Cache:TagTtlPaddingSeconds` (default 60 s). It is extended, never shortened.

## The tag-set TTL trap

`KeyExpireAsync(key, ttl, ExpireWhen.GreaterThanCurrentExpiry)` does **not** set a TTL on a new key: Redis treats
a key with no expiry as infinite, so `GT` is a no-op. Every new tag set would leak forever. The store uses:

```lua
local ttl = redis.call('PTTL', KEYS[1])
if ttl < 0 or ttl < tonumber(ARGV[1]) then
    redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
return ttl
```

`ttl < 0` covers both "missing" (-2) and "no expiry" (-1). The script is single-key, so it is safe on Redis
Cluster. A new tag set therefore always receives a TTL, and a longer entry extends an existing one.

## Invalidation

`RemoveAsync(key)`:

1. Reads the reverse index.
2. Removes the entry from each tag set.
3. Deletes the value and the reverse index with single-key `UNLINK` (via Lua). `UNLINK` frees large SSR values on
   a background thread; the commands are issued individually because Redis Cluster routes different keys to
   different slots.

`RemoveByTagAsync(tag)`:

1. `SSCAN`s the tag set in batches of 256. It never calls `SMEMBERS` on a potentially global set.
2. For each member, reads its reverse index and checks that it still carries `tag`. This prevents deleting a
   newer value that was overwritten with different tags.
3. Removes the entry from its other tag sets, then deletes the value and reverse index.
4. Deletes the tag set after the scan.

`ClearPropsAsync()` and `ClearSsrAsync()` from Inertia.Net reach this method with `inertia:props` and
`inertia:ssr`, so clearing one kind never deletes the other.

### Known concurrency race

Tag invalidation is not a transaction with the loader. Replica B can read the database, replica A can commit and
invalidate, and B can then write the old result back. The store's TTL and short fresh/stale windows bound the
damage. The research design describes an optional epoch guard, but this sample ships the TTL + tag baseline and
does not re-cache authorization data. Keep fresh windows short for mutable data and never authorize from cached
props.

A second race is deleting a tag set while a concurrent `SetAsync` adds a member: that entry keeps its value but
loses its tag membership until it expires. TTL keeps this bounded; the cache remains an optimization.

## Failure behavior

- Redis errors (connection, timeout, I/O, disposed connection) are logged at most once per
  `Cache:FailureLogSeconds` (default 30 s), per store instance. The operations return a miss or no-op.
- `GetAsync` returns `null`; the page loader runs.
- `SetAsync` leaves the previous value/TTL in place.
- `RemoveAsync`/`RemoveByTagAsync` leave entries to expire.
- Cancellation requested by the caller is propagated, not swallowed.
- If `Cache:MaxEntryBytes` > 0 and the value is larger, the store removes any previous entry for that key, skips
  the new value and logs a throttled warning, so an older value cannot survive the guard. The default `0` means
  no limit; a 1 MiB guard is a sensible production starting point for SSR HTML.
- `RedisInertiaCacheHealthCheck` reports **Degraded** when the cache Redis cannot be pinged, so HAProxy keeps the
  replica in rotation. The critical Redis ping from `ReplicaSetup` reports **Unhealthy** and does remove it.

## SSR cache specifics

The SSR key is a hash of the page JSON (component, props, URL, Vite asset version, …). Consequences:

- A new deploy changes the Vite manifest hash, so old renders are not read even when `CACHE_VERSION` is unchanged.
- Different props/query strings create different entries. Per-user props create one entry per user. Set
  `Cache:MaxEntryBytes` and a Redis `maxmemory` policy appropriate for the deployment, or disable SSR caching on
  authenticated/filtered pages with `WithSsrCache(TimeSpan.Zero)`.
- Presigned S3 URLs change every request and defeat SSR cache hits for signed-in users. Set `S3_PUBLIC_URL` to a
  stable CDN/bucket URL in production.
- A cache hit bypasses the SSR render slots and circuit breakers. It is not an HTTP response cache: the same HTML
  is still written for each request.
- Do not add CSP nonces without disabling or keying the SSR cache; cached HTML would carry another request's nonce.

## Configuration summary

| Setting | Default | Meaning |
|---|---|---|
| `REDIS_URL` / `Redis:Url` | — | Shared Redis. Required when `Replicas:Enabled=true`. |
| `Replicas:Namespace` / `REPLICA_NAMESPACE` | `CarParkerReplicas:{env}` | Redis, Data Protection and SignalR namespace. |
| `Cache:Version` / `CACHE_VERSION` | `v1` | Cache key version. |
| `Cache:MaxEntryBytes` | `0` | Per-entry size guard; 0 is unlimited. |
| `Cache:TagTtlPaddingSeconds` | `60` | Tag set outlives entries by this much. |
| `Cache:FailureLogSeconds` | `30` | Failure log throttle. |
| `Inertia:SsrCacheSeconds` | `300` | SSR fresh window (0 disables the SSR cache). |
| `Inertia:SsrStaleSeconds` | `60` | SSR stale-while-revalidate window. |

## Tests

`tests/CarParkerReplicas.Tests/RedisInertiaCacheStoreTests.cs` covers, against a real Redis:

- tag removal visible through a second store instance;
- overwriting with different tags does not delete the new value when the old tag is removed;
- new tag sets receive a TTL and a longer entry extends it (the `GT` trap);
- the `MaxEntryBytes` guard.

The tests read `REDIS_URL` and return early with a message when it is not set or unreachable, so the default
`dotnet test` does not require Docker. Pure configuration/key tests are in
`ReplicaConfigurationTests.cs`. Start a disposable Redis for the integration run:

```sh
docker run -d --rm --name cpr-cache-test -p 127.0.0.1:6399:6379 redis:8-alpine
REDIS_URL=redis://localhost:6399/0 dotnet test tests/CarParkerReplicas.Tests \
  --filter FullyQualifiedName~RedisInertiaCacheStoreTests
docker rm -f cpr-cache-test
```
