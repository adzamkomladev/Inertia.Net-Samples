# Inertia.Net samples

Six .NET 10 applications built with [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net) and React, Vue or
Svelte frontends. Each sample is an independent solution with its own frontend, Dockerfile, Compose files and
`.env.example`; together they exercise the library from basic page rendering to shared caching across replicas.

| Sample | What it is | Frontend | Database | Topology | Live demo |
| --- | --- | --- | --- | --- | --- |
| [Inkwell](samples/Inkwell/README.md) | Multi-user publishing platform with feeds, search, analytics and live notifications | React | SQLite | One application | [inkwell.temtem.dev](https://inkwell.temtem.dev) |
| [TechLandier](samples/TechLandier/README.md) | Electronics storefront with BNPL, warranties, insurance, repairs and rider deliveries | Vue | PostgreSQL | One application | [techlandier.temtem.dev](https://techlandier.temtem.dev) |
| [CarParker](samples/CarParker/README.md) | Parking-spot rentals with sessions, refunds, spot cover and a guard console | Svelte | MongoDB | One application | [carparker.temtem.dev](https://carparker.temtem.dev) |
| [CarParkerReplicas](samples/CarParkerReplicas/README.md) | CarParker as whole-application replicas | Svelte | MongoDB replica set + Redis | HAProxy in front of app replicas; shared MongoDB and Redis | Local only |
| [TechLandierDistributed](samples/TechLandierDistributed/README.md) | TechLandier split into independently scalable roles | Vue | PostgreSQL + PgDog | MainApp, Worker, Realtime and SSR pools; PgDog 1 primary + 2 read replicas; two Redis instances | Local only |
| [KitchenSink](samples/KitchenSink/README.md) | Port of [inertiajs/demo-v3](https://github.com/inertiajs/demo-v3): mini CRM plus every Inertia v3 feature page | Vue | SQLite | One application | Local only |

The hosted demos run the single-application samples. Demo accounts use `@yopmail.com` addresses with the password
`Password@12345`; each sample README lists its exact logins.

## Inertia.Net features in the samples

All samples run **Inertia.Net 1.9.5** with the `@inertiajs/*` 3.11 clients.

- **KitchenSink** mirrors the upstream demo-v3 pages: QUERY visits, instant visits, once props, big integers,
  live props over SignalR, and Precognition, with backend and opt-in Playwright suites.
- `Scroll` with keyset cursors and infinite-scroll components on feeds, catalogs, lots and profiles.
- **MongoDB pagination (1.9)**: CarParker and CarParkerReplicas page `Find` queries with `Inertia.Net.MongoDB`
  `ToPageAsync`; the lots grid and every admin/guard/insurance pager use the same `Page<T>` metadata.
- **Typed pages and routes (1.9)**: public flows use `IInertiaPage` records, and every sample writes
  `ClientApp/types/inertia.d.ts` (page/shared/flash props) and `ClientApp/types/routes.ts` (one typed function per
  named endpoint) with `--inertia-types`. Layouts navigate through `routes.*` instead of hard-coded URLs.
- **Scoped service loaders (1.9)**: shared `auth` and `notifications` props use `Prop<TService, T>`; request-time
  loaders reuse the request's instance while parallel and stale-while-revalidate loaders get their own scope.
- `UseInertiaRedirects()` sends Inertia visits to login/verify like browser navigations (303 after unsafe methods).
- `Defer`, grouped and parallel deferred props, and `Cache` with `StaleFor` for dashboard analytics.
- **Live props (1.6)**: dashboards, notification badges and order timelines mark listeners with
  `InertiaProp.Live(...)` and refresh over the `Inertia.Net.Live.SignalR` transport at `/inertia-live`;
  the durable queue publishes after each write, and the raw `/realtime` hubs keep the toast wording
  plus storefront `Scroll`-prop pulses (see the per-sample CONTRACT.md for each channel).
- `IInertiaCache` tag invalidation after the mutations that change cached props.
- A shared `IInertiaCacheStore` in Redis across replicas (CarParkerReplicas and TechLandierDistributed).
- SSR with cached renders, stale-while-revalidate and a multi-server SSR pool (TechLandierDistributed).
- `Once` shared props, `MapInertia`, Precognition validation, Vite prefetch, lazy XSRF cookie refresh and Inertia
  DevTools.
- The single antiforgery middleware, development page-file checks, response compression and dynamic imports.

## Run a sample

Start from the README in the chosen sample. Copy `.env.example` to `.env` only when `.env` does not already exist,
and keep credentials in local configuration or the deployment platform's environment settings.

The single-application samples share local ports (Inkwell `5100`; CarParker and TechLandier `5199`; KitchenSink
`5170` in development or `8080` in Docker), so run one of those stacks at a time. The replica samples use separate
ports: CarParkerReplicas `8184` and TechLandierDistributed `8185`. Stop stacks after use with `docker compose down`;
omit `--volumes` to preserve data.

## Docs

- [Container images: Alpine, multi-stage and the smallest safe runtime](docs/CONTAINER_IMAGES.md)
- [Inertia.Net 1.9.2 upgrade: Mongo pagination, typed pages and routes](docs/INERTIA_1_9_UPGRADE.md)
- [Inertia.Net 1.6.0 upgrade: live props](docs/INERTIA_1_6_UPGRADE.md)
- [Inertia.Net 1.6.3 upgrade: EF Core pagination](docs/INERTIA_1_6_3_UPGRADE.md)
- [Inertia.Net 1.5.1 upgrade and measurements](docs/INERTIA_1_5_UPGRADE.md)
- [Inertia.Net 1.4.0 feature adoption](docs/INERTIA_1_4_UPGRADE.md)
- [Inertia.Net 1.2.0 upgrade baseline](docs/INERTIA_1_2_UPGRADE.md)
- [Inertia.Net 1.1.0 upgrade measurements](docs/INERTIA_1_1_UPGRADE.md)
- [Memory at rest and database performance research](docs/MEMORY_AND_DB_RESEARCH.md)
- [Performance optimization results](docs/PERFORMANCE_OPTIMIZATION_RESULTS.md)
- [Performance playbook adoption](docs/PERFORMANCE_PLAYBOOK_ADOPTION.md)
- [Native AOT compatibility experiment](docs/NATIVE_AOT_2026_10_22.md)
- [Replica scaling research](docs/REPLICA_SCALING_RESEARCH.md)
- [Shared Inertia cache across replicas](docs/INERTIA_CACHE_MULTI_REPLICA.md)
- [PgDog and TechLandierDistributed](docs/PGDOG_TECHLANDIER.md)
- [Replica performance comparison](docs/REPLICA_PERFORMANCE_COMPARISON.md)
- [Benchmark harness](benchmarks/README.md)
