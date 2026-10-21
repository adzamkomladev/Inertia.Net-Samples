# Inkwell

A multi-user blog built with .NET 10 Minimal APIs, Inertia.Net 1.4 and React, with server-side rendering. Single container, SQLite.

## What it is

- Accounts: register, email verification, login, password reset (ASP.NET Core Identity)
- Posts: markdown editor, drafts/publishing, public post pages, image uploads to S3-compatible storage
- Privacy-friendly page-view analytics (salted hashes, no cookies)
- Settings, weekly email digest, nightly database backups
- Stack: ASP.NET Core Minimal API, EF Core + SQLite, Inertia.Net, React + TypeScript + Vite (client and SSR bundles), RustFS (S3), Maileroo (email)

## Architecture

```
Traefik/Coolify (TLS) --> container :8080
                          â”œâ”€ ASP.NET Core (Inertia pages, API, /health)
                          â”œâ”€ bun ssr/ssr.js   (supervised by the app, 127.0.0.1:13714)
                          â”œâ”€ JobWorker         (bounded Channel fed from the SQLite Jobs table)
                          â””â”€ TickerQ           (runs scheduled jobs)
                          /data volume: app.db (+ -wal/-shm), Data Protection keys
RustFS (S3): `media` bucket (public read), `backups` bucket
Maileroo: transactional email
```

One instance only: SQLite, the in-process queue and cron assume a single replica.

## Local development

Prerequisites: .NET 10 SDK, Bun 1.3+, and an existing S3-compatible service. Docker is optional for containerized execution.

```sh
cp .env.example .env                              # PowerShell: Copy-Item .env.example .env
# Configure S3_ENDPOINT and credentials for your existing S3/RustFS service.
cd src/Inkwell && bun install && bun run dev        # Vite dev server
# second terminal, in src/Inkwell:
dotnet watch                                        # http://localhost:5100
```

- With `EMAIL_API_KEY` empty, emails are printed to the console.
- Tests: `dotnet test`
- Migrations: `dotnet ef migrations add X --project src/Inkwell -o Data/Migrations`
- Production-like stack on your machine: `docker compose up --build` (http://localhost:8080)

## Environment configuration

Copy [.env.example](.env.example) to `.env` **in this sample's root directory**, beside `docker-compose.yml`. The app searches upward from its working directory, so both `dotnet run --project src/Inkwell` and running from `src/Inkwell` find it.

```powershell
Copy-Item .env.example .env
```

A `.env` file is optional because appsettings provides development defaults. For deployment, explicitly configure your database, S3 credentials, public app URL, persistent key directory, and analytics salt. Configure email only when you want actual delivery. Never commit `.env` or put backend secrets in `VITE_` variables: those are exposed to the browser.

An existing process variable wins over the **same name** in `.env`. ASP.NET command-line configuration overrides that name too. Plain names such as `DATABASE_URL` and `S3_ENDPOINT` take precedence over their legacy section names, so use one naming style consistently. Values containing spaces, semicolons, `#`, or cron expressions should be quoted. Variables are loaded at startup; changing `.env` requires you to restart the app.

### Local `.env`

This is the configuration for the bundled development services while .NET runs on your host. Copy the actual `.env.example` for the complete template, including optional cron/SSR overrides.

```dotenv
DATABASE_URL="Data Source=App_Data/app.db;Default Timeout=30;Foreign Keys=True"
DATA_PROTECTION_KEYS_PATH=App_Data/keys
APP_NAME=Inkwell
APP_BASE_URL=http://localhost:5100
S3_ENDPOINT=https://s3.example.com
S3_ACCESS_KEY=replace-with-your-access-key
S3_SECRET_KEY=replace-with-your-secret-key
S3_REGION=us-east-1
S3_BUCKET=media
S3_BACKUP_BUCKET=backups
STORAGE_FORCED_PATH_STYLE=true
EMAIL_API_KEY=
EMAIL_FROM_ADDRESS=no-reply@example.com
EMAIL_FROM_NAME=Inkwell
ANALYTICS_SALT=local-dev-salt
JOBS_CONCURRENCY=4
```

### Database

| Variable | Local example | Deployment example |
|---|---|---|
| `DATABASE_URL` | `Data Source=App_Data/app.db;Default Timeout=30;Foreign Keys=True` | `Data Source=/data/app.db;Default Timeout=30;Foreign Keys=True` |

SQLite uses a connection string, not a `sqlite://` URL. Relative database paths are resolved from the app's working directory. In Docker, use `/data/app.db` so the database and its WAL files stay on the persistent volume.

### Application, keys, jobs, and analytics

| Variable | Example | Purpose |
|---|---|---|
| `APP_NAME` | `Inkwell` | Site name. |
| `APP_BASE_URL` | `http://localhost:5100` | Browser-facing app URL, also used in verification/reset emails. Use your HTTPS domain in production; Compose uses port 8080. |
| `DATA_PROTECTION_KEYS_PATH` | `App_Data/keys` locally; `/data/keys` in Docker | Persist this directory between deployments so authentication cookies and encrypted Inertia state remain readable. |
| `JOBS_CONCURRENCY` | `4` | Parallel background job workers; defaults to 4 and is clamped to at least 1. |
| `ANALYTICS_SALT` | `local-dev-salt` locally | Set a long random, stable value in production for daily visitor hashes. Do not reuse the development example. |

Generate a production salt locally, then paste the output into your deployment environment:

```sh
bun -e "console.log(require('node:crypto').randomBytes(32).toString('hex'))"
```

### S3-compatible storage

| Variable | Example | Purpose |
|---|---|---|
| `S3_ENDPOINT` | Your existing S3 API URL | S3 API endpoint reachable by the .NET app, not the storage console URL. |
| `S3_ACCESS_KEY` | Your provider credential | Access key; use your provider's credential in production. |
| `S3_SECRET_KEY` | Your provider credential | Secret key; use your provider's credential in production. |
| `S3_REGION` | `us-east-1` | Signing region; use the region expected by your provider. |
| `S3_BUCKET` | `media` | Public image bucket. The app creates it if missing and applies a public-read policy. |
| `S3_BACKUP_BUCKET` | `backups` | Private database-backup bucket. |
| `STORAGE_FORCED_PATH_STYLE` | `true` | Defaults to true for RustFS and other path-style endpoints. False uses virtual-hosted addressing. |

The storage credential needs bucket listing/creation, media bucket policy management, and object operations for uploads, pruning, and backups.

You normally **do not need `S3_PUBLIC_URL`**. With `S3_ENDPOINT=https://s3.example.com`, `S3_BUCKET=media`, and `STORAGE_FORCED_PATH_STYLE=true`, the app derives `https://s3.example.com/media/file.jpg`. False gives `https://media.s3.example.com/file.jpg`; your storage must support that hostname. Addressing style does not grant public access.

`S3_PUBLIC_URL` is an optional browser-facing media-bucket base URL for a CDN or a server endpoint that browsers cannot reach. Include the bucket path when needed. Leave it empty to derive URLs from a browser-reachable external S3 endpoint; otherwise provide the correct public URL.

### Email

| Variable | Example | Purpose |
|---|---|---|
| `EMAIL_API_KEY` | empty locally | Maileroo sending API key. Empty logs emails and verification/reset links to the console instead of sending them. |
| `EMAIL_FROM_ADDRESS` | `no-reply@example.com` | Use an address on a verified Maileroo domain for actual delivery. |
| `EMAIL_FROM_NAME` | `Inkwell` | Display name of the sender. |

### SSR and logs

| Variable | Example / default | Purpose |
|---|---|---|
| `SSR_EXECUTABLE` | `bun` | Runtime for the production SSR bundle. The Docker image includes Bun. |
| `SSR_TIMEOUT_SECONDS` | development: `30`; other environments: `5` | Positive, finite request timeout in seconds. |
| `Logging__LogLevel__Inkwell.Infrastructure.RequestLogMiddleware` | `Information` | ASP.NET logging setting for request summaries; shipped logging defaults already enable them. |

SSR applies only to the public post list and post details. Auth, analytics, settings/profile, and create/edit pages render on the client. Post cards prefetch on hover for 30 seconds; hovering does not count as a view.

Every HTTP request logs method, path without the query string, status, duration, process working set and managed heap in MiB, prefetch flag, and trace ID. The memory readings describe the entire .NET process, not allocations by that request.

### Scheduled jobs

Schedules use five-field cron expressions in **UTC**. Omit these variables to use the defaults; quote overrides, for example `CRON_BACKUP="0 2 * * *"`.

| Variable | Default | Job |
|---|---|---|
| `CRON_BACKUP` | `0 3 * * *` | Database backup to S3; retains the latest 14. |
| `CRON_ORPHAN_UPLOADS` | `0 * * * *` | Delete uploads not attached to a post. |
| `CRON_PRUNE` | `30 3 * * *` | Prune old analytics and finished jobs. |
| `CRON_UNVERIFIED_USERS` | `0 4 * * *` | Remove accounts that never verified their email. |
| `CRON_WEEKLY_DIGEST` | `0 8 * * 1` | Send the weekly email digest. |

### Compatibility and Compose-only settings

Legacy names still work, including `ConnectionStrings__Default`, `DataProtection__KeysPath`, `App__BaseUrl`, `Storage__ServiceUrl`, `Storage__AccessKey`, `Storage__SecretKey`, `Jobs__Concurrency`, and `Email__ApiKey`. `__` maps to `:` in ASP.NET configuration. Prefer the plain names above; you do not need both forms.

Normal development uses externally configured S3-compatible storage. Application credentials must match that existing service; changing the application configuration does not rotate remote credentials.

## Production-like Compose stack

The `.env.example` values are for a host-running app. Full Compose honors those values rather than automatically replacing `localhost` or host paths. Before running the full stack, change these entries in `.env`:

```dotenv
DATABASE_URL="Data Source=/data/app.db;Default Timeout=30;Foreign Keys=True"
DATA_PROTECTION_KEYS_PATH=/data/keys
APP_BASE_URL=http://localhost:8080
S3_ENDPOINT=https://s3.example.com
STORAGE_FORCED_PATH_STYLE=true
```

With remote S3, use its endpoint and credentials and follow the public URL override guidance above.

Then, from this sample's root directory:

```sh
docker compose up --build -d
```

Open `http://localhost:8080`. The app's `/data` directory is persisted by the `app-data` volume; storage data is persisted separately. `.env` supplies the remaining email, jobs, and cron configuration and is never baked into the image. For a real deployment, set your public HTTPS `APP_BASE_URL`, production credentials, persistent `/data/keys`, and a fresh analytics salt.

## Deploying on Coolify

1. **RustFS.** Create it as its own resource (Docker image `rustfs/rustfs`, or a compose file). Env: `RUSTFS_ACCESS_KEY`, `RUSTFS_SECRET_KEY`, `RUSTFS_CONSOLE_ENABLE=true`. Attach a persistent volume at `/data`. Give the S3 API (port 9000) a domain, e.g. `https://s3.example.com`.
2. **Application.** New Application -> your Git repo -> Build Pack **Dockerfile**. Base Directory `/Inkwell` if the repo root is the samples folder, else `/`. Ports Exposes `8080`. Domain e.g. `https://blog.example.com`.
3. **Persistent storage.** Add a volume mounted at `/data` (SQLite DB, WAL files, Data Protection keys). **Without it every deploy wipes the DB and logs everyone out.**
4. **Environment variables:**
   ```
   APP_BASE_URL=https://blog.example.com
   S3_ENDPOINT=https://s3.example.com
   S3_ACCESS_KEY=...
   S3_SECRET_KEY=...
   EMAIL_API_KEY=...
   EMAIL_FROM_ADDRESS=no-reply@blog.example.com
   ANALYTICS_SALT=<random string>
   ```
5. **Healthcheck.** Coolify picks up the Dockerfile `HEALTHCHECK`, or set path `/health`, port `8080` in the UI.
6. **Single instance only.** Do not scale replicas. Coolify's rolling update briefly runs old and new containers on the same volume; job claims are atomic so that is safe, but keep migrations additive (never drop or rename a column in the same deploy that stops using it).
7. **Backups.** The nightly cron uploads gzipped `VACUUM INTO` snapshots to the `backups` bucket and keeps the latest 14. To restore: download a snapshot, `gunzip` it, stop the app, replace `/data/app.db`, delete `/data/app.db-wal` and `/data/app.db-shm`, start the app.
8. **Proxy/TLS.** Traefik terminates TLS; the app trusts `X-Forwarded-For` and `X-Forwarded-Proto`.

## Background jobs & cron

All schedules are UTC. Use the exact `CRON_*` variables listed in Environment configuration above.

| Name | Schedule | Does |
|---|---|---|
| `backup` | `0 3 * * *` | Snapshot DB to the backups bucket (keeps 14) |
| `orphan-uploads` | `0 * * * *` | Delete uploads not attached to any post |
| `prune` | `30 3 * * *` | Prune old analytics and finished jobs |
| `unverified-users` | `0 4 * * *` | Remove accounts that never verified their email |
| `weekly-digest` | `0 8 * * 1` | Email the weekly digest |

Failed jobs retry up to 5 attempts with exponential backoff: 1, 2, 4, 8 minutes.

## Project layout

```
Dockerfile, docker-compose.yml, docker-compose.dev.yml
src/Inkwell/
  Program.cs            composition root
  app.html              Inertia root template
  ClientApp/            React (app.tsx, ssr.tsx, pages, components, layouts)
  Features/             Auth, Posts, Settings, Uploads, Analytics
  Jobs/                 job queue + worker
  Cron/                 TickerQ registration and cron task bodies
  Infrastructure/       storage, shared helpers
  Data/                 DbContext, migrations
tests/Inkwell.Tests/
```

## Social feeds and search

See [social features, search, caching, daily email and seed acceptance](docs/SOCIAL_FEATURES.md).

Docker image and timezone evidence: [Docker acceptance](docs/DOCKER_ACCEPTANCE.md).

## Live updates

SignalR at `/realtime` updates visible post counters, announces new posts without moving the feed, and refreshes the authorâ€™s analytics. Signed-in authors receive live like/comment notifications. Events contain persisted values; viewer-specific likes and bookmarks stay local to each session.

The hub accepts same-origin browser connections and has no client mutation methods. Keep the applicationâ€™s public host/scheme in forwarded headers when using a proxy. Events are transient and served by one application instance; add a SignalR backplane for multiple instances and a durable notification inbox for offline delivery.

### Coolify deployment

Set the application port to `8080` and assign its public HTTPS domain in Coolify. TLS terminates at the proxy; the container serves HTTP. Traefik supports WebSocket upgrades without custom upgrade headers or labels. Keep the public Host and forwarded protocol intact, and trust only your proxy's network if its address falls outside the configured private networks.

Redeploy the latest commit, then reload the browser. In DevTools, the `/realtime?id=...` WebSocket request should return `101 Switching Protocols`. The origin check maps forwarded `wss` to `https` (and `ws` to `http`) before comparing the browser's Origin, while still rejecting other origins. Run one application instance with the current setup; multiple instances require a SignalR backplane and session affinity with the current negotiate flow.


## Feature files and scheduled work

Each feature folder has a `<Feature>Feature.cs` with its minimal-API routes (HTTP concerns only) and a `<Feature>Service.cs` with the scoped service that holds the queries and business logic, plus the feature's inputs and DTOs. Services are registered in `Infrastructure/Setup/FeatureSetup.cs` and injected into the route handlers. Shared database entities live in `Data/`.

Search uses the database only: SQLite FTS5 (`PostsFts`, kept in sync by triggers) ranked with bm25 and highlighted snippets. There is no external search or cache service.

`Cron/CronSetup.cs` registers recurring functions with TickerQ and applies `CRON_*` / `Cron__<name>` overrides. Set `CRON_ENABLED=false` to disable scheduled work. Expressions are validated at startup, and execution is limited to one job at a time per process. Definitions are reseeded in memory after a restart; execution history is not persisted in TickerQ. The durable application job queue handles email retries.

Inertia.Net 1.1.0: [upgrade validation and before/after measurements](docs/INERTIA_1_1_UPGRADE.md).
