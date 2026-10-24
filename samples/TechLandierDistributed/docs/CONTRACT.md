# Route & prop contract

Frontend types live in `ClientApp/types/index.d.ts`. JSON is camelCase. All non-GET requests need the antiforgery
header (`X-XSRF-TOKEN` from the `XSRF-TOKEN` cookie); Inertia's router adds it automatically, `fetch` callers must add it.

Route handler methods (in the feature route files) compose the Inertia response; services return plain data/outcomes
(`Features/SaveResult.cs`: `SaveResult { errors, flashType, flashMessage, redirectUrl, notFound }`).

## Storefront (public, SSR)

| Method | Path | Result |
|---|---|---|
| GET | / | `Home` { categories, deals, arrivals, settings } |
| GET | /products?q=&category=&min=&max=&inStock=&sort= | `products/Index` { products: Scroll (wrapper `data`), filters: { q, category, min, max, inStock, sort }, categories } |
| GET | /products/{slug} | `products/Show` { product, specs, reviews (Defer group `reviews`), related (Defer group `related`), canReview (Defer group `reviews`) } ; 404 → `Error` {status:404} |
| GET | /deals | `deals/Index` { deals: Scroll (wrapper `data`) } |
| GET | /about, /support/policy | `pages/About` / `pages/Policy` |
| POST | /products/{id}/view | view beacon JSON { referrer? } → 204 (rate limit `views`, ignores bots/prefetch) |

## Buying (auth + verified)

| Method | Path | Result |
|---|---|---|
| GET | /buy/{productId}?qty=&bnpl=&insure= | `products/Buy` { product, qty, zones, insure, bnpl, creditState (Defer group `credit`) } ; 404 → Error |
| POST | /checkout | body { productId, qty, zoneId?, paymentMethod, address{recipientName, phone, streetLabel}, notes?, insurance, bnplInstallments?, attemptId } → success redirect `/orders/{number}?t=` + flash; idempotent per attemptId; stock race → error flash |
| GET | /orders/{number}?t= | `orders/Track` { token, order: timeline + items + delivery + plan } (public with token or owner) |

## Account (auth + verified)

| Method | Path | Result |
|---|---|---|
| GET | /account | `account/Index` { stats, recentOrders, nextInstallment (Defer group `installment`), openDeliveries (Defer group `deliveries`), unreadAlerts (Defer group `alerts`) } |
| GET | /account/orders | `account/Orders` { orders } |
| GET | /account/orders/{number} | `account/Order` { order, timeline, delivery, plan, installments, warranties, insurance } |
| GET | /account/credit | `account/Credit` { plans, blocked } |
| POST | /account/credit/installments/{id}/pay | → toast (info on blocked / error / success) |
| GET | /account/care/warranties | `account/Warranties` { warranties } |
| POST | /account/care/warranties/{id}/serial | body { serialNumber } → Back() |
| POST | /account/care/warranties/{id}/claims | body { issue } → Back() + flash |
| GET | /account/care/insurance | `account/Insurance` { policies, attachable } |
| POST | /account/care/insurance/{id}/claims | body { incidentType, description, incidentOn } → Back() + flash |
| POST | /account/care/insurance/attach/{orderItemId} | → Back() + flash |
| GET | /account/care/repairs | `account/Repairs` { repairs, categories } |
| GET | /account/care/repairs/{id} | `account/Repairs` { repair, history } (detail view shares the page) |
| POST | /account/care/repairs | body { deviceName, deviceSerial?, problem, categoryId? } → Back() + flash |
| GET | /account/support | `account/Support` { tickets, categories } |
| POST | /account/support | body { subject, category, message, orderNumber? } → Back() + flash |
| POST | /account/support/{id}/reply | body { message } → Back() |
| GET | /account/alerts | `account/Alerts` { alerts, unreadCount } |
| POST | /account/alerts/read · /account/alerts/{id}/read | → Back() |
| GET / PUT | /account/profile · /account/preferences | `account/Preferences` { profile, preferences } → Back() + toast |
| POST | /account/reviews | body { productId, orderItemId, rating, title?, body } → Back() + flash (verified-purchase gate) |

## Admin (role `admin`)

| Method | Path | Result |
|---|---|---|
| GET | /admin?range=7\|30 | `admin/Index` { range, kpis: Defer group `kpis` (.Cache `admin:{days}:kpis`), needsAttention } |
| GET | /admin/analytics?days=&category= | `admin/Analytics` { range, kpis (Defer+Cache), series (Defer+Cache `admin-analytics:{days}:*`) } |
| GET | /admin/catalog?q=&status=&page= | `admin/Catalog` { products (Paged, 20 per page; rows carry imageUrl), q, status (all, active, draft, archived, low), counts, categories }. `?edit={id}` redirects to the product page; `?edit=new` opens the create sheet |
| GET | /admin/catalog/{id} | `admin/Product` { detail (product incl. ordered images, slug, category, sold, rating, dates), categories, movements (latest 25 stock movements) } |
| POST | /admin/catalog | create product (body includes `imageAssetIds`, first = cover) → flash + redirect to the product page; field errors otherwise |
| POST | /admin/catalog/{id} | edit product; `imageAssetIds` replaces the gallery (null keeps it). Images must be ready image uploads by the acting admin or already on the product; up to 8 |
| POST | /admin/catalog/{id}/restock | body { delta, reason?, note? } → Back() + flash |
| POST | /admin/categories · /admin/categories/{id} | create/edit category → Back() + flash |
| GET | /admin/orders | `admin/Orders` { orders, statusFilter } |
| GET | /admin/orders/{number} | `admin/Order` { order, timeline, items, payment, delivery, plan } |
| POST | /admin/orders/{id}/status | body { status, note? } → state machine + timeline + SignalR |
| POST | /admin/orders/{id}/rider | body { riderId } → assign delivery |
| POST | /admin/orders/{id}/verify-payment · /notes · /cancel | → Back() + flash |
| GET | /admin/deliveries | `admin/Deliveries` { deliveries (DeliveryRow: order, zoneId/zone, riderId/rider, customer, phone, street, payment, cash, failedReason, attempts, note, receivedBy, stage timestamps), riders (RiderAdminRow: + capacity, activeRuns, completed in 30 days), zones } |
| POST | /admin/deliveries/{id}/assign · /pickup · /deliver { cash?, receivedBy? } · /fail { reason } · /note { note } | → Back() + flash. A failure increments `attempts` once (repeat posts are no-ops); notes and reasons are capped at 200 characters |
| POST | /admin/riders · /admin/riders/{id} · /admin/zones · /admin/zones/{id} | rider/zone CRUD → Back() + flash. Riders accept `capacity` (1–30, default 6); updates are partial, so `{ isActive }` alone toggles availability |
| GET | /admin/credit?status= | `admin/Credit` { plans, statusFilter } |
| GET | /admin/care?tab= | `admin/Care` { tab, warrantyClaims, insuranceClaims, repairs } |
| POST | /admin/care/warranty-claims/{id} · /insurance-claims/{id} · /repairs/{id} | decide/update → Back() + flash (+ CareDecisionEmail job) |
| GET | /admin/support | `admin/Support` { tickets, view } |
| GET | /admin/support/{number} | `admin/Support` { ticket, messages } |
| POST | /admin/support/{id}/reply · /{id} | reply (reopens) / decide (resolve/close) → Back() + flash |
| GET | /admin/reviews | `admin/Reviews` { reviews } |
| POST | /admin/reviews/{id} | hide/unhide → Back() |

## Auth & uploads

| Method | Path | Auth | Result |
|---|---|---|---|
| GET / POST | /login, /register, /logout, /email/verify(+confirm), /email/verification-notification | guest/signed in | `Auth/Login` { returnUrl }, `Auth/Register`, `Auth/VerifyEmail`; redirects + flash as before |
| POST | /uploads/presign · /uploads/{id}/confirm | signed in | JSON as before (product images, avatars); 422 ValidationProblem on bad type/size |
| GET | /email/unsubscribe/promo?token= | anonymous | data-protected promo unsubscribe (TTL 180 days) |

## Realtime (StoreHub, `/realtime`)

| Event | Audience | Payload |
|---|---|---|
| OrderChanged | `admins` group | { orderNumber, status, paymentStatus } |
| Notification | one user / `admins` group | { title, body, href } |
| OrderTimelineUpdated | owning user | { orderNumber } |
| StockAlert | `admins` group | { productId, stock } |


## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers persist jobs and return after enqueueing. Workers deliver with retries; HTTP success does not promise immediate delivery. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.


## Store operations and detail pages

All `/admin` routes require the current administrator role; public registration cannot create an admin.

| Method | Path | Page / input |
|---|---|---|
| GET | /admin/catalog?edit=new | `admin/Catalog` with the create sheet open; `?edit={id}` redirects to `/admin/catalog/{id}` |
| GET | /admin/orders/{number} | `admin/Order` with order, events, delivery (including id), customer, payments, plan, activeRiders, warranties, insurances |
| GET | /admin/credit/{id} | `admin/Plan` { detail: { orderNumber, customer, email, plan } }; full installment schedule and paid dates |
| GET | /admin/care/{kind}/{id} | `admin/CareDetail` { detail: CareCaseDetail }; kind = warranty-claims / insurance-claims / repairs; unknown kind/id returns 404 |
| GET / POST | /admin/settings | `admin/Settings` { settings, timeZones (IANA ids on every OS, always including the saved zone) }; saves StoreSettingsView |
| GET / POST | /admin/team | `admin/Team` { members }; creates an admin with { name, email, password } |
| POST | /admin/team/{id}/remove | Demotes another admin to customer, preserving orders and account |

`StoreSettingsView` = { name, email, phone, address, timeZoneId, hours }. Each of seven `hours` rows has
{ day (Sunday=0), closed, opens, closes, deliveryClosed, deliveryOpens, deliveryCloses }. Times are `HH:mm`,
windows end on the same day, and start must precede end. Storefront and checkout show saved hours;
orders remain available around the clock, with fulfilment during the published windows. Settings use a new additive migration.

Team creation rejects existing email addresses and passwords outside 12–128 characters. It never silently promotes
or replaces an existing account. Self-demotion is blocked; current role claims refresh on each request. Admin realtime
job recipients are selected from current database roles so removed admins cannot keep receiving admin pushes over an old socket.

The table's `rowHref` renders an actual focusable primary-cell link on desktop and mobile. Order events show an actionable
toast and refresh the appropriate page props. Notification sound is an opt-in browser preference; enabling plays a test chime,
and subsequent user interaction unlocks audio after a reload. No system notification permission is requested.

CLI: `dotnet run --project apps/MainApp -- --create-admin` prompts for name, email and a hidden password, migrates,
creates a verified admin, and exits with a nonzero code on validation failure. `--seed-demo` uses
`inertia.demo42.techlandier.admin@yopmail.com` and `inertia.demo42.techlandier.customer@yopmail.com` with password
`Password@12345`. A one-time credential checkpoint updates existing seeded users without deleting live data.

## In-app notification center

Every authenticated page shares `notifications: { unreadCount, items }` (latest 10, newest first); guests receive null.
The summary is cached per user for 5 minutes fresh / 1 minute stale and invalidated by every alert create or read
path; it is UI data and never authorizes access. The bell opens a keyboard-accessible panel in every signed-in shell.
A queued live notification refreshes this prop and the inbox when open; reconnect also refreshes it. Counts and
mark-read operations always use the authenticated user, never a user ID supplied by the client. Partial reloads
preserve in-progress navigation and forms.

The full paginated inbox remains `/account/alerts`; existing read-all and individual read endpoints are unchanged.
Admin notifications for order changes, stock alerts and operational pulses are committed with their queued push, so
an offline administrator can read them later. Customer order/care/support alerts continue to use the existing Alerts table.

## Deployment contract

Route/prop/job payloads are unchanged by the role split. MainApp owns routes/Inertia and enqueues
jobs; Worker consumes jobs and runs TickerQ cron; Realtime maps only `/realtime`, `/health` and
`/alive`; the SSR pool is selected through `Inertia:SsrUrls` (no managed Bun in MainApp). All roles
share the `.tld.auth` cookie, Data Protection key ring and critical Redis; cache invalidation goes
through `IPropCacheInvalidator`.

Migrations run only through the one-shot `--migrate` CLI (or `Database:MigrateOnStartup=true` in
tests); no serving role migrates at startup. Normal database connections go through PgDog pinned to
the primary; selected lag-tolerant public/analytics reads use `DATABASE_READ_URL` pinned to the
replica role. Auth, checkout, orders, notifications, reviews-after-write and mutations never use the
read URL. Cached props are UI data and never authorize a request. The job queue is at-least-once:
handlers must remain idempotent, and realtime events are transient while the alerts inbox is the
durable copy.
