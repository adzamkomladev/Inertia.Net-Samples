# Deploying TechLandier

After deployment, follow the [user and admin guide](USER_GUIDE.md) to bootstrap an administrator,
seed demo accounts if needed, configure store/delivery hours and manage the team. Feature migrations apply
at startup; rebuilding the app does not require resetting the database or rerunning the demo seeder.

The image runs the web app, server-side rendering (Node in the image, started by the app), the job worker and the crons. It
listens on **8080**, answers **`/health`**, and keeps Data Protection keys and captured mail on a volume at
**`/data`**. EF Core migrations run on every boot.

Run **one app instance**: the crons and SignalR assume a single replica.

## Two ways to deploy

| | What runs | Use when |
|---|---|---|
| **Compose** (`docker-compose.yml`) | app + persistent `/data`; external PostgreSQL | Deploy the app against a self-hosted PostgreSQL (for example a Coolify database) |
| **Image only** (`Dockerfile`) | app; PostgreSQL is a separate resource you point `DATABASE_URL` at | You already run Postgres (a Coolify or Dokploy database, a managed instance, …) |

## Environment

| Variable | Example | Notes |
|---|---|---|
| `APP_BASE_URL` | `https://shop.example.com` | Required. Used in email links. |
| `ANALYTICS_SALT` | 64 random hex chars | Required. `openssl rand -hex 32`; keep it stable. |
| `DATABASE_URL` | `postgresql://user:pass@host:5432/techlandier?sslmode=require` | Required for both deployment methods. Npgsql key/value strings work too. |
| `S3_ENDPOINT` | `http://rustfs:9000` | Optional: without storage, products show generated art and uploads are off. |
| `S3_PUBLIC_ENDPOINT`, `S3_PUBLIC_URL` | `https://s3.example.com`, `https://s3.example.com/uploads` | Browser-facing host and public image base. |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_REGION`, `S3_BUCKET` | | `docker/s3/` has the bucket policy and CORS files. |
| `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | | Maileroo. Required in production (sending throws without a key); demo-account mail is captured under `/data`. |
| `TRUSTED_PROXY_NETWORKS` | `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16` | Default; covers Docker networks. |
| `CRON_ENABLED` | `true` | |
| `RUNWICK_ADMIN_USERNAME`, `RUNWICK_ADMIN_PASSWORD` | | Required outside Development: the app refuses to start while either still has the sample `Admin` / `Admin@12345` value (or is unset). Sign-in for the `/runwick` dashboard. |

## Plain VPS

```sh
git clone <repo> && cd samples/TechLandier
cp .env.example .env    # set APP_BASE_URL, ANALYTICS_SALT, DATABASE_URL, S3_* and EMAIL_*
docker compose up -d --build
```

`docker-compose.override.yml` publishes the app on `${APP_PORT:-8080}`; the database is external. Put Caddy, nginx or
Traefik in front for TLS and WebSocket upgrades (`/realtime`).

Image only, against an existing database:

```sh
docker build -t techlandier .
docker run -d --name techlandier -p 8080:8080 -v techlandier-data:/data --env-file .env \
  -e DATABASE_URL='postgresql://…' --restart unless-stopped techlandier
```

## Coolify

- **Compose:** *New Resource → Docker Compose* from your repo, Base Directory `/samples/TechLandier`, compose file
  `docker-compose.yml`. Set the domain on the `app` service (port 8080) and fill in the variables Coolify lists.
- **Image only:** the database is a self-hosted PostgreSQL resource on Coolify (*New Resource → Database →
  PostgreSQL*). Create *Application → Dockerfile*, Base Directory `/samples/TechLandier`, port 8080, a persistent volume
  at `/data`, and `DATABASE_URL` set to the database's internal connection URL.

In Coolify's normal environment-variable editor, use **Name** `DATABASE_URL` and **Value**
`postgresql://user:password@internal-host:5432/techlandier` (replace with your Coolify database's internal URL).
The value must contain only the URL, without a `DATABASE_URL=` prefix or surrounding quotes. Enable
runtime availability, save, and redeploy. Only Developer view / `.env` format uses the full
`DATABASE_URL=postgresql://...` line.

If startup fails with `Couldn't set database_url`, check for a duplicated `DATABASE_URL=` prefix in
the value. This is a connection-string parsing error, before the app connects or runs migrations.
The Dockerfile already supplies a Node-based `/health` check; a generic curl/wget warning does not
explain this startup crash.

## Dokploy

*Compose* service with compose path `samples/TechLandier/docker-compose.yml`, or a *Postgres* database plus an
*Application* (Dockerfile, build path `samples/TechLandier`) with `DATABASE_URL` set to the database's connection URL.
Add the domain on container port 8080 and, for the application type, a volume mount at `/data`.

## Database

PostgreSQL is self-hosted (a Coolify database resource, or any Postgres you run). Set `DATABASE_URL` to its internal
connection URL, adding `sslmode=require` only when the connection leaves the private network. The app and Runwick use
this one URL and connect directly: no pooler is involved, and none is supported in front of Runwick.
The app runs migrations at startup, so use a role that can create schemas and tables for this single-instance deployment.
Compose does not start a local database. Local development can still use `docker-compose.dev.yml`.
Database backups and recovery are managed separately, through the Coolify database's backup schedule or your own tooling.
## Demo data

From the app container's terminal: `dotnet TechLandier.dll --seed-demo`. Demo logins are listed in the README.


## Worker capacity and optional settings

The environment example contains connection/secret/provider values only. Optional tuning:

| Setting | Default | Purpose |
| --- | --- | --- |
| JOBS_CONCURRENCY | 4 | Consumers per process; start at 1 for limited database/provider capacity, then raise based on queue age and resource use. |
| JOBS_ENABLED | true | Run the Runwick worker in this process. When false the process still stages and dispatches jobs into the shared store. |
| RUNWICK_DEMO_ENABLED | Development only | Map `/demo/jobs` and register the demo sleep queues and four extra worker pools. Leave off in production. |
| CRON_ENABLED | true | Disable schedules on replicas serving HTTP or processing jobs only. |
| SSR_EXECUTABLE | node in the image; bun locally | Override the SSR runtime. |
| S3_REGION | us-east-1 | Storage signing region. |
| S3_PUBLIC_URL | endpoint + bucket | Public media/CDN base URL. |
| S3_PUBLIC_BUCKET | (S3_BUCKET) | Separate public-read bucket for uploads; when set, images are stored there so `S3_PUBLIC_URL` serves them. |
| STORAGE_FORCED_PATH_STYLE | provider-dependent | Override S3 addressing. |

All background work runs on Runwick, which stores jobs in the `runwick` schema of the app's own PostgreSQL database.
Features stage a job inside the same transaction as the business write, so the job and the write commit or roll back
together; work decided without a write (realtime pushes, the weekly statements) is dispatched directly. Delivery is at
least once with retries and backoff: handlers are idempotent, mail jobs carry ids (never rendered email) and several have
uniqueness keys, so a repeated dispatch reuses the existing job. Staged jobs become visible when the transaction commits
and are picked up by polling within about two seconds. Queues: `mail` (retried for about half an hour), `events`
(realtime fan-out, asset-object deletion) and `cron`. Set JOBS_CONCURRENCY on the existing application process; no extra
worker framework is needed.

Upgrading from the custom outbox: the `DropLegacyJobs` migration drops the old `Jobs` table, so any job still pending in
it at deploy time is discarded (drain it first if that matters). The `EmailPromos` preference was only ever stored inside
the user's JSON preferences, so it needs no column change; existing values are ignored.

Realtime sends are Runwick jobs on the `events` queue and retry like any other job. Keep consumers enabled in a single-instance deployment. Consumers in another process require a SignalR backplane to reach web clients. The queue does not provide cross-process SignalR delivery. Events can be duplicated or reordered; clients must reload authoritative state on reconnect. Multiple web replicas require that backplane.

Run recurring schedules on one designated process (CRON_ENABLED=false elsewhere). Keep periodic maintenance schedules; use queued jobs for event-driven work. `Cron:<name>` overrides (UTC) are read only when a Runwick schedule is first registered; after that the stored schedule wins, so an existing deployment keeps its old cron expression until you change it in the dashboard. The same drift applies to the schedule set itself: the removed `prune-jobs` schedule stays in an existing Runwick store until you delete it in the dashboard (it is no longer registered, so it will not run). Optional schedule overrides belong in deployment settings rather than .env.example.

Runwick creates its own `runwick` schema in the same PostgreSQL database at startup (`RunwickBootstrap`) and bootstraps the one-time `scheduler` capability, so no CLI step is required. The first boot needs a role that can create schemas, like the EF migrations already require; later boots only need data access. Its dashboard is at `/runwick`. Outside Development the host refuses to start unless `RUNWICK_ADMIN_USERNAME` and `RUNWICK_ADMIN_PASSWORD` are set to values other than the sample `Admin` / `Admin@12345`; in Development that sample login works. The sign-in form shares the 10-per-minute auth rate limit, and sign-out rejects cross-origin POSTs. The demo trigger endpoints under `/demo/jobs` are open and dispatch only sleep jobs; they are mapped only when `RUNWICK_DEMO_ENABLED` is on (default: Development only).

The worker pools are `events` and `mail` (each 1 to `JOBS_CONCURRENCY`) and `cron` (1); with the demo flag on there are also `fast-fixed` (4), `scaled-auto` (1–8), `slow-auto` (1–4) and `long-auto` (1–4).
S3_PUBLIC_ENDPOINT optionally sets a browser-reachable signing endpoint when storage has an internal hostname.

## PDF receipts and statements

Receipts and installment statements are rendered with QuestPDF on request and streamed; nothing is stored. The package (2026.9.2, Community license) ships `runtimes/linux-musl-x64` with `libquestpdf_skia.so` (8.4 MB) and `libquestpdf_qpdf.so` (3.4 MB), so the Alpine `-r linux-musl-x64` publish carries them with no extra build step. They link `libstdc++`, `libgcc` and `zlib` (verified with `ldd` on Alpine 3.22: a bare Alpine lacks the first two); the ASP.NET Alpine base already includes all three and the Dockerfile lists them explicitly. No fontconfig or font packages are needed: the app sets `QuestPDF.Settings.UseSystemFonts = false` and renders with the Lato font embedded in QuestPDF.dll, so output is identical on any host. Image impact: about 12 MB of native libraries plus a 0.7 MB assembly (roughly +13 MB uncompressed; the compressed layer is smaller).