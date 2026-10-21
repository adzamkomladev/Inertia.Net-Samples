# .NET Inertia blog samples

Independent .NET 10 applications with React, Vue or Svelte frontends.

| Sample | Brand | Frontend | Database | Deployment |
| --- | --- | --- | --- | --- |
| [ReactSqliteBlog](samples/ReactSqliteBlog/README.md) | **Inkwell** — multi-user blog | React | SQLite | One application |
| [VuePgBlog](samples/VuePgBlog/README.md) | **TechLandier** — electronics storefront with BNPL, warranties, insurance, repairs and rider deliveries | Vue | PostgreSQL | One application |
| [SvelteMongoBlog](samples/SvelteMongoBlog/README.md) | **CarParker** — parking-spot rentals with sessions, refunds, spot cover and a guard console | Svelte | MongoDB | One application |
| [VuePgReplicasAdvBlog](samples/VuePgReplicasAdvBlog/README.md) | Inkwell (advanced) | Vue | PostgreSQL | Aspire/YARP; separate MainApp, Worker and Realtime replicas |
| [ReactSqliteReplicasBlog](samples/ReactSqliteReplicasBlog/README.md) | Inkwell (replicas) | React | SQLite | Replicas on one host sharing a local volume |
| [SvelteMongoReplicasBlog](samples/SvelteMongoReplicasBlog/README.md) | Inkwell (replicas) | Svelte | MongoDB | Replicas sharing MongoDB and Redis |

Start from the README in the chosen sample. Each has its own solution, frontend, Dockerfile, Compose files and `.env.example`. Copy the example only when `.env` does not already exist. Keep credentials in local configuration or the deployment platform's environment settings.

For the advanced PostgreSQL sample, see [deployment to Dokploy or Coolify](samples/VuePgReplicasAdvBlog/docs/DEPLOYMENT.md), [migrations and seeding](samples/VuePgReplicasAdvBlog/docs/SEEDING.md), and [worker coordination](samples/VuePgReplicasAdvBlog/docs/WORKER_COORDINATION.md).

The refreshed suites run against local Postgres/Mongo containers and pass: TechLandier builds checkout/BNPL/care flows on Postgres, CarParker proves booking, sessions, refunds and Spot Cover on MongoDB, and Inkwell keeps the blog suite. See the [verification record](samples/VuePgReplicasAdvBlog/docs/DOCKER_ACCEPTANCE.md) for the earlier live YARP, Aspire and seeding checks.

The original standalone sample folders remain in the local workspace with their own Git histories. This repository publishes the renamed copies under `samples/`; it excludes the original checkouts, secrets, databases, generated output and dependencies. Renamed copies use separate Compose project names and volumes. Existing data remains in the original samples' volumes; copying source does not copy database contents.

The original single-app samples share local ports, so run one of those stacks at a time. Replica samples use separate default ports: advanced Vue PostgreSQL 8183/5441/6387, Svelte MongoDB replicas 8181/27019/6385, and React SQLite replicas 8182/6386. Stop stacks after use with `docker compose down`; omit `--volumes` to preserve data.

See the [Inertia.Net 1.1.0 upgrade measurements](docs/INERTIA_1_1_UPGRADE.md) for all nineteen local projects and standalone repositories.
All six samples are now on Inertia.Net 1.4.0 and adopt its new features: cached analytics props, an SSR output cache, Inertia DevTools, Vite page prefetch, the development page-file check and the single antiforgery middleware (see the [1.4.0 adoption notes](docs/INERTIA_1_4_UPGRADE.md); the [1.2.0 upgrade baseline](docs/INERTIA_1_2_UPGRADE.md) records the earlier 1.1.0 → 1.2.0 → 1.3.0 version history).

See [memory at rest and database performance research](docs/MEMORY_AND_DB_RESEARCH.md) for why the containers sit at 130–200 MiB and the measured database fixes.
