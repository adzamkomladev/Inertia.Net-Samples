# Verification — 2026-10-13

## Renamed samples

All six renamed solutions and frontend production builds passed. Backend builds produced zero warnings/errors. Vite/Rolldown emitted existing sourcemap/timing warnings; frontend builds completed successfully.

| Sample | Integration tests passed |
| --- | ---: |
| ReactSqliteBlog | 115 |
| VuePgBlog | 53 |
| SvelteMongoBlog | 49 |
| VuePgReplicasAdvBlog | 83 |
| ReactSqliteReplicasBlog | 120 |
| SvelteMongoReplicasBlog | 49 |
| **Total** | **469** |

No tests were skipped. Test databases were disposable; PostgreSQL and MongoDB tests used running database containers. Every Compose configuration validated. The source audit found no copied credentials or broken relative Markdown links. Original sample checkouts and volumes were preserved; renamed samples use separate database defaults and volumes.

## Advanced PostgreSQL deployment

Pinned Aspire CLI 13.6.1 published Compose from the single-file `apphost.cs`. The actual generated deployment booted PostgreSQL 18, Redis, a one-shot migrator, an optional seeder, three MainApp containers, two Workers, two Realtime containers and YARP. PostgreSQL/Redis health checks gated migrations, and serving roles waited for migration and seed completion. Published settings use secure cookies; the local HTTP acceptance override disabled secure cookies and bound only YARP to localhost:8183.

Live acceptance through YARP passed signup on each app, the unverified-user publication guard, database verification bypass, signin on another app, shared authentication, post create/read/edit/delete, likes, comments, bookmarks, ownership checks, durable view jobs, public/private realtime events, captured email-job processing and logout. Acceptance accounts were removed.

Routing observed app1–app3 and realtime1–realtime2. Vue SSR rendered on every app; independent web/realtime affinity, SignalR negotiation/long polling, and the shared HTTP rate budget passed (ten missing-antiforgery requests returned 419, then two returned 429).

All three role images built. Real Redis integration tests confirmed that two independent Worker service providers see the same persisted TickerQ job and only one receives its claim. Timestamp precision, children serialization, failed cron retries and completed-occurrence guards passed. Running Worker logs also showed TickerQ jobs completing.

## Seeding

The generated deployment's optional seeder created 20 demo users and 40 posts. Rerunning the CLI with the same counts retained exactly those counts. Changing the saved seed size was rejected. A separate PostgreSQL session held the seed advisory lock while a second CLI invocation was correctly rejected. See [seeding](SEEDING.md) for commands and demo login.

## Local Aspire

The file-based AppHost ran with three MainApp, two Worker and two Realtime processes, a successful migrator, PostgreSQL, Redis and YARP. All seven role resources became healthy. Every MainApp dashboard link resolved to the public YARP URL, http://localhost:8183. Its routing checks passed SSR on all three apps, both realtime destinations, independent affinity, SignalR transport and shared rate limiting. Each MainApp used an isolated SSR port; the unrelated existing React SSR process was left untouched.

## Shutdown and limits

Generated Compose and all integration-test Compose stacks were brought down without removing volumes. Local Aspire was stopped with the pinned CLI, preserving volumes.

Verification was bypassed in the database as authorized. Reserved demo addresses captured mail locally; external Maileroo delivery and S3 delivery were not exercised. Deployment to an actual Dokploy or Coolify server was not performed. These checks establish functional replica behavior, not a throughput benchmark. See [deployment](DEPLOYMENT.md) and [worker coordination](WORKER_COORDINATION.md) for operational requirements and retry limits.
