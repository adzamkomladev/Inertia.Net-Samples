# Deploying Inkwell

After deployment, follow the [administration guide](USER_GUIDE.md) to bootstrap an administrator and manage
the team. Existing accounts remain non-admin after the additive team migration; use `--create-admin` with
a new email. No database reset or demo reseed is required to use team management or notification sound.

One image runs everything: the web app, server-side rendering (bun, started by the app), the job worker and the
crons. It listens on **8080**, answers **`/health`**, and keeps its SQLite database and Data Protection keys on a
volume at **`/data`**. Migrations run on every boot.

Run **one instance**. SQLite, the in-process job queue, the crons and SignalR all assume a single replica.

## What you need

- A volume mounted at `/data`. Without it every deploy wipes the database and signs everyone out.
- An S3-compatible bucket for images (RustFS, MinIO, Cloudflare R2, AWS S3). The app creates the bucket
  if it is missing and makes `media` public-read.
- A [Maileroo](https://maileroo.com) API key. Production refuses to send without one (only Development logs mail instead).

## Environment

| Variable | Example | Notes |
|---|---|---|
| `APP_BASE_URL` | `https://blog.example.com` | Required. Used in email links. |
| `ANALYTICS_SALT` | 64 random hex chars | Required. Generate with `openssl rand -hex 32`; keep it stable. |
| `S3_ENDPOINT` | `https://s3.example.com` | S3 API reachable from the container. |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY` | | |
| `S3_REGION`, `S3_BUCKET` | `us-east-1`, `media` | Defaults shown. |
| `S3_PUBLIC_URL` | `https://cdn.example.com/media` | Only when browsers can't reach `S3_ENDPOINT/media`. |
| `STORAGE_FORCED_PATH_STYLE` | `true` | `false` for virtual-hosted buckets. |
| `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | | Maileroo. |
| `CRON_ENABLED` | `true` | |

The image already sets `ASPNETCORE_ENVIRONMENT=Production`, `DATABASE_URL=Data Source=/data/app.db;…` and
`DATA_PROTECTION_KEYS_PATH=/data/keys`. Forwarded headers are trusted from private networks, which covers the
Docker networks of Coolify, Dokploy and a local reverse proxy.

## Plain VPS

```sh
git clone <repo> && cd samples/Inkwell
cp .env.example .env    # set APP_BASE_URL, ANALYTICS_SALT, S3_* and EMAIL_*
docker compose up -d --build
```

`docker-compose.override.yml` publishes the app on `${APP_PORT:-8080}`. Put Caddy, nginx or Traefik in front for TLS
and WebSocket upgrades (`/realtime`).

Or with the image alone:

```sh
docker build -t inkwell .
docker run -d --name inkwell -p 8080:8080 -v inkwell-data:/data --env-file .env --restart unless-stopped inkwell
```

## Coolify

Either build pack works:

- **Dockerfile:** *New Resource → Application → your repo*, Build Pack **Dockerfile**, Base Directory
  `/samples/Inkwell`, Ports Exposes `8080`. Add a persistent volume at `/data` and the environment above.
- **Docker Compose:** Build Pack **Docker Compose**, Base Directory `/samples/Inkwell`, compose file
  `docker-compose.yml`. Coolify creates the `app-data` volume and lists the variables for you.

Set the domain on the `app` service (port 8080). Traefik handles TLS and WebSockets.

## Dokploy

*Create Service → Application* (Dockerfile, build path `samples/Inkwell`) or *Compose* (compose path
`samples/Inkwell/docker-compose.yml`). Add the environment, a volume mount at `/data` for the application type,
and a domain pointing at container port 8080.

## Demo data

From the container's terminal: `dotnet Inkwell.dll --seed-demo`.


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

SQLite consumers share the persistent database file; SQLite is not a remote worker transport. Keep write concurrency modest. Inkwell defaults to path-style S3 addressing. Persist DATA_PROTECTION_KEYS_PATH with the database.
