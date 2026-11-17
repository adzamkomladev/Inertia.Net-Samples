# Deploying CarParkerReplicas

CarParkerReplicas is the multi-process deployment of CarParker. Every app replica runs the web app, its managed
Node SSR process, the Runwick worker and schedules. MongoDB is a replica set, Redis is shared state,
and HAProxy is the only public entry point.

See [REPLICAS.md](REPLICAS.md) for the topology and failover behavior and [CACHING.md](CACHING.md) for the
Inertia cache contract. The application-level guide is [USER_GUIDE.md](USER_GUIDE.md).

## What runs

| Service | Replicas | Responsibility |
|---|---:|---|
| `proxy` | 1 | HAProxy 3.2; publishes the only app port; cookie affinity and `/health` checks |
| `app` | 3 by default | Web + Node SSR + jobs + cron; `Replicas:Enabled=true`, `Database:InitializeOnStartup=false` |
| `initialize` | exits | `--initialize`; creates indexes before any replica serves; `Replicas:Enabled=false` |
| `redis` | 1 | Data Protection, SignalR backplane, rate limits, Inertia cache; AOF + volume |
| `mongo1..3` | 3 | `mongod --replSet rs0`, three data-bearing members |
| `mongo-init` | exits | `rs.initiate` with the three service names, waits for a writable primary |

The image listens on **8080**, answers **`/health`** (readiness) and **`/alive`** (liveness). `/health` is `200`
for `Healthy` and `Degraded`, and `503` for `Unhealthy`. MongoDB and critical Redis are `Unhealthy`; SSR and the
cache check are `Degraded`. In Development every response includes `X-App-Replica` (and `/alive` reports the instance); outside Development neither exposes the container hostname. The bundled HAProxy adds `X-Replica` (its server slot name) for local acceptance checks.

`/data` remains the file Data Protection fallback when Redis is absent. With the replica Compose stack, the key
ring lives in Redis, so losing `/data` does not log users out. Keep `APP_BASE_URL` stable and HTTPS in production.

## Compose

```sh
cp .env.example .env    # set APP_BASE_URL, ANALYTICS_SALT, S3_* and EMAIL_*
docker compose up -d --build
```

`docker-compose.override.yml` publishes HAProxy on `127.0.0.1:${APP_PORT:-8184}`. The base file intentionally
publishes no host ports, so Coolify/Dokploy can route to the proxy without exposing MongoDB or Redis.

Scale the app replicas:

```sh
APP_REPLICAS=5 docker compose up -d
# or
docker compose up -d --scale app=5
```

Do not scale below one replica and do not disable jobs on every replica.

Seed a running stack:

```sh
docker compose run --rm -e Replicas__Enabled=false app --seed-demo
```

`initialize`, `mongo-init` and one-shot commands use the same image. The `initialize` environment deliberately
keeps `Replicas:Enabled=false`; Data Protection still uses Redis because `REDIS_URL` is configured. That is the
required behavior: the initializer must not create a file key ring that app replicas cannot read.

## Environment

| Variable | Example | Notes |
|---|---|---|
| `APP_BASE_URL` | `https://park.example.com` | Required outside Development. Used in email links. |
| `ANALYTICS_SALT` | 64 random hex chars | Required. `openssl rand -hex 32`; keep it stable. |
| `DATABASE_URL` | `mongodb://mongo1:27017,mongo2:27017,mongo3:27017/?replicaSet=rs0&w=majority&maxPoolSize=50` | Multi-node URL: do not add `directConnection=true`. |
| `DATABASE_NAME` | `carparker_replicas` | Same in every process. |
| `MONGO_MODE` | `replica` | `single` is allowed for a standalone image-only deployment. |
| `REDIS_URL` | `redis://redis:6379/0` | Required when `Replicas:Enabled=true`. Supports `rediss://`, auth and DB path. |
| `REPLICA_NAMESPACE` | `CarParkerReplicas:production` | Shared by every process; never reuse another sample's namespace. |
| `REPLICAS_ENABLED` | `true` | App replicas true; `initialize` false. |
| `CACHE_VERSION` | `v1` | Same on every replica; bump when cached prop/SSR shapes change. |
| `AUTH_SECURE_COOKIES` | `false` locally, `true` behind TLS | Defaults to `true` outside Development. |
| `PUBLIC_SCHEME` | `http` locally, `https` behind TLS | HAProxy forwards it as `X-Forwarded-Proto` (default `https`); keep it in step with `AUTH_SECURE_COOKIES`. |
| `DATABASE_INITIALIZE_ON_STARTUP` | `false` on app replicas | `initialize` and CLI commands still initialize. |
| `S3_ENDPOINT` | `http://rustfs:9000` | S3 API reachable from the container. |
| `S3_PUBLIC_ENDPOINT` | `https://s3.example.com` | Host browsers use for presigned uploads. |
| `S3_PUBLIC_URL` | `https://s3.example.com/uploads` | Stable public base for stored images. |
| `S3_ACCESS_KEY`, `S3_SECRET_KEY`, `S3_REGION`, `S3_BUCKET` | | `docker/s3/` has policies and CORS. Use a bucket or key prefix dedicated to this sample. |
| `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME` | | Maileroo. Required in production (sending throws without a key). |
| `TRUSTED_PROXY_NETWORKS` | private CIDRs | Default covers Docker networks. |
| `JOBS_CONCURRENCY` | `4` | Maximum concurrency of each Runwick `mail` and `events` pool per process (each scales from 1); size against provider and PostgreSQL capacity. |
| `JOBS_ENABLED` | `true` | Runs the Runwick worker in this process. Do not set false everywhere; email, realtime and cleanup jobs are queued. |
| `CRON_ENABLED` | `true` | Runs the Runwick scheduler in this process. Materialization is atomic, so every replica may schedule; a production option is cron on one role only. |
| `RUNWICK_DATABASE_URL` | `Host=runwick-db;Port=5432;Database=carparker_runwick;...` | Required. PostgreSQL store for every job and schedule. |
| `RUNWICK_ADMIN_USERNAME`, `RUNWICK_ADMIN_PASSWORD` | | Required outside Development: the host refuses to start with the sample `Admin` / `Admin@12345` dashboard login. Compose requires them. |
| `RUNWICK_DEMO_ENABLED` | `false` (`true` in Development) | Registers the demo sleep queues and pools and the admin-only `/demo/jobs` endpoints. |
| `APP_REPLICAS` | `3` | Compose scaling convenience, not read by the app. |

Optional tuning:

| Setting | Default | Purpose |
|---|---|---|
| `Cache:MaxEntryBytes` / `CACHE_MAX_ENTRY_BYTES` | `0` (unlimited) | Skip a prop/SSR entry larger than this. A 1 MiB guard bounds large SSR responses. |
| `Cache:TagTtlPaddingSeconds` | `60` | Tag sets outlive their entries by this much. |
| `Cache:FailureLogSeconds` | `30` | Throttle repeated Redis cache warnings. |
| `Inertia:SsrCacheSeconds` / `SSR_CACHE_SECONDS` | `300` | Shared SSR response fresh window. |
| `Inertia:SsrStaleSeconds` / `SSR_STALE_SECONDS` | `60` | Stale-while-revalidate window. |
| `SSR_EXECUTABLE` | `node` in the image; `bun` locally | SSR runtime. |
| `S3_REGION` | `us-east-1` | Storage signing region. |
| `S3_PUBLIC_BUCKET` | `S3_BUCKET` | Separate public-read bucket. |
| `STORAGE_FORCED_PATH_STYLE` | provider-dependent | Override S3 addressing. |

## Deployment order

1. **Provision Redis first.** It must be persistent. Data Protection's key ring lives there; losing it logs
   everyone out and invalidates outstanding email-verification links. Use AOF (`appendonly yes`), a volume, and
   do not evict critical keys. For production, use a managed Redis with replication/Sentinel/TLS; separate the
   cache onto another Redis only if you add a cache-specific connection (this sample uses one Redis).
2. **Provision MongoDB as a replica set.** Three data-bearing members, `rs0`, a keyfile or X.509 authentication,
   TLS, and restricted networking. Never expose `27017` publicly. Run the equivalent of `docker/mongo-init.sh`
   once; use service/DNS names that every replica can resolve. Keep read preference `primary` for booking and
   claim decisions.
3. **Run `--initialize` once.** `Replicas:Enabled=false`, `Database:InitializeOnStartup=true`, same namespace,
   same Redis and Mongo. It exits after creating indexes. Wait for success before starting app replicas.
4. **Start app replicas.** `Replicas:Enabled=true`, `Database:InitializeOnStartup=false`, identical
   `CACHE_VERSION`, `REPLICA_NAMESPACE`, build manifest and SSR bundle. The proxy health check waits for `/health`.
5. **Route traffic to the proxy.** It preserves affinity with `CPR_REPLICA`; SignalR negotiation, long polling and
   the Inertia state cookie need it.

During a rolling deploy, run app and SSR bundles from the same release. SSR cache keys include the Vite manifest
version but prop keys include only `CACHE_VERSION`; bump `CACHE_VERSION` when prop shapes change, and clear the
cache if needed. Deploy job-compatible code in an order that lets old and new replicas claim each other's work
(the queue is at-least-once), or drain the queue first.

## Connection pools

MongoDB's driver defaults to 100 connections per process. The Compose URL uses `maxPoolSize=50`; with three app
replicas that is 150 connections plus the initializer. Measure `serverStatus().connections` and lower the pool
before adding replicas. Redis connections are separate per replica (multiplexer + SignalR backplane); use a
managed plan that supports the total client count, or add a Redis proxy.

## TLS and cookies

- Terminate TLS in front of HAProxy, or configure HAProxy TLS. Set `AUTH_SECURE_COOKIES=true` in production and
  `APP_BASE_URL` to the HTTPS origin.
- Cookie names are scoped to this sample: `.cpr.auth`, `.cpr.af` and `.cpr.state`. Run it on a different host
  from CarParker or other samples on localhost, or their cookies will collide.
- Data Protection's application name is the replica namespace. Keep it stable across every process and release.
- HAProxy overwrites `X-Forwarded-For` and `X-Forwarded-Proto`; the app trusts forwarded headers only from private
  networks. If a TLS terminator sits in front of HAProxy, make sure the public scheme reaches the app as HTTPS
  (the bundled config assumes HAProxy is the TLS boundary or a front proxy sets the scheme).

## Coolify and Dokploy

- **Compose:** base directory `/samples/CarParkerReplicas`, compose file `docker-compose.yml`. Set the domain on
  the `proxy` service (container port `8080`). Add a persistent volume for Redis if the platform does not keep
  Compose volumes, and pin the project name `carparkerreplicas`.
- **Image only:** build the Dockerfile and point it at a managed replica set and Redis. Run one `--initialize`
  process before starting replicas; then set `Replicas__Enabled=true`, `Database__InitializeOnStartup=false` and
  the same `REPLICA_NAMESPACE`/`CACHE_VERSION` on every instance. Route the platform's health check to `/health`
  and keep only one public service port.
- **Dokploy:** same Compose path; route the proxy port. Do not expose MongoDB or Redis.

## MongoDB authentication and TLS

The bundled Compose setup has no authentication because it is a private, local demonstration network. Production
must add:

```yaml
command:
  - mongod
  - --replSet
  - rs0
  - --bind_ip_all
  - --keyFile
  - /etc/mongo/keyfile
  - --tlsMode
  - requireTLS
  - --tlsCertificateKeyFile
  - /etc/mongo/server.pem
  - --tlsCAFile
  - /etc/mongo/ca.pem
```

and a `DATABASE_URL` with credentials, `authSource=admin`, `tls=true` and `replicaSet=rs0`. Mount secrets
read-only; never commit them. The same applies to Redis with `rediss://` and an ACL user limited to the sample's
key prefix (Redis ACL patterns can restrict `~CarParkerReplicas:*`).

## Health, failover and troubleshooting

- Stop SSR inside one replica: `/health` should stay `200` (Degraded) and public pages fall back to client
  rendering. Each replica has its own Node process; one failure does not affect the others.
- Stop Redis: `/health` becomes `503`; existing sessions cannot be validated, SignalR reconnects fail, and auth
  rate limits fail closed. Restore Redis before rolling replicas.
- Stop the Mongo primary: the driver discovers the new primary, retryable writes and transactions recover after
  the election. Brief 500s/timeouts are expected; the proxy health check removes a replica only if its Mongo ping
  remains down.
- Check which replica served a request with `curl -I http://localhost:8184/health`; `X-Replica` is HAProxy's server name (`X-App-Replica`, the app
  hostname or `INSTANCE_ID`, is sent in Development only).
- If a page returns no HTML body, check the SSR logs (`document is not defined` was a historical Svelte lifecycle
  issue). `Preferred` falls back to client rendering; a healthy runtime does not prove SSR succeeded.
- From `src/CarParkerReplicas`, run `bun run check:ssr` to render the home page and layout without browser globals.

## Worker capacity

All background work (email, realtime pushes, asset cleanup and the wall-clock schedules) runs on Runwick against the
shared `runwick-db` PostgreSQL store; there is no Mongo job queue any more. **Topology:** every replica runs a worker
and the scheduler by default. That is safe because job claims and schedule materialization are atomic in PostgreSQL:
each job and each cron occurrence executes on exactly one process cluster-wide, whichever replica wins the claim.
To pin the work to one process instead, set `JOBS_ENABLED=false` and `CRON_ENABLED=false` on the web replicas and keep
`true` on one dedicated worker (web replicas still dispatch durably). Never set `JOBS_ENABLED=false` everywhere:
pending jobs stay durable but nothing delivers them. A service commits its Mongo write first and dispatches the job
right after, because Runwick cannot join a MongoDB transaction: a crash in that window loses the job, never the write.
Delivery is at least once: uniqueness keys and unique alert source keys stop duplicate jobs and alerts, but a worker that dies after a mail was accepted and before Runwick records success can send that mail again; size `JOBS_CONCURRENCY`
and each job's execution timeout against that. The retired Mongo `jobs` collection is dropped at startup only when it
holds no Pending or Running rows; otherwise it is kept and a warning logs the count. Back up `runwick-db` with the rest
of the data. The Runwick dashboard is at `/runwick`; the sample `Admin` / `Admin@12345` login works in Development only
and startup fails elsewhere until `RUNWICK_ADMIN_USERNAME` / `RUNWICK_ADMIN_PASSWORD` are set.

Security headers and per-process caches: `SecurityHeaders` sends a CSP whose `connect-src` allows same-origin
WebSocket/SSE for `/realtime` and `/inertia-live` (SignalR over the Redis backplane); the principal lookup cache is per
process (10 s TTL), so a revoked role or claim takes effect on other replicas within 10 s.
Realtime sends are queued and retried through workers. With the Redis backplane, the worker that handles a push
can deliver to clients on any replica. Keep affinity at the proxy; the backplane does not replace it. Events can
be duplicated or reordered; clients reload authoritative state on reconnect.

## PDF receipts

Booking and refund receipts are rendered with QuestPDF (2026.9.2, Community license) on request and streamed as attachments; nothing is stored. The package ships `runtimes/linux-musl-x64` with `libquestpdf_skia.so` and `libquestpdf_qpdf.so`, so the Alpine `-r linux-musl-x64` publish carries them with no extra build step. They link `libstdc++`, `libgcc` and `zlib`, which the Dockerfile installs explicitly in the runtime image. No fontconfig or font packages are needed: the app sets `QuestPDF.Settings.UseSystemFonts = false` and renders with the Lato font embedded in QuestPDF, so output is identical on every host. Image impact: about 12 MB of native libraries plus under 1 MB of managed assembly (roughly +13 MB uncompressed, less compressed). Emails link receipts with absolute URLs built from `APP_BASE_URL`.

In Development, `/dev/emails` lists every email template with sample data (`?format=text` for the plain-text twin); the route is not mapped in any other environment.

The SSR bundle binds the port in `INERTIA_SSR_PORT` (set per instance by Inertia.Net from `Inertia:SsrBasePort`) and falls back to 13714 when it is unset.

## Demo data

From the app container's terminal: `dotnet CarParkerReplicas.dll --seed-demo`. Or use the one-shot Compose
command above. Demo logins are listed in the [README](../README.md); never seed a production database with them.
