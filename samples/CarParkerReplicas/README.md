# CarParkerReplicas

Rent a parking bay for one window or every week, get through the gate by plate, claim on Spot Cover if something
happens. This is the replica deployment of [CarParker](../CarParker): the whole application (ASP.NET web app,
managed Bun SSR process, durable jobs and TickerQ crons) runs in every replica, behind HAProxy, with a three-node
MongoDB replica set and shared Redis.

.NET 10 Minimal APIs, Inertia.Net 1.5.1 and Svelte 5 with server-side rendering, on MongoDB.

## Topology

```text
                         HAProxy :8184 (cookie affinity)
                    /--------------+--------------\
                    v              v               v
                 app-1          app-2           app-3
          ASP.NET + Bun SSR  ASP.NET + Bun SSR  ASP.NET + Bun SSR
          jobs + cron        jobs + cron        jobs + cron
                    \              |               /
                     +-------------+--------------+
                                   |
                    +--------------+--------------+
                    |                             |
                 Redis                       MongoDB rs0
       Data Protection, SignalR       mongo1 / mongo2 / mongo3
       rate limits, Inertia cache     (three data-bearing members)
```

- **App replicas** are identical. Each serves HTTP, owns a loopback Bun SSR process, claims durable jobs and
  schedules crons. Mongo claims/leases already make that safe.
- **Redis** is shared state: Data Protection keys, the SignalR backplane, fixed-window rate limits and the
  Inertia prop/SSR cache. It is required when `Replicas:Enabled=true`.
- **MongoDB** is a three-member replica set named `rs0`. App writes use `w=majority` and transactions.
- **HAProxy** publishes the only application port, health-checks `/health`, and pins a browser session with the
  `CPR_REPLICA` affinity cookie. Every response carries `X-App-Replica` (and HAProxy adds `X-Replica`).
- The one-shot `initialize` service creates indexes before the app replicas start. App replicas run with
  `Database:InitializeOnStartup=false`; use `--initialize` manually for an image-only deployment.

See [docs/REPLICAS.md](docs/REPLICAS.md) for failover, deployment order and limits, and
[docs/CACHING.md](docs/CACHING.md) for the Redis cache key/tag design.

## The domain

See the [user and admin guide](docs/USER_GUIDE.md) for lot hours, booking/claim details, team management,
terminal admin creation and notification sound.

- **Lots and bays.** Each lot has levels and sections of numbered bays (`B-07`) typed `standard | compact |
  handicap | covered | ev`, each `free | booked | occupied | blocked`. The lot page draws a live floor plan.
- **Bookings.** One window, or a weekly pattern materialised into up to 8 occurrences. Claiming a bay is a
  conditional update (`free → booked`), so two shoppers can't both win it; the loser gets a 409. Price is the hourly
  rate capped per 24 h, plus optional **Spot Cover** (12%, minimum $2).
- **Gate.** Guards check plates in and out against the window (±15 min). Overstays alert the customer and staff;
  bookings with no arrival after 30 min become no-shows and release the bay.
- **Money.** Mocked card / mobile money / pay-at-gate payments. Cancelling requests a refund by rule (≥24 h full,
  ≥4 h half, later none); admins approve, pay or reject.
- **Spot Cover claims**, an **alerts inbox** pushed over SignalR, guard **incident** logs, and **daily lot rollups**
  (occupancy, revenue, sessions) behind the admin analytics.

## How it uses Inertia.Net 1.5.1

| Feature | Where |
|---|---|
| `Scroll` | Lots listing (`<InfiniteScroll>`) |
| `Defer` + `.Parallel()` + `.Cache(key, ttl, staleFor, tags)` | Admin analytics: KPIs, series, top lots, refund mix. The Mongo driver is thread-safe, so the loaders run concurrently; cache keys carry lot and range, and every prop carries the `analytics` tag |
| `IInertiaCache` invalidation | Bookings, payments, check-in/out, refund decisions and lot/spot changes remove the tagged analytics entries at once |
| Redis `IInertiaCacheStore` | Props and SSR HTML are shared by all replicas; tag invalidation and the Lua tag-set TTL are in `Infrastructure/Caching/` |
| `MapInertia` | `/about`, `/policies` |
| `Once` | Shared `appName` |
| Precognition | Registration, booking, lot create/edit |
| SSR + SSR cache with `StaleFor` | All public pages; an expired render is served while one request refreshes it |
| Vite prefetch, lazy XSRF cookie refresh, DevTools | `InertiaSetup.cs` |

## Run it locally

Prerequisites: .NET 10 SDK, Bun, Docker. The dev Compose file starts a single-node MongoDB replica set on port
**27018** and Redis on **6388**.

```sh
cp .env.example .env
docker compose -f docker-compose.dev.yml up -d
bun install --cwd src/CarParkerReplicas
bun run --cwd src/CarParkerReplicas dev                               # Vite
dotnet watch --project src/CarParkerReplicas --urls http://localhost:5199
```

Development defaults to `Replicas:Enabled=false` (one app process), but because `.env.example` sets `REDIS_URL`,
Data Protection and the Inertia cache still use Redis. Remove `REDIS_URL` for a fully file/memory-backed local
run. Set `Replicas__Enabled=true` only when you want to exercise shared rate limits and the SignalR backplane
against the dev Redis. The example `APP_BASE_URL` points at the replica proxy (`8184`); set
`APP_BASE_URL=http://localhost:5199` in `.env` if you rely on email links while running `dotnet watch`.

Seed demo data with `dotnet run --project src/CarParkerReplicas -- --seed-demo`. Logins (password `Password@12345`):

| Role | Email |
|---|---|
| Admin | `inertia.demo42.carparker.admin@yopmail.com` |
| Guard | `inertia.demo42.carparker.guard@yopmail.com` |
| Customer | `inertia.demo42.carparker.customer@yopmail.com` |

- Uploads need an S3-compatible endpoint in `.env`; without an email key, Development logs mail.
- Checks: `bun run --cwd src/CarParkerReplicas build` (svelte-check + client + SSR) and
  `dotnet test tests/CarParkerReplicas.Tests` (uses a replica set on `localhost:27017`, or `INERTIA_TEST_CONNECTION`).
  The Redis cache integration tests run only when `REDIS_URL` is set and reachable; otherwise they skip.

## Run the replica stack

```sh
cp .env.example .env    # set ANALYTICS_SALT, APP_BASE_URL, S3_* and EMAIL_*
docker compose up -d --build
open http://localhost:8184
```

`docker-compose.override.yml` publishes the HAProxy port (default **8184**). The base file runs three app
replicas, `initialize` once, three MongoDB members, Redis and the proxy. Scale app replicas with
`APP_REPLICAS=5 docker compose up -d --scale app=5` or by editing `deploy.replicas`. Database/Redis volumes are
namespaced by the `carparkerreplicas` Compose project.

Seed the running stack:

```sh
docker compose run --rm -e Replicas__Enabled=false app --seed-demo
```

This creates a one-shot app container, initializes indexes and seeds demo data; it does not join the proxy pool.

## MongoDB mode

Set `MONGO_MODE=single` for standalone MongoDB (also the default outside production Compose), with a URL such as
`mongodb://localhost:27017/?directConnection=true`. Remove `replicaSet` from standalone connection URLs.
Set `MONGO_MODE=replica` for multi-document transactions, with a URL such as
`mongodb://localhost:27018/?replicaSet=rs0&directConnection=true`. Production Compose defaults to replica mode
and a three-member set.

Single mode preserves single-document conditional updates, including the bay claim, but writes across collections
cannot roll back together. A failed booking, refund or job enqueue can leave partial data requiring repair.
Use replica mode when those operations must be atomic. There is no automatic fallback on transaction errors.

Integration tests default to replica mode on port 27017. To test a disposable standalone server, set
`INERTIA_TEST_CONNECTION=mongodb://localhost:27019/?directConnection=true` and `INERTIA_TEST_MONGO_MODE=single`
before running `dotnet test tests/CarParkerReplicas.Tests`. Each fixture creates and drops its own test database.

## Layout

```
src/CarParkerReplicas/
  Features/<Feature>/    <Feature>.cs (routes, HTTP only) + <Feature>Service.cs (queries, rules, realtime)
  Data/                  AppDb (collections, indexes), demo seeder
  Jobs/, Cron/           durable job queue; TickerQ schedules with a database lease
  Infrastructure/
    Caching/             RedisInertiaCacheStore, key/options/health
    Setup/ReplicaSetup   Redis, Data Protection, SignalR backplane, health
  ClientApp/             Svelte pages, layouts, components (app.ts, ssr.ts)
tests/CarParkerReplicas.Tests/
docker/haproxy.cfg       affinity + health checks
docker/mongo-init.sh     rs.initiate + wait for a writable primary
```

Schedules (UTC): prune jobs 03:30 · orphan uploads hourly · prune unverified 04:00 · policy expiry 04:00 ·
lot rollup every 15 min · reminders 07:05 · no-show and overstay checks every 10 min. Every replica schedules
them; Mongo `CronLeases` fences each occurrence. `CRON_ENABLED=false` turns them off on a replica (production can
run cron on a dedicated process). Keep `JOBS_ENABLED=true` somewhere, or realtime push jobs are never consumed.

## CLI

```sh
dotnet run --project src/CarParkerReplicas -- --initialize    # create indexes and exit
dotnet run --project src/CarParkerReplicas -- --seed-demo     # initialize + seed demo data
dotnet run --project src/CarParkerReplicas -- --create-admin  # initialize + prompt for an admin
```

`Database:InitializeOnStartup` defaults to `true` for a single app and tests; the production app replicas set it
to `false` and rely on the `initialize` service. `--seed-demo` and `--create-admin` always initialize.

AI workflow setup: [docs/ai/README.md](docs/ai/README.md). Feature conventions: [AGENTS.md](AGENTS.md).
Deployment, Redis, Mongo auth/TLS, connection pools and cookie scoping: [docs/DEPLOY.md](docs/DEPLOY.md),
[docs/REPLICAS.md](docs/REPLICAS.md) and [docs/CACHING.md](docs/CACHING.md).

For the booking UI and queued live refresh check, start a disposable demo-seeded app, open it with
`playwright-cli` from this sample directory, then run
`playwright-cli run-code --filename=scripts/check-booking-browser.js`.

### Console UI verification

Against a disposable, demo-seeded local app, open `http://localhost:5199` with `playwright-cli` from this sample
root, then run `playwright-cli run-code --filename=scripts/check-console-browser.js`. This signs in as all three
demo roles, creates customer/staff accounts and lots, force-closes a demo session, checks search and panel
behavior, and saves screenshots under `.playwright/shots/`. Never point this mutation check at live data.

The admin People directory supports name/email search and role filters, customer creation, and account detail
pages. Lot/staff creation, session details, guard incident reports and customer claim forms open in side panels.
On mobile, open the current-section menu to navigate the admin or customer console.
