# .NET Inertia samples

- [React + SQLite](react-inertia-sqlite/README.md)
- [React + SQLite — multiple app replicas on one host](react-inertia-sqlite-multiple-replicas/README.md)
- [React + MongoDB](react-inertia-mongodb/README.md)
- [React + PostgreSQL](react-inertia-postgresql/README.md)
- [Svelte + SQLite](svelte-inertia-sqlite/README.md)
- [Svelte + PostgreSQL](svelte-inertia-postgresql/README.md)
- [Svelte + MongoDB](svelte-inertia-mongodb/README.md)
- [Svelte + MongoDB — multiple app replicas](svelte-inertia-mongodb-multiple-replicas/README.md)
- [Vue + SQLite](vue-inertia-sqlite/README.md)
- [Vue + PostgreSQL](vue-inertia-postgresql/README.md)
- [Vue + PostgreSQL — multiple app replicas](vue-inertia-pg-multiple-replicas/README.md)
- [Vue + PostgreSQL — advanced replicas with Aspire](vue-inertia-pg-multiple-replicas-advanced/README.md)
- [Vue + MongoDB](vue-inertia-mongodb/README.md)

Each sample has its own `.env.example`: copy it only when `.env` does not exist and follow its README. The original samples share local ports, so run one of those Compose stacks at a time. Replica samples use separate ports: Vue PostgreSQL 8180/5440/6384, Svelte MongoDB 8181/27019/6385, React SQLite 8182/6386, and advanced Vue PostgreSQL 8183/5441/6387 (Compose). The advanced sample uses Aspire for local orchestration and separates MainApp, Worker and Realtime; its Aspire database ports are dynamic. They can run alongside each other. SQLite replicas must remain on one host sharing a local volume; see its replica documentation for Turso research.

The Svelte samples are the newest ports: they keep the same .NET backends, provider setups, test suites and deployment pipeline as their Vue counterparts, with a Svelte 5 (runes) + bits-ui + Tailwind v4 frontend. See [`plans/03-svelte-port.md`](plans/03-svelte-port.md) for the port record and verification commands.

The committed [portfolio overview](vue-inertia-sqlite/docs/PORTFOLIO.md), [implementation checkpoints](vue-inertia-sqlite/docs/IMPLEMENTATION.md) and [acceptance record](vue-inertia-sqlite/docs/ACCEPTANCE.md) live in Vue SQLite. This parent directory is a local navigation workspace, not a repository.

`bin/`, `obj/`, frontend dependencies/builds, local app data and `.env` files are ignored by Git. Ignore rules do not remove files already tracked: use `git rm -r --cached <tracked-build-directory>` if a repository has previously committed build output. This preserves the local files. The existing React and Vue repositories currently track no `bin/` or `obj/` files.
