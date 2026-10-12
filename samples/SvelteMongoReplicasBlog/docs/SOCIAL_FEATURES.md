# Social feeds, search and demo data

## Features

- Bookmark posts, follow people, like comments and repost posts. PUT enables each relationship; DELETE removes it. Repeated requests keep the same state.
- Authors can archive and restore their posts. Archives are visible to their owner; public feeds, profiles, search and recommendations exclude them. React also keeps unpublished drafts out of public results.
- `/feed?mode=for-you` ranks posts using your likes, comments and authenticated views. `/feed?mode=following` uses the interests of people you follow. `/me/posts` contains your authored posts and reposts; `/bookmarks` and `/me/archives` collect your saved and archived posts.
- Posts accept up to five normalized topic tags. `/users` finds people; `/users/{id}` shows a profile and public timeline.

## Pagination and rendering

Chronological posts, personal timelines and comments use opaque cursors. Recommendation feeds use a thirty-minute snapshot so loading more retains the same ordering. An expired recommendation cursor returns 410; restart the feed to obtain a new snapshot.

Posts and people use measured TanStack window virtualization. Comments use measured rows in a scroll region, with fifty comments per server request and a Load older comments control. The initial SSR page renders normally; virtualization starts after mount. This bounds mounted rows while allowing the client to retain loaded data for scrolling back.

Search results carry `searchEngine` (`native` or `meilisearch`) on initial and partial responses. Pagination sends that engine back. If indexing status or service availability changes the engine, the server resets to page one and the client discards merged rankings and fetches a fresh first page. Results from different ranking engines are never intentionally concatenated.

A hover prefetch does not mark a post as seen. The mounted detail page sends a view beacon; successful post likes and comments also persist authenticated seen state.

## Optional external services

Configure the sample's ignored `.env` using existing service endpoints:

```dotenv
SEARCH_DRIVER=meilisearch
MEILISEARCH_URL=https://search.example.com
MEILISEARCH_API_KEY=replace-with-a-private-key
REDIS_URL=rediss://username:password@cache.example.com:6379/0
```

Use `SEARCH_DRIVER=native` or leave Meilisearch unset to use the database search implementation. Meilisearch is used only when its durable index state is caught up; indexing failures and unavailable services fall back to database search. Public visibility is checked again when external document IDs are hydrated.

Post creation, editing, archival and deletion record indexing intent in the database transaction. Background jobs submit upserts/deletions, poll Meilisearch task completion and retry failed work. Reconciliation recovers pending indexing work after restarts. Deleted posts retain an indexing tombstone until the external document is removed.

Redis optionally caches reconstructible recommendation snapshot data. Database state remains authoritative and cache failures fall back to database reads. Keys start with `SvelteMongoReplicasBlog:<environment>:`; Meilisearch index names also include this exact sample folder and environment, isolating samples on shared external services.

Use your existing S3/RustFS endpoint for uploads and backups. Start the application and required database services separately when using external storage; no local RustFS service is needed.

## Opt-in local search/cache containers

Normal Compose uses the externally configured service URLs. To explicitly start only local search or cache:

```sh
docker compose --profile search up -d meilisearch
docker compose --profile cache up -d redis
```

The normal Compose search profile publishes port 7700 (override MEILISEARCH_PORT) and takes MEILISEARCH_API_KEY as its master key. Redis is reachable within that Compose network. For a host-run development app, use docker-compose.dev.yml; its Redis service publishes port 6380 by default (override REDIS_PORT). These profiles do not change application URLs automatically: set MEILISEARCH_URL and REDIS_URL to the selected services. Container applications use http://meilisearch:7700 and redis://redis:6379/0; host-run development uses http://127.0.0.1:7700 and redis://127.0.0.1:6380/0.

External S3/RustFS remains configured separately; these optional profiles start no local storage service.

## Daily recommendation email

Enable Daily recommendation digest in account preferences and choose an IANA time zone such as `Europe/London` or `Asia/Kathmandu`. The daily window is the previous calendar day in that zone, including daylight-saving transitions.

The digest selects matching posts from that window that the user has not seen. Delivery rechecks visibility, consent and seen state before sending, and records delivery state to avoid duplicate successful sends. Empty digests are skipped. Disabling consent stops future daily deliveries. The existing weekly digest preference is separate.

The following cron tasks default to every five minutes:

| Task | Override |
| --- | --- |
| Queue due daily recommendations | `Cron__daily-recommendation-digest` |
| Reconcile search documents | `Cron__search-index-reconcile` |
| Refresh interest profiles | `Cron__refresh-interest-profiles` |

Cron expressions are UTC; daily digest windows and due times use the user's selected time zone. Set the listed hierarchical overrides in the deployment environment, or use the matching Cron property names in appsettings.json. The database-backed queue runs asynchronously in the application process; keep an application worker running for indexing and delivery.

## Seed 42

From this sample directory, after configuring its database:

```sh
dotnet run --project src/SvelteMongoReplicasBlog -- --seed-demo
```

The seeder creates exactly 7,800 demo users and 50,000 original posts, including archives, plus topic tags, follows, likes, comments, comment likes, seen records, bookmarks and reposts. It preserves existing non-demo data. Five-hundred-document transactions store checkpoints so rerunning resumes safely. Profile and search-state batches use insert-only upserts, preserving live profiles and pending/applied search task state. If a seed process stops, rerun `--seed-demo` using the latest image against the same database; checkpoints resume the work. Run only one seed process per database at a time. Application-compatible passwords are hashed in parallel; database writes remain sequential.

Demo login: `inertia.demo42.00001@yopmail.com`, password `Demo-42-correct-horse!`. Demo accounts disable mail preferences initially. Any mail to the seeded `inertia.demo42.*@yopmail.com` accounts is captured under `storage/captured-mail` (or the configured application data directory), even when an external mail provider is configured.

## Acceptance checks

```sh
cd src/SvelteMongoReplicasBlog
bun install --frozen-lockfile
bun run typecheck
bun run build
```

Run the sample's .NET test project against a disposable MongoDB replica set. Tests use generated database names and remove only their owned test databases.

For pagination resilience, search for a term with multiple pages and load another page while switching Meilisearch availability or index readiness. The list should restart with one ranking order and report the new engine. For virtualization, supply thousands of client rows, scroll well beyond the initial viewport and check that mounted list items remain near viewport size plus overscan; no thousands of mounted cards should remain.

Browser measurement (Chrome, 1,100 × 800 viewport, synthetic 5,000 posts): React SQLite mounted 11 rows at the top and 16 after scrolling 250,000 pixels; the Vue predecessor of this Svelte sample mounted 11 and 16 respectively. Both checks reported no browser errors. PostgreSQL uses the same corresponding virtual-list component source. This measures mounted DOM size, not end-to-end feed latency.

### Independent service outage checks

These are separate running-service checks, not claimed as passed by the virtual-list measurement:

1. With Meilisearch caught up, search and load multiple pages. Make only the search endpoint unavailable. Database search must still succeed; pagination must restart at page one without mixed ranks. Restore search, wait for indexing reconciliation and repeat the transition back to Meilisearch.
2. With Redis caching enabled, open a recommendation feed. Make only Redis unavailable. Database-backed feed snapshots must remain usable, and engagement writes must still persist. Restore cache and check that its keys retain the sample/environment prefix.
3. Keep both optional services unavailable and verify creation, edits, archival/restoration and deletion. Restore search and confirm that queued indexing eventually reflects the final visibility/content state.

Running-service outage results should be recorded in the coordinator's acceptance report. The added automated search-engine test covers native fallback pagination and the engine prop on partial responses; it does not substitute for shutting down an actual service.

## Seed follow-up jobs

After saving seed checkpoints, `--seed-demo` queues an initial coalesced batch of up to 500 interest-profile refreshes. When Meilisearch is enabled and configured, it also queues a resumable full reindex. The command exits without starting workers or sending email. Start the application to process the queue; the profile scheduler continues subsequent batches. Repeated seed runs preserve data and reuse pending work.

To explicitly rebuild the optional search index later, run the same project with `--reindex-search`, then start the application. This command records durable work and exits; it does not require an immediately available search server.

## Native MongoDB persistence and backups

This sample uses the official MongoDB .NET driver directly, with startup-created collection indexes and Mongo transactions. A replica set is required, including in local development; standalone MongoDB does not support the multi-document transactions used for engagement, queues and indexing intent. There are no EF migrations. Use a separate database per sample; DATABASE_URL identifies the replica set and DATABASE_NAME selects the application database.

Native search uses MongoDB text indexes. Timeline, author, engagement, unique relationship, lease and recommendation indexes support the cursor and background operations. Configure optional Meilisearch only when desired; its document IDs are hydrated through MongoDB with fresh visibility checks.

The daily 03:00 UTC backup job runs mongodump and uploads compressed archives under SvelteMongoReplicasBlog/<environment>/db/, retaining the latest 14 archives in that namespace. A restricted temporary configuration file carries the database URI instead of exposing it in process arguments. MongoDB Database Tools must be installed on a host deployment; the runtime image includes them. Back up S3 upload objects separately. Rehearse restoration into a disposable database or replica set before recovery.
