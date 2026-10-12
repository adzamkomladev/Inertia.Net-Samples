# Acceptance record — 2026-10-12

Verified on Windows with Docker Desktop and Node 24.21.0. The original sample was left unchanged.

- `dotnet test ReactSqliteReplicasBlog.slnx`: **120 passed, 0 failed, 0 skipped**. New checks cover independent hosts sharing one cron occurrence, retained completion, annual/leap-day/second-level schedules, and a native SQLite version with the WAL-reset fix.
- `bun run build`: TypeScript, client and SSR production builds passed. `check:realtime` and `check:social` passed.
- `node scripts/check-replica-flows.mjs`: real HTTP through HAProxy to three app processes. Signup on each, verification through SQLite, signin on another, authenticated editor and antiforgery access across all three; **concurrent** creation, reading and editing posts; likes, comments, bookmarks, forbidden ownership actions, live SignalR counters across processes, deletion, verification job processing and logout all passed. Fixture accounts/posts removed afterward. The script honored the shared five-attempt auth budget, including Inertia's redirect and Retry-After.
- Scaled from three to **five healthy app processes**, all mounting the same local database volume. With `EXPECTED_REPLICAS=5`, `node scripts/check-replicas.mjs` discovered all five, checked sticky routing and Inertia rendering, negotiated SignalR long polling, and proved the **same five-attempt auth budget** across all five (five missing-CSRF probes allowed through the limiter, two rejected with Retry-After).
- Native backend SQLite is pinned through SQLitePCLRaw.lib.e_sqlite3 3.53.3; startup verifies >= 3.51.3 before WAL initialization. Bun's database client in the container reported SQLite 3.53.4.
- Resolved Compose configuration retained the source's Maileroo, S3, Meilisearch and Redis credentials, with isolated DB/cache/index/key namespaces. `.env` is ignored; shareable files contain no inherited secret values.

Synthetic mail uses the reserved `inertia.demo42.*@yopmail.com` capture path. Email jobs were processed; external Maileroo delivery was not exercised. Node is recommended for acceptance scripts: the installed Windows Bun 1.3.14 intermittently timed out in synchronous Docker subprocess queries. These are HTTP checks, not browser click automation.

This validates app replica scaling **on one Docker host**, not multi-host SQLite, database high availability or Turso. See [REPLICAS.md](REPLICAS.md) for the migration research and limits.

Both new Compose stacks were brought down after testing without deleting their data volumes.
