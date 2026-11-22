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
| POST | /checkout | body { productId, qty, zoneId?, paymentMethod, address{recipientName, phone, streetLabel}, notes?, insurance, bnplInstallments?, attemptId } → success renders the owner's `account/Order` inline at url `/account/orders/{number}` for an Inertia visit (the guest tracking GET then the owner redirect are gone); other outcomes redirect `/orders/{number}?t=` as before; idempotent per attemptId; stock race → error flash |
| GET | /orders/{number}?t= | `orders/Track` { token, order (with `plan`), events, delivery, plan (PlanView, lazy, live on the order timeline) } (public with token; a verified signed-in owner is redirected to `/account/orders/{number}`) |

## Account (auth + verified)

| Method | Path | Result |
|---|---|---|
| GET | /account | `account/Index` { stats, recentOrders (id, number, status, total, createdAt, itemName, itemCount), nextInstallment (Defer group `installment`), openDeliveries (Defer group `deliveries`), unreadAlerts (Defer group `alerts`) } |
| GET | /account/orders | `account/Orders` { orders: rows with `plan` } |
| GET | /account/orders/{number} | `account/Order` { order, timeline, delivery, plan, installments, warranties, insurance } |
| GET | /account/credit | `account/Credit` { plans, blocked }. PlanView carries `orderNumber` and `productName` (first item by name) so each card names its order |
| GET | /account/orders/{number}/receipt.pdf | PDF receipt (`application/pdf`, inline). Owner only, paid orders only; any other caller or an unpaid/refunded order gets 404 |
| GET | /orders/{number}/receipt.pdf?t= | The same receipt for guests: the order's access token (as on `/orders/{number}?t=`) or the signed-in owner. Wrong or missing token: 404. The order-paid email links here |
| GET | /account/installments/{planId}/statement.pdf | PDF statement for one BNPL plan: summary, full schedule with status and paid dates, totals (waived installments are not owed and excluded). Plan owner only, otherwise 404 |
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
| GET | /account/support | `account/Support` { tickets (newest 100), categories } |
| POST | /account/support | body { subject, category, message, orderNumber? } → Back() + flash |
| POST | /account/support/{id}/reply | body { message } → Back() |
| GET | /account/alerts?page= | `account/Alerts` { alerts: lazy live `Paged` (25 per page, listens for `Notification` on the owner's channel), unreadCount: lazy live count } |
| POST | /account/alerts/read · /account/alerts/{id}/read | → Back() |
| GET / PUT | /account/profile · /account/preferences | `account/Preferences` { profile, preferences } → Back() + toast |
| POST | /account/reviews | body { productId, orderItemId, rating, title?, body } → Back() + flash (verified-purchase gate) |

## Admin (role `admin`)

| Method | Path | Result |
|---|---|---|
| GET | /admin?range=7\|30 | `admin/Index` { range, kpis: Defer group `kpis` (.Cache `admin:{days}:kpis`), needsAttention }. `attention.deliveriesToAssign` counts runs to dispatch (unassigned or failed, order not cancelled), the same definition as the Deliveries board "To dispatch" lane; `kpis.planExposure` is the unpaid installment total on active plans, distinct from Analytics "Financed" (`bnplShare`) |
| GET | /admin/analytics?days=&category= | `admin/Analytics` { range, kpis (Defer+Cache), series (Defer+Cache `admin-analytics:{days}:*`) } |
| GET | /admin/catalog?q=&status=&page= | `admin/Catalog` { products (Paged, 20 per page; rows carry imageUrl), q, status (all, active, draft, archived, low), counts, categories }. `?edit={id}` redirects to the product page; `?edit=new` opens the create sheet |
| GET | /admin/catalog/{id} | `admin/Product` { detail (product incl. ordered images, slug, category, sold, rating, dates), categories, movements (latest 25 stock movements) } |
| POST | /admin/catalog | create product (body includes `imageAssetIds`, first = cover) → flash + redirect to the product page; field errors otherwise |
| POST | /admin/catalog/{id} | edit product; `imageAssetIds` replaces the gallery (null keeps it). Images must be ready image uploads by the acting admin or already on the product; up to 8 |
| POST | /admin/catalog/{id}/restock | body { delta, reason?, note? } → Back() + flash |
| POST | /admin/categories · /admin/categories/{id} | create/edit category → Back() + flash |
| GET | /admin/orders?page= | `admin/Orders` { orders: lazy live `Paged` of rows with `plan`, statusFilter }. Row `customerName`/`customerEmail` are the account holder; `recipientName` is the delivery recipient (shown only when different) |
| GET | /admin/orders/{number} | `admin/Order` { order, timeline, items, payment, delivery, plan } |
| GET | /admin/orders/{number}/receipt.pdf | PDF receipt for any paid order (admin role); 404 for unpaid orders |
| POST | /admin/orders/{id}/status | body { status, note? } → state machine + timeline + SignalR |
| POST | /admin/orders/{id}/rider | body { riderId } → assign delivery |
| POST | /admin/orders/{id}/verify-payment · /notes · /cancel | → Back() + flash |
| GET | /admin/deliveries?page= | `admin/Deliveries` { deliveries (every open run, not delivered/cancelled: DeliveryRow: order, zoneId/zone, riderId/rider, customer, phone, street, payment, cash, failedReason, attempts, note, receivedBy, stage timestamps), delivered (`NumberedPage` of 20 completed runs, newest first; `?page=`), deliveredToday (count, UTC day), riders (RiderAdminRow: + capacity, activeRuns, completed in 30 days), zones }. Every prop is lazy, parallel and Live on `AdminPulse`, so an event reloads each through its own query |
| POST | /admin/deliveries/{id}/assign · /pickup · /deliver { cash?, receivedBy? } · /fail { reason } · /note { note } | → Back() + flash. A failure increments `attempts` once (repeat posts are no-ops); notes and reasons are capped at 200 characters |
| POST | /admin/riders · /admin/riders/{id} · /admin/zones · /admin/zones/{id} | rider/zone CRUD → Back() + flash. Riders accept `capacity` (1–30, default 6); updates are partial, so `{ isActive }` alone toggles availability |
| GET | /admin/credit?status= | `admin/Credit` { plans, statusFilter }. Plan row `overdue` counts unpaid installments whose due date has passed; `unpaid` is all not-yet-paid installments |
| GET | /admin/care?tab=&show=&page= | `admin/Care` { tab, show (`open` default = needs a decision, or `all`), warrantyClaims, insuranceClaims, repairs } - each queue is a `NumberedPage` (`data`, `currentPage`, `perPage`, `total`, `lastPage`, `from`, `to`, `hasMore`, `pageName`) of 25, newest first; only the open tab is queried, the others are empty pages |
| POST | /admin/care/warranty-claims/{id} · /insurance-claims/{id} · /repairs/{id} | decide/update → Back() + flash (+ CareDecisionEmail job) |
| GET | /admin/support | `admin/Support` { tickets (`Paged` rows incl. `firstResponseAt`), view }. The response clock (SlaDue, `overdue` filter) runs only while a ticket is received or open and has no first response, like the ticket-escalation cron; answered, waiting, resolved and closed tickets are never overdue |
| GET | /admin/support/{number} | `admin/Support` { ticket, messages } |
| POST | /admin/support/{id}/reply · /{id} | reply (reopens) / decide (resolve/close) → Back() + flash |
| GET | /admin/reviews | `admin/Reviews` { reviews } |
| POST | /admin/reviews/{id} | hide/unhide → Back() |

## Auth & uploads

| Method | Path | Auth | Result |
|---|---|---|---|
| GET / POST | /login, /register, /logout, /email/verify(+confirm), /email/verification-notification | guest/signed in | `Auth/Login` { returnUrl }, `Auth/Register`, `Auth/VerifyEmail`; an Inertia visit renders the next page inline (login → `products/Index` at `/products`, register → `Auth/VerifyEmail` at `/email/verify`, logout → `Auth/Login` at `/login`; admin/returnUrl/plain posts redirect as before) + flash |
| GET | /ping | anonymous | 204 heartbeat the client uses for its offline indicator (unnamed; the route generator skips it) |
| POST | /uploads/presign · /uploads/{id}/confirm | verified | JSON as before (product images, avatars); PNG/JPEG/WebP/GIF only (no PDF), 5 MB. The presigned PUT signs the declared `Content-Length`; confirm re-checks the stored size and type and reads the first bytes for the file signature. 422 ValidationProblem on a bad type/size or a body that is not the declared image (the asset is discarded) |
| GET / POST | /email/unsubscribe?token= | anonymous | RFC 8058 one-click unsubscribe from installment mail: GET shows a confirm page and changes nothing, POST turns `EmailInstallmentReminders` off. Replaces the removed promo unsubscribe and `EmailPromos` preference |

## Runwick operations

| Method | Path | Role | Result |
|---|---|---|---|
| GET / POST | /runwick, /runwick-api/v1/... | Dashboard | Standalone Runwick dashboard and management API (separate login; see below) |
| GET / POST | /runwick-login, /runwick-logout | Dashboard | Sign-in form (POST rate limited, `auth` policy) and sign-out (a cross-origin `Origin` gets 403). `Admin` / `Admin@12345` works in Development only; elsewhere the Dashboard refuses to start until `RUNWICK_ADMIN_USERNAME` / `RUNWICK_ADMIN_PASSWORD` are set to other values |
| GET | /demo/jobs | MainApp producer | Demo job catalog and trigger URLs (mapped only when `RUNWICK_DEMO_ENABLED`; default on in Development only) |
| POST | /demo/jobs/{kind} · /demo/jobs/{kind}/batch?count=100 | MainApp producer | Dispatch one or up to 100 side-effect-free sleep jobs |

MainApp is the producer, Worker owns the Runwick worker pools and scheduler, and Dashboard is a
management-only host with no worker or scheduler.

### Runwick jobs and queues

All background work runs on Runwick; the old `Jobs` outbox table (and the unused `CronLeases` table) is dropped by the `DropLegacyJobs` migration. Features stage jobs with `IJobOutbox.StageAsync(db, job)` (Runwick.Postgres `IPostgresJobDispatcher.StageAsync` with the EF `NpgsqlTransaction`) inside the transaction of the business write, so the job commits or rolls back with it; work decided without a write uses `DispatchAsync`. MainApp, Worker and cron only stage; handlers and worker pools live in the Worker role (`RUNWICK_DATABASE_URL` must reach the same primary database as `DATABASE_URL`). Payloads carry ids, never rendered content.

| Queue | Job (Runwick name) | Uniqueness | Retry |
|---|---|---|---|
| mail | `mail.verification` (user id; the handler mints the link), `mail.order-paid`, `mail.order-shipped`, `mail.order-cancelled`, `mail.order-refund` (order id), `mail.installment-paid` (installment id), `mail.installment-reminder` (plan, kind reminder/overdue, day), `mail.installment-statement` (user, day), `mail.ticket-reply` (ticket id), `mail.care-decision` (kind + id) | verification while active; one-shot facts within a 2-30 day window; ticket replies and care decisions are not unique (each is its own event) | 5 executions, backoff 10 s to 10 min |
| events | `events.realtime-push` (SignalR method, payload, group/user), `events.pulse-reload` (one trailing admins-pulse reload per gate window, unique while active), `events.delete-assets` (storage keys) | realtime-push and delete-assets none | 3 / 2 / 5 executions |
| cron | `cron.scheduled` (schedule name) | serialized per schedule | 1 execution |

Worker pools: `events` and `mail` auto-scale 1..`JOBS_CONCURRENCY`, `cron` is fixed at 1; the four demo pools and queues exist only when `RUNWICK_DEMO_ENABLED`. `JOBS_ENABLED=false` keeps a Worker dispatch-only. Schedules (`CronJobs.Schedule`, misfire RunOnce, overlap Serialize): sales-rollup, bnpl-reminders, bnpl-overdue, bnpl-statement (one email per customer across all their plans), ticket-escalation, order-timeout (cancels stale unpaid orders in batches; stock release, alerts and the cancel email commit together), prune-product-views, orphan-assets, prune-unverified. `prune-jobs` is gone; Runwick retention replaces it.

## Realtime (StoreHub, `/realtime`)

| Event | Audience | Payload |
|---|---|---|
| OrderChanged | `admins` group | { orderNumber, status, paymentStatus } |
| Notification | one user / `admins` group | { title, body, href } |
| OrderTimelineUpdated | owning user | { orderNumber } |
| StockAlert | `admins` group | { productId, stock } |

## Live props (Inertia.Net.Live.SignalR, `/inertia-live`)

The web tier declares what listens and the Worker publishes: MainApp pages mark the listeners (`LiveEvents` builds the
events from the Core's `RealtimeNames` constants, so both tiers speak the same channels), and the Worker's realtime
handler publishes through the shared backplane after the raw sends. Admin bursts are folded: the first event of a
window publishes at once and the rest of the burst joins one trailing `events.pulse-reload` job, so a burst of order
moves costs two admin reload waves instead of one per move. Channels:

| Prop | Channel | Event |
|---|---|---|
| shared `notifications` (+ account alerts page) | `private:users.{userId}` | `Notification` (200 ms throttle) |
| admin KPI lists/boards: `admin/Index`, `admin/Analytics` (deferred), `admin/Orders` `orders`, `admin/Deliveries` `deliveries`/`delivered`/`deliveredToday`/`riders`/`zones`, `admin/Order` detail props (all lazy `Prop(...).Live(...)`, so an event runs only the affected prop's query) | `private:admins` | `AdminPulse` |
| `orders/Track` and `account/Order` `events`/`delivery`(/`plan`) | `public:orders.{orderNumber}` | `OrderTimelineUpdated` |

The Realtime tier maps the hub at `/inertia-live` next to `/realtime` (HAProxy routes both to the realtime backend);
its authorizer admits each signed-in user to their own `users.{id}` channel by `UserIdentifier` and admins to the
`admins` channel by role claim; public order channels are open, because event payloads carry no values and the page
reload still authorizes the data it returns. Publishes go through the same Redis backplane as the raw sends, so the
Worker reaches every realtime replica; per-user cache invalidation still precedes each enqueue.

## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers stage or dispatch Runwick jobs and return after the commit. Workers deliver with retries; HTTP success does not promise immediate delivery. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.


## Store operations and detail pages

All `/admin` routes require the current administrator role; public registration cannot create an admin.

| Method | Path | Page / input |
|---|---|---|
| GET | /admin/catalog?edit=new | `admin/Catalog` with the create sheet open; `?edit={id}` redirects to `/admin/catalog/{id}` |
| GET | /admin/orders/{number} | `admin/Order` with order, events, delivery (including id), customer (`accountName`/`email` = account holder; `name`/`phone`/`street` = delivery recipient), payments, plan, activeRiders, warranties, insurances |
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

## Security headers, guest support and rate limits

- Every MainApp response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` and `Cross-Origin-Opener-Policy: same-origin`; HSTS (365 days, includeSubDomains) is added outside Development. HTML responses add a `Content-Security-Policy` built by `Infrastructure/SecurityHeaders.cs`: `script-src 'self' 'nonce-{per request}' 'sha256-{inline theme script of app.html}'` (Inertia.Net stamps the nonce on the page-data script and Vite tags through `InertiaOptions.CspNonce`), `style-src 'self' 'unsafe-inline'` plus the Google Fonts hosts, `img-src` / `connect-src` with the object-storage origins from config, same-origin `ws(s)` for `/realtime` and `/inertia-live`, `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'`, plus `upgrade-insecure-requests` outside Development on requests that arrived over HTTPS (directly or via a trusted `X-Forwarded-Proto`). The Runwick dashboard is a separate host and is not covered.
- `POST /support` (public guest complaint): the per-address `guest-support` window is sized for a room behind one shared IP (500 per 10 minutes, playbook; the Redis limiter takes a per-policy window), so the reply-to email is the abuse control — five complaints per email per 10 minutes in the service guard, database-backed so it holds across replicas. Name 100, email 254, subject 150, message 4000 and order number 40 characters at most (also on ticket create and replies). A known order number links the ticket to that order and its customer only when the email matches the order customer's email (case-insensitive); otherwise it stays a guest ticket with no order.
- Account and admin route groups apply the `writes` rate limit (mutations only: GET/HEAD are never counted; a signed-in user keeps 120 per minute, an address without a session falls back to a room-sized 12,000 per minute window). A rejected Inertia request is answered with `Back()` + the error `rateLimit` + an error toast; other clients get a bare 429.
- Per-address windows (`auth` 1,000/min, `register` 1,000/min with the precognition field checks at 6,000/min, and the guest fallbacks of `uploads` 3,000/min / `views` 6,000/min / `writes` 12,000/min) are sized for a whole room behind one NAT — ×100 of a single user's budget (playbook: a campus or office shares one public IP). Signed-in callers keep the tight per-user budgets above; sign-in attempts are also guarded per account (30 attempts per 5 minutes, in-process) in `AuthService`.
- `RealtimeOriginMiddleware` checks `Origin` against the request origin for both SignalR hubs (`/realtime` and `/inertia-live`) on the Realtime role.
- Signed-in `/account`, `/admin` and `/checkout` pages set `encryptHistory`; `POST /logout` clears the history key.

## Email, PDFs and unsubscribe

- **Templates** (`Infrastructure/Emails.cs` in Core): verify, order-paid (items, totals, address, receipt number, "Download receipt (PDF)"), order-shipped, order-cancelled, order-refund, installment-paid, installment-reminder, installment-overdue, installment-statement (one per user across active plans, a "Download statement (PDF)" link per plan), ticket-reply and care-decision, each with a plain-text twin, every interpolated value HTML-encoded and money through `Pricing.Show`. Worker handlers build an `EmailContext` (store identity plus the public base URL) through `EmailContexts`.
- **Footers:** order, ticket, care and verification mail are transactional (reason plus a link to `/account/preferences`). Installment mail is preference-backed (`EmailInstallmentReminders`) and adds an Unsubscribe link; `Mailer` sends `List-Unsubscribe` and `List-Unsubscribe-Post: List-Unsubscribe=One-Click` on those messages only. The token is a Data Protection time-limited protector (purpose `TechLandier.Email.Unsubscribe.v1`, 60 days); a forged or expired token answers 400. `PipelineSetup` skips antiforgery for `/email/unsubscribe`.
- **Dev preview (Development only):** `GET /dev/emails` lists every template; `GET /dev/emails/{name}` renders it with sample data and `?format=text` returns the plain-text twin.
- **PDFs** use QuestPDF (Community license; system fonts off, Lato embedded; the Docker image keeps libstdc++, libgcc and zlib). They are generated per request, streamed inline with `Cache-Control: private, no-store` and never stored; `DocumentsService` (Core) also feeds the receipt data to the order-paid email.

## Order plan summary and prices

- Every order DTO (OrderDetail, AccountOrderRow, AdminOrderRow) carries `plan: { count, paid } | null` (`PlanBrief`), null when the order is not on a plan.
- `ProductCard`/`ProductDetail` send `compareAtPrice` = the list `price` while a discount applies (else null).
- Device insurance is priced on the price paid per unit (`Pricing.Premium` / `Pricing.Coverage`: 15% premium, 90% payout), at checkout and when attached later from a delivered order. `UserPreferences.EmailPromos` is removed.

## Deployment contract
Route/prop/job payloads are unchanged by the role split. MainApp owns routes/Inertia and is a Runwick
producer; Worker consumes jobs and runs Runwick workers plus the scheduler; Dashboard is a standalone
Runwick management host at `/runwick` and `/runwick-api`; Realtime maps only `/realtime`,
`/inertia-live`, `/health` and `/alive`; the SSR pool is selected through `Inertia:SsrUrls` (no managed
SSR runtime in MainApp). All roles share the `.tld.auth` cookie, Data Protection key ring and critical Redis;
cache invalidation goes through `IPropCacheInvalidator`.

Migrations run only through the one-shot `--migrate` CLI (or `Database:MigrateOnStartup=true` in
tests); no serving role migrates at startup. Normal database connections go through PgDog pinned to
the primary; selected lag-tolerant public/analytics reads use `DATABASE_READ_URL` pinned to the
replica role. Auth, checkout, orders, notifications, reviews-after-write and mutations never use the
read URL. Cached props are UI data and never authorize a request. Runwick jobs are at-least-once:
handlers must remain idempotent, and realtime events are transient while the alerts inbox is the
durable copy.
