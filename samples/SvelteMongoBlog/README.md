# CarParker (SvelteMongoBlog)

A car-park rental platform built with .NET 10 Minimal APIs, Inertia.Net 1.4 and Svelte 5, with server-side rendering. Customers rent a parking spot for a single duration or for recurring weekly windows; a security-guard console runs the gates; an admin dashboard manages lots, tariffs, refunds and claims.

## The domain in one pass

- **Lots & spots.** Each lot holds labelled spots (section letter × number, e.g. `B-07`) with types `standard | compact | handicap | covered | ev` and per-spot state (`free | booked | occupied | blocked`). The client renders a live spot map per lot.
- **Bookings.** A reservation targets one spot: a single window (`StartAt/EndAt`) or a recurring weekly pattern (days of the week + time range, materialised into up to 8 occurrence documents). Booking conditionally updates the spot (`free → booked`) so two shoppers can never win the same spot; the loser gets a 409. Price is the hourly rate with a per-24h daily cap, plus an optional **Spot Cover** premium (12% of the booking, minimum $2).
- **Sessions.** Guards check a plate in and out against the occurrence window (±15 min grace). Sessions can be resolved early (overstay) or force-closed by an admin.
- **Payments & refunds.** Payments are recorded per booking (`card | mobile_money | pay_at_gate`, mocked gateway). Cancellation creates a refund request automatically: ≥24 h before start → full, ≥4 h → 50%, later → none. Admins approve, pay or reject with a note; paying queues the `RefundPaidEmail` job.
- **Spot Cover (security insurance).** Policies live per reservation; customers file claims (`damage | theft | broken_window | other`), admins decide (`approved → paid` with payout, or rejected with a note). The `ClaimDecisionEmail` job notifies the customer.
- **Alerts.** In-app inbox per user (`reservation_soon`, `overstay`, `refund`, `policy`, `incident`, …) pushed live over SignalR; the reservation sweeper emails tomorrow's occurrences.
- **Incidents.** Guards log `unauthorized_entry | theft | damage | patrol_note | other` from the console; admins see the board.
- **Analytics.** Daily rollups per lot (occupancy, revenue, sessions) power the admin dashboard; deferred props are cached with Inertia.Net 1.4's `.Cache()`.

## Local development

Prerequisites: .NET 10 SDK, [Bun](https://bun.sh), Docker, and an external S3-compatible object store. `MONGO_MODE` (default `single`) selects how the app talks to MongoDB; see [MongoDB modes](#mongodb-modes).

1. Copy `.env.example` to `.env` and set the S3 endpoint and credentials. The `.env` file is ignored by Git.
2. Start the Mongo replica set: `docker compose -f docker-compose.dev.yml up -d mongo`.
3. Install frontend dependencies: `bun install --cwd src/SvelteMongoBlog`.
4. Start Vite: `bun run --cwd src/SvelteMongoBlog dev`.
5. Start the app: `dotnet watch --project src/SvelteMongoBlog --urls http://localhost:5199`.

The Compose Mongo service maps to host port `27018` by default. For a local MongoDB already running on port `27017`, point `DATABASE_URL` at that host instead and ensure `rs0` is initialized. The app creates required collections and indexes at startup. Mongo database name defaults to `svelte_mongo_blog`.

Uploads use the S3-compatible endpoint in `.env`; the Compose files do not start a local S3 service. In Development, emails are logged when no Maileroo key is configured.

To use Redis cache, configure `REDIS_URL`; cache keys always include `SvelteMongoBlog` and the environment. Leave the URL empty to use the in-memory distributed cache. For an optional local Redis service, start `docker compose -f docker-compose.dev.yml --profile cache up -d` and set `REDIS_URL=redis://localhost:6379` for a host-run app or `redis://redis:6379` in the app container.

### Demo accounts

Seed with `dotnet run --project src/SvelteMongoBlog -- --seed-demo` (MongoDB ready first). The deterministic, resumable seeder creates lots with spots, bookings across every status, in-progress and exited sessions, payments, refund requests, Spot Cover policies with claims, incidents, alerts and daily stats. Demo logins (password `CarParker-42-demo!`):

| Role | Email |
|---|---|
| Admin | `admin@carparker.test` |
| Guard | `guard@carparker.test` |
| Customer | `customer@carparker.test` |

Demo users share `@carparker.test`-style addresses; demo mail is captured locally and never sent externally.

### MongoDB modes

`MONGO_MODE` in `.env` selects how the app talks to MongoDB:

| Mode | Default | Behavior |
|---|---|---|
| `single` | yes | Any MongoDB server, including a standalone. Multi-document transactions are disabled and the same write paths run best effort; the app logs the detected topology at startup. |
| `replica` | | Requires a replica set (or sharded cluster). Transactions are used and verified at startup; the app stops with a clear message when the configured server reports as a standalone. |

Booking, check-in/out, refunds, claim decisions and cron leases group writes across collections. Prefer `replica` when that atomicity matters; a single-node replica set is still one server:

- Native install: add `replication: { replSetName: rs0 }` to `mongod.cfg`, restart the service, then run `mongosh --eval 'rs.initiate()'` once.
- Docker: `docker compose -f docker-compose.dev.yml up -d mongo` (already configured as `rs0`), then set `MONGO_MODE=replica` and the replica-set `DATABASE_URL` from `.env.example`.

Meilisearch is optional. Set `SEARCH_DRIVER=meilisearch` to enable it. Start the local service with `docker compose --profile search up -d meilisearch` (or add `--profile search` to the development Compose command), then set `MEILISEARCH_URL=http://localhost:7700` for a host-run app or `http://meilisearch:7700` in the app container. Set `MEILISEARCH_API_KEY` to the same master key used by the service. The index name is namespaced as `SvelteMongoBlog-<environment>-lots`. Index creation and document writes happen through durable jobs; if Meilisearch is down, the application still starts and MongoDB text search remains available.

## Production container

`docker compose up --build` starts the app and MongoDB replica set. It exposes the app on port 8080 and Mongo on 27018. Configure `APP_BASE_URL`, S3-compatible storage, email, and other secrets through `.env` or deployment environment variables. Compose connects the app to its Mongo service by default; use `DATABASE_URL_DOCKER` to point it at an external Mongo replica set. Host-run development uses `DATABASE_URL`. Mount persistent storage for `/data` so Data Protection keys survive redeploys.

MongoDB is configured as a single-node replica set for local Compose usage. For production, provide a managed or separately operated replica set and set the app's `DATABASE_URL`; keep a normal Mongo backup/restore process for the deployed database. A Redis service can be enabled with the `cache` Compose profile. S3 storage remains external.

## Mongo persistence details

- Collection indexes cover unique user email, unique `(LotId, Label)` spot labels, lot listings and text search, bookings per user and per spot, occurrence windows (the availability query), sessions, payments/refunds, policies and claims, incidents, alerts, lot daily stats, and durable job claim/lease scans.
- Ids are UUIDv7 Guids stored as Standard (subtype 4) binary; the driver Guid serializer is registered once in `AppDb`.
- Booking, check-in/out, session resolution, refund decisions, claim decisions and cron lease operations run in Mongo sessions. In `replica` mode they are wrapped in multi-document transactions; in the default `single` mode they run without transactions on any server. Spot occupancy transitions use conditional single-document updates (`State` guard), so races are rejected rather than doubled.
- The jobs collection is the durable source of truth. Workers claim due documents atomically with a lease and retry with backoff; an in-process signal only shortens polling delay.
- Mongo text search backs lot search when no external search integration is enabled. Index definitions are created at app startup; no EF Core migrations are used.

## Backup and restore

The app runs a daily 03:00 UTC `mongodump` job and stores compressed archives in the configured private `S3_BACKUP_BUCKET` (default `backups`) under `SvelteMongoBlog/<environment>/db/`. It keeps the 14 newest archives per sample/environment. The URI is passed to `mongodump` through a mode-restricted temporary config file, not a process argument. Set up MongoDB Database Tools (`mongodump`/`mongorestore`) in the runtime image or host when running outside Compose. The MongoDB service account needs read access to the sample database; the S3 credential needs object write/list/delete rights for the backup prefix.

For a manual local backup, use `mongodump --uri "$DATABASE_URL" --db "$DATABASE_NAME" --archive=./backup.archive --gzip`; restore it with `mongorestore --uri "$DATABASE_URL" --archive=./backup.archive --gzip --drop`. Use a new database name for a rehearsal restore, validate the restored app and uploads, then promote it through your deployment process. MongoDB metadata does not include S3 objects, so back up the configured upload bucket separately. Restore does not run automatically; validate an archive in a disposable database before using it for recovery.

## Checks

```bash
bun run --cwd src/SvelteMongoBlog build
bun run --cwd src/SvelteMongoBlog check:ssr
dotnet build SvelteMongoBlog.slnx -warnaserror
dotnet test SvelteMongoBlog.slnx
docker compose config --quiet
docker compose -f docker-compose.dev.yml config --quiet
```

The integration tests use a unique Mongo database per fixture and drop only that generated test database when the fixture is disposed. They target `mongodb://localhost:27017/?replicaSet=rs0&directConnection=true` by default; set `INERTIA_TEST_CONNECTION` to a disposable replica-set MongoDB before running them.

## Sync all lots to search

For Meilisearch, set `SEARCH_DRIVER=meilisearch`, `MEILISEARCH_URL`, and `MEILISEARCH_API_KEY` in `.env`, then run from this sample directory:

```sh
dotnet run --project src/SvelteMongoBlog -- --reindex-search
```

This queues a full sync in batches and exits. Start the application normally afterward to let its background job worker process the batches. Repeated requests are coalesced while a full sync is active. Use the same database, environment, and Meilisearch configuration for both commands.

Creating or editing a lot queues a coalesced, durable `SyncLot`/`SyncLotsBatch` job for Meilisearch indexing when enabled. Inactive lots drop out of the index. Native database search is maintained by the database and needs no external sync command.

## Live updates

SignalR at `/realtime` (the `ParkHub`) broadcasts `SpotChanged {lotId, spotId, state}` and `LotPulse` to everyone, pushes `BoardChanged` to the `staff` group (admins and guards) when bookings, sessions, refunds, claims or incidents change, and delivers `Notification` alerts to a specific user or to staff. The client refreshes authorized Inertia partial reloads on these events, so every open screen updates without a full reload.

The hub accepts same-origin browser connections and has no client mutation methods. Keep the application's public host/scheme in forwarded headers when using a proxy. Events are transient and served by one application instance; add a SignalR backplane for multiple instances and the alerts inbox already covers offline delivery.

### Coolify deployment

Set the application port to `8080` and assign its public HTTPS domain in Coolify. TLS terminates at the proxy; the container serves HTTP. Traefik supports WebSocket upgrades without custom upgrade headers or labels. Keep the public Host and forwarded protocol intact, and trust only your proxy's network if its address falls outside the configured private networks.

Redeploy the latest commit, then reload the browser. In DevTools, the `/realtime?id=...` WebSocket request should return `101 Switching Protocols`. The origin check maps forwarded `wss` to `https` (and `ws` to `http`) before comparing the browser's Origin, while still rejecting other origins. Run one application instance with the current setup; multiple instances require a SignalR backplane and session affinity with the current negotiate flow.

## Feature files and scheduled work

Each feature keeps route registration in its original routing file; the route file's static handler methods compose the Inertia response (`Render`, `Back`, flash, redirects). `<Feature>Service.cs` contains the domain logic — queries, mutations, Mongo conditional updates, job staging and realtime signals — and returns plain data plus outcome records, never `IResult`. Entities live in `<Feature>Entities.cs` (or `Data/` for shared ones); request inputs and DTOs in `<Feature>Models.cs`.

| Feature | Routes | Service | Entities / models |
|---|---|---|---|
| Lots (storefront + spot map) | Lots.cs | LotsService.cs | LotEntities.cs |
| Bookings, payments, refunds, alerts inbox | Bookings.cs | BookingsService.cs | BookingEntities.cs / BookingModels.cs |
| Guard console (check-in/out, board) | Guard.cs | GuardService.cs | GuardEntities.cs |
| Insurance (Spot Cover + claims) | Bookings.cs | InsuranceService.cs | InsuranceEntities.cs |
| Incidents | Guard.cs / Admin.cs | GuardService.cs | IncidentEntities.cs |
| Alerts | Bookings.cs | AlertsService.cs | AlertEntities.cs |
| Analytics (admin) | Admin.cs | LotAnalyticsService.cs | StatEntities.cs |
| Admin (lots, bookings, sessions, refunds, claims, incidents, users) | Admin.cs | (feature services) | - |
| Auth | Auth.cs | AuthService.cs / AuthHelpers.cs | AuthModels.cs |
| Preferences | Preferences.cs | PreferencesService.cs | PreferencesModels.cs |
| Uploads | Uploads.cs | UploadsService.cs / UploadsHelpers.cs | UploadsModels.cs |

Services never compose Inertia responses: handlers map the shared `Outcome` records (in `Infrastructure/Http.cs`) onto `Render`/`Back`/redirects. Authorization, rate limits and Inertia validation stay on the route registrations.

`Cron/CronSetup.cs` registers recurring functions with TickerQ and applies `CRON_*` / `Cron__<name>` overrides. Set `CRON_ENABLED=false` to disable scheduled work. Expressions are validated at startup, and execution is limited to one job at a time per process. Definitions are reseeded in memory after a restart; execution history is not persisted in TickerQ.

`Cron/CronJobs.cs` retains the database lease guard: overlapping replicas cannot claim the same occurrence while the lease is held. Startup performs catch-up checks, missed slots collapse into one run, and held leases are rechecked with TickerQ one-off timers. The schedule: `backup` 03:00 (mongodump to S3, keep 14) · `prune-jobs` 03:30 · `orphan-assets` hourly · `prune-unverified` 04:00 · `policy-expiry` 04:00 · `search-lot-reconcile` */5 min · `lots-stat-rollup` */15 min · `reservation-sweeper` 07:05 (tomorrow's occurrence reminders) · `no-show-check` */10 min (release + refund) · `overstay-check` */10 min (alert user and staff).

### Meilisearch index

Set `MEILISEARCH_INDEX` in `.env` or deployment environment variables to the exact lots index UID, for example `my-parking-production-lots`. Search requests, document writes/deletes and full reindexing all resolve this same value. If unset or blank, the default remains `<sample>-<environment>-lots` (environment is lowercase). Only ASCII letters, numbers, hyphens and underscores are accepted.
