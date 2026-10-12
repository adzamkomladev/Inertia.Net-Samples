# Vue Inertia PostgreSQL — advanced replicas

This independent sample splits the PostgreSQL blog into three executable roles, orchestrated locally by Aspire 13.6.1. Defaults are **3 MainApp processes, 2 Workers and 2 Realtime hosts**, sharing PostgreSQL 18 and Redis. These are application replicas; the database is one writable primary.

```text
apphost.cs                  File-based Aspire resources, dependencies and YARP gateway
apps/MainApp/               HTTP routes, Vue/Inertia, SSR and queue producers
apps/Worker/                Generic Host, queue consumers, handlers and cron scheduling
apps/Realtime/              SignalR connections, authentication and origin checks
lib/Common/                 Database, migrations, queue, cache and replica infrastructure
lib/Auth/, lib/Posts/, ...   One class-library project per feature
lib/ServiceDefaults/        Aspire telemetry, service discovery and health defaults
```

Apps and libraries may reference only projects under `lib/`. MSBuild rejects other project references. The file-based AppHost locates executable projects by path; it does not import app code. Tests can host all three roles.

MainApp owns endpoint registration, HTTP policies, Inertia/SSR setup and the demo-seeding CLI. Worker owns handlers, queue consumption, cron schedules and its service registration. Realtime owns hub mapping, origin checks and its authentication setup. Feature libraries provide models and services without mapping routes or configuring an application. Common cannot reference feature projects. ServiceDefaults remains a separate project for Aspire telemetry, service discovery and health-check registration; each web app maps its own health endpoints.

Feature projects are **Auth, Posts, Comments, Likes, Uploads, Social, Preferences, Analytics, Discovery, Digests, Email and Search**. Every application references the libraries it uses; no application references another application. Project namespaces follow feature ownership, and queue type names, cookie names and existing migrations remain compatible with persisted data.

MainApp is the only production project that references Inertia.Net. Its endpoint handlers own rendering, deferred props, scroll metadata, flash messages, redirects and JSON responses. Feature services accept explicit user IDs and desired state, return plain data, and retain their validation, ownership checks and transactions. For example, Posts returns a saved slug; MainApp chooses the toast and redirect. Feature failures are translated into HTTP responses by MainApp's endpoint filter. MSBuild rejects direct or transitive Inertia references in libraries, and boundary tests reject HTTP request/response and cookie-handler dependencies in features. Cookie configuration belongs to each HTTP application; Auth provides reusable identity and token logic.

## Run with Aspire

Requires .NET SDK 10, Docker and Bun. Run from this sample folder:

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
Push-Location apps/MainApp
bun install --frozen-lockfile
bun run build
Pop-Location
dotnet run --file apphost.cs
```

Open [the blog](http://localhost:8183). The CLI prints the dashboard address; database and individual app ports are allocated dynamically. AppHost provisions PostgreSQL and Redis, runs migrations once, then starts each role. Aspire service defaults supply logs, traces and metrics to the dashboard. Worker uses `Host.CreateApplicationBuilder`, `BackgroundService` and host shutdown; it has no HTTP endpoint. Realtime uses a top-level `WebApplication` and exposes only `/realtime`, `/health` and `/alive`.

With Aspire CLI 13.6.1 installed, `aspire run` also locates the file through `aspire.config.json`. A pinned CLI launch needs no global CLI installation:

```powershell
dotnet dnx --yes aspire.cli@13.6.1 -- run --apphost (Resolve-Path apphost.cs).Path
```

Aspire allocates a separate SSR port for each MainApp process through `Inertia__SsrPort`. Inertia.Net passes that port to the Bun process as `INERTIA_SSR_PORT`; the built SSR bundle reads it at runtime. Containers retain port 13714 inside their separate networks. Rebuild frontend assets after changing the SSR entry.

Stop a foreground run with Ctrl+C. For a detached run:

```powershell
dotnet dnx --yes aspire.cli@13.6.1 -- stop --apphost (Resolve-Path apphost.cs).Path
```

PostgreSQL and Redis use named volumes and retain data when Aspire stops. Replica counts accept 1–10; set these before starting:

```powershell
$env:Replicas__MainApp = '5'
$env:Replicas__Worker = '3'
$env:Replicas__Realtime = '3'
dotnet run --file apphost.cs
```

The ignored `.env` in this workspace preserves the reference sample's Maileroo, S3 and search credentials. Do not replace it with the example or commit it. Aspire overrides database and Redis connections with managed resources, and uses its own cookie, realtime and search namespaces. Other settings remain available to the appropriate services. Mail requires `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS` and `EMAIL_FROM_NAME`; uploads require the S3 settings documented in `.env.example`. Search can fall back to PostgreSQL.

Host-run backup cron requires PostgreSQL 18 `pg_dump` on PATH, or `PG_DUMP_PATH` pointing to it. Docker's Worker image includes that client. `DATA_DIR` controls captured demo email and backup files; Aspire stores these under this sample's `storage/`.

## How the roles cooperate

| Concern | Mechanism |
| --- | --- |
| Cookies, antiforgery and tokens | Data Protection keys in PostgreSQL, one namespace shared by all roles |
| Jobs | Producers commit rows with business changes; Worker polls every second and claims due rows with PostgreSQL `SKIP LOCKED` |
| Retry and process failure | Five-minute job leases and persisted retry state; handlers must tolerate repeated execution |
| View analytics | MainApp persists a `RecordView` job; Worker inserts by stable ID, making retry safe |
| Crons | Only Worker schedules TickerQ; PostgreSQL leases prevent duplicate cron completion across workers |
| Live updates | MainApp/Worker publish through the SignalR Redis backplane; only Realtime hosts client connections |
| Routing | YARP sends `/realtime` and its subpaths to Realtime, other paths to MainApp |
| Affinity | Separate `REPLICA` and `REALTIME_REPLICA` cookies keep each transport on its selected process |
| Migrations | One successful migration resource gates all running roles |

Each Aspire replica is a separate resource and YARP destination so affinity selects the actual process. PostgreSQL and Redis remain shared dependencies. Redis backplane events are transient; reconnecting clients fetch current state rather than replaying missed messages. Job leases do not have a heartbeat: a handler exceeding its lease can run again. Email-provider delivery remains subject to queue retry semantics.

## Docker deployment sample

Compose is an alternative to the local Aspire run; stop Aspire before using the same public port.

```powershell
docker compose up --build -d --wait
docker compose up -d --wait --scale app=5 --scale worker=3 --scale realtime=3
docker compose down
```

Compose builds separate MainApp, Worker and Realtime images. HAProxy exposes localhost:8183; PostgreSQL uses localhost:5441 and Redis localhost:6387. Each role has independent replica counts. Worker health checks process liveness; the web roles check database and Redis readiness. Volumes survive `down`.

## Verification

Build frontend assets before backend integration tests. Their local defaults use Compose's database/Redis ports:

```powershell
docker compose up -d --wait postgres redis
$env:DOTNET_PROCESSOR_COUNT = '2'
dotnet test tests/VuePgReplicasAdvBlog.Tests
Remove-Item Env:DOTNET_PROCESSOR_COUNT
Push-Location apps/MainApp
bun run typecheck
bun run check:realtime
bun run check:social
Pop-Location
```

Against the default three-app/two-realtime deployment:

```powershell
node scripts/check-replica-flows.mjs
node scripts/check-replicas.mjs
```

Flows cover signup, the unverified-user guard, signin across replicas, post creation/edit/delete, likes, comments, bookmarks, view jobs, public/private realtime updates, logout and locally captured email. Only verification and fixture cleanup use SQL. For Aspire, set `ACCEPTANCE_POSTGRES_CONTAINER` to its PostgreSQL container name (`docker ps`); leave it unset for Compose. Run flows before the limiter check, or allow its one-minute rate window to expire. For scaled routing checks set `EXPECTED_REPLICAS` and `EXPECTED_REALTIME_REPLICAS`.

See [hosting research](docs/HOSTING_RESEARCH.md), [acceptance record](docs/DOCKER_ACCEPTANCE.md) and [social feature notes](docs/SOCIAL_FEATURES.md).
