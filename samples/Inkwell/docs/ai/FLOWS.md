# Inkwell change and verification flows

Use the rows relevant to the task. Paths and commands are relative to this sample root; this workflow must work when the sample is copied on its own. Read [CONTRACT.md](../CONTRACT.md) for route/prop/business contracts and [DEPLOY.md](../DEPLOY.md) before changing workers or topology. The running code and tests establish implemented behavior; a historical optimization plan is not evidence that every proposal shipped.

| Flow | Behavior to preserve and exercise | Existing checks |
|---|---|---|
| Feed, people and social | Check latest/following/For you feeds, profile counts, repeated like toggles, self-activity and comment ownership. Preserve independent loader concurrency with a separate EF context per concurrent query. | DiscoveryLogicTests, DiscoveryPersistenceTests, SocialEndpointTests, SocialPersistenceTests, RequestAndRenderingTests; `bun run check:social` |
| Auth, settings and team | Shared auth is UI data cached per user for 3 minutes fresh / 1 minute stale. Profile, theme, verification and role writes invalidate it; authorization still checks current administrator status. Team removal preserves author/content and rejects self-removal. | AuthCacheTests, AuthTests, TeamTests |
| Notifications | Bell summary and `/notifications` agree after likes/comments and read-all; retries do not duplicate activity and self-activity creates no notification. Check reconnect and opt-in sound. | NotificationTests, RealtimeTests; `bun run check:realtime` |
| Publishing and background work | Preserve draft publication, durable enqueueing, idempotency, lease fencing, digest preferences and upload ownership. | PostsTests, DraftPublicationTests, JobWorkerTests, CronTests, AnalyticsAndUploadsTests |

All authenticated shells share `notifications: { unreadCount, items }` (latest 10); guests get null. The summary cache is per user, 5 minutes fresh / 1 minute stale, invalidated by create/read writes. Warm it before testing a write, then reload as the owner and a different user. Cached UI props must never authorize requests or private realtime delivery.

For UI changes, use playwright-cli against a disposable local app with test accounts. Exercise the affected role's success, invalid, pending and permission-denied states on desktop/mobile; inspect screenshots, overflow, keyboard/focus, and console/network errors. Notifications must refresh after queued delivery/reconnect without discarding an in-progress form or navigation. Deferred widgets need loading/empty/error handling and a successful partial reload; do not assume a missing initial prop means an empty result.

Run checks according to the changed surface:

- AI instructions/skills/roles/commands: edit canonical files, run `node scripts/ai/sync.mjs`, then `node scripts/ai/sync.mjs --check` and `node --test scripts/ai/sync.test.mjs`. Commit the generated adapters.
- Backend: run relevant tests from the table with `dotnet test tests/Inkwell.Tests --filter FullyQualifiedName~<TestClass>`; run the full project suite for changes spanning features. Use disposable database infrastructure as described in README.md.
- Frontend: from `src/Inkwell`, run `bun run typecheck`, `bun run build`, and the relevant installed `check:*` scripts. Browser evidence is separate from build evidence.
- Performance: compare the same seeded flow before/after with cold and warm caches. Include initial HTML/Inertia JSON and deferred/partial requests, database operation counts, and total time until widgets settle. A faster shell alone is not a faster completed page. Rendering microbenchmarks do not prove authenticated query savings.

Add or update a focused regression check for changed behavior. Update docs/CONTRACT.md when routes, props, deferred groups, jobs or realtime contracts change. Report actual commands/results, browser coverage, skips and remaining risks. Never seed or migrate a live database, send real email, or deploy as part of verification.
