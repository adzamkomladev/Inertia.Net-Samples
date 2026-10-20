# Memory at rest and database performance — research

Measured 2026-10-20 against the production Dockerfiles (the ones Coolify deploys) for
VuePgBlog, vue-inertia-postgresql, ReactSqliteBlog and SvelteMongoBlog, all on Inertia.Net 1.3.0.

## Summary

- **Nothing is leaking, and Inertia.Net is not the cause.** Inertia.Net adds about **5 MiB** to a
  bare ASP.NET Core app. The managed (.NET GC) heap of VuePgBlog at rest is only **~10 MB of live objects
  (17 MB committed)**.
- **What Coolify reports is the whole container**: the `dotnet` process **plus the `bun` SSR
  process** Inertia.Net starts, plus page cache for the DLLs and binaries the processes map.
  A fresh VuePgBlog container idles at **~135 MiB**. About 45 MiB of that is bun, which you can see by
  turning SSR off (91 MiB).
- **~180–230 MiB "at rest" after traffic is normal.** After a 3-minute load the container
  settles at **~179–193 MiB** and stays there. The .NET runtime keeps JIT-compiled code, type data
  and a GC budget it already paid for. Bun keeps its grown JS heap. Neither one hands that
  memory back while it's idle unless the container is short of memory. Setting a memory limit makes both
  stay smaller.
- **Runtime settings help, but only by 10–15%.** ReadyToRun publishing, `DOTNET_GCConserveMemory`,
  workstation GC (already set in three of the four Dockerfiles but **not ReactSqliteBlog**, which costs about +21 MiB) are
  the levers that measurably move the number. Most of what's left is simply the size of the app:
  EF Core, SignalR, TickerQ, the AWS SDK, Redis, auth and 162 loaded assemblies, plus a second runtime
  (bun) for SSR.
- **The big problem is in the database: Postgres full-text search.** In a 50-user load test against 500k
  seeded posts, native search was **86.7% of all Postgres time**: average **7.0 s**,
  p95 **17.1 s**, and it slowed every other request down with it. A stored `tsvector` column
  cut the same queries from **427 ms → 123 ms** (typical word) and **83 s → 0.7 s**
  (common word). This affects VuePgBlog **and** vue-inertia-postgresql.

## How it was measured

- Docker Desktop (WSL2), 8 vCPU, 7.7 GiB visible to Docker; .NET runtime 10.0.13, bun 1.4.3,
  PostgreSQL 18, MongoDB 9.
- Each image was built from the sample's own `Dockerfile`, unchanged. Variants changed only
  environment variables, except the ReadyToRun variant, which added
  `/p:PublishReadyToRun=true -r linux-x64` to restore and publish.
- **"Idle"** means: start the container, wait for `/health`, request `/`, `/posts`, `/login` and
  `/register` 5 times each, wait 45 s, then read the numbers.
- **Container memory** is `docker stats`, the same cgroup figure Coolify displays.
  **RSS** and its anon/file/shmem split come from `/proc/<pid>/status` and `smaps_rollup`. **GC numbers**
  come from `dotnet-counters` (System.Runtime) and `dotnet-gcdump`, run from a sidecar that shares
  the container's PID namespace.
- **Data:** the PostgreSQL database was seeded with the repo's `--seed-demo`: 78,000 users,
  500,000 posts, 1.95M likes, 750k comments, 1.5M comment likes, 3.5M seen-post rows and 500k bookmarks.
  The Posts table is 1.3 GB.
- **Load:** the repo's `performance-tests/scripts/blog-user-flow.js` (`PROFILE=load`, ramping to 50 VUs
  over 9 min, with logins, feeds, search, posts, likes, comments and profiles). Also an anonymous
  browse script (30 VUs for 3 min: `/posts` then a random `/posts/{slug}`) for the post-load idle tests.
- Each variant was measured once. Expect a few MiB of run-to-run noise, so treat differences
  under ~5 MiB as noise.

## 1. Where the memory goes (VuePgBlog, fresh container, idle)

| Part | Size | What it is |
| --- | ---: | --- |
| Container total (`docker stats`/Coolify) | **135.4 MiB** | everything below, counted once per cgroup |
| `dotnet` RSS | 185 MB | |
| ├ file-backed | 95 MB | mapped DLLs and `.so` files (CoreLib 13 MB, coreclr 6 MB, clrjit 4.5 MB, EF Core 2.8 MB…); clean, shared, and reclaimable under pressure |
| ├ `memfd:doublemapper` (shmem) | 53 MB | JIT-compiled code, stubs and runtime type data (W^X double mapping) |
| └ anonymous | 41 MB | GC heap (17 MB committed) plus native allocations and thread stacks |
| managed heap (live objects) | 9.5 MB | 132k objects. The largest are DI caches, Npgsql type maps and routing tables. Nothing Inertia-specific shows up in the dump |
| `bun ssr/ssr.js` RSS | 67 MB | 30 MB anon plus 37 MB of the bun binary mapped |
| Docker `HEALTHCHECK` | ~14 MB every 15 s | `bun -e fetch(...)` starts a second bun process for each check |

Container-level split (cgroup `memory.stat`): anon 73 MB, file 158 MB (only the active part is
counted), shmem 54 MB.

## 2. Is it Inertia.Net? (control apps, same base image)

A minimal app (`app.MapGet("/", ...)`), built and measured the same way, with features added one at a time:

| App | Container idle | dotnet RSS | Assemblies loaded |
| --- | ---: | ---: | ---: |
| Hello world (minimal API) | 21.2 MiB | 79 MB | 91 |
| + Inertia.Net (CSR page render) | 26.7 MiB | 94 MB | 101 |
| + EF Core/Npgsql query only | 49.0 MiB | 127 MB | 111 |
| + Inertia.Net + EF Core | 51.5 MiB | 137 MB | 120 |
| VuePgBlog, SSR off | 91.0 MiB | 184 MB | — |
| VuePgBlog, SSR on (as shipped) | 135.4 MiB | 185 MB | 162 |

**Inertia.Net costs about 5 MiB.** EF Core costs about 25 MiB. The remaining ~40 MiB is the app's own
features: SignalR, TickerQ, the AWS S3 SDK, StackExchange.Redis, cookie auth, Data Protection,
rate limiting, health checks, hosted workers and many compiled EF queries. The SSR process
(bun) accounts for the other ~45 MiB.

## 3. Runtime knobs tested (VuePgBlog)

### Fresh container, idle

| Variant | Container | dotnet RSS | Notes |
| --- | ---: | ---: | --- |
| Baseline (as shipped: workstation GC, SSR on, cron on) | **135.4 MiB** | 185 MB | |
| `Inertia__Ssr=false` | 91.0 MiB | 184 MB | bun gone: **−44 MiB** |
| ReadyToRun publish | **114.5 MiB** | 192 MB | precompiled code is file-mapped instead of JIT'd: **−21 MiB**, shmem 54→41 MB |
| ReadyToRun + SSR off | 82.1 MiB | 191 MB | |
| SSR off + `Cron__Enabled=false` | 87.9 MiB | 180 MB | TickerQ costs about 3 MiB |
| `DOTNET_gcServer=1` (server GC) | 139.2 MiB | 204 MB | +4 MiB idle; heap is lower, but each GC heap reserves more |
| `DOTNET_EnableWriteXorExecute=0` | 127.6 MiB | 192 MB | the shmem just becomes anon memory; same RSS |
| `DOTNET_TieredPGO=0` | n/a (stats timed out) | 183 MB | no meaningful change |
| `DOTNET_TieredCompilation=0` | 127.3 MiB | 188 MB | slower startup (114% CPU while measured) |
| `--memory=128m` limit | 124 MiB / 128 MiB | 141 MB | stays healthy; the kernel reclaims file pages (and swaps on Docker Desktop) |

### Idle 4 minutes after a 3-minute, 30-VU browse load

This is the number that matches what you see in Coolify.

| Variant | Fresh (30 s after start) | **After-load idle** | dotnet RSS | bun RSS | Throughput, p95 |
| --- | ---: | ---: | ---: | ---: | --- |
| Baseline | 98 MiB | **178.6 MiB** | 236 MB | 78 MB | — |
| `MALLOC_ARENA_MAX=2` | 93 MiB | 179.1 MiB | 234 MB | 78 MB | 75 req/s, 641 ms |
| ReadyToRun + `MALLOC_ARENA_MAX=2` | 118 MiB | 170.5 MiB | 243 MB | 78 MB | |
| `DOTNET_GCConserveMemory=7` + `MALLOC_ARENA_MAX=2` | 116 MiB | 167.3 MiB | 223 MB | 78 MB | |
| `BUN_OPTIONS=--smol` | 116 MiB | 171.9 MiB | 228 MB | 78 MB | 88 req/s, 442 ms |
| **ReadyToRun + GCConserveMemory=7 + bun --smol** | 85 MiB | **164.9 MiB** | 239 MB | 76 MB | 88 req/s, 433 ms |

An earlier run of the same browse load settled at **192–194 MiB**, with dotnet at 234 MB and bun at 78 MB,
five minutes after the load ended. At that point:

- GC committed was 32 MB (heap 23 MB, of which 10 MB is gen2 fragmentation).
- `doublemapper` (JIT code and type data) had grown from 54 to 67 MB.
- Anonymous memory had grown from 41 to 78 MB.

**Throughput and latency did not get worse with any of the memory settings.**

During the 50-VU `load` profile the container peaked at **228 MiB** (dotnet 263 MB, bun 88 MB).
It began dropping as soon as the VUs ramped down.

Takeaways:

- `MALLOC_ARENA_MAX` and `--smol` make no measurable difference here, so skip them.
- ReadyToRun and `GCConserveMemory` together take about 14 MiB (8%) off the after-load idle, and
  about 20 MiB off a fresh start. ReadyToRun also shortens startup and JIT CPU after a redeploy.
- **A memory limit is the most effective setting.** .NET reads the cgroup limit and sizes its
  GC to it (heap hard limit = 75% of the limit). The kernel then reclaims the ~95 MB of
  file-backed pages before it would ever kill the container. With 128 MiB the app ran fine idle.
  For production, 256–384 MiB per app gives the GC room under load without letting it sprawl.

## 4. What you can change

| # | Finding | Where | Effect |
| --- | --- | --- | --- |
| M1 | **ReactSqliteBlog runs Server GC.** Its Dockerfile doesn't set `DOTNET_gcServer=0` (the other three do) | `samples/ReactSqliteBlog/Dockerfile` | **151 → 129.5 MiB** idle (−21 MiB) measured |
| M2 | Publish ReadyToRun | all Dockerfiles | −21 MiB fresh, −8 MiB after load, faster cold start |
| M3 | `DOTNET_GCConserveMemory=7` | all Dockerfiles | about −11 MiB after load |
| M4 | Set a memory limit in Coolify (Resource limits → Memory) | Coolify | caps growth; GC adapts to the limit |
| M5 | SSR is ~30% of the container (44–78 MB of bun). It only renders `Posts/Index` and `Posts/Show` | `PublicPostRootView` | to drop it entirely, use `SsrMode.Disabled` and keep client rendering. Otherwise accept it as the cost of SSR |
| M6 | The `HEALTHCHECK` starts a new bun process (~14 MB, ~100 ms CPU) every 15 s | Vue/Svelte Dockerfiles | use `curl -fsS` (ReactSqliteBlog already does) or let Coolify run the check, and use a 30 s interval |
| M7 | `RequestLogMiddleware` calls `Process.GetCurrentProcess()` (reads `/proc`) and logs at Information on **every** request | `Infrastructure/RequestLogMiddleware.cs` (all samples) | CPU and allocations per request. Read `Environment.WorkingSet`/`GC.GetTotalMemory` instead, or log at Debug in production |
| M8 | `PublicPostRootView` reads `app.html` from disk and does 3 string replaces per CSR request | `Infrastructure/PublicPostRootView.cs` | cache the file contents once at startup |
| M9 | OpenTelemetry (vue-inertia-postgresql) traces every request with AlwaysOn sampling | `TelemetrySetup.cs` | measured: **123.4 → 127.8 MiB** idle, GC committed 19 → 32 MB. Use `ParentBased(TraceIdRatioBased(0.1))` in production |
| M10 | SvelteMongoBlog idles at **181 MiB**: its image adds `mongodump` plus a second ICU (`libicu-dev`), which shows up as ~88 MB of *active* file cache | `samples/SvelteMongoBlog/Dockerfile` | this is page cache, so the kernel reclaims it under pressure. `libicu-dev` isn't needed (the aspnet image has ICU) |

Recommended Dockerfile changes (for VuePgBlog; same idea for the others):

```dockerfile
# api stage
RUN dotnet restore -r linux-x64 /p:PublishReadyToRun=true
RUN dotnet publish -c Release -r linux-x64 --self-contained false -o /app --no-restore \
    /p:UseAppHost=false /p:PublishReadyToRun=true

# runtime stage
ENV DOTNET_gcServer=0 \
    DOTNET_GCConserveMemory=7
```

## 5. Database findings

### 5.1 PostgreSQL: VuePgBlog and vue-inertia-postgresql (same code)

**Load test, 50 VUs, 9 min, 500k posts.** 10,051 requests at 18.4 req/s. Latency overall
p50 76 ms, p95 6,435 ms, p99 15,390 ms. The `http_req_duration` threshold failed. Postgres was at
~800% CPU throughout.

| Step | Count | Avg ms | p95 ms | p99 ms |
| --- | ---: | ---: | ---: | ---: |
| **search** | 1100 | **6977** | **17102** | **19688** |
| feed | 1100 | 78 | 196 | 500 |
| post | 1100 | 73 | 201 | 456 |
| comments_partial | 1100 | 93 | 236 | 424 |
| view | 1100 | 96 | 255 | 1040 |
| like | 1100 | 121 | 340 | 762 |
| comment | 1100 | 169 | 437 | 1086 |
| profile | 1100 | 110 | 264 | 448 |
| login | 50 | 503 | 963 | 1122 |

**Top statements from `pg_stat_statements` over the same run:**

| Statement | Calls | Total s | Mean ms | Share of DB time |
| --- | ---: | ---: | ---: | ---: |
| Native search (`ts_headline` + `to_tsvector(...) @@` + `ORDER BY ts_rank(to_tsvector(...))`) | 1085 | 7548.5 | 6957 | **86.7%** |
| `EnrichAsync` viewer-state query (Liked/Bookmarked/Reposted/RepostCount/ViewCount) | 5535 | 27.0 | 4.9 | 0.3% |
| Comments page (`ShowAsync` deferred prop) | 2200 | 10.0 | 4.5 | 0.1% |
| Jobs claim (`UPDATE … FOR UPDATE SKIP LOCKED`) | 1428 | 8.4 | 5.9 | 0.1% |
| `DISCARD ALL` (connection reset on pool return) | 67,661 | 2.9 | 0.04 | 0.0% |
| `SELECT * FROM Users WHERE Id = $1` (`ValidatePrincipal`) | 10,382 | 1.9 | 0.18 | 0.0% |

#### P1. Native full-text search (fix first)

`PostsService.FeedAsync` searches with:

```sql
WHERE to_tsvector('english', "Title" || ' ' || "Body") @@ to_tsquery(...)
ORDER BY ts_rank(to_tsvector('english', "Title" || ' ' || "Body"), ...) DESC, ...
```

The GIN index on that expression finds the matching rows quickly, but `ts_rank(to_tsvector(...))`
**re-tokenises the full body of every match**, and the bitmap recheck tokenises it again. A
term with 285k matches read 62k buffers and ran for 27.9 s under load. The search terms are
prefix queries (`word:*`, from `BuildMatch`), which widens the matches further.

Measured on the seeded DB (single run each, idle DB unless noted):

| Query | Matches | Current query | Stored `tsvector`, headline after LIMIT | Stored, ORDER BY recency (no rank) |
| --- | ---: | ---: | ---: | ---: |
| `streamlined:*` (typical feed-title word) | 4,878 | 427 ms | **123 ms** | — |
| `voluptas` (common word) | ~285,000 | 27.9 s under load / 83 s cold | **703 ms** | **1.9 ms** |

Proposed migration. On a 500k-row table, `ADD COLUMN … STORED` rewrote the table in 3 min 37 s
under an exclusive lock, and the index took 1 min 25 s. On production, either do it in a maintenance window, or
add a plain `tsvector` column kept up to date by a trigger, backfill it in batches, and use `CREATE INDEX CONCURRENTLY`:

```sql
ALTER TABLE "Posts" ADD COLUMN "SearchVector" tsvector
  GENERATED ALWAYS AS (setweight(to_tsvector('english', "Title"), 'A')
                    || setweight(to_tsvector('english', "Body"),  'B')) STORED;
CREATE INDEX "IX_Posts_SearchVector" ON "Posts" USING GIN ("SearchVector") WHERE "ArchivedAt" IS NULL;
DROP INDEX "IX_Posts_Search";   -- the old expression index (same 56 MB size as the new one)
```

The A/B weights rank title matches higher than body matches, which changes ordering slightly. Use
`to_tsvector('english', "Title" || ' ' || "Body")` instead to keep today's ranking exactly.

Query: rank on the stored vector, and compute `ts_headline` only for the rows on the page:

```sql
SELECT p."Id", ts_headline('english', p."Body", p.q, 'StartSel=<mark>, StopSel=</mark>, MaxWords=24, MinWords=8') AS "Snippet"
FROM (SELECT "Id", "Body", "CreatedAt", ts_rank("SearchVector", q) AS r, q
      FROM "Posts", to_tsquery('english', {match}) q
      WHERE "ArchivedAt" IS NULL AND "SearchVector" @@ q
      ORDER BY r DESC, "CreatedAt" DESC, "Id" DESC
      LIMIT {take} OFFSET {skip}) p
ORDER BY p.r DESC, p."CreatedAt" DESC, p."Id" DESC
```

Further options:

- Very broad terms will still rank hundreds of thousands of rows (about 0.7 s each). Either cap the candidates
  (rank only the newest N matches), or order by recency for one-word queries (1.9 ms measured).
- `OFFSET` paging gets slower on deep pages. Meilisearch (already wired up) avoids all of this.
- The SQLite sample already does search the efficient way: an FTS5 table ranked with `bm25`.

#### P2. Per-render `COUNT(*)` of views and reposts

`EnrichAsync` computes `ViewCount = COUNT(PostViews)` and `RepostCount = COUNT(Reposts)` per post on
every feed, post and profile render. `RealtimeEvents.ChangedAsync` computes them again for every post
in each 500-view batch. The seed creates no views, so this cost was invisible in the load test.
With 20k views on each of 10 feed posts, the page's view counts took **14 ms** after `VACUUM`. The cost grows
linearly with views, and it gets worse when autovacuum lags (heap fetches on a write-heavy table). ReactSqliteBlog
already stores `Posts.ViewCount`. Do the same here: increment it in `ViewEventWriter.Insert` (one
`UPDATE … SET "ViewCount" = "ViewCount" + n` per post per batch), and add a `RepostCount` column
kept up to date where reposts are created and deleted.

#### P3. `RealtimeEvents.ChangedAsync` work per event

Each call loads the **full Post entity, including `Body`**, runs two counts, and is serialized behind a
single global `SemaphoreSlim`. `ViewEventWriter` calls it once per distinct post in each batch, one after another.
Project only `AuthorId, Slug, ArchivedAt, LikeCount, CommentCount` (and the stored counters from P2).

#### P4. Two queries on every authenticated request

`AuthService.ValidatePrincipal` loads the whole `Users` row (including the `Preferences` JSON) and then
runs a second query for the avatar key, on **every** authenticated request (10,382 calls in the run).
`FeedAsync` then queries `Users.Preferences.PostsPerPage` again. Options:

- Project the 5 claim fields and the avatar key in one query (join on `Assets`).
- Revalidate at most every N minutes, by storing a "validated at" claim and refreshing when it's stale.
- Put `PostsPerPage` in a claim.

#### P5. Npgsql connection reset (`DISCARD ALL`)

67,661 resets in 9 minutes. Each one is cheap (0.04 ms, sent along with the first query), but it also throws
away server-side prepared statements. Consider `No Reset On Close=true;Max Auto Prepare=20`. This is safe
here because the app doesn't use session state (temp tables, `SET`). Benchmark it before adopting; it's a small
improvement, not a fix.

#### P6. Smaller items

- `AddDbContext` → `AddDbContextPool<AppDb>`: avoids building a new DbContext and its service
  scope for every request and job.
- `JobWorker.ClaimAsync` runs `ResetExpiredLocksAsync` before **every** claim (1,430 calls). Run it on
  a timer instead (e.g. every minute). Make `IX_Jobs_Status_RunAt` a partial index `WHERE "Status" = 'Pending'`
  so Done/Failed rows (kept until the daily prune) don't bloat it.
- `IX_Posts_CreatedAt_Id` / `IX_Posts_PublishedAt_Id` → partial indexes `WHERE "ArchivedAt" IS NULL`,
  to match the query filter on every feed.
- `LoadDetail` reads the post twice (summary projection, then `Body/CoverAssetId/UpdatedAt`) before
  running `EnrichAsync`'s two queries. Fold the second read into the first projection.
- The search path runs three `AnyAsync` checks (index-state, jobs, posts without state) per search
  request whenever the Meilisearch client is registered. The last one can scan all posts. Cache that "ready"
  result for a few seconds.

### 5.2 SQLite: ReactSqliteBlog

Not load-tested here (no seeded SQLite DB). These come from code review:

- **Connection pragmas.** Only `journal_mode=WAL` is set (once). Add a `DbConnectionInterceptor`
  that runs on each opened connection: `PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=5000;
  PRAGMA temp_store=MEMORY; PRAGMA mmap_size=268435456; PRAGMA cache_size=-20000;`.
  `synchronous=NORMAL` is safe with WAL and removes an fsync per commit. That matters a lot because SQLite
  has a single writer.
- **Writes in the GET path.** `ViewTracker.TrackAsync` (called from `GET /posts/{id}`) runs
  `INSERT OR IGNORE` and then a separate `UPDATE … ViewCount + 1`. That's two auto-commits (two WAL
  syncs) on the first daily view of each post, inside the request. Wrap both in one transaction, or queue views
  into a channel and batch them the way VuePgBlog's `ViewEventWriter` does.
- **Analytics predicates that can't use an index.** `AnalyticsService` filters with
  `substr(l.CreatedAt, 1, 10) >= {f} AND substr(...) <= {t}` on `PostLikes` and `Comments`, in both the
  chart query and the per-post subqueries of `TopPostsAsync`. Wrapping the column in `substr` stops
  SQLite from using the existing `(PostId, CreatedAt)` indexes for the range. Compare the raw
  column instead: `l.CreatedAt >= '2026-10-01' AND l.CreatedAt < '2026-10-21'`. EF stores
  DateTime as sortable ISO text, so string comparison is correct.
- Search is fine: FTS5 with external content and `bm25`.

### 5.3 MongoDB: SvelteMongoBlog

- **Sequential round trips per page.** Building a list of post summaries (`SocialService`) awaits
  about 7 queries one after another: likes, bookmarks, reposts, repost counts, view counts, users, assets. The single-post path (`PostsService`)
  awaits 6. `IMongoClient` is thread-safe, so run the independent ones with `Task.WhenAll`.
- **Counts from raw collections on every render.** Repost and view counts are `$group` aggregations
  over `Reposts`/`PostViews`, and `Realtime.cs` runs `CountDocumentsAsync` on both per event. Same fix
  as P2: keep `ViewCount`/`RepostCount` on the post document and `$inc` them.
- **Redundant indexes.** `author_timeline {AuthorId, CreatedAt}` is a prefix of
  `author_timeline_cursor {AuthorId, CreatedAt, Id}`, so drop it. Check `user_repost_timeline` against
  `user_repost_cursor` (they differ only in the last key), and `post_user_unique` against `post_likes` on Likes,
  with `$indexStats` after some real traffic. Every index costs WiredTiger cache and write time.
- **mongod memory.** By default WiredTiger takes 50% of (RAM − 1 GB) for its cache. On a shared
  Coolify host, set `--wiredTigerCacheSizeGB` (e.g. 0.25–0.5) or a container memory limit.
  The empty `mongo:9` container idled at 145 MiB here.

## 6. Prioritised action list

1. **P1: stored `tsvector` for Postgres search** (VuePgBlog and vue-inertia-postgresql). Search was 86.7% of
   DB time, and fixing it removes the p95/p99 blow-ups for every page.
2. **M1:** add `DOTNET_gcServer=0` to the ReactSqliteBlog Dockerfile (−21 MiB).
3. **M2 + M3 + M4:** ReadyToRun, `DOTNET_GCConserveMemory=7`, and a Coolify memory limit (256–384 MiB).
4. **P2/P3 + Mongo counts:** store the view and repost counters instead of counting per render.
5. **P4:** cut `ValidatePrincipal` down to one cheap query, or revalidate periodically.
6. **SQLite pragmas** and the `substr` predicates (ReactSqliteBlog).
7. Smaller items: M6–M8, P5, P6, the Mongo round trips and redundant indexes.

## Appendix: raw idle measurements

| Container | Container idle | dotnet RSS (anon / file) | bun RSS | GC committed |
| --- | ---: | ---: | ---: | ---: |
| VuePgBlog | 135.4 MiB | 189 MB (41 / 95) | 67 MB | 17.0 MB |
| vue-inertia-postgresql, telemetry off | 123.4 MiB | 189 MB (44 / 96) | 60 MB | 19.3 MB |
| vue-inertia-postgresql, telemetry on | 127.8 MiB | 197 MB (50 / 97) | 60 MB | 32.2 MB |
| ReactSqliteBlog (as shipped, Server GC) | 151.0 MiB | 199 MB (53 / 97) | 68 MB | 20.1 MB |
| ReactSqliteBlog, `DOTNET_gcServer=0` | 129.5 MiB | 191 MB (47 / 97) | 68 MB | 14.1 MB |
| SvelteMongoBlog | 181.0 MiB | 166 MB (40 / 89) | 49 MB | 12.8 MB |

The test containers, volumes, network and images used for these measurements were removed afterwards.
