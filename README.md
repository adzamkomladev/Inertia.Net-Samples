# Inertia.Net samples

Five .NET 10 applications built with [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net) and React, Vue or
Svelte frontends. Each sample is an independent solution with its own frontend, Dockerfile, Compose files and
`.env.example`; together they exercise the library from basic page rendering to shared caching across replicas.

| Sample | What it is | Frontend | Database | Topology | Live demo |
| --- | --- | --- | --- | --- | --- |
| [Inkwell](samples/Inkwell/README.md) | Multi-user publishing platform with feeds, search, analytics and live notifications | React | SQLite | One application | [inkwell.yebi.africa](https://inkwell.yebi.africa) |
| [TechLandier](samples/TechLandier/README.md) | Electronics storefront with BNPL, warranties, insurance, repairs and rider deliveries | Vue | PostgreSQL | One application | [techlandier.yebi.africa](https://techlandier.yebi.africa) |
| [CarParker](samples/CarParker/README.md) | Parking-spot rentals with sessions, refunds, spot cover and a guard console | Svelte | MongoDB | One application | [carparker.yebi.africa](https://carparker.yebi.africa) |
| [CarParkerReplicas](samples/CarParkerReplicas/README.md) | CarParker as whole-application replicas | Svelte | MongoDB replica set + Redis | HAProxy in front of app replicas; shared MongoDB and Redis | Local only |
| [TechLandierDistributed](samples/TechLandierDistributed/README.md) | TechLandier split into independently scalable roles | Vue | PostgreSQL + PgDog | MainApp, Worker, Realtime and SSR pools; PgDog 1 primary + 2 read replicas; two Redis instances | Local only |

The hosted demos run the single-application samples. Demo accounts use `@yopmail.com` addresses with the password
`Password@12345`; each sample README lists its exact logins.

## Inertia.Net features in the samples

- `Scroll` with keyset cursors and infinite-scroll components on feeds, catalogs, lots and profiles.
- `Defer`, grouped and parallel deferred props, and `Cache` with `StaleFor` for dashboard analytics.
- `IInertiaCache` tag invalidation after the mutations that change cached props.
- A shared `IInertiaCacheStore` in Redis across replicas (CarParkerReplicas and TechLandierDistributed).
- SSR with cached renders, stale-while-revalidate and a multi-server SSR pool (TechLandierDistributed).
- `Once` shared props, `MapInertia`, Precognition validation, Vite prefetch, lazy XSRF cookie refresh and Inertia
  DevTools.
- The single antiforgery middleware, development page-file checks, response compression and dynamic imports.

## Run a sample

Start from the README in the chosen sample. Copy `.env.example` to `.env` only when `.env` does not already exist,
and keep credentials in local configuration or the deployment platform's environment settings.

The single-application samples share local ports (Inkwell `5100`; CarParker and TechLandier `5199`), so run one of
those stacks at a time. The replica samples use separate ports: CarParkerReplicas `8184` and TechLandierDistributed
`8185`. Stop stacks after use with `docker compose down`; omit `--volumes` to preserve data.

## Docs

- [Inertia.Net 1.5.1 upgrade and measurements](docs/INERTIA_1_5_UPGRADE.md)
- [Inertia.Net 1.4.0 feature adoption](docs/INERTIA_1_4_UPGRADE.md)
- [Inertia.Net 1.2.0 upgrade baseline](docs/INERTIA_1_2_UPGRADE.md)
- [Inertia.Net 1.1.0 upgrade measurements](docs/INERTIA_1_1_UPGRADE.md)
- [Memory at rest and database performance research](docs/MEMORY_AND_DB_RESEARCH.md)
- [Performance optimization results](docs/PERFORMANCE_OPTIMIZATION_RESULTS.md)
- [Native AOT compatibility experiment](docs/NATIVE_AOT_2026_10_22.md)
- [Replica scaling research](docs/REPLICA_SCALING_RESEARCH.md)
- [Shared Inertia cache across replicas](docs/INERTIA_CACHE_MULTI_REPLICA.md)
- [PgDog and TechLandierDistributed](docs/PGDOG_TECHLANDIER.md)
- [Replica performance comparison](docs/REPLICA_PERFORMANCE_COMPARISON.md)
- [Benchmark harness](benchmarks/README.md)
