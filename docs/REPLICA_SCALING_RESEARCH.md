# Replica and split-scale research

Research for the next two samples:

1. **CarParkerReplicas** — several replicas of the whole CarParker application, with a
   multi-node MongoDB replica set.
2. **TechLandierDistributed** — TechLandier split into web, worker, realtime and SSR roles,
   with each role replicated independently.

The names are working names; they can change without affecting the design. Both samples
stay independent, like the existing replica samples, and do not modify the original
CarParker or TechLandier folders.

**Status:** implemented. `samples/CarParkerReplicas` and
`samples/TechLandierDistributed` are built and verified. The designs below remain the
reference for both samples; see their READMEs and the focused PgDog/cache documents for the
operational details.

The cache design is in [INERTIA_CACHE_MULTI_REPLICA.md](INERTIA_CACHE_MULTI_REPLICA.md).
This document covers the two deployment shapes, the shared infrastructure, the concrete
findings in the current code, and the migration plan.

## 1. Executive summary

### CarParkerReplicas

- Three (or five) identical app replicas behind HAProxy.
- Each replica keeps the current whole-app shape: ASP.NET web app, a Bun SSR process
  managed by Inertia.Net, the durable job consumer and the TickerQ cron schedules.
- MongoDB becomes a three-member replica set (`rs0`), with a one-shot `mongo-init` service
  and an `initialize` service that creates indexes once.
- Redis becomes the shared state: Data Protection keys, SignalR backplane, distributed rate
  limits and the Inertia cache.
- This demonstrates that the existing CarParker boundaries (Mongo atomic claims, cron
  leases, S3 uploads) already tolerate replicas; the remaining work is shared auth, realtime
  and caching.

### TechLandierDistributed

- One PostgreSQL primary and **two physical read replicas**, fronted by **PgDog** as the
  connection pooler, load balancer and single application endpoint. All three servers use
  `postgres:18-alpine`.
- `MainApp` replicas: routes, Inertia, Vue client assets, no local Bun.
- `Worker` replicas: PostgreSQL queue consumers, job handlers, cron schedules.
- `Realtime` replicas: SignalR connections only, behind `/realtime`.
- An independent SSR pool (`ssr-1..N`, Bun only). MainApp receives a list of SSR URLs in
  `Inertia:SsrUrls`; 5 app replicas can share 3 SSR replicas.
- Redis is split conceptually:
  - critical Redis: Data Protection, SignalR backplane, rate limits;
  - cache Redis: Inertia props and SSR HTML (optional to fail).
- The split exposes two current single-process assumptions that must be fixed:
  - `StockAlerts` low-stock dedupe is an in-memory dictionary; replicas can duplicate
    alerts.
  - TechLandier's `Jobs` table lacks an index for the expired-lease reset; add
    `(Status, LockedUntil)` before scaling workers.

### What this proves

| Sample | Replication model | Scaling unit | Trade-off |
|---|---|---|---|
| CarParkerReplicas | Whole app repeated | Everything together | Simple, but SSR/worker CPU cannot scale independently and each app carries Bun |
| TechLandierDistributed | Roles separated | Web, worker, realtime, SSR independently | More projects and shared contracts, but each layer scales for its own bottleneck |

## 2. Current state

The existing single-app samples already contain most of the correctness work:

| Concern | CarParker today | TechLandier today |
|---|---|---|
| Durable jobs | Mongo atomic `findOneAndUpdate` claims with 5-minute leases | PostgreSQL `FOR UPDATE SKIP LOCKED` claims with 5-minute leases |
| Cron | TickerQ in memory + Mongo `CronLeases` fencing | TickerQ in memory + PostgreSQL `CronLeases` fencing |
| Realtime | SignalR, process-local | SignalR, process-local |
| Inertia cache | Per-process `IMemoryCache` | Per-process `IMemoryCache` |
| Data Protection | File system under `/data` | File system under `/data` |
| Rate limits | In-memory fixed windows | In-memory fixed windows |
| SSR | Bun supervised by the app | Bun supervised by the app |
| Uploads | S3-compatible | S3-compatible |
| Deployment | One app instance (docs say so explicitly) | One app instance (docs say so explicitly) |

The existing replica samples provide proven patterns:

- `SvelteMongoReplicasBlog`: HAProxy, Redis Data Protection, SignalR backplane, shared
  rate limits, app replicas, one Mongo replica set.
- `VuePgReplicasAdvBlog`: separate MainApp/Worker/Realtime projects, YARP/HAProxy, Redis
  backplane, PostgreSQL queue, TickerQ coordination, independent replica counts.

This migration reuses those patterns rather than inventing new ones.

## 3. Shared infrastructure

Both samples need the same shared-state decisions.

### 3.1 Data Protection

Without shared keys, a cookie or antiforgery token created by replica A is unreadable by
replica B. That breaks:

- the authentication cookie;
- the antiforgery cookie (`XSRF-TOKEN`) and therefore every Inertia mutation;
- the Inertia state cookie (flash messages and validation errors across redirects);
- email verification and other time-limited tokens.

Use the same `SetApplicationName` in every role and persist the key ring in Redis (or a
shared certificate-backed store). Redis must be persistent; losing the key ring logs every
user out and invalidates outstanding links.

### 3.2 SignalR

Add the Redis backplane to every process that publishes or accepts SignalR connections:

```csharp
builder.Services.AddSignalR()
    .AddStackExchangeRedis(options =>
    {
        options.Configuration.ChannelPrefix = RedisChannel.Literal(namespace + ":signalr");
    });
```

Keep sticky sessions at the proxy. The backplane distributes messages; it does not remove
the affinity needed for negotiate and long polling. Use a separate affinity cookie for web
and realtime when the roles are separate.

### 3.3 Rate limits

The current fixed-window limiters are per process. With N replicas the effective limit is
N × the configured value. Reuse the Svelte sample's Redis Lua limiter, keyed by:

```text
{namespace}:limits:{policy}:{user-or-ip}
```

Auth limits should remain fail-closed if Redis is unavailable. Other limits can be tuned
separately, but a single shared limiter is simpler for the first migration.

### 3.4 Inertia cache

Register one `IInertiaCacheStore` per process, pointing at the shared cache Redis. See the
focused cache document for the store design, tag index, versioning and SSR cache findings.

### 3.5 Health and readiness

Suggested split:

| Check | Failure status | Why |
|---|---|---|
| MongoDB / PostgreSQL | Unhealthy | Requests cannot be served correctly |
| Critical Redis (DP, backplane, limits) | Unhealthy | Auth/realtime/limits are not safe |
| Cache Redis | Degraded | Pages still render from DB/SSR |
| Inertia SSR | Degraded | `Preferred` falls back to client rendering |
| Disk/storage | Unhealthy if required | Uploads/backups |

HAProxy should treat `200` as healthy. ASP.NET Core maps `Healthy` and `Degraded` to `200`
and `Unhealthy` to `503` by default, so an SSR or cache outage does not remove every app
replica from rotation.

Add `/alive` (liveness, process only) and `/health` (readiness) where a platform needs both.

### 3.6 Proxy affinity

Use HAProxy cookie affinity for both the web and realtime pools:

```haproxy
cookie REPLICA insert indirect nocache httponly dynamic
dynamic-cookie-key <sample-specific-key>
```

For the split sample, route `/realtime` and `/realtime/*` to the realtime backend with its
own `REALTIME_REPLICA` cookie. The web backend keeps `REPLICA`.

### 3.7 Cookie and namespace isolation

Samples on `localhost` share cookies across ports. New samples must scope:

- auth cookie: `.cpr.auth` / `.tld.auth`;
- antiforgery cookie: `.cpr.af` / `.tld.af`;
- Inertia state cookie: `o.State.CookieName = ".cpr.state"` / `.tld.state`;
- HAProxy affinity cookies;
- Data Protection application name;
- Redis key prefix and SignalR channel prefix;
- MongoDB database / PostgreSQL database;
- S3 bucket or key prefix;
- analytics salt.

Without this, signing into one sample can overwrite another sample's local login.

### 3.8 Connection pools

Replicas multiply database connections.

- Npgsql defaults to a pool of 100 per process. Five web + three worker + three realtime
  processes can ask for 1,100 connections against a default PostgreSQL limit of 100.
  Set `Maximum Pool Size` per role (e.g. web 20, worker 10, realtime 5). TechLandier uses
  PgDog transaction pooling, so its Npgsql pools can stay at 10 and PgDog owns the backend
  pool. Keep migrations on a direct connection.
- MongoDB's driver defaults to 100 connections per process. Set `maxPoolSize=50` for the
  sample and measure. Transactions use the same pool.

### 3.9 Deployment and cache versioning

- All app replicas must run the same Vite manifest. The Inertia asset version is the
  manifest hash; mismatched builds produce 409 full-page reloads and different SSR keys.
- Prop cache keys do not include the build. Set `CACHE_VERSION` to the release tag and use
  the **same value in web and worker**. Bump it when cached prop shapes change, or clear
  the cache on deploy.
- Deploy app and SSR bundles from the same release. During a rolling deploy, an old app can
  meet a new SSR bundle; `Preferred` falls back to client rendering, but it is noisy.
- Job payload changes need a compatible deploy order (workers before web for new job types)
  or versioned payloads. The queue is at-least-once.

## 4. CarParkerReplicas

### 4.1 Topology

```text
                         HAProxy :8080
                  affinity cookie REPLICA
             /----------------+----------------\
             v                v                 v
        app-1            app-2             app-3
   ASP.NET + Bun SSR  ASP.NET + Bun SSR  ASP.NET + Bun SSR
   jobs + cron        jobs + cron        jobs + cron
             \                |                 /
              \---------------+----------------/
                              |
              +---------------+----------------+
              |                                |
          Redis                       MongoDB rs0
   DP keys, SignalR backplane      mongo1 / mongo2 / mongo3
   rate limits, Inertia cache      (three data-bearing members)
                                           ^
                                      mongo-init
                                           ^
                                     initialize
                                  (indexes, then exits)
```

### 4.2 MongoDB

- Three data-bearing members, not two plus an arbiter. An arbiter can break a tie but
  cannot take reads or help data durability.
- `mongo-init` is a one-shot service:
  - waits for all three `mongod` processes;
  - runs `rs.initiate` with service names
    (`mongo1:27017`, `mongo2:27017`, `mongo3:27017`);
  - waits for a writable primary.
- App connection string:

```text
mongodb://mongo1:27017,mongo2:27017,mongo3:27017/?replicaSet=rs0&w=majority&maxPoolSize=50
```

- Do not use `directConnection=true` for the multi-node app connection. The driver must
  discover the primary and secondaries.
- `MONGO_MODE=replica` is required. `AppDb.InitializeAsync` already rejects a standalone
  server in this mode.
- `w=majority` makes a write survive the loss of one node. Transactions already use
  `WithTransactionAsync`; the driver retries transient transaction errors, but handlers
  must remain idempotent.
- Local Compose can leave authentication off on a private network. Production must use a
  keyfile or X.509, TLS and restricted networking. Do not copy the local no-auth setup to a
  public host.
- Read preference stays `primary` by default. `secondaryPreferred` for the cached lot
  analytics is an optional later step; do not use secondaries for booking/claim decisions.

The three-node initiate and a transaction were verified locally:

```text
members=3, primary=mongo3:27017
transaction ok
```

### 4.3 Index initialization

CarParker currently runs `db.InitializeAsync()` on every boot. With three replicas that is
three concurrent index builds. It is idempotent, but noisy and wasteful.

Add:

- `Database:InitializeOnStartup` (default `true` for single-app and tests);
- `--initialize` CLI flag that runs `InitializeAsync` and exits;
- Compose `initialize` service with the flag and `Database:InitializeOnStartup=false` on
  the app replicas;
- app replicas depend on `initialize: service_completed_successfully`.

`--seed-demo` and `--create-admin` still need initialization; keep calling it explicitly in
those CLI paths.

### 4.4 Shared Redis

CarParker has no Redis today. Add:

- `IConnectionMultiplexer` with a parsed `REDIS_URL`;
- Data Protection key ring in Redis;
- SignalR backplane with a sample channel prefix;
- Redis fixed-window rate limiters for `auth`, `register` (including its precognition branch),
  `uploads`, `views`, `writes`;
- `RedisInertiaCacheStore` for props and SSR HTML;
- a Redis health check.

Use one Redis for the whole-app sample, or a critical Redis plus a cache Redis. The
whole-app sample is already the simpler of the two; one Redis with persistence and
`volatile-lru` is acceptable for the demo. Production should split cache from critical
state.

### 4.5 Jobs and cron in every replica

Keeping the whole application in each replica means every app process runs:

- the web pipeline;
- the managed Bun SSR process;
- `JobWorker` consumers;
- TickerQ schedules.

This is safe because:

- jobs are claimed atomically from MongoDB;
- cron occurrences are fenced by `CronLeases`;
- delivery is at-least-once and handlers are expected to be idempotent.

It is also noisy: every replica schedules the same cron and the lease decides the winner.
Document that a production deployment can set `CRON_ENABLED=false` on web replicas and run
a dedicated cron process, but the sample keeps the whole-app promise. Do not set
`JOBS_ENABLED=false` on every replica, or realtime push jobs will never be consumed.

### 4.6 SSR

Each app replica keeps `o.Ssr.UseNodeProcess(...)`, so each container has its own Bun on
loopback port 13714. That is intentional:

- the app image already contains Bun and the SSR bundle;
- no SSR URL configuration is needed;
- an SSR failure only affects that replica, and `Preferred` falls back to client rendering.

The shared Redis SSR cache still helps: a render produced by replica A can be reused by B,
even though each has its own Bun.

This is the deliberate contrast with TechLandierDistributed, where the SSR pool is
independent.

### 4.7 Compose services

| Service | Replicas | Notes |
|---|---:|---|
| `proxy` | 1 | HAProxy; only public port; affinity |
| `app` | 3 (default) | Whole app; `Replicas__Enabled=true`; depends on initialize + Redis |
| `initialize` | 0 after exit | `--initialize`; `Replicas__Enabled=false` |
| `redis` | 1 | AOF + volume; critical state and cache |
| `mongo1..3` | 3 | `mongod --replSet rs0 --bind_ip_all` |
| `mongo-init` | 0 after exit | `rs.initiate` + wait for primary |

Suggested local ports: app `8184`, Mongo `27020` (only for debugging), Redis `6388`.

### 4.8 Tests and acceptance

- Existing CarParker tests keep running against a single-node replica set on 27017.
- Add store tests against Redis (shared invalidation, TTL, tags).
- Add a Compose acceptance script:
  - sign in through HAProxy repeatedly and verify `X-Replica` changes;
  - create/cancel a booking on one replica and read it on another;
  - admin analytics invalidates after a booking/payment on another replica;
  - notification badge read on one replica is cleared on another;
  - stop the Mongo primary, wait for election, make a write/read, verify the app recovers;
  - verify `/health` stays `200` when SSR is stopped (degraded) and `503` when Mongo or
    critical Redis is stopped.
- Multi-node Mongo failover is a Compose test, not a host `dotnet test` default; the
  replica set advertises container names, so host tests should keep using the single-node
  test set.

### 4.9 CarParker findings

- `JobWorker` and `CronJobs` are already replica-safe; no new coordination is required.
- `AlertsService` uses a unique `sourceKey` partial index, so duplicate alert inserts are
  possible to reject; preserve that.
- `RealtimeEvents` enqueues `RealtimePush`; with the backplane, the worker that handles the
  job can deliver to clients on every replica.
- The in-memory job wake channel is only an optimization; other replicas poll. Lower the
  poll interval if realtime latency matters, or accept the current 10 seconds.
- Data Protection currently writes to `/data`; replace the per-container volume with Redis.
  Do not keep both and assume they share keys.

## 5. TechLandierDistributed

### 5.1 Topology

```text
                              HAProxy :8080
                    /realtime -> REALTIME_REPLICA
                    /          -> REPLICA
             /----------------+------------------\
             v                v                  v
       mainapp-1..N      realtime-1..N       worker-1..N
       routes + Inertia   SignalR only        queue + cron
       Vue assets         hub connections     job handlers
             |                  |                  |
             +----------+-------+------------------+
                        |
             +----------+-----------+
             |                      |
       critical Redis            PgDog :6432
   DP keys, backplane, limits   pool + read/write split
             |                    /       |       \
       cache Redis        postgres-primary  replica-1  replica-2
   Inertia props + SSR HTML   writes          reads      reads
             ^
             |
       ssr-1..N (Bun only)
       selected through Inertia:SsrUrls
```

### 5.2 Project layout

Recommended:

```text
apps/
  MainApp/        Web SDK, routes, Inertia, Vue assets, migrate/seed CLI
  Worker/         Worker SDK, queue consumer, job handlers, TickerQ schedules
  Realtime/       Web SDK, StoreHub, cookie auth, origin checks, health
lib/
  TechLandier.Core/       data, entities, feature services, jobs queue, realtime contracts
  TechLandier.Caching/    RedisInertiaCacheStore + cache adapters
  TechLandier.ServiceDefaults/  optional telemetry/health helpers
tests/
  TechLandier.Tests/
```

This is a **layer split**, not a feature-per-library split. It keeps the migration
tractable while giving independent deployables. Feature libraries can be extracted later
from `Core` if the sample needs to demonstrate that boundary too.

Rules:

- `MainApp`, `Worker` and `Realtime` may reference only `lib/` projects.
- `Core` must not reference Inertia.Net. Route rendering, `FeatureResponses` and
  `InertiaSetup` stay in `MainApp`.
- `Caching` may reference Inertia.Net and Redis; it is the only shared library that does.
- No app references another app.
- Tests may host all three roles.

### 5.3 Core extraction

Move to `TechLandier.Core`:

- `Data/` (`AppDb`, migrations, seeder, connection parsing, seed models);
- feature models, services, validators, mappers and utils;
- `Email`, `Storage`, `AuthHttp`, `AuthService`, `JobQueue`, job payload records;
- `StoreHub`, `RealtimeEvents`, `RealtimeOriginMiddleware`, `StockAlerts`;
- `CronTasks` and `CronLease` (but not the TickerQ setup);
- `SaveResult`, `Paged`, `Toast`, user-id extensions.

Stay in `MainApp`:

- route files (`Features/*/*.cs`);
- `FeatureResponses`, `NotFoundPage`, `Forbidden`;
- `InertiaSetup`, `PipelineSetup`, `StoreRootView`;
- `ViewTracker` / `ViewEventWriter` (request-path analytics);
- `PromoUnsubscribe`, `RequestLogMiddleware`;
- `Cli` (`--migrate`, `--seed-demo`, `--create-admin`);
- `app.html`, `ClientApp`, `wwwroot`.

Stay in `Worker`:

- `JobWorker`, all `IJobHandler` implementations;
- `CronSetup` / TickerQ registration;
- `WorkerSetup`.

Stay in `Realtime`:

- `Program`, hub mapping, realtime auth setup, origin middleware, health.

### 5.4 Removing Inertia from Core

Core services currently inject `IInertiaCache` in ten places. Introduce a narrow port:

```csharp
public interface IPropCacheInvalidator
{
    ValueTask RemoveByTagAsync(string tag, CancellationToken ct = default);
}
```

- `MainApp` implements it with `IInertiaCache`.
- `Worker` implements it with `IInertiaCacheStore` directly.
- `NotificationsCache`, `AlertsService`, `RealtimeEvents`, `CronTasks`, `AdminService`,
  `CareService`, `CreditService`, `CheckoutService`, `OrderService` and `SupportService`
  depend on the port, not Inertia.

`IInertiaCache.SetPropAsync` is not used today, so the port only needs tag invalidation.
If write-through is added later, extend the port deliberately.

### 5.5 Worker role

`Worker` is a Generic Host, not a web app:

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.AddCoreData().AddCoreServices().AddWorkerServices();
using var host = builder.Build();
if (builder.Configuration.GetValue("Cron:Enabled", true)) host.UseTickerQ();
await host.RunAsync();
```

It registers:

- `JobWorker` consumers;
- all job handlers, including `RealtimePushHandler`;
- TickerQ schedules and `CronTasks`;
- `AddSignalR().AddStackExchangeRedis(...)` so `IHubContext<StoreHub>` publishes to the
  backplane (the hub is not mapped in this process);
- the Redis `IPropCacheInvalidator`;
- `Storage`, `Mailer`, `StockAlerts`.

It does not map HTTP endpoints. Docker health is process liveness (`kill -0 1`).

### 5.6 Realtime role

`Realtime` is a small ASP.NET Core app:

- shared Data Protection and cookie authentication;
- `AuthHttp.ValidatePrincipal` and `AuthService.FindAsync` for deleted/updated users;
- `AddSignalR().AddStackExchangeRedis(...)`;
- `/realtime` hub mapping with `CloseOnAuthenticationExpiration = true`;
- origin checks;
- `/health` and `/alive`.

It does not reference Inertia, does not run jobs/cron, and does not enqueue. Its database
pool can be small because it only validates connections.

### 5.7 MainApp role

`MainApp` owns:

- Inertia and Vue;
- all routes and `FeatureResponses`;
- `InertiaSetup` with remote SSR URLs;
- the Redis cache store;
- Redis rate limits and antiforgery;
- the migrate/seed CLI.

It does **not**:

- map `/realtime`;
- start a Bun process;
- run `JobWorker` or TickerQ;
- run migrations on normal startup.

It enqueues jobs through `JobQueue`; the worker consumes them.

### 5.8 SSR pool

Build an SSR-only image from the same frontend build:

```dockerfile
FROM oven/bun:1-debian AS ssr
WORKDIR /app
COPY --from=web /src/ssr ./ssr
ENV NODE_ENV=production \
    INERTIA_SSR_HOST=0.0.0.0 \
    INERTIA_SSR_PORT=13714
EXPOSE 13714
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
  CMD ["bun","-e","fetch('http://127.0.0.1:13714/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
ENTRYPOINT ["bun","ssr/ssr.js"]
```

The current SSR build binds `127.0.0.1:13714` because `vite.config.ts` passes
`host: '127.0.0.1'` to `@inertiajs/vite`. A standalone container must bind `0.0.0.0`.
Two options:

- build-time: set the plugin host to `0.0.0.0` for `command === 'build'`;
- runtime: replace the plugin-generated bootstrap with an explicit `createServer` in
  `ClientApp/ssr.ts` that reads `INERTIA_SSR_HOST` / `INERTIA_SSR_PORT`.

The runtime option is better for one bundle used by app-managed and standalone SSR. The
plugin recognizes a top-level `createServer` and wraps it for the Vite dev endpoint, so the
custom entry still works in development. Verify the built bundle after the change.

The SSR image needs only `ssr/` and Bun. `ssr.noExternal: true` already bundles dependencies,
so no `node_modules` copy is required.

`MainApp` configuration:

```csharp
o.Ssr.Mode = SsrMode.Preferred;
o.Ssr.Urls.Add("http://ssr-1:13714");
o.Ssr.Urls.Add("http://ssr-2:13714");
o.Ssr.Urls.Add("http://ssr-3:13714");
o.Ssr.Timeout = TimeSpan.FromSeconds(2);
o.Ssr.Concurrency.MaxActive = 2;
o.Ssr.Concurrency.MaxQueued = 8;
o.Ssr.CircuitBreaker.FailureThreshold = 2;
o.Ssr.Cache.Duration = TimeSpan.FromSeconds(300);
o.Ssr.Cache.StaleFor = TimeSpan.FromSeconds(60);
// Do not call UseNodeProcess.
```

An explicit list is used instead of a single `ssr` DNS name because Inertia's least-in-flight
selection is per URL. Five app replicas each balance across the three URLs. A production
proxy in front of SSR is still recommended for active health checks; see the cache document
for the timeout finding.

### 5.9 PostgreSQL and PgDog

The full design, tested configuration and consistency rules are in
[PGDOG_TECHLANDIER.md](PGDOG_TECHLANDIER.md). Summary:

- **Topology:** `mainapp`/`worker`/`realtime` -> PgDog `:6432` -> one
  `postgres-primary` + two `postgres-replica-*` streaming replicas. All three servers use
  `postgres:18-alpine`.
- **Pooling:** PgDog transaction mode with `default_pool_size=20`, `min_pool_size=2`.
  Npgsql pools can stay small (`Maximum Pool Size=10`) because PgDog multiplexes backend
  connections.
- **Read/write split:** `read_write_split="include_primary_if_replica_banned"`,
  `round_robin` and prepared statements enabled. PgDog sends manual transactions to the
  primary; read-only transactions can use replicas.
- **Application connections:** `DATABASE_URL` is pinned with
  `Options=-c pgdog.role=primary` for writes and fresh reads. `DATABASE_READ_URL` uses
  `Options=-c pgdog.role=replica` for selected lag-tolerant public catalog/search/related
  and cached analytics reads. Do not use the replica role for authorization, checkout,
  order/payment, inventory or read-after-write flows. The implementation uses
  `IReadDbFactory`/`ReadOnlyDbFactory` to create `AppDb` contexts with the read
  `DbContextOptions`; `StoreService` (public catalog/search/detail/related) and
  `SalesService` (analytics) use it.
- **Migrations:** `migrate` connects **directly** to `postgres-primary` via
  `DATABASE_MIGRATE_URL`, bypassing PgDog. `Database:MigrateOnStartup=false` on
  MainApp/Worker/Realtime.
- **Replication:** primary init creates a `replicator` role and appends a replication line
  to `pg_hba.conf`; replicas run `pg_basebackup -R` and stream from the primary. Health is
  `pg_isready`; PgDog `SHOW REPLICATION` exposes lag.
- **Failover:** PgDog does **not** promote a replica. Use Patroni/managed PostgreSQL for
  promotion. With `role="auto"` PgDog follows a database that has already been promoted;
  the sample uses static roles and documents the production step.
- **Admin/metrics:** PgDog admin database `admin` on 6432 (`SHOW POOLS`,
  `SHOW REPLICATION`, `SHOW SERVERS`), HTTP health on 8080 and OpenMetrics on 9090.
- **Index:** add `(Status, LockedUntil)` on `Jobs` for the expired-lease reset.

### 5.10 Cron

Keep TickerQ in the Worker with the existing database lease. Multiple workers may schedule
the same occurrence, but `CronLeases` fences execution. A TickerQ Redis provider, as used by
`VuePgReplicasAdvBlog`, is an alternative if scheduler logs or missed-run behavior become a
problem; it adds a Redis database reservation and serialization caveats. Do not introduce
it unless needed.

### 5.11 Current-code findings to fix during the split

1. **`StockAlerts` is per process.** `CheckoutService` calls
   `alerts.ShouldAlert(productId, stock, threshold)` and the implementation is a
   `ConcurrentDictionary`. Two web replicas can both fire the same low-stock alert.
   Replace with a Redis `SET key 1 NX EX ...` gate keyed by
   `stock-alert:{productId}:{threshold}`, and delete the key when stock returns above the
   threshold. Keep an in-memory implementation when replicas are disabled for tests.
2. **Job lease reset index.** Add `(Status, LockedUntil)` to `Job` before running several
   workers; `ResetExpiredLocksAsync` currently scans by those columns.
3. **SSR cache inputs.** Fix the presigned-avatar and filtered-listing findings from the
   cache document before enabling a large shared SSR cache.
4. **`ViewTracker` stays in MainApp.** It writes view rows directly; each request is handled
   by one web replica, so there is no cross-replica duplication. Moving it to Worker would
   require a new queue and is unnecessary.
5. **`RealtimeEvents` is a producer only.** In the split, it must not depend on a local hub;
   it enqueues `RealtimePush`. The Worker's handler publishes through the backplane.
6. **Cookie/state isolation.** Scope auth, antiforgery and Inertia state cookies; otherwise
   local TechLandier and TechLandierDistributed sessions collide on `localhost`.

### 5.12 Compose services

| Service | Replicas | Notes |
|---|---:|---|
| `proxy` | 1 | HAProxy; `/realtime` to realtime, rest to mainapp |
| `mainapp` | 3–5 | Inertia, Vue assets, no Bun, no jobs/cron/migrations |
| `worker` | 2–3 | Queue consumers, handlers, TickerQ |
| `realtime` | 2–3 | SignalR + auth + health |
| `ssr-1..3` | 3 | Bun only; private; health check |
| `migrate` | 0 after exit | `--migrate` before serving roles |
| `cache` | 1 | Redis for Inertia props/SSR |
| `redis` | 1 | Critical Redis: DP, backplane, limits |
| `postgres-primary` | 1 | `postgres:18-alpine`; writes and replication source |
| `postgres-replica-1..2` | 2 | `postgres:18-alpine`; streaming replicas |
| `pgdog` | 1 | `ghcr.io/pgdogdev/pgdog:v0.1.61`; pooler + read/write split |

Suggested local ports: app `8185`, PostgreSQL primary `5442`, PgDog `5443`, critical
Redis `6389`, cache Redis `6390`. Replica ports stay internal.

### 5.13 Tests and acceptance

- Unit/integration:
  - Core services resolve without Inertia;
  - `IPropCacheInvalidator` reaches the shared store from MainApp and Worker;
  - `StockAlerts` fires once across two service providers sharing Redis;
  - `JobWorker` claims are exclusive across two workers (existing behavior, keep the test);
  - `RealtimeHost` builds and maps only `/realtime`, `/health`, `/alive`;
  - SSR URL configuration builds `Ssr.Urls` and does not call `UseNodeProcess`.
- Cache/SSR:
  - two MainApp hosts share one SSR cache;
  - two fake SSR servers receive balanced renders;
  - stop/refuse one SSR server; assert circuit/fallback behavior;
  - filtered product pages do not fill the SSR cache.
- Compose acceptance:
  - signup/login through the proxy and verify `X-Replica` changes;
  - place an order on one web replica, see it on another;
  - admin product edit invalidates catalog on another replica immediately;
  - notification read on one replica clears the badge on another;
  - order status change from Worker reaches a client connected to a different Realtime
    replica;
  - scale `ssr` down/up without touching web replicas; pages still render (fallback or
    remaining SSR);
  - scale workers and verify jobs are processed once/at-least-once with no handler errors;
  - stop cache Redis; pages still work and health is degraded; stop critical Redis; health
    fails.

## 6. Configuration matrix

### CarParkerReplicas

```text
APP_PORT=8184
MONGO_PORT=27020
REDIS_PORT=6388
DATABASE_URL=mongodb://mongo1:27017,mongo2:27017,mongo3:27017/?replicaSet=rs0&w=majority&maxPoolSize=50
DATABASE_NAME=carparker_mongo_replicas
MONGO_MODE=replica
DATABASE_URL_DOCKER=...
REDIS_URL=redis://redis:6379
REPLICA_NAMESPACE=CarParkerReplicas:production
CACHE_VERSION=v1
DATABASE_INITIALIZE_ON_STARTUP=false   # app replicas
REPLICAS_ENABLED=true
JOBS_CONCURRENCY=4
CRON_ENABLED=true
AUTH_SECURE_COOKIES=false              # local HTTP; true behind TLS
```

### TechLandierDistributed

```text
APP_PORT=8185
POSTGRES_PORT=5442
PGDOG_PORT=5443
REDIS_PORT=6389
CACHE_REDIS_PORT=6390
DATABASE_URL=Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=...;Options=-c pgdog.role=primary;Maximum Pool Size=10
DATABASE_READ_URL=Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=...;Options=-c pgdog.role=replica;Maximum Pool Size=10
DATABASE_MIGRATE_URL=Host=postgres-primary;Port=5432;Database=techlandier;Username=techlandier;Password=...
DATABASE_MIGRATE_ON_STARTUP=false
REDIS_URL=redis://redis:6379
CACHE_REDIS_URL=redis://cache:6379
REPLICA_NAMESPACE=TechLandierDistributed:production
CACHE_VERSION=v1
INERTIA__SSR=true
INERTIA__SSRURLS__0=http://ssr-1:13714
INERTIA__SSRURLS__1=http://ssr-2:13714
INERTIA__SSRURLS__2=http://ssr-3:13714
INERTIA__SSRTIMEOUTSECONDS=2
INERTIA__SSRCACHESECONDS=300
INERTIA__SSRSTALESECONDS=60
AUTH_SECURE_COOKIES=false
CRON_ENABLED=false                     # mainapp/realtime; true on worker
JOBS_ENABLED=false                     # mainapp/realtime; true on worker
```

Exact env names should follow the existing `ApplyPlainNames` conventions in each sample.
Do not expose SSR, Redis or PostgreSQL ports publicly.

## 7. Migration plan

### Phase 0 — research spike (done)

- Redis `IInertiaCacheStore` prototype: tag invalidation, overwrite cleanup, TTL, cross-store
  visibility, Inertia `SetPropAsync` integration.
- Two app hosts + two fake SSR servers + shared cache: second host reused the first render.
- Three-node MongoDB replica set + `w=majority` transaction.
- Confirmed the `Ssr.Urls` timeout/failover behavior and the `ExpireWhen.GT` tag-TTL trap.

### Phase 1 — shared building blocks

- Add `RedisInertiaCacheStore` and its tests.
- Add `ReplicaSetup` (Redis connection, Data Protection, SignalR backplane, limiter,
  health checks) as a reusable pattern inside each sample (samples stay independent).
- Add cache version/namespace conventions.
- Add acceptance scripts for cache invalidation and SSR sharing.

### Phase 2 — CarParkerReplicas

- Copy CarParker, rename project/solution/namespaces, scope cookies/namespaces/ports.
- Add Redis infrastructure and cache store.
- Add `--initialize` and `Database:InitializeOnStartup`.
- Add the three-node Mongo Compose topology and `mongo-init`.
- Add HAProxy and app replica service.
- Update README, DEPLOY, REPLICAS, cache notes, acceptance record.
- Run existing tests plus replica acceptance.

### Phase 3 — TechLandierDistributed core extraction

- Create `lib/TechLandier.Core` and move data/services/infrastructure.
- Introduce `IPropCacheInvalidator`; remove Inertia from Core.
- Create `lib/TechLandier.Caching` with the Redis store and adapters.
- Add the project-boundary guard.
- Keep the existing single-app sample untouched.

### Phase 4 — TechLandierDistributed hosts

- Add `MainApp`, `Worker`, `Realtime`.
- Split `FeatureSetup`, `AuthSetup`, `Cli`, `PipelineSetup`.
- Add worker handlers/cron, realtime hub/auth, remote SSR setup.
- Add SSR Docker target and runtime host/port.
- Add Compose, HAProxy and migrate/seed services.
- Add `postgres:18-alpine` primary + two streaming replicas and PgDog (`v0.1.61`),
  `DATABASE_URL`/`DATABASE_READ_URL`/`DATABASE_MIGRATE_URL`, and the selected read path.
- Fix `StockAlerts` and add the job index.
- Update docs, tests and acceptance.

### Phase 5 — hardening and measurement

- Pool sizing and load tests.
- Cache hit-rate and SSR outcome measurements.
- Mongo failover and worker scale tests.
- README/root table/port notes.
- Optional OpenTelemetry metrics for Inertia SSR/cache, SignalR and queues.

Rough effort (not a commitment): shared cache/infrastructure 1–2 days; CarParker sample
3–5 days; TechLandier Core extraction 3–5 days; hosts/SSR/Compose 4–7 days; hardening and
docs 2–3 days.

## 8. Risks

| Risk | Mitigation |
|---|---|
| Redis is a single point of failure | Persistent Redis, AOF, replicas/Sentinel in production; separate cache Redis |
| Losing the key ring logs everyone out | Back up Redis/key ring; use a stable application name |
| Cache invalidation races | Short TTL/stale, no cached authorization, optional epoch guard, acceptance test |
| Stale cache after deploy | `CACHE_VERSION`, Vite version in SSR keys, clear props/SSR on release if needed |
| SSR version skew | Build app and SSR from one release; deploy together; `Preferred` fallback |
| Hung SSR server costs a timeout per request | Short timeout, low circuit threshold, SSR proxy with active checks |
| Per-user SSR cache memory | Stable avatar URLs, skip cache for auth/filtered pages, Redis maxmemory |
| PostgreSQL connection exhaustion | PgDog transaction pooling + small Npgsql pools; keep PgDog pool sizes below `max_connections` |
| PgDog is a single database entry point | Restartable/managed PgDog, HTTP health, multiple PgDog instances behind a TCP load balancer |
| Replica lag / read-after-write | Primary role for auth/checkout/order/read-after-write paths; replica role only for selected lag-tolerant reads; `ban_replica_lag_bytes` |
| PgDog does not promote replicas | Use Patroni/managed PostgreSQL; `role="auto"` only after external promotion |
| Mongo failover transient errors | `w=majority`, retryable writes, `WithTransactionAsync`, health checks |
| Duplicate low-stock alerts | Redis `SET NX` gate |
| Duplicate/reordered jobs | Idempotent handlers, at-least-once contract, lease renewal for long jobs |
| SignalR without affinity | Keep proxy cookies; backplane does not replace affinity |
| Local cookie collisions | Scoped cookie names and state cookie |
| Rolling deploy job schema mismatch | Deploy order or versioned payloads; drain queue |
| Multi-node Mongo on one host is not host HA | Document; use managed replica set for real HA |

## 9. Decisions needed

1. **Names and ports.** Recommended: `CarParkerReplicas` and
   `TechLandierDistributed`; ports 8184/27020/6388 and 8185/5442/5443/6389/6390.
2. **TechLandier split shape.** Recommended: `Core` + `Caching` + three hosts, not
   feature-per-library yet and not a single image with role flags.
3. **Redis topology.** Recommended: separate critical and cache Redis for
   TechLandierDistributed; one Redis is acceptable for CarParkerReplicas.
4. **Database read scaling.** CarParker keeps primary reads. TechLandierDistributed uses
   PgDog with one primary + two read replicas; the main connection is primary-pinned and
   selected lag-tolerant reads use the replica role. PgDog does not promote replicas.
5. **Orchestration.** Recommended: Compose first, Aspire optional later. Compose is easier
   to inspect and matches the existing Svelte replica sample.
6. **CarParker cron placement.** Recommended: every whole-app replica runs cron with the
   existing lease; document the production option to disable cron on web replicas.
7. **Observability.** Optional but recommended: OpenTelemetry for Inertia metrics, SignalR,
   queue age and cache hit rate.

## 10. References

- Inertia.Net 1.5.1 package README and XML docs (installed under
  `~/.nuget/packages/inertia.net/1.5.1`), especially “Several SSR servers”, “Response
  cache”, `IInertiaCacheStore`, `SsrOptions`, and “Deploying behind a proxy”.
- Inertia.Net repository: <https://github.com/adzamkomladev/Inertia.Net>
- Existing samples:
  - `samples/SvelteMongoReplicasBlog/docs/REPLICAS.md`
  - `samples/VuePgReplicasAdvBlog/docs/DEPLOYMENT.md`
  - `samples/VuePgReplicasAdvBlog/docs/WORKER_COORDINATION.md`
  - `samples/VuePgReplicasAdvBlog/docs/HOSTING_RESEARCH.md`
- Microsoft Learn:
  - SignalR Redis backplane: <https://learn.microsoft.com/aspnet/core/signalr/redis-backplane>
  - SignalR scale-out: <https://learn.microsoft.com/aspnet/core/signalr/scale>
  - Data Protection configuration: <https://learn.microsoft.com/aspnet/core/security/data-protection/configuration/overview>
  - Distributed caching: <https://learn.microsoft.com/aspnet/core/performance/caching/distributed>
- PgDog:
  - <https://pgdog.dev/>
  - <https://docs.pgdog.dev/>
  - <https://docs.pgdog.dev/features/connection-pooler/>
  - <https://docs.pgdog.dev/features/load-balancer/>
  - <https://docs.pgdog.dev/administration/replication/>
  - <https://docs.pgdog.dev/administration/pools/>
  - <https://docs.pgdog.dev/administration/clients/>
  - <https://docs.pgdog.dev/administration/tasks/>
  - <https://docs.pgdog.dev/administration/servers/>
  - Full notes: [PGDOG_TECHLANDIER.md](PGDOG_TECHLANDIER.md)
- MongoDB:
  - Replication: <https://www.mongodb.com/docs/manual/replication/>
  - Read preference: <https://www.mongodb.com/docs/manual/core/read-preference/>
  - Transactions: <https://www.mongodb.com/docs/manual/core/transactions/>
- HAProxy affinity:
  <https://www.haproxy.com/blog/load-balancing-affinity-persistence-sticky-sessions-what-you-need-to-know/>
- .NET worker services:
  <https://learn.microsoft.com/dotnet/core/extensions/workers>
