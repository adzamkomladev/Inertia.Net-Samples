# Replica topology

TechLandierDistributed separates four workloads so each scales independently. The sample stays a
single deployable release: one Vite manifest, one SSR bundle, one database schema. PostgreSQL is a
primary plus two streaming replicas behind PgDog; see [PGDOG.md](PGDOG.md).

## Roles and scaling units

| Role | Scale by | State | Notes |
|---|---|---|---|
| MainApp | HTTP concurrency/CPU | none (S3 for uploads) | Inertia rendering, Vue assets, auth, rate limits, enqueueing. No Bun/jobs/cron. |
| Worker | queue age/CPU | PostgreSQL claims + cron leases | Consumers and TickerQ only. No HTTP. |
| Realtime | connected sockets | Redis backplane | Cookie auth, origin checks, health. Sticky sessions required. |
| SSR | render CPU/memory | none | Bun only. MainApp selects by `Inertia:SsrUrls`; it never starts Bun. |
| PgDog | one endpoint; HA via a TCP LB later | transaction pool | Primary/replica routing, transaction pooling, OpenMetrics, admin DB. |
| PostgreSQL | primary + two physical replicas | streaming replication | Migrations only through the one-shot `migrate` service; reads split by `pgdog.role`. |
| Critical Redis | one highly available instance | Data Protection, backplane, limits, gates | Must be persistent; loss signs users out. |
| Cache Redis | one evictable instance | Inertia props + SSR HTML | Failure is a performance event, not an outage. |

## Why the split is safe

1. **Claims are atomic.** Workers use `UPDATE ... RETURNING` with `FOR UPDATE SKIP LOCKED` and a
   five-minute lease; expired locks are reset before each claim. The `(Status, LockedUntil)` index
   supports that reset.
2. **Cron is fenced.** TickerQ may schedule the same occurrence in several workers, but
   `CronLeases` holds a unique lease per occurrence. Completion updates only match the held lease.
3. **Jobs are at-least-once.** Handlers are idempotent and retry with backoff
   (`1, 2, 4, 8` minutes, then failed at five attempts).
4. **Database routing is explicit.** Normal role connections use `pgdog.role=primary`. Public
   catalogue/home/search/product/related/deals and cached analytics use a separate read connection
   with `pgdog.role=replica`. Auth, checkout, orders, notifications, reviews-after-write and every
   mutation stay on the primary. Migrations use `DATABASE_MIGRATE_URL` directly.
5. **Auth is shared.** Every role uses the same Data Protection key ring in critical Redis and the
   `.tld.auth` cookie. A session created by MainApp opens `/realtime` on any Realtime replica.
6. **Passwords/links are shared.** Verification and unsubscribe tokens use the same key ring, so
   Worker-generated email links validate on any MainApp replica.
7. **Invalidation is shared.** Writes call `IPropCacheInvalidator`; MainApp uses `IInertiaCache`,
   Worker uses the Redis store directly. Both remove the same tags.
8. **Low-stock alerts are shared.** The Redis `SET NX EX` gate fires once per product/threshold
   crossing across replicas; restock deletes the gate.

## Request and event flow

```text
Browser -> HAProxy --/realtime--> Realtime (cookie auth, hub)
                   --everything else--> MainApp (Inertia render)
                                           | DATABASE_URL / DATABASE_READ_URL
                                           v
                                         PgDog
                                      /         |
                              primary            replicas
                              writes + jobs      public/analytics reads
                                           ^
                                           | FOR UPDATE SKIP LOCKED
                                        Worker -> job handler
                                           | realtime jobs
                                           v
                                     critical Redis backplane
                                           |
                                  every Realtime replica -> clients
```

MainApp invalidates the cache after commits. The next request on any replica rebuilds from the
database. Realtime events are queued, so a client may see the toast slightly after the write; the
inbox row is durable and clients reload authoritative props.

## Proxy affinity

HAProxy uses two cookies:

- `TLD_REPLICA` for the MainApp pool.
- `TLD_REALTIME` for `/realtime` and `/realtime/*`.

WebSockets and long polling need affinity even with the backplane. The backplane does not replace
it. Configure `timeout tunnel` (the sample uses 1 hour) and allow Upgrade/Connection headers.

## Health and readiness

- `/health` includes PostgreSQL (through PgDog), PgDog/read-replica health, critical Redis, cache Redis
  (Degraded) and Inertia SSR where applicable.
- `/alive` runs only the process check; use it for liveness probes.
- HAProxy's active check is `GET /health` and expects 200. ASP.NET maps Degraded to 200, so a cache
  or SSR outage does not remove every web replica.
- Worker has no HTTP endpoint; Docker liveness is `kill -0 1`.
- SSR health is `GET /health` on port 13714.

## Connection pools

Npgsql defaults to a pool of 100 per process. Those are client connections to PgDog, not direct
PostgreSQL connections: PgDog keeps `default_pool_size=20` server connections per backend and
multiplexes transactions. Compose still sizes `Maximum Pool Size` per role (10/10/5) to bound client
connections and PgDog queueing. Keep migrations direct to the primary. Raise the Npgsql/PgDog limits
only with measured capacity and PostgreSQL `max_connections` headroom.

## Deploy order

1. Run `--migrate` once (the `migrate` service).
2. Deploy Workers before MainApp when job payload types are new; old Workers cannot deserialize
   new payloads.
3. Deploy MainApp and Realtime with the same release/config.
4. Deploy the SSR bundle from the **same frontend build** as MainApp.
5. Bump `CACHE_VERSION` when a prop shape changes; clear cache Redis if desired.
6. Rolling deploy: old MainApp + new SSR is tolerated by `Preferred` fallback, but noisy. Drain or
   deploy together where possible.

## Failure behavior

| Failure | Expected behavior |
|---|---|
| MainApp replica dies | HAProxy removes it; cookie affinity rebalances; no data loss. |
| Realtime replica dies | Clients reconnect and land on another replica; events are transient. |
| Worker dies | Claims expire and another Worker reclaims; at-least-once. |
| SSR replica hangs | Each URL has its own 2 s timeout/circuit; `Preferred` falls back. Refused connections retry once; timeouts do not. |
| Cache Redis dies | Inertia cache is a miss; pages render from DB/SSR; health Degraded. |
| Critical Redis dies | Health Unhealthy; auth/limits/backplane are not safe. Restore Redis/data. |
| PostgreSQL primary dies | Requests and workers fail; PgDog does not promote replicas. Use Patroni/managed HA. |
| PgDog dies | Normal roles cannot query; `/health` fails on their DB checks. Put PgDog replicas behind a TCP LB in production. |
| Read replica dies or is banned | Public reads fall back to the primary because `read_write_split=include_primary_if_replica_banned`; health is Degraded. |

## Scale-out checklist

- [ ] Run `--migrate` once and confirm all roles wait for it.
- [ ] Set the same `REPLICA_NAMESPACE` and `CACHE_VERSION` on every role.
- [ ] Scale MainApp; sign in through the proxy repeatedly and confirm `X-Replica` changes without
      re-login or 419s.
- [ ] Place an order on one MainApp replica; read it on another.
- [ ] Admin edit invalidates catalog props on another MainApp replica (warm, mutate, reload).
- [ ] Mark notifications read on one replica; the badge clears on another.
- [ ] Change an order status in Worker; a Realtime client on a different Realtime replica receives
      the event.
- [ ] Stop one SSR replica; pages still render (other URLs or client fallback).
- [ ] Scale Workers; verify queue drains and no duplicate side effects.
- [ ] `SHOW POOLS` from PgDog's admin DB lists the primary and both replicas; `SHOW REPLICATION` shows `pg_is_in_recovery=t` for both.
- [ ] A write through `pgdog.role=primary` is visible through `pgdog.role=replica` after replication.
- [ ] Stop cache Redis; pages still render and `/health` is 200/degraded.
- [ ] Stop critical Redis; `/health` is 503 and no role silently serves unsafe auth/realtime.
- [ ] Confirm sessions created by MainApp work on a different Realtime replica (shared keys and
      cookie name).
- [ ] Confirm no role except `migrate` applies migrations; check startup logs.

## Intentionally out of scope

- PostgreSQL promotion/failover: PgDog routes reads/writes but does not promote a replica. Use
  Patroni/managed PostgreSQL for automatic failover.
- Multi-region Redis: use a managed HA provider in production.
- Exactly-once jobs: the queue is at-least-once by design.
- Persistent realtime delivery: the alerts inbox is the durable copy.
- Bun inside the web image: SSR is a separate pool.
