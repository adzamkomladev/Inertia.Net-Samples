# TechLandier change and verification flows

Use the rows relevant to the task. Paths and commands are relative to this sample root; this workflow must work when the sample is copied on its own. Read [CONTRACT.md](../CONTRACT.md) for route/prop/business contracts and [DEPLOY.md](../DEPLOY.md) before changing workers or topology. The running code and tests establish implemented behavior; a historical optimization plan is not evidence that every proposal shipped.

| Flow | Behavior to preserve and exercise | Existing checks |
|---|---|---|
| Account dashboard | Initial props keep `stats` and `recentOrders`; `nextInstallment`, `openDeliveries` and `unreadAlerts` arrive in groups `installment`, `deliveries` and `alerts`. Concurrent EF loaders need separate factory-created contexts. | DeferredPropsTests, FeatureTests |
| Product and checkout | `canReview` loads in group `reviews`; `creditState` loads in group `credit` on the buy page. Pending credit is not eligibility approval. Preserve purchase validation, stock concurrency and checkout attempt idempotency. | DeferredPropsTests, FeatureTests, AppTests |
| Operations and care | Follow order/delivery transitions, installment payments, warranty/insurance/repair/support details, store/delivery hours and team demotion. Public registration cannot grant admin; demotion preserves orders and removes private push access. | ManagementTests, FeatureTests, AppTests |
| Notifications | Bell summary, paginated `/account/alerts`, individual read and read-all agree for the signed-in account after customer and admin events. Check reconnect and opt-in sound. | NotificationCacheTests, NotificationTests |
| Analytics and invalidation | Preserve deferred groups, range/category cache keys and sales/catalog tag invalidation. Warm the cache before mutations to verify updated values, including partial reloads. A write through MainApp must invalidate the shared store seen by Worker. | CacheInvalidationTests, PropCacheInvalidatorTests, RedisCacheStoreTests, FeatureTests |
| Distributed roles | MainApp renders and enqueues; Worker consumes and runs cron; Realtime maps only /realtime, /health and /alive. Low-stock gating and cache invalidation are shared through Redis. PgDog pins writes/auth to the primary and selected public reads to replicas. | RoleHostTests, StockAlertGateTests, SsrConfigurationTests, PgDogReadTests |

All authenticated shells share `notifications: { unreadCount, items }` (latest 10); guests get null. The summary cache is per user, 5 minutes fresh / 1 minute stale, invalidated by create/read writes. Warm it before testing a write, then reload as the owner and a different user. Cached UI props must never authorize requests or private realtime delivery.

For UI changes, use playwright-cli against a disposable local app with test accounts. Exercise the affected role's success, invalid, pending and permission-denied states on desktop/mobile; inspect screenshots, overflow, keyboard/focus, and console/network errors. Notifications must refresh after queued delivery/reconnect without discarding an in-progress form or navigation. Deferred widgets need loading/empty/error handling and a successful partial reload; do not assume a missing initial prop means an empty result.

Run checks according to the changed surface:

- AI instructions/skills/roles/commands: edit canonical files, run `node scripts/ai/sync.mjs`, then `node scripts/ai/sync.mjs --check` and `node --test scripts/ai/sync.test.mjs`. Commit the generated adapters.
- Backend: run relevant tests from the table with `dotnet test tests/TechLandierDistributed.Tests --filter FullyQualifiedName~<TestClass>`; run the full project suite for changes spanning features. Use disposable database infrastructure as described in README.md; set REDIS_URL for cache/tag/stock-gate tests, otherwise they skip.
- Frontend: from `apps/MainApp`, run `bun run typecheck`, `bun run build`, and the relevant installed `check:*` scripts. Browser evidence is separate from build evidence.
- Performance: compare the same seeded flow before/after with cold and warm caches. Include initial HTML/Inertia JSON and deferred/partial requests, database operation counts, and total time until widgets settle. A faster shell alone is not a faster completed page. Rendering microbenchmarks do not prove authenticated query savings.

Add or update a focused regression check for changed behavior. Update docs/CONTRACT.md when routes, props, deferred groups, jobs or realtime contracts change. Report actual commands/results, browser coverage, skips and remaining risks. Never seed or migrate a live database, send real email, or deploy as part of verification.
