# Six-sample implementation checkpoints

All SQLite/PostgreSQL samples use EF Core. MongoDB samples use the official MongoDB.Driver directly. Applications pin Inertia.Net 1.0.3; tests pin Inertia.Net.Testing 1.0.2.

## Ownership

| Agent | Model / effort | Work |
| --- | --- | --- |
| Coordinator | Current chat settings | Contracts, integration, verification, commits and pushes |
| structure | GPT-6.1 Sol / low | Structure, frontends, virtualization, SSR and documentation |
| databases | GPT-6 Luna / high | Providers, persistence, indexes, migrations, seeding and integration tests |
| social_features | GPT-6 Luna / xhigh | Recommendations, seen tracking, digest, indexing, Redis and concurrency |

Only the coordinator commits and pushes. Agents have exclusive file ownership.

## Required checkpoints

- [x] A: Vue SQLite source layout and existing-data preservation
- [ ] B: Inertia.Net 1.0.3 and Testing 1.0.2 in all six samples
- [ ] C: Vue PostgreSQL and MongoDB provider baselines
- [ ] D1: Topics, discovery and following
- [ ] D2: Bookmarks
- [ ] D3: Comment likes
- [ ] D4: Private archive and restore
- [ ] D5: Reposts and authored timelines
- [ ] E: Cursor pagination and actual DOM virtualization
- [ ] F1: Optional Meilisearch with transactional queued indexing
- [ ] F2: Optional Redis with database fallback
- [ ] G1: Durable authenticated seen records
- [ ] G2: Topic/author recommendations and stable feed snapshots
- [ ] H: Opt-in local-day daily digest with safe delivery records
- [ ] I: Resumable seed (7,800 users; 50,000 original posts per sample)
- [ ] Final: Six-sample acceptance, commits, pushes and remote-SHA verification

## Verification policy

Each meaningful working checkpoint is committed after relevant checks pass. Final acceptance includes warning-free .NET builds/tests, frontend typechecks/builds, SSR/HMR, Compose/Docker, browser checks, provider-backed concurrency tests, optional-service outages and large-data measurements. Secrets, database files, dependency/build output and captured messages are never committed.

Seed 42 uses unique @yopmail.com demo emails, batches of 500, stable keys and resumable checkpoints. Demo mail is captured locally even when a real email API key exists. Seeding never resets existing databases or overwrites real users.

Digest matches must be public, unseen, first published during the recipient's previous local calendar day, and have personal score >= 0.05. Send at 08:00 local time, at most 10 posts, only for verified opted-in real accounts. Ambiguous provider outcomes require reconciliation rather than blind retries.

## Progress

A: Vue structure gates passed: 0-warning build, 14 integration tests, typecheck, client/SSR build, development SSR, Compose configuration and Docker image build. React SQLite package upgrade passed 77 tests; React MongoDB passed 59 tests. Remaining checkpoints are in progress.
