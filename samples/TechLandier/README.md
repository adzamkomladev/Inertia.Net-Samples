# TechLandier

An electronics storefront built with .NET 10 Minimal APIs, Inertia.Net 1.5.1 and Vue 3, with server-side rendering. Gadgets today, paid over time: the sample showcases a full commerce domain — catalog with inventory, checkout with card / mobile-money / pay-on-delivery, **buy-now-pay-later installment plans**, warranties, device insurance, repairs, reviews, deals, rider deliveries and complaints — with an admin dashboard and a customer dashboard.

## Live demo

For product creation, order/installment/care details, drivers, regions, store hours, team management and sound,
see the [user and admin guide](docs/USER_GUIDE.md).

Try [TechLandier](https://techlandier.yebi.africa/) with these accounts:

- Admin: `inertia.demo42.techlandier.admin@yopmail.com`
- Customer: `inertia.demo42.techlandier.customer@yopmail.com`
- Password for both: `Password@12345`

## The domain in one pass

- **Catalog & inventory.** Products (`draft | active | archived`) with category, brand, specs JSON, compare-at price and a percent discount resolved by one `Pricing.Display` function; stock is guarded by conditional updates at checkout and every change writes a `StockMovement` (stock always equals the sum of movements). Low stock raises an admin `StockAlert` over SignalR.
- **Orders & payments.** Checkout is idempotent per `CheckoutAttemptId` (unique index + one transaction), snapshots the address and item prices, and walks a pure state machine `pending → paid → processing → shipped → delivered` (+ `cancelled`) writing an `OrderEvent` timeline row per hop. Payment methods: `card`, `mobile_money`, `pay_on_delivery` (cash reconciled on the delivery board); a simulated gateway settles in dev.
- **BNPL.** `PayPlan` (3/6/12 installments, 14 days apart, 5% fee baked in) requires a verified customer, at most two active plans and no overdue arrears. Crons: due-soon reminders (daily 07:00), overdue marking + financing pause (07:10), day-14 default escalation, and a Monday weekly statement email.
- **Care.** Warranties are auto-created per order item with `WarrantyMonths > 0` (serial editable, claims only inside the window); device insurance covers insurable products for 12 months (premium 15%, payout 90% of price); repair tickets run `received → quoted → repairing → ready → picked_up → closed` with a flat status history and quoted cost. Every decision notifies the customer (SignalR + email job).
- **Reviews.** Verified-purchase only (one per user per product, `OrderItemId` gate), aggregates maintained on the product row; admins can hide/unhide.
- **Deliveries & riders.** Delivery zones carry fees and ETA text (pay-on-delivery per zone); riders (`in_house | yango | bolt | uber | other`) are assigned on the admin board through `unassigned → assigned → picked_up → delivered` (+ `failed` with reason).
- **Complaints.** Public guest form + account tickets with SLA due dates by priority, staff replies (which re-open resolved tickets) and an hourly escalation cron.
- **Analytics.** Admin overview and `/admin/analytics` build KPIs/series from `SalesDailyStat` rollups; the deferred props load in parallel (each on its own `DbContext` from `IDbContextFactory`) and are cached with Inertia.Net 1.5.1's `.Cache()` (60 s TTL, 30 s stale, per-range keys and `sales`/`catalog` tags).

## How it uses Inertia.Net 1.5.1

| Feature | Where |
|---|---|
| `.Parallel()` | Home rails, admin overview and analytics, customer dashboard, order pages. Each loader opens its own `DbContext` from `IDbContextFactory<AppDb>`, since a context is not thread-safe |
| `.Cache(key, ttl, staleFor, tags)` | Categories, rails, KPIs, related products; keys carry the range or product id, and catalog/sales tags connect them to the mutations that invalidate them |
| `IInertiaCache` invalidation | Product/category edits, restocks, checkout, order transitions and installment payments remove only the affected tags |
| `Defer` with groups | Product reviews and related products; admin KPIs and charts |
| `Scroll` | Catalog and list pages (`<InfiniteScroll>`) |
| `MapInertia` | `/about` |
| Precognition | Registration |
| SSR + SSR cache with `StaleFor`, Vite prefetch, lazy XSRF cookie refresh, DevTools | `InertiaSetup.cs` |

## Local development

Prerequisites: .NET 10 SDK, [Bun](https://bun.sh), Docker, and an external S3-compatible object store (optional — product cards render deterministic gradient art when no image is set; admin uploads replace the art).

1. Copy `.env.example` to `.env` and fill in credentials. The `.env` file is ignored by Git.
2. Start Postgres: `docker compose -f docker-compose.dev.yml up -d postgres` (host port 5433; the app defaults to `Host=localhost;Port=5432` — set `DATABASE_URL`).
3. Install frontend dependencies: `bun install --cwd src/TechLandier`.
4. Start Vite: `bun run --cwd src/TechLandier dev`.
5. Start the app: `dotnet watch --project src/TechLandier --urls http://localhost:5199`.

Migrations apply at startup (`DATABASE_URL` from `.env`, Postgres 18). In Development, emails are logged when no Maileroo key is configured.

### Demo accounts

Seed with `dotnet run --project src/TechLandier -- --seed-demo` (database ready first). The deterministic (Bogus seed 42), resumable seeder creates 10 categories, 120 products with specs/deals, 400 orders across every status with payments and timelines, 60 BNPL plans (including one overdue story), warranties + claims, device insurances + claims, repair tickets, verified reviews, tickets with staff replies, rider deliveries, alerts and 30 days of sales rollups. Demo logins (password `Password@12345`):

| Role | Email |
|---|---|
| Admin | `inertia.demo42.techlandier.admin@yopmail.com` |
| Customer | `inertia.demo42.techlandier.customer@yopmail.com` (plus `customer2@`, `customer3@`) |

Demo mail is captured locally and never sent externally.

## Deploy

Compose or the image alone against external PostgreSQL (for example Neon), on a VPS, Coolify or Dokploy:
see [docs/DEPLOY.md](docs/DEPLOY.md).

## Checks

```bash
bun run --cwd src/TechLandier build
dotnet build TechLandier.slnx -warnaserror
dotnet test TechLandier.slnx
```

The integration tests create a unique Postgres database per fixture and drop only that generated test database when the fixture is disposed. They target `Host=localhost;Port=55432;Username=inertiablog;Password=local-dev-password` by default; set `INERTIA_TEST_CONNECTION` to a disposable Postgres before running them.

## Search

Product search runs in PostgreSQL only: a generated, GIN-indexed `tsvector` over name, brand, keywords and summary (`websearch_to_tsquery`, ranked by `ts_rank`), plus `pg_trgm` on the name for substrings and typos. The `PostgresSearch` migration creates the extension, the column and both indexes; there is nothing to sync or reindex.

## Live updates

SignalR at `/realtime` (the `StoreHub`) pushes `OrderChanged {orderNumber, status, paymentStatus}` and `Notification` to the `admins` group, `OrderTimelineUpdated {orderNumber}` to the owning customer whenever their order, delivery, plan, care item or ticket changes, and `StockAlert {productId, stock}` when stock crosses its threshold. The client refreshes authorized Inertia partial reloads on these events, so dashboards stay live without polling.

The hub accepts same-origin browser connections and has no client mutation methods; admins join the `admins` group automatically from their role claim. Events are transient and served by one application instance; add a SignalR backplane for multiple instances (the alerts inbox covers offline delivery).

## Feature files and scheduled work

Each feature has a routing file (`Orders/Orders.cs`, ...) and a scoped service (`OrderService.cs`, ...). Route handlers are thin: HTTP concerns, `Render(...)` and the Inertia props. The service owns the queries, mutations, state machines, job staging and realtime signals. Commands return a `SaveResult` (`Features/SaveResult.cs`) that renders itself as back-with-errors, flash, redirect or 404, so most handlers are one line. `*Models.cs` / `Catalog.cs` hold the EF entities and constants; DTO records live next to the service that returns them.


`Cron/CronSetup.cs` registers recurring functions with TickerQ and applies `CRON_*` / `Cron__<name>` overrides. Set `CRON_ENABLED=false` to disable scheduled work. Expressions are validated at startup, and execution is limited to one job at a time per process. `Cron/CronJobs.cs` retains the database lease guard: overlapping replicas cannot claim the same occurrence while the lease is held. The schedule: `sales-rollup` */15 min · `bnpl-reminders` 07:00 daily · `bnpl-overdue` 07:10 daily · `bnpl-statement` Mon 08:00 · `ticket-escalation` hourly · `order-timeout` hourly (cancels stale unpaid orders, releases stock) · `backup` 02:00 (pg_dump to S3) · `prune-product-views` 03:00 · `orphan-assets` hourly · `prune-unverified` 04:00 · `prune-jobs` 03:30.

Durable jobs (`Jobs/JobQueue.cs`) stage in the same transaction as the business write and retry with backoff: `SendEmail`, `WelcomeEmail`, `OrderPaidEmail`, `OrderShippedEmail`, `OrderCancelledEmail`, `OrderRefundEmail`, `InstallmentPaidEmail`, `InstallmentReminderEmail` (reminder | overdue | statement), `TicketReplyEmail`, `CareDecisionEmail` (warranty-claim | insurance-claim | repair), `DeleteAssets`.


AI workflow setup: [docs/ai/README.md](docs/ai/README.md). Feature conventions: [AGENTS.md](AGENTS.md). Optional worker/cron tuning: [docs/DEPLOY.md](docs/DEPLOY.md).


### Create an administrator from the terminal

```powershell
dotnet run --project src/TechLandier -- --create-admin
```

Enter the name, email and password when prompted (password input is hidden). Existing accounts are never overwritten.
Use the team screen to add or remove administrators. Demo passwords are `Password@12345` and demo emails use `@yopmail.com`.
Rerunning `--seed-demo` updates old seeded credentials once without resetting live data. Migrations apply automatically.

Store settings are at `/admin/settings`; team management at `/admin/team`. Order rows now open their detail pages; installment plans and care cases have dedicated details. Store and delivery hours appear on the home and checkout pages. Enable notification chimes with the speaker button.
