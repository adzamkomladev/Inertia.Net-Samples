# Inertia.Net 1.1 upgrade measurements

Measured on 2026-10-13 on the same Windows x64 host (.NET SDK 10.0.401, runtime 10.0.12), in Release mode. Inertia.Net **1.0.3 → 1.1.0** and Inertia.Net.Testing **1.0.2 → 1.1.0**. Package versions were confirmed against NuGet's live flat-container registry.

The opt-in `InertiaPerformanceTests` runs the actual app through ASP.NET TestServer against an isolated, empty database. Each mode warms up with 100 GET `/posts` requests, then records five rounds of 500 sequential requests. Startup, migrations and disposal are outside the timer. HTML uses the root template with SSR explicitly disabled; JSON sends `X-Inertia` and the correct asset version; partial JSON selects `posts` for the returned component. Every timed request checks HTTP 200 and its expected HTML or JSON shape. The advanced PostgreSQL comparisons were repeated with healthy Redis. No frontend dependencies changed. Vue/Svelte SSR listener configuration now explicitly binds to `127.0.0.1` to follow the 1.1 managed-process guidance.

Tables use the median of five round means, round p95s, rates and process-wide allocation counts. Positive reduction means improvement; negative means regression. Allocation includes the app, database driver, TestServer, response reads and assertions, plus any remaining host background activity. Throughput is the reciprocal of sequential request latency, not measured concurrent capacity. Old runs precede new runs, rather than being randomized or interleaved. These short measurements have host/database noise and do not establish statistically significant package-only gains. They do not measure actual Node/Bun SSR, seeded feeds, browser rendering, remote network latency or replica scaling; use the existing seeded k6 harness for those workloads.

| Sample / repository | HTML latency reduction | JSON latency reduction | Partial latency reduction |
| --- | ---: | ---: | ---: |
| [ReactSqliteBlog](../samples/ReactSqliteBlog/docs/INERTIA_1_1_UPGRADE.md) | 0.2% | 1.3% | -7.1% |
| [ReactSqliteReplicasBlog](../samples/ReactSqliteReplicasBlog/docs/INERTIA_1_1_UPGRADE.md) | 38.6% | 44.0% | 45.6% |
| [SvelteMongoBlog](../samples/SvelteMongoBlog/docs/INERTIA_1_1_UPGRADE.md) | 27.5% | 42.8% | 20.5% |
| [SvelteMongoReplicasBlog](../samples/SvelteMongoReplicasBlog/docs/INERTIA_1_1_UPGRADE.md) | 30.7% | 33.0% | 41.0% |
| [VuePgBlog](../samples/VuePgBlog/docs/INERTIA_1_1_UPGRADE.md) | 24.5% | 31.4% | 38.0% |
| [VuePgReplicasAdvBlog](../samples/VuePgReplicasAdvBlog/docs/INERTIA_1_1_UPGRADE.md) | -16.0% | 0.6% | -0.6% |
| [react-inertia-mongodb](https://github.com/adzamkomladev/react-inertia-mongodb/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 31.1% | 20.5% | 7.2% |
| [react-inertia-postgresql](https://github.com/adzamkomladev/react-inertia-postgresql/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 27.5% | 25.0% | 27.1% |
| [react-inertia-sqlite](https://github.com/adzamkomladev/react-inertia-sqlite/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 34.4% | 27.8% | 38.9% |
| [react-inertia-sqlite-multiple-replicas](https://github.com/adzamkomladev/react-inertia-sqlite-multiple-replicas/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 33.8% | 28.0% | 17.9% |
| [svelte-inertia-mongodb](https://github.com/adzamkomladev/svelte-inertia-mongodb/blob/main/docs/INERTIA_1_1_UPGRADE.md) | -5.0% | 5.3% | 38.9% |
| [svelte-inertia-mongodb-multiple-replicas](https://github.com/adzamkomladev/svelte-inertia-mongodb-multiple-replicas/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 35.5% | -5.2% | 39.7% |
| [svelte-inertia-postgresql](https://github.com/adzamkomladev/svelte-inertia-postgresql/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 34.8% | 34.8% | 23.9% |
| [svelte-inertia-sqlite](https://github.com/adzamkomladev/svelte-inertia-sqlite/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 26.8% | 34.0% | 43.7% |
| [vue-inertia-mongodb](https://github.com/adzamkomladev/vue-inertia-mongodb/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 2.5% | 7.6% | 3.1% |
| [vue-inertia-pg-multiple-replicas](https://github.com/adzamkomladev/vue-inertia-pg-multiple-replicas/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 33.9% | 25.8% | 40.6% |
| [vue-inertia-pg-multiple-replicas-advanced](https://github.com/adzamkomladev/vue-inertia-pg-multiple-replicas-advanced/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 5.6% | 19.5% | 12.6% |
| [vue-inertia-postgresql](https://github.com/adzamkomladev/vue-inertia-postgresql/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 5.3% | -6.3% | -2.5% |
| [vue-inertia-sqlite](https://github.com/adzamkomladev/vue-inertia-sqlite/blob/main/docs/INERTIA_1_1_UPGRADE.md) | 3.2% | -2.8% | 6.3% |

Each sample or standalone repository includes `docs/INERTIA_1_1_UPGRADE.md`, the rerun command and all thirty raw measurement rounds. All nineteen integration suites and frontend builds passed; the per-sample reports record the test totals. Existing databases and volumes were not altered. Four previously local replica repositories were created as private GitHub repositories for publication.
