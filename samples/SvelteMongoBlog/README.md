# SvelteMongoBlog

Social feeds, optional services, demo seeding and daily digests: [feature guide](docs/SOCIAL_FEATURES.md).

A .NET 10 and Svelte 5 Inertia sample backed by MongoDB.Driver collections. It keeps the SQLite sample's custom cookie authentication and endpoint model while using MongoDB indexes, atomic document updates, UUIDv7 (`Guid.CreateVersion7()`) ids assigned in app code, and replica-set transactions for multi-document writes. The frontend uses Svelte 5 runes, bits-ui and Tailwind v4, with Bun/Vite client and SSR bundles. This repository is the Svelte port of the Vue MongoDB sample; historical records under `docs/` describe the Vue predecessor unless noted.

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

### MongoDB modes

`MONGO_MODE` in `.env` selects how the app talks to MongoDB:

| Mode | Default | Behavior |
|---|---|---|
| `single` | yes | Any MongoDB server, including a standalone. Multi-document transactions are disabled and the same write paths run best effort; the app logs the detected topology at startup. |
| `replica` | | Requires a replica set (or sharded cluster). Transactions are used and verified at startup; the app stops with a clear message when the configured server reports as a standalone. |

Registration, comments, likes and cron leases group writes across collections. Prefer `replica` when that atomicity matters; a single-node replica set is still one server:

- Native install: add `replication: { replSetName: rs0 }` to `mongod.cfg`, restart the service, then run `mongosh --eval 'rs.initiate()'` once.
- Docker: `docker compose -f docker-compose.dev.yml up -d mongo` (already configured as `rs0`), then set `MONGO_MODE=replica` and the replica-set `DATABASE_URL` from `.env.example`.

Meilisearch is optional. Set `SEARCH_DRIVER=meilisearch` to enable it. Start the local service with `docker compose --profile search up -d meilisearch` (or add `--profile search` to the development Compose command), then set `MEILISEARCH_URL=http://localhost:7700` for a host-run app or `http://meilisearch:7700` in the app container. Set `MEILISEARCH_API_KEY` to the same master key used by the service. The index name is namespaced as `SvelteMongoBlog-<environment>-posts`. Index creation and document writes happen through durable jobs; if Meilisearch is down, the application still starts and MongoDB text search remains available.

To populate the repeatable demonstration dataset, run `dotnet run --project src/SvelteMongoBlog -- --seed-demo` after MongoDB is ready. It creates 7,800 users with `@yopmail.com` addresses, 50,000 posts, topics, follows, likes, comments, comment likes, seen markers, bookmarks, reposts, and recommendation/search state. The seed is deterministic and resumes from transactionally saved batch checkpoints after interruption. Demo users share the password `Demo-42-correct-horse!`; demo mail is captured locally and never sent externally.

## Production container

`docker compose up --build` starts the app and MongoDB replica set. It exposes the app on port 8080 and Mongo on 27018. Configure `APP_BASE_URL`, S3-compatible storage, email, and other secrets through `.env` or deployment environment variables. Compose connects the app to its Mongo service by default; use `DATABASE_URL_DOCKER` to point it at an external Mongo replica set. Host-run development uses `DATABASE_URL`. Mount persistent storage for `/data` so Data Protection keys survive redeploys.

MongoDB is configured as a single-node replica set for local Compose usage. For production, provide a managed or separately operated replica set and set the app's `DATABASE_URL`; keep a normal Mongo backup/restore process for the deployed database. A Redis service can be enabled with the `cache` Compose profile. S3 storage remains external.

## Mongo persistence details

- Collection indexes cover unique user email and post slug, post timelines and author timelines, text search, post comments, unique post/user likes, asset cleanup, views/analytics, and durable job claim/lease scans.
- Ids are UUIDv7 Guids stored as Standard (subtype 4) binary; the driver Guid serializer is registered once in `AppDb`. The aggregate key for likes and daily stats is also stored on each document.
- Registration, comments, likes, post deletion, cron lease operations, analytics rollup and related durable jobs run in Mongo sessions. In `replica` mode they are wrapped in multi-document transactions; in the default `single` mode they run without transactions on any server.
- The jobs collection is the durable source of truth. Workers claim due documents atomically with a lease and retry with backoff; an in-process signal only shortens polling delay.
- Mongo text search is used for search when no external search integration is enabled. Index definitions are created at app startup; no EF Core migrations are used.

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

## Sync all posts to search

For Meilisearch, set `SEARCH_DRIVER=meilisearch`, `MEILISEARCH_URL`, and `MEILISEARCH_API_KEY` in `.env`, then run from this sample directory:

```sh
dotnet run --project src/SvelteMongoBlog -- --reindex-search
```

This queues a full sync in batches of 500 posts and exits. Start the application normally afterward to let its background job worker process the batches. Repeated requests are coalesced while a full sync is active. Use the same database, environment, and Meilisearch configuration for both commands.

Creating a post queues a coalesced, durable `SyncSearchPostsBatch` job for Meilisearch indexing when enabled. Edits and deletions also update the search index through jobs; unpublished or archived posts are excluded. Native database search is maintained by the database and needs no external sync command.

## Live updates

SignalR at `/realtime` updates visible post counters, announces new posts without moving the feed, and refreshes the authorï¿½s analytics. Signed-in authors receive live like/comment notifications. Events contain persisted values; viewer-specific likes and bookmarks stay local to each session.

The hub accepts same-origin browser connections and has no client mutation methods. Keep the applicationï¿½s public host/scheme in forwarded headers when using a proxy. Events are transient and served by one application instance; add a SignalR backplane for multiple instances and a durable notification inbox for offline delivery.

### Coolify deployment

Set the application port to `8080` and assign its public HTTPS domain in Coolify. TLS terminates at the proxy; the container serves HTTP. Traefik supports WebSocket upgrades without custom upgrade headers or labels. Keep the public Host and forwarded protocol intact, and trust only your proxy's network if its address falls outside the configured private networks.

Redeploy the latest commit, then reload the browser. In DevTools, the `/realtime?id=...` WebSocket request should return `101 Switching Protocols`. The origin check maps forwarded `wss` to `https` (and `ws` to `http`) before comparing the browser's Origin, while still rejecting other origins. Run one application instance with the current setup; multiple instances require a SignalR backplane and session affinity with the current negotiate flow.


## Feature files and scheduled work

Each feature keeps route registration in its original routing file. `<Feature>Service.cs` contains request handlers, queries and mutations; `<Feature>Models.cs` contains the feature's inputs, DTOs and internal data shapes; `<Feature>Helpers.cs` contains its reusable helpers, validation, formatting and scoring. Features without their own models or helper functions omit those files. Shared database entities remain in `Data/`.


The files under each feature folder are:

| Feature | Routes | Service | Models / DTOs | Helpers |
|---|---|---|---|---|
| Analytics | Analytics.cs | AnalyticsService.cs | AnalyticsModels.cs | AnalyticsHelpers.cs |
| Auth | Auth.cs | AuthService.cs | AuthModels.cs | AuthHelpers.cs |
| Comments | Comments.cs | CommentsService.cs | CommentsModels.cs | - |
| Discovery | - | DiscoveryService.cs | DiscoveryModels.cs | DiscoveryHelpers.cs |
| Likes | Likes.cs | LikesService.cs | - | - |
| Posts | Posts.cs | PostsService.cs | PostsModels.cs | PostsHelpers.cs |
| Preferences | Preferences.cs | PreferencesService.cs | PreferencesModels.cs | - |
| Social | Social.cs | SocialService.cs | SocialModels.cs | SocialHelpers.cs |
| Uploads | Uploads.cs | UploadsService.cs | UploadsModels.cs | UploadsHelpers.cs |

Services follow the existing static handler pattern; dependencies are supplied by ASP.NET endpoint binding. Discovery remains a scoped service. Authorization, rate limits and Inertia validation stay on the route registrations.

`Cron/CronSetup.cs` registers recurring functions with TickerQ and applies `CRON_*` / `Cron__<name>` overrides. Set `CRON_ENABLED=false` to disable scheduled work. Expressions are validated at startup, and execution is limited to one job at a time per process. Definitions are reseeded in memory after a restart; execution history is not persisted in TickerQ. The existing durable application job queue still handles email and indexing retries.

`Cron/CronJobs.cs` retains the database lease guard: overlapping replicas cannot claim the same occurrence while the lease is held. Startup performs catch-up checks, missed slots collapse into one run, and held leases are rechecked with TickerQ one-off timers.

### Meilisearch index

Set `MEILISEARCH_INDEX` in `.env` or deployment environment variables to the exact posts index UID, for example `my-blog-production-posts`. Search requests, document writes/deletes and full reindexing all resolve this same value. If unset or blank, the default remains `<sample>-<environment>-posts` (environment is lowercase). Only ASCII letters, numbers, hyphens and underscores are accepted.

Use a different UID for each sample/environment sharing a Meilisearch server. After changing the UID, run the existing `--reindex-search` command to populate the new index.
