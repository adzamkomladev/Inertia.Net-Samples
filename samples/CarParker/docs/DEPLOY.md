# Deploying CarParker

After deployment, follow the [user and admin guide](USER_GUIDE.md) to create an administrator,
configure lot hours, manage staff and open booking/claim details. Changing MongoDB mode requires a restart,
not a reseed; the application setting does not change the database server's topology.

The image runs the web app, server-side rendering (bun, started by the app), the job worker and the crons. It
listens on **8080**, answers **`/health`**, and keeps Data Protection keys on a volume at
**`/data`**. Indexes are created on boot.

MongoDB can be standalone (`MONGO_MODE=single`) or a replica set (`MONGO_MODE=replica`). Outside production
Compose, single mode is the default. Replica mode enables multi-document transactions and rejects standalone
servers at startup. Single mode cannot roll back writes across collections; failed bookings, refunds or job
enqueueing can leave partial data requiring repair. Use replica mode when atomicity is required.

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
| `S3_BUCKET` | `uploads` | `docker/s3/` has the bucket policy and CORS files. |
| `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | | Maileroo. Required in production (sending throws without a key). |
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

Image only, against an existing replica set (use `MONGO_MODE=single` and omit `replicaSet` for standalone):

```sh
docker build -t carparker .
docker run -d --name carparker -p 8080:8080 -v carparker-data:/data --env-file .env \
  -e MONGO_MODE=replica -e DATABASE_URL='mongodb://…/?replicaSet=rs0' --restart unless-stopped carparker
```

## Coolify

- **Compose:** *New Resource → Docker Compose* from your repo, Base Directory `/samples/CarParker`, compose file
  `docker-compose.yml`. Set the domain on the `app` service (port 8080) and fill in the variables Coolify lists.
- **Image only:** create *Database → MongoDB* first. For a standalone server, set `MONGO_MODE=single` and
  `DATABASE_URL` to its internal URL without `replicaSet`. For transactions, configure the database with
  `--replSet rs0`, run `rs.initiate()` once (or use Atlas), then set `MONGO_MODE=replica` and include
  `replicaSet=rs0` in the URL. Use *Application → Dockerfile*, Base Directory `/samples/CarParker`, port 8080,
  and a volume at `/data`.

## Dokploy

*Compose* service with compose path `samples/CarParker/docker-compose.yml`, or an *Application* (Dockerfile, build
path `samples/CarParker`) next to a standalone MongoDB service (`MONGO_MODE=single`) or a replica set
(`MONGO_MODE=replica`, database started with `--replSet rs0` and initialized). Add the domain on container port
8080 and, for the application type, a volume mount at `/data`.

## Demo data

From the app container's terminal: `dotnet CarParker.dll --seed-demo`.


## Worker capacity and optional settings

The environment example contains connection/secret/provider values only. Optional tuning:

| Setting | Default | Purpose |
| --- | --- | --- |
| MONGO_MODE | single (replica in production Compose) | `single` skips transactions; `replica` requires replica-set/sharded topology. Also accepts `replica-set` and `replicaset`. |
| DATABASE_URL_DOCKER | internal replica URL | Production Compose override for an external MongoDB URL; pair with the matching MONGO_MODE. The bundled Mongo service still starts. |
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
