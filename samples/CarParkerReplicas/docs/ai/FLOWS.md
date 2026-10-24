# CarParkerReplicas change and verification flows

Use the rows relevant to the task. Paths and commands are relative to this sample root; this workflow must work when the sample is copied on its own. Read [CONTRACT.md](../CONTRACT.md) for route/prop/business contracts and [DEPLOY.md](../DEPLOY.md) before changing workers or topology. The running code and tests establish implemented behavior; a historical optimization plan is not evidence that every proposal shipped.

| Flow | Behavior to preserve and exercise | Existing checks |
|---|---|---|
| Lot discovery and booking | Preserve filters/Scroll, active-lot checks, opening hours/time zones and the 409 lost-race response. Detail and booking pages read spots once, then derive card counts and availability from those same rows; do not add a second spots query. | LotListingTests, BookingFlowTests, TeamAndHoursTests |
| Customer and guard dashboards | Keep independent Mongo lookups concurrent and await all started work. Verify customer KPIs/next visit/active session, lot-scoped guard queue and open/exited sessions after check-in/out. | AccountDashboardTests, SessionFlowTests, LotStatsTests |
| Staff, money and incidents | Preserve admin/guard boundaries, demotion without losing bookings, self-removal rejection, refund accounting, claim decisions and force-close validation. Staff pushes resolve current recipients. | TeamAndHoursTests, RefundAccountingTests, InsuranceTests, IncidentTests |
| Notifications | Bell summary, `/account/alerts` and read-all agree for the signed-in account; payout/claim retries do not duplicate entries. Check admin/customer dashboard switching, reconnect and opt-in sound. | NotificationCacheTests, NotificationTests, RealtimeTests |
| Mongo modes and jobs | Single mode has no cross-document rollback; replica mode requires a replica set. Keep atomic single-document guards and fenced/idempotent workers in both modes. | MongoModeTests, AlertSweepTests, NoShowTests; run both modes for transaction changes |

All authenticated shells share `notifications: { unreadCount, items }` (latest 10); guests get null. The summary cache is per user, 5 minutes fresh / 1 minute stale, invalidated by create/read writes. Warm it before testing a write, then reload as the owner and a different user. Cached UI props must never authorize requests or private realtime delivery.

For UI changes, use playwright-cli against a disposable local app with test accounts. Exercise the affected role's success, invalid, pending and permission-denied states on desktop/mobile; inspect screenshots, overflow, keyboard/focus, and console/network errors. Notifications must refresh after queued delivery/reconnect without discarding an in-progress form or navigation. Deferred widgets need loading/empty/error handling and a successful partial reload; do not assume a missing initial prop means an empty result.

Run checks according to the changed surface:

- AI instructions/skills/roles/commands: edit canonical files, run `node scripts/ai/sync.mjs`, then `node scripts/ai/sync.mjs --check` and `node --test scripts/ai/sync.test.mjs`. Commit the generated adapters.
- Backend: run relevant tests from the table with `dotnet test tests/CarParkerReplicas.Tests --filter FullyQualifiedName~<TestClass>`; run the full project suite for changes spanning features. Use disposable database infrastructure as described in README.md.
- Frontend: from `src/CarParkerReplicas`, run `bun run typecheck`, `bun run build`, and the relevant installed `check:*` scripts. Browser evidence is separate from build evidence.
- Performance: compare the same seeded flow before/after with cold and warm caches. Include initial HTML/Inertia JSON and deferred/partial requests, database operation counts, and total time until widgets settle. A faster shell alone is not a faster completed page. Rendering microbenchmarks do not prove authenticated query savings.

Add or update a focused regression check for changed behavior. Update docs/CONTRACT.md when routes, props, deferred groups, jobs or realtime contracts change. Report actual commands/results, browser coverage, skips and remaining risks. Never seed or migrate a live database, send real email, or deploy as part of verification.
