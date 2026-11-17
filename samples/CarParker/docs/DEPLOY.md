# Deploying CarParker

After deployment, follow the [user and admin guide](USER_GUIDE.md) to create an administrator,
configure lot hours, manage staff and open booking/claim details. Changing MongoDB mode requires a restart,
not a reseed; the application setting does not change the database server's topology.

The image runs the web app, server-side rendering (Node in the image, started by the app), the job worker and the crons. It
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

## SSR troubleshooting

If the logs report `document is not defined` at `lib/realtime.svelte.ts`, rebuild and redeploy an image
containing the browser-only realtime cleanup fix. Svelte's `onDestroy` also runs during SSR; DOM listeners
must be removed by the cleanup returned from `onMount`. A preferred-SSR failure falls back to client rendering,
so an HTTP 200 or healthy runtime does not prove the page HTML was rendered successfully.
Run `bun run check:ssr` from `src/CarParker` to render the home page and its layout without browser globals.

## Demo data

From the app container's terminal: `dotnet CarParker.dll --seed-demo`.


## PDF receipts

Booking and refund receipts are rendered with QuestPDF (2026.9.2, Community license) on request and streamed as attachments; nothing is stored. The package ships `runtimes/linux-musl-x64` with `libquestpdf_skia.so` and `libquestpdf_qpdf.so`, so the Alpine `-r linux-musl-x64` publish carries them with no extra build step. They link `libstdc++`, `libgcc` and `zlib`, which the Dockerfile installs explicitly in the runtime image. No fontconfig or font packages are needed: the app sets `QuestPDF.Settings.UseSystemFonts = false` and renders with the Lato font embedded in QuestPDF, so output is identical on every host. Image impact: about 12 MB of native libraries plus under 1 MB of managed assembly (roughly +13 MB uncompressed, less compressed). Emails link receipts with absolute URLs built from `APP_BASE_URL`.

In Development, `/dev/emails` lists every email template with sample data (`?format=text` for the plain-text twin); the route is not mapped in any other environment.

## Worker capacity and optional settings

The environment example contains connection/secret/provider values only. Optional tuning:

| Setting | Default | Purpose |
| --- | --- | --- |
| MONGO_MODE | single (replica in production Compose) | `single` skips transactions; `replica` requires replica-set/sharded topology. Also accepts `replica-set` and `replicaset`. |
| DATABASE_URL_DOCKER | internal replica URL | Production Compose override for an external MongoDB URL; pair with the matching MONGO_MODE. The bundled Mongo service still starts. |
| JOBS_CONCURRENCY | 4 | Maximum concurrency of the Runwick `mail` and `events` pools (each scales from 1); start at 1 for limited provider capacity. |
| JOBS_ENABLED | true | Disable the Runwick worker on a web replica while it continues to dispatch jobs. |
| RUNWICK_ADMIN_USERNAME, RUNWICK_ADMIN_PASSWORD | (none) | Required outside Development: the host refuses to start with the sample `Admin` / `Admin@12345` dashboard login. |
| RUNWICK_DEMO_ENABLED | false (true in Development) | Registers the demo sleep queues, pools and the admin-only `/demo/jobs` endpoints. |
| CRON_ENABLED | true | Disable schedules on replicas serving HTTP or processing jobs only. |
| SSR_EXECUTABLE | node in the image; bun locally | Override the SSR runtime. |
| S3_REGION | us-east-1 | Storage signing region. |
| S3_PUBLIC_URL | endpoint + bucket | Public media/CDN base URL. |
| S3_PUBLIC_BUCKET | (S3_BUCKET) | Separate public-read bucket for uploads; when set, images are stored there so `S3_PUBLIC_URL` serves them. |
| STORAGE_FORCED_PATH_STYLE | provider-dependent | Override S3 addressing. |

Background jobs (email, realtime pushes, asset cleanup) run on Runwick, stored in the SQLite file `runwick.db` on the persistent data volume. SQLite cannot share a transaction with MongoDB, so a service commits its Mongo write and dispatches the job right after: a crash in that window loses the job, not the write. Delivery is at least once: uniqueness keys and unique alert source keys stop duplicate jobs and alerts, but a worker that dies after a mail was accepted and before Runwick records success can send that mail again. Pending jobs remain durable when the worker is disabled, but only the process that owns `runwick.db` can run them, so run one app process per volume (or one dedicated worker with JOBS_ENABLED=true and the web replicas on JOBS_ENABLED=false pointing at the same file system, never a network share). The retired Mongo `jobs` collection is dropped at startup only when it holds no Pending or Running rows. Otherwise it is kept and a warning logs the count: let the previous release drain it (or drop it yourself once you accept losing those jobs), then restart. The compose file requires `RUNWICK_ADMIN_USERNAME` and `RUNWICK_ADMIN_PASSWORD`.

Realtime sends are queued and retry through workers. Keep consumers enabled in a single-instance deployment. Consumers in another process require a SignalR backplane to reach web clients. The queue does not provide cross-process SignalR delivery. Events can be duplicated or reordered; clients must reload authoritative state on reconnect. Multiple web replicas require that backplane.

Run recurring schedules on one designated process (CRON_ENABLED=false elsewhere). Keep periodic maintenance schedules; use queued jobs for event-driven work. `Cron:<name>` overrides are read when a Runwick schedule is first registered; later changes belong in the dashboard. Runwick keeps its SQLite job store on the persistent data volume, so back it up with the rest of `/data`. The dashboard at `/runwick` uses the sample `Admin` / `Admin@12345` login only in Development; elsewhere startup fails until `RUNWICK_ADMIN_USERNAME` / `RUNWICK_ADMIN_PASSWORD` are set. The `prune-jobs` schedule was removed (Runwick prunes its own history); an existing store may keep the old schedule row, which then fails each run with `cron_unknown`: delete it in the dashboard.

The SSR bundle binds the port in `INERTIA_SSR_PORT` (set per instance by Inertia.Net from `Inertia:SsrBasePort`) and falls back to 13714 when it is unset.

S3_PUBLIC_ENDPOINT optionally sets a browser-reachable signing endpoint when storage has an internal hostname.
