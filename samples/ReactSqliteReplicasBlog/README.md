# ReactSqliteReplicasBlog

Three app replicas behind HAProxy, with shared authentication keys, Redis rate limits and a SignalR backplane. Derived from the corresponding original sample. See [replica architecture and limits](docs/REPLICAS.md) and [reference feature documentation](docs/REFERENCE_SAMPLE.md).

The ignored local .env is already configured with the reference sample's Maileroo, S3, Redis and optional Meilisearch credentials. New databases, cache namespaces, authentication keys and indexes are isolated. Never overwrite that file to rerun this sample.

For a fresh clone in PowerShell (Node 20+ for HTTP acceptance, Bun for frontend builds):

```powershell
if (-not (Test-Path .env)) { Copy-Item .env.example .env }
# Fill EMAIL_API_KEY, EMAIL_FROM_ADDRESS, EMAIL_FROM_NAME and S3 settings in .env.
docker compose up -d --build
# Open http://localhost:8182
node scripts/check-replica-flows.mjs
# Auth limits are shared; wait a minute before a rate-budget probe.
node scripts/check-replicas.mjs
docker compose down
```

Scale within the documented deployment scope with `docker compose up -d --no-deps --scale app=5 app`. HAProxy discovers up to ten backends; raise its server-template bound for more. Run initialization once before replicas, never concurrent migrations. Use HTTPS and SECURE_COOKIES=true in production. Compose down keeps database and Redis volumes.

Backend tests: dotnet test ReactSqliteReplicasBlog.slnx. Frontend checks: run bun install --frozen-lockfile and bun run build in src/ReactSqliteReplicasBlog. Live acceptance uses real HTTP requests through HAProxy and reserved captured-mail accounts; verification and fixture cleanup use the database. It does not send test mail to real inboxes. See [acceptance record](docs/DOCKER_ACCEPTANCE.md).