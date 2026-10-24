# TechLandierDistributed

TechLandier split into independently scalable roles: an Inertia/Vue web tier, a PostgreSQL job
worker, a SignalR realtime tier, and a Bun-only SSR pool. The storefront, admin dashboard and
commerce domain are unchanged; this sample demonstrates how the same application behaves when
web, background work, realtime connections and server rendering scale separately.

The full domain is still here: catalog and stock ledger, idempotent checkout, BNPL installments,
warranties/insurance/repairs, reviews, deliveries, complaints, analytics and durable email jobs.
Brand and C# namespaces stay **TechLandier**; project names are `TechLandierDistributed.*`.

## Topology

```text
                          HAProxy :8185
                 /realtime -> realtime pool, REPLICA cookies
                 /          -> mainapp pool
          /----------------+------------------\
          v                v                  v
    mainapp x3        realtime x2        worker x2
    routes + Inertia   SignalR only       queue + TickerQ cron
    Vue assets         cookie auth        job handlers
          |                |                  |
          +----------+-----+------------------+
                     |
          +----------+-----------+
          |                      |
    critical Redis          PgDog :6432
    DP keys, backplane      transaction pool
    rate limits             read/write split
          |                      |
    cache Redis          /-------+-------\
    Inertia props        v               v
    + SSR HTML        primary         replica x2
                      writes/migrate  lag-tolerant reads
          ^
          |
    ssr-1..3 (Bun only, selected by Inertia:SsrUrls)
```

| Role | Project / image | Replicas | Responsibilities |
|---|---|---:|---|
| MainApp | `apps/MainApp` (`mainapp`) | 3 | Routes, Inertia, Vue assets, shared auth, cache invalidation, enqueueing. No Bun, jobs, cron or startup migrations. |
| Worker | `apps/Worker` (`worker`) | 2 | PostgreSQL `FOR UPDATE SKIP LOCKED` consumers, all job handlers, TickerQ cron with the database lease, SignalR publishing through the backplane. |
| Realtime | `apps/Realtime` (`realtime`) | 2 | `/realtime` hub connections, shared cookie auth/key ring, origin checks, `/health`, `/alive`. No Inertia or jobs. |
| SSR | `Dockerfile` target `ssr` | 3 | `bun ssr/ssr.js`, binds `0.0.0.0:13714`, `/health`. MainApp picks URLs from `Inertia:SsrUrls`. |
| PgDog | `ghcr.io/pgdogdev/pgdog:v0.1.61` | 1 | Transaction pooler on 6432 with primary/replica split; app connections use `pgdog.role`. |
| PostgreSQL | `postgres:18-alpine` | 1 primary + 2 physical replicas | Primary serves writes/migrations; replicas serve selected lag-tolerant reads. |
| Migrate | `mainapp` image, `--migrate` | one-shot | EF migrations once through `DATABASE_MIGRATE_URL` directly to the primary. |

`lib/TechLandier.Core` holds data/entities/feature services/models/validators/mappers/utils, email,
storage, auth logic, the job queue and payloads, realtime contracts, cron tasks and the
`IPropCacheInvalidator` port. It must not reference Inertia.Net.
`lib/TechLandier.Caching` holds the Redis `IInertiaCacheStore`, cache options/health and the two
invalidator adapters. MainApp invalidates through `IInertiaCache`; Worker uses the store directly.

## Local development

Prerequisites: .NET 10 SDK, [Bun](https://bun.sh), Docker, and optionally an S3-compatible store.
PostgreSQL and **two** Redis instances are required for the replicated shape.

1. Copy `.env.example` to `.env` (it is ignored by Git). The example points at the host ports below.
2. Start PostgreSQL (primary + two replicas), PgDog, critical Redis and cache Redis:

   ```sh
   docker compose -f docker-compose.dev.yml up -d
   ```

   Host ports: PostgreSQL primary `5442`, PgDog `5443` (metrics `9090` internally), critical Redis
   `6389`, cache Redis `6390`. The replicas take their base backup from the primary on first boot.
3. Apply migrations once: `dotnet run --project apps/MainApp -- --migrate`. It uses
   `DATABASE_MIGRATE_URL` (direct primary), while the roles use `DATABASE_URL` through PgDog.
4. Install and start the frontend dev server:

   ```sh
   bun install --cwd apps/MainApp
   bun run --cwd apps/MainApp dev          # Vite on 5173
   ```

5. For server rendering, build and run the standalone SSR bundle in another terminal:

   ```sh
   bun run --cwd apps/MainApp build
   INERTIA_SSR_HOST=127.0.0.1 INERTIA_SSR_PORT=13714 bun apps/MainApp/ssr/ssr.js
   ```

   Set `INERTIA__SSR=false` when you only need client rendering. MainApp never starts Bun itself.
6. Start the roles in separate terminals:

   ```sh
   dotnet run --project apps/MainApp   --urls http://localhost:8185
   dotnet run --project apps/Worker
   dotnet run --project apps/Realtime  --urls http://localhost:8186
   ```

   In development the Vite dev server is proxied by MainApp through `wwwroot/hot`; open
   <http://localhost:8185>. The browser connects to SignalR at the same-origin `/realtime`, so use
   Compose/HAProxy (or a local reverse proxy) to exercise realtime across roles. The direct 8186
   port is useful for `/health` while developing.

Migrations do **not** run on role startup (`Database:MigrateOnStartup=false`). `--migrate` is the
one-shot path used by Compose; tests set the flag in-memory only.

### Demo accounts

Seed with `dotnet run --project apps/MainApp -- --seed-demo` (database ready first). The
deterministic (Bogus seed 42), resumable seeder creates 10 categories, 120 products, 400 orders,
BNPL plans, warranties/claims, insurance, repairs, reviews, tickets, deliveries, alerts and 30 days
of sales rollups. Demo logins (password `Password@12345`):

| Role | Email |
|---|---|
| Admin | `inertia.demo42.techlandier.admin@yopmail.com` |
| Customer | `inertia.demo42.techlandier.customer@yopmail.com` (plus `customer2@`, `customer3@`) |

Demo mail is captured under `/data/captured-mail` by the Worker and never sent externally.

## Compose

```sh
cp .env.example .env
docker compose up -d --build
# optional demo data after the migrate service exits:
docker compose --profile seed run --rm seed
```

Open <http://localhost:8185>. HAProxy is the only published application port. PostgreSQL primary
`5442`, PgDog `5443`, critical Redis `6389` and cache Redis `6390` are bound to localhost for
debugging. The full stack starts `migrate` first; MainApp, Worker and Realtime wait for it and for
PgDog.

Scale roles without touching the others:

```sh
docker compose up -d --scale mainapp=5 --scale worker=3 --scale realtime=3
```

## Configuration

`.env.example` contains only the connection/secret/provider values needed to run. Optional tuning
lives in [docs/DEPLOY.md](docs/DEPLOY.md). The important distributed settings are:

| Setting | Purpose |
|---|---|
| `DATABASE_URL` | PgDog connection for normal roles, with `Options=-c pgdog.role=primary`. Size `Maximum Pool Size` per role. |
| `DATABASE_READ_URL` | PgDog connection with `Options=-c pgdog.role=replica` for selected lag-tolerant reads. |
| `DATABASE_MIGRATE_URL` | Direct primary connection used by `--migrate`/seed (DDL bypasses PgDog). |
| `POSTGRES_PASSWORD`, `REPLICATOR_PASSWORD` | Database app and streaming-replication passwords. |
| `REDIS_URL` | Critical Redis: Data Protection, SignalR backplane, rate limits, stock-alert gates. |
| `CACHE_REDIS_URL` | Shared Inertia props + SSR HTML; failures degrade to a cache miss. |
| `REPLICAS_ENABLED` | `true` in Compose. `false` uses process-local Data Protection/rate limits/stock gates for tests and single-process development. |
| `REPLICA_NAMESPACE` | Cookie/DP/Redis/SignalR namespace. Keep it identical in every role. |
| `CACHE_VERSION` | Bump when cached prop shapes change; must match in MainApp and Worker. |
| `INERTIA__SSRURLS__0..2` | Remote SSR base URLs. MainApp uses `Ssr.Mode=Preferred` and a 2 s timeout. |
| `INERTIA__SSR` | SSR kill switch. MainApp never uses `UseNodeProcess`. |
| `APP_BASE_URL` | Public gateway URL used in email links and redirects. |
| `AUTH_SECURE_COOKIES` | `false` only for local HTTP. |

## How it fits together

- **Shared auth.** All web and realtime replicas use the `.tld.auth` cookie, `.tld.af` antiforgery
  cookie, `.tld.state` Inertia state cookie and the same Data Protection application name. The key
  ring is in Redis when `REPLICAS_ENABLED=true`.
- **Database routing.** All role connections go through PgDog transaction pooling with
  `pgdog.role=primary`, preserving read-after-write. Public catalogue/home/search/product/related/
  deals and cached analytics use `DATABASE_READ_URL` with `pgdog.role=replica`. Auth, checkout,
  orders, notifications, reviews and every mutation stay on the primary. Migrations bypass PgDog
  with `DATABASE_MIGRATE_URL`.
- **Shared cache.** `RedisInertiaCacheStore` keeps props and SSR HTML in the cache Redis with
  `{namespace}:cache:{version}:` keys, reverse tag indexes and Lua-extended tag-set TTLs. Inertia
  prop keys never authorize a request; every service still checks ownership and roles.
- **Jobs.** MainApp stages work in the same transaction as the business write; Worker consumes it.
  Delivery is at-least-once, so handlers are idempotent. The in-process wake channel is only an
  optimization in MainApp; Worker polls every 10 seconds.
- **Realtime.** MainApp/Worker enqueue `RealtimePush`; Worker publishes through the Redis backplane.
  Each Realtime replica accepts `/realtime` and joins admins to the `admins` group from their role
  claim. Sticky sessions still matter for negotiate/long polling/WebSockets.
- **Cron.** TickerQ lives only in Worker; the existing `CronLeases` row fences duplicate
  occurrences across worker replicas. `CRON_ENABLED=false` disables it in a process.
- **Rate limits.** Auth/register/uploads/views/writes use one atomic Redis fixed window shared by
  every MainApp replica. The registration Precognition branch keeps its roomier window.
- **Low-stock alerts.** A Redis `SET key 1 NX EX` gate keyed
  `stock-alert:{productId}:{threshold}` fires once per dip across replicas and is cleared on
  restock. Tests and `REPLICAS_ENABLED=false` fall back to an in-memory gate.

## Checks

```sh
dotnet build TechLandierDistributed.slnx -warnaserror
bun install --cwd apps/MainApp --frozen-lockfile
bun run --cwd apps/MainApp build
docker compose config
```

Integration tests create a unique PostgreSQL database per fixture and drop only that database.
They default to `Host=localhost;Port=55432;Username=inertiablog;Password=local-dev-password`; set
`INERTIA_TEST_CONNECTION` to a disposable PostgreSQL. Redis store/tag/stock-gate tests read
`REDIS_URL` and skip when it is unavailable. The opt-in `PgDogReadTests` read
`PGDOG_TEST_PRIMARY_URL` and `PGDOG_TEST_READ_URL` and are a no-op otherwise.

```sh
INERTIA_TEST_CONNECTION='Host=localhost;Port=55432;Username=testuser;Password=testpass' \
REDIS_URL='redis://127.0.0.1:6389' \
dotnet test tests/TechLandierDistributed.Tests
```

The browser UI is unchanged; verify UI changes with playwright-cli on desktop/mobile, exercise the
changed controls and inspect screenshots plus console/network errors.

## Docs

- [Route and prop contract](docs/CONTRACT.md)
- [Deploying and scaling](docs/DEPLOY.md)
- [Replica topology and acceptance](docs/REPLICAS.md)
- [PgDog and PostgreSQL topology](docs/PGDOG.md)
- [Inertia cache and SSR cache](docs/CACHING.md)
- [User and admin guide](docs/USER_GUIDE.md)

## CLI

```powershell
dotnet run --project apps/MainApp -- --migrate
dotnet run --project apps/MainApp -- --seed-demo
dotnet run --project apps/MainApp -- --create-admin
```

`--create-admin` prompts for name, email and a hidden password; existing accounts are never
overwritten. `--seed-demo` is resumable and updates old seeded credentials once without resetting
live data.
