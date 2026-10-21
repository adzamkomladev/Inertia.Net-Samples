# Deploying CarParker

The image runs the web app, server-side rendering (bun, started by the app), the job worker and the crons. It
listens on **8080**, answers **`/health`**, and keeps Data Protection keys and temporary backups on a volume at
**`/data`**. Indexes are created on boot.

MongoDB **must be a replica set** (bookings, check-ins and refunds use multi-document transactions). A single-node
replica set is fine.

Run **one app instance**: the crons, the in-process job wake-up and SignalR assume a single replica.

## Two ways to deploy

| | What runs | Use when |
|---|---|---|
| **Compose** (`docker-compose.yml`) | app + `mongo:9` single-node replica set, both with volumes | One VPS / one Coolify or Dokploy project holds everything |
| **Image only** (`Dockerfile`) | app; MongoDB is a separate resource you point `DATABASE_URL` at | You already run MongoDB (Coolify/Dokploy database, Atlas, …) |

## Environment

| Variable | Example | Notes |
|---|---|---|
| `APP_BASE_URL` | `https://park.example.com` | Required. Used in email links. |
| `ANALYTICS_SALT` | 64 random hex chars | Required. `openssl rand -hex 32`; keep it stable. |
| `DATABASE_URL` | `mongodb://user:pass@mongo:27017/?replicaSet=rs0&authSource=admin` | Image only; compose sets it. |
| `DATABASE_NAME` | `carparker` | |
| `S3_ENDPOINT` | `http://rustfs:9000` | S3 API reachable from the container. |
| `S3_PUBLIC_ENDPOINT` | `https://s3.example.com` | Host browsers use for presigned uploads. |
| `S3_PUBLIC_URL` | `https://s3.example.com/uploads` | Public base for stored images. |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_REGION` | | |
| `S3_BUCKET`, `S3_BACKUP_BUCKET` | `uploads`, `backups` | `docker/s3/` has the bucket policy and CORS files. |
| `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | | Maileroo. Without a key, mail is only logged. |
| `TRUSTED_PROXY_NETWORKS` | `10.0.0.0/8,172.16.0.0/12,192.168.0.0/16` | Default; covers Docker networks. |
| `CRON_ENABLED` | `true` | |

## Plain VPS

```sh
git clone <repo> && cd samples/CarParker
cp .env.example .env    # set APP_BASE_URL, ANALYTICS_SALT, S3_* and EMAIL_*
docker compose up -d --build
```

`docker-compose.override.yml` publishes the app on `${APP_PORT:-8080}`; MongoDB is not published. Put Caddy, nginx
or Traefik in front for TLS and WebSocket upgrades (`/realtime`).

Image only, against an existing replica set:

```sh
docker build -t carparker .
docker run -d --name carparker -p 8080:8080 -v carparker-data:/data --env-file .env \
  -e DATABASE_URL='mongodb://…/?replicaSet=rs0' --restart unless-stopped carparker
```

## Coolify

- **Compose:** *New Resource → Docker Compose* from your repo, Base Directory `/samples/CarParker`, compose file
  `docker-compose.yml`. Set the domain on the `app` service (port 8080) and fill in the variables Coolify lists.
- **Image only:** create *Database → MongoDB* first. Coolify's MongoDB is a standalone server, so add
  `--replSet rs0` to its start command and run `rs.initiate()` once from its terminal (or use Atlas). Then
  *Application → Dockerfile*, Base Directory `/samples/CarParker`, port 8080, a volume at `/data`, and
  `DATABASE_URL` set to the database's internal URL with `replicaSet=rs0`.

## Dokploy

*Compose* service with compose path `samples/CarParker/docker-compose.yml`, or an *Application* (Dockerfile, build
path `samples/CarParker`) next to a MongoDB service started with `--replSet rs0`. Add the domain on container port
8080 and, for the application type, a volume mount at `/data`.

## Backups

A daily 03:00 UTC cron runs `mongodump` (bundled in the image) and uploads a gzipped archive to `S3_BACKUP_BUCKET`
under `CarParker/<environment>/db/`, keeping the newest 14. Restore with
`mongorestore --uri "$DATABASE_URL" --archive=backup.archive --gzip --drop`. Uploaded files live in S3; back up that
bucket separately.

## Demo data

From the app container's terminal: `dotnet CarParker.dll --seed-demo`.
