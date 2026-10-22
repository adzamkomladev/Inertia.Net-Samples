# Deploying TechLandier

The image runs the web app, server-side rendering (bun, started by the app), the job worker and the crons. It
listens on **8080**, answers **`/health`**, and keeps Data Protection keys and captured mail on a volume at
**`/data`**. EF Core migrations run on every boot.

Run **one app instance**: the crons, the in-process job wake-up and SignalR assume a single replica.

## Two ways to deploy

| | What runs | Use when |
|---|---|---|
| **Compose** (`docker-compose.yml`) | app + `postgres:18`, both with volumes | One VPS / one Coolify or Dokploy project holds everything |
| **Image only** (`Dockerfile`) | app; PostgreSQL is a separate resource you point `DATABASE_URL` at | You already run Postgres (Coolify/Dokploy database, Neon, RDS, …) |

## Environment

| Variable | Example | Notes |
|---|---|---|
| `APP_BASE_URL` | `https://shop.example.com` | Required. Used in email links. |
| `ANALYTICS_SALT` | 64 random hex chars | Required. `openssl rand -hex 32`; keep it stable. |
| `POSTGRES_PASSWORD` | URL-safe random string | Compose only; it is placed in the connection URL. `openssl rand -hex 24`. |
| `POSTGRES_USER`, `POSTGRES_DB` | `techlandier` | Compose only. |
| `DATABASE_URL` | `postgresql://user:pass@host:5432/techlandier?sslmode=require` | Image only; compose builds it. Npgsql key/value strings work too. |
| `S3_ENDPOINT` | `http://rustfs:9000` | Optional: without storage, products show generated art and uploads are off. |
| `S3_PUBLIC_ENDPOINT`, `S3_PUBLIC_URL` | `https://s3.example.com`, `https://s3.example.com/uploads` | Browser-facing host and public image base. |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_REGION`, `S3_BUCKET` | | `docker/s3/` has the bucket policy and CORS files. |
| `S3_BACKUP_BUCKET` | `backups` | Nightly `pg_dump` archives; empty uses `S3_BUCKET` (under `backups/`). |
| `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | | Maileroo. Required in production (sending throws without a key); demo-account mail is captured under `/data`. |
| `TRUSTED_PROXY_NETWORKS` | `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16` | Default; covers Docker networks. |
| `CRON_ENABLED` | `true` | |

## Plain VPS

```sh
git clone <repo> && cd samples/TechLandier
cp .env.example .env    # set APP_BASE_URL, ANALYTICS_SALT, POSTGRES_PASSWORD, S3_* and EMAIL_*
docker compose up -d --build
```

`docker-compose.override.yml` publishes the app on `${APP_PORT:-8080}`; Postgres is not published. Put Caddy, nginx or
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
- **Image only:** create *Database → PostgreSQL* (`postgres:18`) and copy its internal URL. Then *Application →
  Dockerfile*, Base Directory `/samples/TechLandier`, port 8080, a persistent volume at `/data`, and
  `DATABASE_URL` set to that URL.

## Dokploy

*Compose* service with compose path `samples/TechLandier/docker-compose.yml`, or a *Postgres* database plus an
*Application* (Dockerfile, build path `samples/TechLandier`) with `DATABASE_URL` set to the database's internal URL.
Add the domain on container port 8080 and, for the application type, a volume mount at `/data`.

## Backups

A daily cron runs `pg_dump` (client 18, bundled in the image) and uploads a custom-format
archive to `S3_BACKUP_BUCKET` (or `S3_BUCKET`) under `backups/TechLandier/<environment>/`, keeping the newest 14. Restore with `pg_restore --clean --if-exists -d "$DATABASE_URL" backup.dump`.

## Demo data

From the app container's terminal: `dotnet TechLandier.dll --seed-demo`. Demo logins are listed in the README.


## Worker capacity and optional settings

The environment example contains connection/secret/provider values only. Optional tuning:

| Setting | Default | Purpose |
| --- | --- | --- |
| JOBS_CONCURRENCY | 4 | Consumers per process; start at 1 for limited database/provider capacity, then raise based on queue age and resource use. |
| JOBS_ENABLED | true | Disable consumers on a web replica while continuing to enqueue durable work. |
| CRON_ENABLED | true | Disable schedules on replicas serving HTTP or processing jobs only. |
| SSR_EXECUTABLE | bun | Override the SSR runtime. |
| S3_REGION | us-east-1 | Storage signing region. |
| S3_PUBLIC_URL | endpoint + bucket | Public media/CDN base URL. |
| STORAGE_FORCED_PATH_STYLE | provider-dependent | Override S3 addressing. |

Workers use database claims and five-minute leases. Delivery is at least once: handlers must be idempotent, and jobs longer than five minutes need lease renewal before raising concurrency. Pending jobs remain durable when consumers are disabled. Set JOBS_CONCURRENCY on the existing application process; no extra worker framework or cron-per-worker is needed.

Realtime sends are queued and retry through workers. Keep consumers enabled in a single-instance deployment. Consumers in another process require a SignalR backplane to reach web clients. The queue does not provide cross-process SignalR delivery. Events can be duplicated or reordered; clients must reload authoritative state on reconnect. Multiple web replicas require that backplane.

Run recurring schedules on one designated process (CRON_ENABLED=false elsewhere). Keep periodic maintenance schedules; use queued jobs for event-driven work. Existing Cron:<name> overrides remain available in UTC. Optional schedule overrides belong in deployment settings rather than .env.example.

S3_PUBLIC_ENDPOINT optionally sets a browser-reachable signing endpoint when storage has an internal hostname.
