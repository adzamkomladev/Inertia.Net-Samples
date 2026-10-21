# TechLandier

An electronics storefront built with .NET 10 Minimal APIs, Inertia.Net 1.4 and Vue 3, with server-side rendering. Gadgets today, paid over time: the sample showcases a full commerce domain — catalog with inventory, checkout with card / mobile-money / pay-on-delivery, **buy-now-pay-later installment plans**, warranties, device insurance, repairs, reviews, deals, rider deliveries and complaints — with an admin dashboard and a customer dashboard.

## The domain in one pass

- **Catalog & inventory.** Products (`draft | active | archived`) with category, brand, specs JSON, compare-at price and a percent discount resolved by one `Pricing.Display` function; stock is guarded by conditional updates at checkout and every change writes a `StockMovement` (stock always equals the sum of movements). Low stock raises an admin `StockAlert` over SignalR.
- **Orders & payments.** Checkout is idempotent per `CheckoutAttemptId` (unique index + one transaction), snapshots the address and item prices, and walks a pure state machine `pending → paid → processing → shipped → delivered` (+ `cancelled`) writing an `OrderEvent` timeline row per hop. Payment methods: `card`, `mobile_money`, `pay_on_delivery` (cash reconciled on the delivery board); a simulated gateway settles in dev.
- **BNPL.** `PayPlan` (3/6/12 installments, 14 days apart, 5% fee baked in) requires a verified customer, at most two active plans and no overdue arrears. Crons: due-soon reminders (daily 07:00), overdue marking + financing pause (07:10), day-14 default escalation, and a Monday weekly statement email.
- **Care.** Warranties are auto-created per order item with `WarrantyMonths > 0` (serial editable, claims only inside the window); device insurance covers insurable products for 12 months (premium 15%, payout 90% of price); repair tickets run `received → quoted → repairing → ready → picked_up → closed` with a flat status history and quoted cost. Every decision notifies the customer (SignalR + email job).
- **Reviews.** Verified-purchase only (one per user per product, `OrderItemId` gate), aggregates maintained on the product row; admins can hide/unhide.
- **Deliveries & riders.** Delivery zones carry fees and ETA text (pay-on-delivery per zone); riders (`in_house | yango | bolt | uber | other`) are assigned on the admin board through `unassigned → assigned → picked_up → delivered` (+ `failed` with reason).
- **Complaints.** Public guest form + account tickets with SLA due dates by priority, staff replies (which re-open resolved tickets) and an hourly escalation cron.
- **Analytics.** Admin overview and `/admin/analytics` build KPIs/series from `SalesDailyStat` rollups; the deferred props are cached with Inertia.Net 1.4's `.Cache()` (60 s TTL, per-range keys).

## Local development

Prerequisites: .NET 10 SDK, [Bun](https://bun.sh), Docker, and an external S3-compatible object store (optional — product cards render deterministic gradient art when no image is set; admin uploads replace the art).

1. Copy `.env.example` to `.env` and fill in credentials. The `.env` file is ignored by Git.
2. Start Postgres: `docker compose -f docker-compose.dev.yml up -d postgres` (host port 5433; the app defaults to `Host=localhost;Port=5432` — set `DATABASE_URL`).
3. Optional services: `docker compose -f docker-compose.dev.yml --profile cache --profile search up -d` starts Redis (6379) and Meilisearch (7700); set `REDIS_URL` and `SEARCH_DRIVER=meilisearch` + `MEILISEARCH_URL` in `.env`. Without them the app runs on the in-memory cache and native Postgres full-text search.
4. Install frontend dependencies: `bun install --cwd src/TechLandier`.
5. Start Vite: `bun run --cwd src/TechLandier dev`.
6. Start the app: `dotnet watch --project src/TechLandier --urls http://localhost:5199`.

Migrations apply at startup (`DATABASE_URL` from `.env`, Postgres 18). In Development, emails are logged when no Maileroo key is configured.

### Demo accounts

Seed with `dotnet run --project src/TechLandier -- --seed-demo` (database ready first). The deterministic (Bogus seed 42), resumable seeder creates 10 categories, 120 products with specs/deals, 400 orders across every status with payments and timelines, 60 BNPL plans (including one overdue story), warranties + claims, device insurances + claims, repair tickets, verified reviews, tickets with staff replies, rider deliveries, alerts and 30 days of sales rollups. Demo logins (password `TechLandier-42-demo!`):

| Role | Email |
|---|---|
| Admin | `admin@techlandier.test` |
| Customer | `customer@techlandier.test` (plus `customer2@`, `customer3@`) |

Demo mail is captured locally and never sent externally.

## Production container

`docker compose up --build` starts the app and Postgres 18. It exposes the app on port 8080 and Postgres on 5433. Configure `APP_BASE_URL`, S3-compatible storage, email, and other secrets through `.env` or deployment environment variables. Mount persistent storage for `/data` so Data Protection keys survive redeploys.

## Backup and restore

The app runs a daily 02:00 UTC `pg_dump` job and stores compressed archives in the configured private `S3_BACKUP_BUCKET` under `TechLandier/<environment>/`. It keeps the 14 newest archives per environment. For a manual local backup, use `pg_dump "$DATABASE_URL" --archive=./backup.archive` (custom format) and restore with `pg_restore --clean --dbname "$DATABASE_URL" ./backup.archive`. Validate restores in a disposable database before using them for recovery.

## Checks

```bash
bun run --cwd src/TechLandier build
dotnet build TechLandier.slnx -warnaserror
dotnet test TechLandier.slnx
docker compose config --quiet
docker compose -f docker-compose.dev.yml config --quiet
```

The integration tests create a unique Postgres database per fixture and drop only that generated test database when the fixture is disposed. They target `Host=localhost;Port=55432;Username=inertiablog;Password=local-dev-password` by default; set `INERTIA_TEST_CONNECTION` to a disposable Postgres before running them.

## Sync all products to search

For Meilisearch, set `SEARCH_DRIVER=meilisearch`, `MEILISEARCH_URL`, and `MEILISEARCH_API_KEY` in `.env`, then run from this sample directory:

```sh
dotnet run --project src/TechLandier -- --reindex-search
```

This queues a full sync in batches and exits. Start the application normally afterward to let its background job worker process the batches. Repeated requests are coalesced while a full sync is active. Use the same database, environment, and Meilisearch configuration for both commands.

Product mutations queue coalesced, durable `SyncProduct`/`SyncProductsBatch` jobs for Meilisearch indexing when enabled; drafts and archived products are excluded. Native Postgres search (ILIKE over name/brand/summary/keywords) is maintained by the database and needs no external sync command.

## Live updates

SignalR at `/realtime` (the `StoreHub`) pushes `OrderChanged {orderNumber, status, paymentStatus}` and `Notification` to the `admins` group, `OrderTimelineUpdated {orderNumber}` to the owning customer whenever their order, delivery, plan, care item or ticket changes, and `StockAlert {productId, stock}` when stock crosses its threshold. The client refreshes authorized Inertia partial reloads on these events, so dashboards stay live without polling.

The hub accepts same-origin browser connections and has no client mutation methods; admins join the `admins` group automatically from their role claim. Events are transient and served by one application instance; add a SignalR backplane for multiple instances (the alerts inbox covers offline delivery).

### Coolify deployment

Set the application port to `8080` and assign its public HTTPS domain in Coolify. TLS terminates at the proxy; the container serves HTTP (compression is enabled for HTTPS). Keep the public Host and forwarded protocol intact, and trust only your proxy's network if its address falls outside the configured private networks.

## Feature files and scheduled work

Each feature keeps route registration in its routing file; the route file's static handler methods compose the Inertia response (`Render`, `Back`, flash, redirects). `<Feature>Service.cs` contains the domain logic — queries, mutations, state machines, job staging and realtime signals — and returns plain data plus outcome records, never `IResult`.

| Feature | Routes | Service | Models / shared |
|---|---|---|---|
| Store (home, catalog, product, deals, static pages) | Store/Store.cs | StoreService.cs | StoreModels.cs |
| Orders + payments + timeline | Orders/Orders.cs, Orders/Checkout.cs | OrderService.cs / CheckoutService.cs / OrderWorkflow.cs / OrderNumbers.cs | OrderModels.cs |
| Credit (BNPL plans + installments) | Credit/Credit.cs | CreditService.cs | CreditModels.cs |
| Care (warranty, insurance, repairs) | Care/Care.cs | CareService.cs | CareModels.cs |
| Reviews | Reviews/Reviews.cs | ReviewsService.cs | ReviewModels.cs |
| Deliveries (board, riders, zones) | Deliveries/Deliveries.cs | DeliveriesService.cs | DeliveryModels.cs |
| Support (complaints) | Support/Support.cs | SupportService.cs | SupportModels.cs |
| Notifications (alerts inbox) | Notifications/Alerts.cs | AlertsService.cs | AlertModels.cs |
| Account (overview, profile, preferences) | Account/Account.cs | AccountService.cs | AuthModels.cs |
| Admin (overview, catalog, categories) | Admin/Admin.cs | AdminService.cs | CatalogModels.cs |
| Analytics (sales rollups + report) | Admin/Admin.cs | SalesService.cs | SalesModels.cs |
| Auth | Auth/Auth.cs | AuthService.cs / AuthHelpers.cs | AuthModels.cs |
| Uploads | Uploads/Uploads.cs | UploadsService.cs / UploadsHelpers.cs | UploadsModels.cs |

Services never compose Inertia responses: handlers map the shared `Outcome`/`SaveResult` records (see `Features/SaveResult.cs`) onto `Render`/`Back`/redirects. Authorization, rate limits and Inertia validation stay on the route registrations.

`Cron/CronSetup.cs` registers recurring functions with TickerQ and applies `CRON_*` / `Cron__<name>` overrides. Set `CRON_ENABLED=false` to disable scheduled work. Expressions are validated at startup, and execution is limited to one job at a time per process. `Cron/CronJobs.cs` retains the database lease guard: overlapping replicas cannot claim the same occurrence while the lease is held. The schedule: `sales-rollup` */15 min · `bnpl-reminders` 07:00 daily · `bnpl-overdue` 07:10 daily · `bnpl-statement` Mon 08:00 · `ticket-escalation` hourly · `order-timeout` hourly (cancels stale unpaid orders, releases stock) · `backup` 02:00 (pg_dump to S3) · `prune-product-views` 03:00 · `orphan-assets` hourly · `prune-unverified` 04:00 · `prune-jobs` 03:30 · `search-reconcile` */5 min.

Durable jobs (`Jobs/JobQueue.cs`) stage in the same transaction as the business write and retry with backoff: `SendEmail`, `WelcomeEmail`, `OrderPaidEmail`, `OrderShippedEmail`, `OrderCancelledEmail`, `OrderRefundEmail`, `InstallmentPaidEmail`, `InstallmentReminderEmail` (reminder | overdue | statement), `TicketReplyEmail`, `CareDecisionEmail` (warranty-claim | insurance-claim | repair), `SyncProduct`/`SyncProductsBatch`/`ReindexSearchBatch`, `DeleteAssets`.

### Meilisearch index

Set `MEILISEARCH_INDEX` in `.env` or deployment environment variables to the exact products index UID, for example `my-store-production-products`. Search requests, document writes/deletes and full reindexing all resolve this same value. If unset or blank, the default remains `<sample>-<environment>-products` (environment is lowercase). Only ASCII letters, numbers, hyphens and underscores are accepted.
