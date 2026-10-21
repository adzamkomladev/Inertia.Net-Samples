# Deploying Inkwell

One image runs everything: the web app, server-side rendering (bun, started by the app), the job worker and the
crons. It listens on **8080**, answers **`/health`**, and keeps its SQLite database and Data Protection keys on a
volume at **`/data`**. Migrations run on every boot.

Run **one instance**. SQLite, the in-process job queue, the crons and SignalR all assume a single replica.

## What you need

- A volume mounted at `/data`. Without it every deploy wipes the database and signs everyone out.
- An S3-compatible bucket for images and backups (RustFS, MinIO, Cloudflare R2, AWS S3). The app creates the buckets
  if they are missing and makes `media` public-read.
- A [Maileroo](https://maileroo.com) API key. Production refuses to send without one (only Development logs mail instead).

## Environment

| Variable | Example | Notes |
|---|---|---|
| `APP_BASE_URL` | `https://blog.example.com` | Required. Used in email links. |
| `ANALYTICS_SALT` | 64 random hex chars | Required. Generate with `openssl rand -hex 32`; keep it stable. |
| `S3_ENDPOINT` | `https://s3.example.com` | S3 API reachable from the container. |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY` | | |
| `S3_REGION`, `S3_BUCKET`, `S3_BACKUP_BUCKET` | `us-east-1`, `media`, `backups` | Defaults shown. |
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

## Backups and restore

The `backup` cron (03:00 UTC) uploads a gzipped `VACUUM INTO` snapshot to the backups bucket and keeps the latest 14.
To restore: stop the app, download and `gunzip` a snapshot, replace `/data/app.db`, delete `/data/app.db-wal` and
`/data/app.db-shm`, start the app.

## Demo data

From the container's terminal: `dotnet Inkwell.dll --seed-demo`.
