# Deploy with Aspire-generated Compose

The root `apphost.cs` is the deployment model. It publishes PostgreSQL, Redis, a one-shot migrator, three MainApp containers, two Workers, two Realtime containers and a YARP gateway. `Seed__Demo=true` adds one optional seeder. Only the gateway is exposed to the platform proxy, on **container port 5000**. The development `docker-compose.yml` uses HAProxy; use the Aspire-generated file for this YARP deployment.

## Generate the deployment files

From `samples/VuePgReplicasAdvBlog`, with .NET SDK 10 and Docker installed:

```powershell
dotnet dnx --yes aspire.cli@13.6.1 -- publish `
  --apphost (Resolve-Path apphost.cs).Path `
  --output-path aspire-output `
  --non-interactive -- --Parameters:app-base-url=https://blog.example.com
```

With CLI 13.6.1 already installed, the equivalent is `aspire publish --output-path aspire-output -- --Parameters:app-base-url=https://blog.example.com`. The global CLI on another machine may be older; use the pinned command above when in doubt.

This generates `aspire-output/docker-compose.yaml` and `aspire-output/.env`. Publishing generates configuration; it does **not** upload images or deploy to Dokploy/Coolify. Re-publish after changing replica counts or infrastructure. AppHost supplies the same `APP_BASE_URL` and canonical `App__BaseUrl` to every role. Verification emails, notifications and digests use that public URL. The local Aspire dashboard's MainApp links also point to YARP; private endpoints remain available in resource details.

Generated environment files may contain local credentials and are ignored by Git. Keep the generated `.env` private; transfer its required settings to your deployment platform. If you keep the generated YAML in a deployment repository, commit only that YAML after checking that its credentials remain `${...}` placeholders.

## Build and publish the application images

Use a registry the deployment server can access. Example with GitHub Container Registry, from the sample folder:

```powershell
docker login ghcr.io
docker build --target mainapp -t ghcr.io/adzamkomladev/vuepgreplicasadvblog-mainapp:v1 .
docker build --target worker -t ghcr.io/adzamkomladev/vuepgreplicasadvblog-worker:v1 .
docker build --target realtime -t ghcr.io/adzamkomladev/vuepgreplicasadvblog-realtime:v1 .
docker push ghcr.io/adzamkomladev/vuepgreplicasadvblog-mainapp:v1
docker push ghcr.io/adzamkomladev/vuepgreplicasadvblog-worker:v1
docker push ghcr.io/adzamkomladev/vuepgreplicasadvblog-realtime:v1
```

MainApp's image builds Vue client/SSR assets and includes Bun. Worker includes the PostgreSQL 18 backup client. Realtime has only its own application and shared libraries. These Dockerfile targets are also attached to the corresponding resources through `PublishAsDockerFile`, so Aspire's container build pipeline can use them.

Set the generated image variables in Dokploy/Coolify:

| Variables | Image |
| --- | --- |
| `MIGRATE_IMAGE`, `APP1_IMAGE`, `APP2_IMAGE`, `APP3_IMAGE` | `ghcr.io/adzamkomladev/vuepgreplicasadvblog-mainapp:v1` |
| `WORKER1_IMAGE`, `WORKER2_IMAGE` | `ghcr.io/adzamkomladev/vuepgreplicasadvblog-worker:v1` |
| `REALTIME1_IMAGE`, `REALTIME2_IMAGE` | `ghcr.io/adzamkomladev/vuepgreplicasadvblog-realtime:v1` |
| `SEED_IMAGE`, if seeding was enabled | The same MainApp image |

For different replica counts, set each generated role's image variable. Use an immutable tag for each release. Private registry images require pull credentials on the deployment server; GitHub CLI login alone does not authenticate Docker.

## Environment settings

Set these in the platform's Compose environment settings before deployment:

- `APP_BASE_URL=https://blog.example.com`: the public HTTPS domain routed to YARP, without a trailing slash.
- `POSTGRES_PASSWORD`, `REDIS_PASSWORD`: strong deployment credentials. Keep them stable across redeploys of existing volumes.
- `ANALYTICS_SALT`: a stable, private random value for viewer hashing.
- `EMAIL_API_KEY`, `EMAIL_FROM_ADDRESS`, `EMAIL_FROM_NAME`: your Maileroo settings.
- `S3_ENDPOINT`, `S3_REGION`, `S3_BUCKET`, `S3_BACKUP_BUCKET`, `S3_ACCESS_KEY`, `S3_SECRET_KEY`: your storage provider's settings. `S3_PUBLIC_URL`/`S3_PUBLIC_ENDPOINT` select browser-accessible addresses when needed; configure bucket CORS for the public blog origin.
- `SEARCH_DRIVER=native` for PostgreSQL search, or `meilisearch` with `MEILISEARCH_URL` and `MEILISEARCH_API_KEY` pointing to an accessible service.

Empty optional storage/search values allow the app to run without those external features. Maileroo needs valid credentials for external mail. Database and Redis connection strings are composed by Aspire; do not substitute a replica URL, localhost connection or another sample's `.env` connection string.

AppHost enables secure auth cookies in published deployments. Terminate HTTPS at the platform proxy and forward the original host/protocol to YARP. The apps trust private proxy networks and process forwarded headers. Keep MainApp, Realtime, PostgreSQL and Redis private. YARP maintains separate affinity cookies for web and realtime traffic; keep WebSockets enabled on the platform proxy.

TickerQ workers reserve Redis database 1 by default. Change `TickerQ__RedisDatabase` in AppHost configuration before publishing when that database is already used by another scheduler. See [worker coordination](WORKER_COORDINATION.md) for provider isolation and retry limits.

## Dokploy

1. Create a Docker Compose service in a project. Import the generated YAML using Dokploy's raw Compose input, or point it at a deployment repository containing that file.
2. Add the required environment/image variables above and registry pull credentials.
3. In **Domains**, add `blog.example.com`, choose service **gateway**, choose container port **5000**, and enable HTTPS. Route the entire domain, including `/realtime`, to gateway.
4. Deploy. PostgreSQL and Redis health checks gate migrations; serving roles wait for the migrator to exit successfully. Inspect the migrator's logs if startup stops there.
5. Verify signup/signin, verification links, posts and realtime through the public domain.

Dokploy adds the domain proxy labels/network. The generated gateway uses `expose: 5000` and has no host port binding; internal roles and databases have no public host ports. Do not add per-replica domains.

## Coolify

1. Create a Docker Compose application/service. Paste the generated YAML using **Docker Compose Empty**, or select **Docker Compose from Git** for a deployment repository containing the generated file.
2. Supply the environment/image variables and private registry credentials.
3. Assign the HTTPS domain to **gateway**. Select its detected port **5000**; where the domain field requires a port selector, use `https://blog.example.com:5000`. The public `APP_BASE_URL` remains `https://blog.example.com`, without the internal port.
4. Keep all other services without domains or public port mappings, then deploy and verify through the gateway domain.

The generated Compose file contains named database/cache/worker-data volumes. Keep these attached across redeploys and back up PostgreSQL separately. Do not remove volumes when restarting. Aspire's development dashboard is intentionally excluded from published Compose; local Aspire runs still have the dashboard.

## Migrations, demo content and maintenance

Each deployment includes `migrate`, running `dotnet MainApp.dll --migrate`. It applies existing migrations without resetting data. Keep the migration image on the same release tag as all MainApp replicas. See [seeding](SEEDING.md) for the optional Aspire seed resource, container commands, configurable counts and resumable checkpoints.

Sources: [Aspire Docker integration](https://aspire.dev/integrations/compute/docker/), [project container publishing](https://aspire.dev/reference/api/csharp/aspire.hosting/projectresourcebuilderextensions/), [Dokploy Compose domains](https://docs.dokploy.com/docs/core/docker-compose/domains), [Coolify Compose builds](https://coolify.io/docs/applications/builds/docker-compose), and [Coolify domains](https://coolify.io/docs/core/networking/domains).
