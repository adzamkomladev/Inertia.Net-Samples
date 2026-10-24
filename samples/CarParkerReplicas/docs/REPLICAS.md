# Running several CarParker replicas

CarParkerReplicas keeps the **whole application** in each replica: ASP.NET Core web pipeline, managed Bun SSR
process, durable job consumer and TickerQ cron schedules. The replica boundary is the process, not a role. This is
the simplest way to scale the sample and the deliberate contrast with a split web/worker/realtime/SSR deployment.

## Topology

```text
                         HAProxy :8184
                  affinity cookie CPR_REPLICA
             /----------------+----------------\
             v                v                 v
        app-1            app-2             app-3
   ASP.NET + Bun SSR  ASP.NET + Bun SSR  ASP.NET + Bun SSR
   jobs + cron        jobs + cron        jobs + cron
             \                |                 /
              +---------------+----------------+
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

- `mongo-init` waits for all three members, runs `rs.initiate` with the Compose service names and waits for a
  writable primary. The app URL is
  `mongodb://mongo1:27017,mongo2:27017,mongo3:27017/?replicaSet=rs0&w=majority&maxPoolSize=50`. Do not add
  `directConnection=true` for the multi-node app connection.
- `initialize` is the only process that runs index creation during a deploy. It uses the same Redis key ring as
  the app replicas and exits; app replicas set `Database:InitializeOnStartup=false`.
- Redis is required. Every app replica has the same `Replicas:Namespace`, `CACHE_VERSION` and Data Protection
  application name. Redis holds the key ring, the SignalR channel prefix, rate-limit counters and the shared
  Inertia cache.
- HAProxy is the only public port. It health-checks `/health`, uses `CPR_REPLICA` cookie affinity, and adds
  `X-Replica`. The app adds `X-App-Replica` (container hostname or `INSTANCE_ID`).

## Why the whole app can repeat

The existing CarParker boundaries are already multi-process safe:

| Concern | Mechanism |
|---|---|
| Bookings, payments, refunds | Mongo conditional updates and `WithTransactionAsync`; `w=majority` |
| Durable jobs | Atomic Mongo claims with five-minute leases; at-least-once delivery; idempotent handlers |
| Cron | TickerQ schedules in every process, fenced by Mongo `CronLeases` |
| Realtime delivery | `RealtimeEvents` enqueues `RealtimePush`; workers publish through the Redis SignalR backplane |
| Auth/antiforgery/state | Shared Data Protection key ring in Redis; scoped cookie names |
| Rate limits | Redis fixed-window counters shared by all replicas |
| Inertia props/SSR | `RedisInertiaCacheStore` shared by all replicas |
| Uploads | S3-compatible object storage, not local disk |

No new queue or scheduler coordination is required. The queue is at least once: duplicate job delivery and
duplicate pushes are expected, and handlers must stay idempotent. Job leases are not renewed; a handler longer
than five minutes needs a smaller unit of work or explicit lease renewal before scaling concurrency.

## Startup and deploy order

1. Start MongoDB members and `mongo-init`.
2. Start Redis and wait for its health check.
3. Run `initialize` (`--initialize`, `Replicas:Enabled=false`, `Database:InitializeOnStartup=true`).
4. Start app replicas (`Replicas:Enabled=true`, `Database:InitializeOnStartup=false`).
5. Start HAProxy, or route traffic once every app replica is healthy.

The app replicas wait for `initialize: service_completed_successfully` and `redis: service_healthy` in Compose.
Do not run multiple `initialize` processes during a deploy; concurrent index builds are usually harmless but
wasteful and noisy. A failed index build should stop the deploy.

## Affinity and SignalR

HAProxy uses:

```haproxy
cookie CPR_REPLICA insert indirect nocache httponly dynamic
dynamic-cookie-key CarParkerReplicas
```

Affinity is required for SignalR negotiation and long polling, and it keeps the Inertia state/antiforgery flow on
one replica during a redirect. The backplane distributes messages; it does **not** remove the need for affinity.
The cookie is scoped to the host, not the port. Do not run CarParkerReplicas and another sample on the same host
with overlapping cookie names; this sample uses `.cpr.auth`, `.cpr.af`, `.cpr.state` and `CPR_REPLICA`.

## Scaling

- Scale `app` up or down. Each replica is independent and advertises itself to HAProxy through DNS
  (`server-template app 1-10 app:8080`).
- Keep at least one replica with `JOBS_ENABLED=true`. Disabling jobs on every replica leaves queued realtime
  pushes and emails undelivered.
- Every replica may run cron because `CronLeases` fences occurrences. If the duplicate scheduler work is noisy,
  set `CRON_ENABLED=false` on web replicas and run a dedicated app-shaped cron process. The sample keeps cron on
  every replica to demonstrate the safe default.
- Each app replica runs its own Bun SSR process on loopback `13714`. SSR CPU scales with app replicas. The shared
  Redis SSR cache still lets replica A reuse replica B's HTML.
- Size MongoDB and Redis connection counts per replica. The Compose Mongo URL uses `maxPoolSize=50`; lowering it
  is the first lever when adding replicas.

## Health and failure behavior

| Check | Failure status | Effect |
|---|---|---|
| MongoDB ping | Unhealthy (503) | Replica removed; requests cannot be trusted |
| Shared Redis ping | Unhealthy (503) | Replica removed; auth/realtime/limits are unavailable |
| Inertia cache ping | Degraded (200) | Cache misses; pages still read MongoDB and render |
| Inertia SSR | Degraded (200) | `Preferred` falls back to client rendering |
| Disk/storage | Required checks decide | Uploads/backups fail independently of the cache |

HAProxy treats HTTP 200 as healthy, so a degraded SSR/cache does not remove every replica. A replica whose Mongo
or critical Redis connection is down returns 503 and is removed.

## Failover

- **Mongo primary loss:** the driver discovers the new primary. Retryable writes and transactions recover after
  the election (typically a few seconds). Some requests can time out; clients should retry transient failures.
  Keep read preference `primary` for booking/claim decisions.
- **Redis loss:** Data Protection cannot unprotect cookies, rate limits fail closed and SignalR clients disconnect.
  Restore Redis (AOF/volume) before restarting app replicas. The cache degrades to database/SSR reads.
- **App replica loss:** HAProxy removes it after its health checks fail; the affinity cookie sends new sessions to
  a remaining replica. Existing SignalR connections reconnect and should reload authoritative state.
- **SSR process loss:** only that replica falls back to client rendering. The managed process restarts; the health
  check stays degraded until a render succeeds.
- **HAProxy loss:** no public entry point. Run it under a platform that restarts it, or add a redundant proxy pair
  with a shared virtual IP; this is outside the sample.

A three-node replica set on one Docker host protects against process failure, not host failure. Production HA
needs separate hosts or a managed MongoDB/Redis service.

## Acceptance

With the stack running on `http://localhost:8184`:

```sh
# 1. Requests are served by different replicas.
for i in 1 2 3 4 5 6; do curl -sI http://localhost:8184/health | grep -i '^X-App-Replica'; done

# 2. A replica reports readiness and identity.
curl -sI http://localhost:8184/health
curl -s http://localhost:8184/alive

# 3. Stop Redis; readiness must fail (503) rather than serving unsafe auth/rate limits.
docker compose stop redis
curl -s -o /dev/null -w '%{http_code}\n' http://localhost:8184/health

# 4. Start it again before continuing.
docker compose start redis

# 5. Stop one app replica; the proxy should keep serving from the others.
docker compose ps
```

For a data-level check, sign in through the proxy, create a booking, then read it after the affinity cookie is
cleared (or request a different replica via `curl`) and confirm the shared Mongo data. Admin analytics
invalidation, notification badges and SignalR delivery are exercised by the existing app tests; a Compose
acceptance run should repeat them across replicas when changing cache or realtime code.

## Limits

- Whole-app replication couples SSR, worker and web scaling. A large SSR workload or worker backlog scales
  everything, including Bun and job concurrency.
- Every replica schedules cron. The database lease prevents duplicate execution but not duplicate wake-ups.
- The Compose stack is a local demonstration: no Mongo auth/TLS, one Redis, one HAProxy and three data members on
  one host. See [DEPLOY.md](DEPLOY.md) for production hardening.
- The Inertia cache is eventually consistent. Invalidation races can reinsert a value read before a write; short
  TTLs bound the damage. Cached props are display data only and never authorize a request.
