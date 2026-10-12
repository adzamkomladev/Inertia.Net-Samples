# Vue Inertia PostgreSQL

This is the Vue 3 version of the Inertia blog backed by PostgreSQL 18 and EF Core 10. It preserves the SQLite sample's routes, cookie authentication, background queue, analytics, uploads, SSR, and Vue client contract. PostgreSQL search uses a GIN full-text index.

## Run locally

Copy `.env.example` to `.env`, set `DATABASE_URL` and any external S3-compatible settings, then start PostgreSQL and the app:

```powershell
docker compose up -d postgres
cd src/VueInertiaPostgresql
bun install --frozen-lockfile
bun run build
dotnet run
```

The default local database is on port 5433. Migrations run automatically at startup. Uploads work when an external S3-compatible service is configured; this sample does not start a local object store.

## Docker Compose

`docker compose up --build` starts the app and PostgreSQL. Set `S3_ENDPOINT`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_BUCKET`, and optionally `S3_PUBLIC_ENDPOINT`/`S3_PUBLIC_URL` for the external storage service. Keep `.env` private. The named volumes persist PostgreSQL data and Data Protection keys.

## Test

The integration tests create a temporary database for each test fixture and drop it when finished. They use the acceptance PostgreSQL connection by default; override it with `INERTIA_TEST_CONNECTION` when needed.

```powershell
dotnet test VueInertiaPostgresql.slnx
```

## Database backup and restore

Use PostgreSQL's logical backup tools, and keep backups outside the database volume:

```powershell
pg_dump --format=custom --file=vue-inertia.dump "$env:DATABASE_URL"
pg_restore --clean --if-exists --dbname="$env:DATABASE_URL" vue-inertia.dump
```

`DATA_DIR` stores Data Protection keys only. Persist it across redeploys so auth cookies remain valid.

## Social feeds and optional services

See [social features, search, caching, daily email and seed acceptance](docs/SOCIAL_FEATURES.md).

Docker image, timezone and PostgreSQL restore evidence: [Docker acceptance](docs/DOCKER_ACCEPTANCE.md).

## Sync all posts to search

For Meilisearch, set `SEARCH_DRIVER=meilisearch`, `MEILISEARCH_URL`, and `MEILISEARCH_API_KEY` in `.env`, then run from this sample directory:

```sh
dotnet run --project src/VueInertiaPostgresql -- --reindex-search
```

This queues a full sync in batches of 500 posts and exits. Start the application normally afterward to let its background job worker process the batches. Repeated requests are coalesced while a full sync is active. Use the same database, environment, and Meilisearch configuration for both commands.

Creating a post queues a coalesced, durable `SyncSearchPostsBatch` job for Meilisearch indexing when enabled. Edits and deletions also update the search index through jobs; unpublished or archived posts are excluded. Native database search is maintained by the database and needs no external sync command.

## Live updates

SignalR at `/realtime` updates visible post counters, announces new posts without moving the feed, and refreshes the author’s analytics. Signed-in authors receive live like/comment notifications. Events contain persisted values; viewer-specific likes and bookmarks stay local to each session.

The hub accepts same-origin browser connections and has no client mutation methods. Keep the application’s public host/scheme in forwarded headers when using a proxy. Events are transient and served by one application instance; add a SignalR backplane for multiple instances and a durable notification inbox for offline delivery.

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
