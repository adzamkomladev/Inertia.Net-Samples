# Deploying TechLandierDistributed

This sample separates the web, job, realtime and SSR workloads so each can scale for its own
bottleneck. They share PostgreSQL, a critical Redis and a separate cache Redis. All processes are
built from one Dockerfile with targets `mainapp`, `worker`, `realtime` and `ssr`.

The application is stateless except for uploads (S3-compatible storage) and captured demo mail
(the Worker's `/data` volume). Data Protection keys are in critical Redis when
`REPLICAS_ENABLED=true`; cache entries are disposable.

## Topology and ports

| Service | Image target | Suggested replicas | Port / health |
|---|---|---:|---|
| `proxy` | `haproxy:3.2-alpine` | 1 | `8185` public; routes `/realtime` separately |
| `mainapp` | `mainapp` | 3–5 | `8080` internal; `/health`, `/alive` |
| `worker` | `worker` | 2–3 | no HTTP; Docker liveness is `kill -0 1` |
| `realtime` | `realtime` | 2–3 | `8080` internal; `/health`, `/alive` |
| `ssr-1..3` | `ssr` | 3 | `13714` private; `/health` |
| `pgdog` | `ghcr.io/pgdogdev/pgdog:v0.1.61` | 1 | `6432` internal PgDog endpoint; admin/health `8080`, metrics `9090` |
| `postgres-primary` | `postgres:18-alpine` | 1 | `5442` for debugging; writes and migrations |
| `postgres-replica-1/2` | `postgres:18-alpine` | 2 | physical streaming replicas; no host port |
| `migrate` | `mainapp` with `--migrate` | one-shot | direct-primary EF migrations |
| `seed` | `mainapp` with `--seed-demo` | optional profile | exits after seeding |
| `redis` | `redis:8-alpine` | 1 critical | `6389` for debugging |
| `cache` | `redis:8-alpine` | 1 cache | `6390` for debugging |

The web and realtime pools have **separate** HAProxy affinity cookies (`TLD_REPLICA` and
`TLD_REALTIME`). Keep sticky sessions even with the backplane: the backplane distributes messages,
but negotiate, long polling and WebSockets need one host per connection.

## Migrations

Normal roles **never** run migrations. `Database:MigrateOnStartup=false` is the default, and
Compose sets it explicitly on every long-running service. The `migrate` one-shot service runs
`dotnet TechLandierDistributed.MainApp.dll --migrate` before MainApp/Worker/Realtime start. It uses
`DATABASE_MIGRATE_URL`, which must be a **direct primary** connection, so DDL and
`__EFMigrationsHistory` bypass PgDog's transaction pool. This is the only process allowed to change
the schema, so replicas cannot race.

For a manual deploy:

```sh
docker run --rm --env-file .env techlandier-distributed-mainapp:release --migrate
```

Use a direct (non-pooler) PostgreSQL connection for migrations. Keep a direct connection available
for future migrations even if normal roles use PgDog. The `Job` table has a
`(Status, LockedUntil)` index for the expired-lease reset.

`--seed-demo` and `--create-admin` migrate first and then exit. They are safe to run once after the
schema is ready.

## Environment

| Variable | Example | Notes |
|---|---|---|
| `APP_BASE_URL` | `https://shop.example.com` | Required outside Development. Used in email links. |
| `ANALYTICS_SALT` | 64 random hex chars | Required. `openssl rand -hex 32`; keep it stable. |
| `DATABASE_URL` | `Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=<secret>;Options=-c pgdog.role=primary;Maximum Pool Size=10` | Normal role connection through PgDog, pinned to the primary. Size Npgsql pools per role. |
| `DATABASE_READ_URL` | `Host=pgdog;Port=6432;Database=techlandier;Username=techlandier;Password=<secret>;Options=-c pgdog.role=replica;Maximum Pool Size=5` | Selected public/analytics reads through PgDog replicas. |
| `DATABASE_MIGRATE_URL` | `Host=postgres-primary;Port=5432;Database=techlandier;Username=techlandier;Password=<secret>` | Direct primary for the one-shot migrate/seed CLI. Never PgDog. |
| `POSTGRES_PASSWORD`, `REPLICATOR_PASSWORD` | local secrets | App role and streaming replication credentials. |
| `PGDOG_PORT`, `PGDOG_METRICS_PORT` | `5443`, `9090` | Local host ports for PgDog and OpenMetrics. |
| `REDIS_URL` | `redis://redis:6379` | Critical Redis. Required when `REPLICAS_ENABLED=true`. |
| `CACHE_REDIS_URL` | `redis://cache:6379` | Shared Inertia props/SSR. Required when `REPLICAS_ENABLED=true`; missing means process-local memory only for the single-process fallback. |
| `REPLICAS_ENABLED` | `true` | `false` is the single-process/test fallback. |
| `REPLICA_NAMESPACE` | `TechLandierDistributed:production` | Data Protection app name, Redis keys, SignalR channel. Keep identical. |
| `CACHE_VERSION` | `v1` | Bump when cached prop shapes change. Same value in MainApp and Worker. |
| `AUTH_SECURE_COOKIES` | `true` | `false` only for local HTTP. |
| `INERTIA__SSR` | `true` | MainApp SSR kill switch. |
| `INERTIA__SSRURLS__0..2` | `http://ssr-1:13714` | Explicit SSR replica URLs. |
| `INERTIA__SSRTIMEOUTSECONDS` | `2` | Remote SSR deadline. |
| `INERTIA__SSRCACHESECONDS` | `300` | Shared SSR cache fresh window. |
| `INERTIA__SSRSTALESECONDS` | `60` | Stale-while-revalidate window. |
| `S3_ENDPOINT`, `S3_PUBLIC_URL`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_REGION`, `S3_BUCKET` | | Optional storage. Without it, products show generated art and uploads are off. |
| `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | | Maileroo. Demo-account mail is captured by Worker under `/data`. |
| `TRUSTED_PROXY_NETWORKS` | `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16` | Forwarded-header sources. |
| `JOBS_CONCURRENCY` | `4` | Consumers **per Worker process**. |
| `JOBS_ENABLED` | `true` on Worker; `false` on web/realtime | Producers still enqueue when false. |
| `CRON_ENABLED` | `true` on Worker; `false` elsewhere | TickerQ schedules. |
| `CACHE_REDIS_MAXMEMORY` | `256mb` | Compose cache Redis budget. |

## PostgreSQL and PgDog

See [PGDOG.md](PGDOG.md) for the full configuration, admin commands and verification recipes. PgDog sits between every normal role and PostgreSQL. It runs in transaction pooling mode with
`read_write_split=include_primary_if_replica_banned` and `read_write_strategy=conservative`:

- `DATABASE_URL` uses `pgdog.role=primary`, so auth, checkout, orders, notifications, support/care
  and every mutation stay on the primary and reads after a write are never stale.
- `DATABASE_READ_URL` uses `pgdog.role=replica`. Public catalogue/home/search/product-detail/
  related/deals and cached analytics/sales queries use it. PgDog balances replicas round-robin; if
  all replicas are banned it includes the primary rather than failing reads.
- Manual transactions are sent to the primary; read-only transactions can use replicas. PgDog's
  admin database (`admin`) does not support transactions, but the application's PostgreSQL
  transactions are fine.
- Replicas are physical streaming standbys created by `pg_basebackup -R`. The primary's
  `init-replication.sh` creates the `replicator` role and appends the replication `pg_hba` line.
- `ban_replica_lag_bytes=16777216` removes a replica that falls more than 16 MiB behind. Byte-based
  lag avoids the idle-time false positives of `pg_last_xact_replay_timestamp`; add
  `ban_replica_lag` only with measured headroom. Reads safely fall back to the primary according to
  `include_primary_if_replica_banned`. An explicit `RECONNECT` in the admin database resets
  pools/bans.
- PgDog does **not** promote replicas or perform failover. Use Patroni/managed PostgreSQL with
  promotion and health checks in production.

Admin usage (local password `pgdog-admin`; production must inject a secret):

```sh
psql "host=pgdog port=6432 dbname=admin user=admin password=pgdog-admin" -c 'SHOW POOLS;'
psql "host=pgdog port=6432 dbname=admin user=admin password=pgdog-admin" -c 'SHOW REPLICATION;'
psql "host=pgdog port=6432 dbname=admin user=admin password=pgdog-admin" -c 'RECONNECT;'
```

`SHOW REPLICATION` reports `replica_lag`, `pg_lsn`, `lsn_age` and `pg_is_in_recovery`; use it to
debug stale reads. `/health` reports the read path as **Degraded**, not unhealthy, when it is not a
replica or is unavailable.

Connection strings use Npgsql's `Options=-c pgdog.role=primary`/`replica` startup parameters. Do
not use replicas for authorization, checkout, stock decisions, notifications or any flow that
expects a just-committed write.

## Plain VPS / Compose

```sh
git clone <repo> && cd samples/TechLandierDistributed
cp .env.example .env    # APP_BASE_URL, ANALYTICS_SALT, DATABASE_URL, S3_*, EMAIL_*
docker compose up -d --build
docker compose --profile seed run --rm seed   # optional
```

Open `http://localhost:8185`. Put Caddy, nginx or Traefik in front for TLS and WebSocket upgrades.
Only publish HAProxy; keep PgDog, PostgreSQL and both Redis instances private in production.

Scaling:

```sh
docker compose up -d --scale mainapp=5 --scale worker=3 --scale realtime=3
```

`ssr-1..3` are explicit services because Inertia balances per URL (least requests in flight). If
you scale SSR to a different count, add/remove `INERTIA__SSRURLS__n` entries. A production SSR proxy
with active health checks is preferable to direct URLs; `Preferred` falls back to client rendering
on a refused connection, timeout or open circuit.

## Health and failure semantics

| Check | Failure | Result |
|---|---|---|
| PostgreSQL primary | Unhealthy | MainApp/Realtime `/health` returns 503. |
| PgDog | Unhealthy/connection refused | Normal roles cannot query; `/health` fails on its DB checks. |
| Read replica/read path | Degraded | `/health` stays 200; reads fall back to the primary. |
| Critical Redis | Unhealthy | MainApp/Realtime `/health` returns 503; auth/limits/backplane are not safe. |
| Cache Redis | Degraded | `/health` stays 200; Inertia cache operations become misses. |
| Inertia SSR | Degraded/reported | `Preferred` falls back to client rendering. |
| Process | `/alive` | Liveness only; no database/Redis work. |

HAProxy treats any `200` as healthy, so a degraded cache or SSR does not remove every web replica.

## Coolify

- **Compose:** *New Resource → Docker Compose* from the repo, Base Directory
  `/samples/TechLandierDistributed`, compose file `docker-compose.yml`. Set the domain on `proxy`
  (port 8080) and fill the variables it lists.
- **Images only:** build the `mainapp`, `worker`, `realtime` and `ssr` targets separately. Point
  MainApp/Worker/Realtime at a managed PostgreSQL and both Redis URLs. Mount `/data` on Worker for
  captured demo mail. Run `--migrate` once from the MainApp image before starting the roles.

## Dokploy

Use a *Compose* service with compose path
`samples/TechLandierDistributed/docker-compose.yml`, or separate *Applications* for each target
plus *Postgres* and Redis resources. Add the domain on HAProxy/`proxy` port 8080. Keep the SSR
services private.

## Neon / managed PostgreSQL

For a managed provider, point `DATABASE_MIGRATE_URL` at the direct primary URL with
`sslmode=require`. If the provider exposes a built-in read endpoint, you can either keep the PgDog
topology in front of it or set `DATABASE_URL` to the provider pooler and `DATABASE_READ_URL` to its
replica endpoint; the application only relies on the two role-pinned URLs, not on PgDog-specific
SQL. Keep migrations on the direct connection. PgDog's local physical replicas are for the sample
topology, not a replacement for managed HA.

## Data Protection and cookies

All web and realtime replicas must use the same `REPLICA_NAMESPACE` and critical Redis key ring.
Losing the key ring signs everyone out and invalidates outstanding verification/unsubscribe links;
back up critical Redis. Cookies are scoped to avoid collisions with the original TechLandier:

- auth `.tld.auth`
- antiforgery `.tld.af`
- Inertia state `.tld.state`
- HAProxy affinity `TLD_REPLICA`, `TLD_REALTIME`

## Cache and deploy order

The prop cache is versioned by `CACHE_VERSION`; bump it when a prop's shape changes. Deploy MainApp
and Worker with the **same** version and Vite manifest. Deploy SSR from the same frontend build.
During a rolling deploy an old app may meet a new SSR bundle; `Preferred` falls back, but it is
noisy. For new job payload types, deploy Workers first or version the payload.

Cache Redis is disposable. `clear`/evict it on release if needed; old `CACHE_VERSION` keys expire
on their own. See [CACHING.md](CACHING.md).

## Worker capacity

Workers claim jobs with a five-minute lease and retry with backoff. Delivery is at-least-once;
handlers must stay idempotent. Jobs longer than five minutes need lease renewal before raising
concurrency. `JOBS_CONCURRENCY` is per process. Multiple workers are safe because claims use
`FOR UPDATE SKIP LOCKED`; cron is fenced by `CronLeases`.

MainApp and Realtime enqueue but never consume. Keep at least one Worker running, or durable jobs
(including realtime pushes) remain pending. Realtime delivery is transient; the alerts inbox is the
durable copy.

## Demo data

From a MainApp container's shell: `dotnet TechLandierDistributed.MainApp.dll --seed-demo`.
Demo logins are listed in the README. `--create-admin` uses the same shared key ring and database as
the running roles.
