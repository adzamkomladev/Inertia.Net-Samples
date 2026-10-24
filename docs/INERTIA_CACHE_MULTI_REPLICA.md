# Inertia.Net caches in a multi-replica deployment

Research notes for the CarParker replica sample and the split TechLandier sample.
Checked against **Inertia.Net 1.5.1**, the installed package XML/README, the two current
applications, and small local prototypes (Redis store, two app instances, two SSR servers).
It is implemented in `samples/CarParkerReplicas` and `samples/TechLandierDistributed`; this note
remains the design and evidence reference.

## 1. Conclusion

Inertia.Net already has the right extension point. `IInertiaCacheStore` is a singleton
abstraction behind **both** the cached-prop store and the SSR response cache. Register a
Redis-backed implementation and every app replica, worker and SSR renderer can share the
same entries and the same tag invalidations.

What must change for replicas:

- The default `MemoryInertiaCacheStore` is **per process**. A mutation on replica A only
  clears A's copy. Replica B keeps serving stale props until its TTL expires.
- The SSR cache is also per process by default, so every replica renders the same public
  page again even when another replica just rendered it.
- Register one Redis-backed `IInertiaCacheStore` in every process that renders or
  invalidates Inertia data.
- Keep the cache optional for availability: a cache outage should degrade to database/SSR
  reads, not fail requests. A separate cache Redis makes that possible.
- Keep the cache **versioned** and **namespaced**. Inertia's prop keys do not include the
  app build; prop JSON can otherwise outlive a deploy.

The rest of this document is the design and the evidence.

## 2. The two caches

| Cache | What is stored | Default key | Invalidation |
|---|---|---|---|
| Cached props | A prop's serialized JSON, plus fresh/stale times and tags | `inertia:prop:{key}` | `IInertiaCache.RemovePropAsync`, `RemoveByTagAsync`, `ClearPropsAsync` |
| SSR response | The rendered `head` and `body` of one page | `inertia:ssr:{sha256(pageJson)}` | TTL/stale, `IInertiaCache.ClearSsrAsync` |

Both go through `IInertiaCacheStore`:

```csharp
ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken ct);
ValueTask SetAsync(InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions options, CancellationToken ct);
ValueTask RemoveAsync(InertiaCacheKey key, CancellationToken ct);
ValueTask RemoveByTagAsync(string tag, CancellationToken ct);
```

`InertiaCacheEntryOptions` contains:

- `Expiration`: fresh time + stale time. It is the only TTL a store needs.
- `Tags`: custom tags plus `inertia:props` for props, or `inertia:ssr` for renders.

Verified behaviour of the 1.5.1 implementation:

- `SetPropAsync("notifications:42", ..., 5 min, 1 min, ["notifications:42"])` writes an
  entry with expiration **6 minutes** and tags `notifications:42,inertia:props`.
- `ClearPropsAsync()` calls `RemoveByTagAsync("inertia:props")`.
- `ClearSsrAsync()` calls `RemoveByTagAsync("inertia:ssr")`.
- `RemovePropAsync("notifications:42")` removes `inertia:prop:notifications:42`.
- A store that throws during a render is logged and treated as a miss. `IInertiaCache`
  lets exceptions through, so the store itself should absorb Redis failures.

## 3. Why the default store is wrong here

With three web replicas and the default store:

1. Admin edits a product on replica A.
2. `AdminService` calls `cache.RemoveByTagAsync("catalog")`.
3. Only A's memory cache is cleared.
4. Replica B serves the old catalog for the rest of its TTL (up to 5 minutes in TechLandier).
5. The admin sees "saved", but some users still see the old price.

The same happens to the shared `notifications:{userId}` prop. The badge can stay stale on
another replica after a notification is read. Realtime pushes make this more visible: a
client receives an event and reloads, lands on a different replica, and gets old props.

The SSR cache has the same issue but with a performance rather than correctness symptom.
Each replica misses its own cache and calls its own Bun process for the same page.

## 4. Redis store design

### 4.1 Key layout

Use one prefix for the sample, environment and cache format version:

```text
{namespace}:cache:{version}:
```

Examples:

```text
cpr:prod:v1:inertia:prop:notifications:42
cpr:prod:v1:inertia:ssr:0f3c...
cpr:prod:v1:tag:notifications:42
cpr:prod:v1:tag:inertia:props
cpr:prod:v1:tags:{entryKey}          // reverse index: which tags an entry has
```

`{namespace}` must include the sample name and environment, because several samples may
share one Redis instance. `{version}` is a deployment value (`CACHE_VERSION`), not the
assembly version. It must be **identical in every role that shares the cache**, especially
web and worker.

### 4.2 Algorithms

`GetAsync(key)`

1. `GET prefix + key.ToString()`.
2. Return the bytes, or `null` on a missing key.
3. On Redis failure: log (rate-limited) and return `null`.

`SetAsync(key, value, options)`

1. Read the old reverse index `prefix + "tags:" + entryKey`.
2. Remove `entryKey` from each old tag set.
3. `SET entryKey value EX options.Expiration`.
4. `SET reverseIndex JSON(options.Tags) EX options.Expiration`.
5. For every tag:
   - `SADD tagKey entryKey`.
   - Extend the tag set's TTL to at least `max(existing TTL, options.Expiration + 1 min)`.
6. On Redis failure: log and return; the page still renders.

`RemoveAsync(key)`

1. Read the reverse index.
2. Remove the entry from each tag set.
3. Delete the entry and the reverse index.
4. Ignore failures; the entry expires by TTL anyway.

`RemoveByTagAsync(tag)`

1. `SSCAN` the tag set in batches (do not `SMEMBERS` an unbounded global tag).
2. For each member, read its reverse index and confirm it still carries `tag`. This avoids
   deleting a newer value that was overwritten with different tags.
3. Remove the member from its other tag sets, then delete the entry, reverse index and
   (preferably with `UNLINK`) the value.
4. Delete the tag set.
5. Ignore failures; stale entries expire.

### 4.3 Tag-set TTL trap

`KeyExpireAsync(key, ttl, ExpireWhen.GreaterThanCurrentExpiry)` does **not** set a TTL on a
new key. A key with no expiry is treated as infinite, so `GT` is a no-op. I reproduced this
against Redis 8:

```text
ttl after GT on new key:
ttl after Always: 23:59:59.9980000
```

Use a small Lua script so the tag set gets the larger of the current TTL and the new TTL:

```lua
local ttl = redis.call('PTTL', KEYS[1])
if ttl < 0 or ttl < tonumber(ARGV[1]) then
  redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
return ttl
```

Without this, every tag set leaks forever.

### 4.4 Code sketch

```csharp
public sealed class RedisInertiaCacheStore(
    IConnectionMultiplexer redis,
    InertiaCacheStoreOptions options,
    ILogger<RedisInertiaCacheStore> log) : IInertiaCacheStore
{
    readonly IDatabase db = redis.GetDatabase();

    string Entry(InertiaCacheKey key) => options.Prefix + key;
    string Tags(string entry) => entry + ":tags";
    string TagKey(string tag) => options.Prefix + "tag:" + tag;

    public async ValueTask<byte[]?> GetAsync(InertiaCacheKey key, CancellationToken ct)
    {
        try
        {
            var value = await db.StringGetAsync(Entry(key));
            return value.IsNullOrEmpty ? null : (byte[])value!;
        }
        catch (Exception ex) when (IsRedisFailure(ex))
        {
            options.ReportFailure(log, ex);
            return null;
        }
    }

    public async ValueTask SetAsync(
        InertiaCacheKey key, byte[] value, InertiaCacheEntryOptions entry, CancellationToken ct)
    {
        try
        {
            var entryKey = Entry(key);
            var tagsKey = Tags(entryKey);
            var old = await ReadTags(tagsKey);
            foreach (var tag in old) await db.SetRemoveAsync(TagKey(tag), entryKey);

            await db.StringSetAsync(entryKey, value, entry.Expiration);
            await db.StringSetAsync(tagsKey, JsonSerializer.Serialize(entry.Tags), entry.Expiration);

            foreach (var tag in entry.Tags)
            {
                await db.SetAddAsync(TagKey(tag), entryKey);
                await db.ScriptEvaluateAsync(ExtendTagTtl, [TagKey(tag)],
                    [(long)(entry.Expiration + TimeSpan.FromMinutes(1)).TotalMilliseconds]);
            }
        }
        catch (Exception ex) when (IsRedisFailure(ex)) { options.ReportFailure(log, ex); }
    }

    // RemoveAsync / RemoveByTagAsync: same pattern, failures are non-fatal.
}
```

`IConnectionMultiplexer` is already needed for Data Protection, SignalR and rate limits, so
this adds no new connection technology.

## 5. Coherence: TTL is not a proof

A shared store makes invalidation global, but it does not make the read-modify-write cycle
atomic. This race exists on one replica too; replicas widen it:

1. Replica B starts loading `catalog` from the database.
2. Replica A commits a product edit and calls `RemoveByTagAsync("catalog")`.
3. Replica B finishes its loader and writes the old result into Redis.
4. The old value is now cached again for the full TTL.

Recommendations, in order:

1. **Bound the damage.** Keep short fresh windows for data that changes often (the current
   60 s / 30 s admin analytics and 1 minute rails are already sensible). `StaleFor` extends
   the window; do not set it to hours for mutable data.
2. **Never authorize from cached props.** The current apps already check ownership and
   permissions in services; keep that invariant.
3. **Add a strict mode if the race matters.** A cache epoch makes stale reinsertion much
   less likely:
   - `RemoveByTagAsync` increments a Redis counter `{prefix}epoch`.
   - `GetAsync` on a miss records the current epoch in a short-lived fill key
     `{prefix}fill:{entryKey}`.
   - `SetAsync` compares the fill epoch with the current epoch in a Lua script; if they
     differ, it discards the value instead of caching it.
   - This is conservative: an unrelated invalidation may discard a fill, but it prevents a
     pre-invalidation read from being re-cached after the invalidation.
4. **Write through only when you know the new value.** `IInertiaCache.SetPropAsync` can
   replace a prop after a mutation, but it has the same concurrent-write problem and is not
   a substitute for invalidation.

For the first migration I would ship TTL + tag invalidation and add the epoch only if an
acceptance test reproduces a stale reinsertion. It is a small, isolated addition to the
store and does not change application code.

## 6. SSR cache specifics

### 6.1 It is a function of the page JSON

The key is `sha256(component, props, url, version, ...)`. Consequences:

- A new deploy changes the Vite manifest hash (`version`), so old SSR entries are not read.
- Different props produce different entries. **Per-user props create one entry per user.**
- Different query strings produce different entries.
- Cache hits bypass SSR render slots and circuit breakers.
- A cache hit still writes the same HTML; it is not a full HTTP response cache.

### 6.2 Findings in the current apps

These matter before turning on a shared SSR cache at scale:

1. **Presigned avatar URLs break SSR cache hits for signed-in users.**
   `AuthHttp.SharedAuth` includes `Storage.Url(avatarKey)`. When neither `S3_PUBLIC_URL` nor
   an `S3_ENDPOINT` that can build a public base is configured, `Storage.Url` returns a
   presigned GET whose signature changes on every request. The page JSON therefore changes
   on every request, so signed-in pages never hit the SSR cache. They still create entries,
   which is worse: Redis fills with entries that are never read.
   Fix one of:
   - set a public/stable avatar URL in production (`S3_PUBLIC_URL`), or
   - disable SSR caching for authenticated requests
     (`Render(...).WithSsrCache(TimeSpan.Zero)`), or
   - serve avatars through a stable application/CDN URL rather than a per-request presign.

2. **Filtered listing pages multiply entries.**
   TechLandier server-renders `products/Index`; its page JSON includes `q`, category, price,
   stock, sort, page and filters. CarParker server-renders `lots/Index` with `q`, type,
   max price, sort and page. A shared Redis cache will accumulate one HTML entry per
   combination. Cache only the canonical first page, or set `WithSsrCache(TimeSpan.Zero)`
   when query parameters are present. `Home`, product/lot detail and `deals` are safer.

3. **Per-user entries need a budget.** Even with stable avatar URLs, `auth` is per-user.
   Use a shorter SSR duration for authenticated pages or skip the SSR cache for them.
   The cache Redis needs `maxmemory` and `volatile-lru`, not an unbounded volume.

4. **CSP nonces are incompatible with cached HTML.** The apps do not currently use
   `CspNonce`. If that changes, cached SSR HTML would carry a nonce from the original
   request. Disable the SSR cache or make the nonce a cache-key input.

5. **Cache only successful, empty-flash pages.** A flash/error payload changes the page
   JSON and creates short-lived variants. The existing public pages normally have no flash
   on first visit, so this is mainly a watch item.

### 6.3 Shared SSR cache proof

Prototype: two ASP.NET app instances, two fake SSR servers, one Redis store, SSR cache
duration 5 minutes.

```text
A rendered: True,  hits=0,1
B rendered: True,  hits=0,1
B reused A's HTML: True
```

The second app instance reused the first instance's HTML and did not call an SSR server.
That is the behaviour the split TechLandier sample needs: 5 app replicas and 3 SSR servers
share one render cache.

## 7. Multi-SSR facts from 1.5.1

`Ssr.Urls` is `IList<string>`. With several URLs:

- Each render goes to the server with the fewest renders in flight whose circuit is closed.
  Ties rotate.
- Each URL has its own slots, queue and circuit breaker. Limits are **per app instance**:
  a server behind N apps can receive up to N × `Concurrency.MaxActive` renders.
- Only a **refused connection** is retried once on another server within the same deadline.
  A timeout or HTTP error is not retried; the page falls back to client rendering in
  `Preferred` mode.
- Remote SSR is trusted input: its HTML is written into the page unescaped. Keep it on a
  private network or HTTPS. It has no authentication; `/render` costs CPU and `/shutdown`
  exits the process.
- `Ssr.Urls` is not used with `UseNodeProcess()`. The split app must not start a local Bun.
- A cache hit bypasses the circuit and the SSR call.
- `AddInertiaSsr` reports what the renders have shown; it does not actively poll servers.
  A server that just went down is reported healthy until a render fails.

### 7.1 The timeout finding

I reproduced a two-SSR setup and stopped one fake server. The stopped process left the
socket hanging rather than refusing connections. Inertia did **not** retry the other server
for timeouts; every other request waited for `Ssr.Timeout` and rendered client-side until
the circuit opened. A refused connection is the fast path; a hung or overloaded server is
not.

Operational implications:

- Use a short `Ssr.Timeout` for remote SSR (1–2 seconds, not the default 5).
- Lower `CircuitBreaker.FailureThreshold` (2–3) so a hung server is skipped sooner.
- Set `Concurrency.MaxActive` to a small number per app per server (1–2), not the default
  8, unless you have measured that the SSR process can use the concurrency.
- Prefer an SSR-aware proxy with active `/health` checks for production. A proxy removes a
  dead server immediately; direct `Ssr.Urls` only learns from render failures. Direct URLs
  are still useful for the sample and for small deployments.

## 8. Configuration matrix

| Setting | Purpose | Replica guidance |
|---|---|---|
| `Inertia:Ssr` / `SSR_ENABLED` | SSR kill switch | `true`; `Preferred` mode |
| `Inertia:SsrUrls:0..N` | Remote SSR pool | List every SSR replica, e.g. `http://ssr-1:13714` |
| `Inertia:SsrTimeoutSeconds` | Total render deadline | 1–2 s remote |
| `Inertia:SsrCacheSeconds` | SSR fresh window | 300 s for public pages; 0 for filtered/auth pages |
| `Inertia:SsrStaleSeconds` | SSR stale-while-revalidate | 30–60 s |
| `Cache:RedisUrl` | Inertia cache Redis | Required for cross-replica correctness |
| `Cache:Version` / `CACHE_VERSION` | Cache key namespace | Same in web and worker; bump on prop shape changes |
| `Cache:MaxEntryBytes` | Per-entry guard | e.g. 1 MiB; skip larger SSR renders |
| `Replicas:Namespace` | Redis/DP/SignalR namespace | Unique per sample/environment |
| `Auth:SecureCookies` | Cookie policy behind TLS | `true` in production |
| `CACHE_REDIS_MAXMEMORY` (container) | Cache memory bound | 128–512 MiB for a demo; monitor |
| `CACHE_REDIS_POLICY` (container) | Eviction | `volatile-lru` |

## 9. Test plan for the cache

1. **Store unit/integration tests (real Redis):**
   - set two entries with tags `a,b` and `a`; remove `a`; both return null.
   - overwrite an entry with different tags; removing the old tag must not delete it.
   - `ClearPropsAsync` removes only props; `ClearSsrAsync` removes only renders.
   - tag sets have a TTL; new tag sets get one (the `GT` trap).
   - two store instances sharing Redis see each other's sets/removals.
2. **Inertia integration:**
   - `IInertiaCache.SetPropAsync` visible through a second store.
   - `IInertiaCache.RemoveByTagAsync` removes the entry for a second app instance.
   - two app hosts with one Redis and two fake SSR servers: second host reuses the first
     host's SSR HTML.
3. **Coherence:**
   - start a slow loader, mutate and invalidate on another replica, let the loader finish;
     assert the value either is not re-cached (strict mode) or expires within the documented
     TTL (default mode).
4. **Outage:**
   - stop cache Redis; pages still render from DB/SSR; mutations still commit; health is
     `Degraded`, not `Unhealthy`.
5. **Deploy:**
   - old and new `CACHE_VERSION` do not read each other's prop entries; old keys expire.
   - new Vite manifest changes the SSR key; old renders are not reused.

## 10. Prototype notes

Local prototypes (not committed):

- Redis 8 on port 6390, `maxmemory 64mb`, `volatile-lru`.
- `RedisInertiaCacheStore` with prefix, reverse tag index, Lua TTL extension.
- Results:
  - cross-store visibility and invalidation: pass;
  - overwrite tag cleanup: pass;
  - SSR tag clear: pass;
  - `IInertiaCache.SetPropAsync` visible through a second store: pass;
  - tag-set TTL after Lua: 1 day; after `GT` without Lua: none (the trap).
- Two app hosts + two fake SSR servers + one Redis:
  - first HTML render hit one SSR server;
  - second host reused the same HTML with zero additional SSR hits.

## 11. Recommendation

For both samples, make the cache a first-class shared dependency:

- Redis-backed `IInertiaCacheStore`, namespaced and versioned.
- Separate cache Redis from the critical Redis where possible. Cache failure is then a
  performance event, not an authentication/realtime outage.
- Keep TTL + tag invalidation as the correctness baseline; add the epoch guard only if the
  acceptance test shows stale reinsertion.
- Fix the per-user SSR cache findings before enabling a large SSR cache:
  stable avatar URLs, filtered-page exclusions, and a memory budget.
- Put the cache hit/miss and SSR `reason` metrics into the acceptance record so the
  replication design is measured, not assumed.
