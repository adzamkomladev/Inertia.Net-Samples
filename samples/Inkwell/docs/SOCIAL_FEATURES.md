# Social feeds, search and demo data

## Features

- Bookmark posts, follow people, like comments and repost posts. PUT enables each relationship; DELETE removes it. Repeated requests keep the same state.
- Authors can archive and restore their posts. Archives are visible to their owner; public feeds, profiles, search and recommendations exclude them. React also keeps unpublished drafts out of public results.
- `/feed?mode=for-you` ranks posts using your likes, comments and authenticated views. `/feed?mode=following` uses the interests of people you follow. `/me/posts` contains your authored posts and reposts; `/bookmarks` and `/me/archives` collect your saved and archived posts.
- Posts accept up to five normalized topic tags. `/users` finds people; `/users/{id}` shows a profile and public timeline.

## Pagination and rendering

Chronological posts, personal timelines and comments use opaque cursors. Recommendation feeds use a thirty-minute snapshot so loading more retains the same ordering. An expired recommendation cursor returns 410; restart the feed to obtain a new snapshot.

Posts and people use measured TanStack window virtualization. Comments use measured rows in a scroll region, with fifty comments per server request and a Load older comments control. The initial SSR page renders normally; virtualization starts after mount. This bounds mounted rows while allowing the client to retain loaded data for scrolling back.

Search uses SQLite FTS5 in the database, ranked by bm25, and pages by offset. Results carry highlighted snippets; only the `<mark>` tags SQLite adds survive HTML encoding.

A hover prefetch does not mark a post as seen. The mounted detail page sends a view beacon; successful post likes and comments also persist authenticated seen state.

Use your existing S3/RustFS endpoint for uploads. Start the application and required database services separately when using external storage; no local RustFS service is needed.

## Daily recommendation email

Enable Daily recommendation digest in account preferences and choose an IANA time zone such as `Europe/London` or `Asia/Kathmandu`. The daily window is the previous calendar day in that zone, including daylight-saving transitions.

The digest selects matching posts from that window that the user has not seen. Delivery rechecks visibility, consent and seen state before sending, and records delivery state to avoid duplicate successful sends. Empty digests are skipped. Disabling consent stops future daily deliveries. The existing weekly digest preference is separate.

The following cron tasks default to every five minutes:

| Task | Override |
| --- | --- |
| Queue due daily recommendations | `CRON_RECOMMENDATION_DIGEST` |
| Refresh interest profiles | `CRON_INTEREST_PROFILES` |

Cron expressions are UTC; daily digest windows and due times use the user's selected time zone. The database-backed queue runs asynchronously in the application process; keep an application worker running for delivery.

## Seed 42

From this sample directory, after configuring its database:

```sh
dotnet run --project src/Inkwell -- --seed-demo
```

The seeder creates exactly 7,800 demo users and 50,000 original posts, including archives and drafts, plus topic tags, follows, likes, comments, comment likes, seen records, bookmarks and reposts. It preserves existing non-demo data. Five-hundred-row transactions store checkpoints so rerunning resumes safely. Identity-compatible passwords are hashed in parallel; database writes remain sequential.

If a previous seed stopped with a duplicate InterestProfiles key, deploy the latest image and rerun --seed-demo against the same database. Existing worker-created profiles are preserved; saved batches resume without deleting posts or users. Run only one seed process per database.

Demo login: `inertia.demo42.00001@yopmail.com`, password `Demo-42-correct-horse!`. Demo accounts disable mail preferences initially. Any mail to the seeded `inertia.demo42.*@yopmail.com` accounts is captured under `storage/captured-mail` (or the configured application data directory), even when an external mail provider is configured.

## Acceptance checks

```sh
cd src/Inkwell
bun install --frozen-lockfile
bun run typecheck
bun run build
```

Run the sample's .NET test project from the sample root. PostgreSQL tests require `INERTIA_TEST_CONNECTION` for a local acceptance database and create isolated temporary databases.

For virtualization, supply thousands of client rows, scroll well beyond the initial viewport and check that mounted list items remain near viewport size plus overscan; no thousands of mounted cards should remain.

Browser measurement (Chrome, 1,100 Ã— 800 viewport, synthetic 5,000 posts): React SQLite mounted 11 rows at the top and 16 after scrolling 250,000 pixels; Vue SQLite mounted 11 and 16 respectively. Both checks reported no browser errors. PostgreSQL uses the same corresponding virtual-list component source. This measures mounted DOM size, not end-to-end feed latency.

## Draft publication

The post form can save a private draft by clearing **Publish this post**. My posts includes the author’s drafts; public feeds, search, recommendations and digests exclude them. Publishing a draft requires a verified account and sets its first publication time. Omitting `isPublished` when editing preserves its current state. Published posts cannot become drafts again: use Archive and restore to hide/show them while preserving their publication date.

## Seed follow-up jobs

After saving seed checkpoints, `--seed-demo` queues an initial coalesced batch of up to 500 interest-profile refreshes. The command exits without starting workers or sending email. Start the application to process the queue; the profile scheduler continues subsequent batches. Repeated seed runs preserve data and reuse pending work.
