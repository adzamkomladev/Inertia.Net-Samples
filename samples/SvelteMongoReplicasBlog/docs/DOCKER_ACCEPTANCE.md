# Acceptance record — 2026-10-12

Verified on Windows with Docker Desktop and Node 24.21.0. The original sample was left unchanged.

- `dotnet test SvelteMongoReplicasBlog.slnx`: **49 passed, 0 failed, 0 skipped**, with `INERTIA_TEST_CONNECTION=mongodb://localhost:27019/?replicaSet=rs0&directConnection=true` and built frontend assets.
- `bun run build`: Svelte check **0 errors, 0 warnings**; client and SSR production builds passed. `check:realtime` and `check:social` passed.
- `node scripts/check-replica-flows.mjs`: real HTTP through HAProxy to three app processes. Signup on each, verification through MongoDB, signin on another, authenticated editor and antiforgery access across all three; **concurrent** creation, reading and editing posts; likes, comments, bookmarks, forbidden ownership actions, live SignalR counters across processes, deletion, verification job processing and logout all passed. Fixture accounts/posts removed afterward.
- Scaled from three to **five healthy app processes**. With `EXPECTED_REPLICAS=5`, `node scripts/check-replicas.mjs` discovered all five, checked sticky routing and Inertia rendering, negotiated SignalR long polling, and proved the **same ten-attempt auth budget** across all five (ten missing-CSRF probes allowed through the limiter, two rejected with Retry-After).
- Resolved Compose configuration retained the source's Maileroo, S3, Meilisearch and Redis credentials, with isolated DB/cache/index/key namespaces. `.env` is ignored; shareable files contain no inherited secret values.

Synthetic mail uses the reserved `inertia.demo42.*@yopmail.com` capture path. Email jobs were processed; external Maileroo delivery was not exercised. These are HTTP acceptance checks, not browser click automation. Production multi-host infrastructure and database failover were not tested; the bundled Mongo replica set has one member.

Both new Compose stacks were brought down after testing without deleting their data volumes.
