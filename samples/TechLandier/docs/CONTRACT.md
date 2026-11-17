# Route & prop contract

Frontend types live in `ClientApp/types/index.d.ts`. JSON is camelCase. All storefront/admin non-GET requests need the
antiforgery header (`X-XSRF-TOKEN` from the `XSRF-TOKEN` cookie); Inertia's router adds it automatically, `fetch` callers
must add it. The Runwick management API and the open demo triggers below have their own antiforgery rules.

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
| GET | /orders/{number}?t= | `orders/Track` { token, order (with `plan`), events, delivery, plan (PlanView, lazy, live on the order timeline) } (public with token; a verified signed-in owner is redirected to `/account/orders/{number}`) |

## Account (auth + verified)

| Method | Path | Result |
|---|---|---|
| GET | /account | `account/Index` { stats, recentOrders (id, number, status, total, createdAt, itemName, itemCount), nextInstallment (Defer group `installment`), openDeliveries (Defer group `deliveries`), unreadAlerts (Defer group `alerts`) } |
| GET | /account/orders | `account/Orders` { orders: rows with `plan` } |
| GET | /account/orders/{number} | `account/Order` { order, timeline, delivery, plan, installments, warranties, insurance } |
| GET | /account/credit | `account/Credit` { plans, blocked } |
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
| GET | /account/alerts?page= | `account/Alerts` { alerts: live `Page` (25 per page, no total, listens for `Notification` on the owner's channel), unreadCount: live count } |
| POST | /account/alerts/read · /account/alerts/{id}/read | → Back() |
| GET / PUT | /account/profile · /account/preferences | `account/Preferences` { profile, preferences } → Back() + toast |
| POST | /account/reviews | body { productId, orderItemId, rating, title?, body } → Back() + flash (verified-purchase gate) |

## Admin (role `admin`)

| Method | Path | Result |
|---|---|---|
| GET | /admin?range=7\|30 | `admin/Index` { range, kpis: Defer group `kpis` (.Cache `admin:{days}:kpis`), needsAttention }. `attention.deliveriesToAssign` counts runs to dispatch (unassigned or failed, order not cancelled), the same definition as the Deliveries board "To dispatch" lane; `kpis.planExposure` is the unpaid installment total on active plans (card "Outstanding on plans"), distinct from Analytics "Financed" (`bnplShare`, order value placed on plans in the range) |
| GET | /admin/analytics?days=&category= | `admin/Analytics` { range, kpis (Defer+Cache), series (Defer+Cache `admin-analytics:{days}:*`) } |
| GET | /admin/catalog?q=&status=&page=&perPage= | `admin/Catalog` { products: `Page` (20 per page, `data`/`currentPage`/`total`/`lastPage`, from ToPageAsync), q, status (all, active, draft, archived, low), counts, categories }. `?edit={id}` redirects to the product page; `?edit=new` opens the create sheet |
| GET | /admin/catalog/{id} | `admin/Product` { detail (product incl. ordered images, slug, category, sold, rating, dates), categories, movements (latest 25 stock movements) } |
| POST | /admin/catalog | create product (body includes `imageAssetIds`, first = cover) → flash + redirect to the product page; field errors otherwise |
| POST | /admin/catalog/{id} | edit product; `imageAssetIds` replaces the gallery (null keeps it). Images must be ready image uploads by the acting admin or already on the product; up to 8 |
| POST | /admin/catalog/{id}/restock | body { delta, reason?, note? } → Back() + flash |
| POST | /admin/categories · /admin/categories/{id} | create/edit category → Back() + flash |
| GET | /admin/orders | `admin/Orders` { orders: rows with `plan`, statusFilter } |
| GET | /admin/orders/{number} | `admin/Order` { order, timeline, items, payment, delivery, plan } |
| GET | /admin/orders/{number}/receipt.pdf | PDF receipt for any paid order (admin role); 404 for unpaid orders |
| POST | /admin/orders/{id}/status | body { status, note? } → state machine + timeline + SignalR |
| POST | /admin/orders/{id}/rider | body { riderId } → assign delivery |
| POST | /admin/orders/{id}/verify-payment · /notes · /cancel | → Back() + flash |
| GET | /admin/deliveries?page= | `admin/Deliveries` { deliveries (every open run, not delivered/cancelled: DeliveryRow: order, zoneId/zone, riderId/rider, customer, phone, street, payment, cash, failedReason, attempts, note, receivedBy, stage timestamps), delivered (numbered page of 20 completed runs, newest first; `?page=`), deliveredToday (count, UTC day), riders (RiderAdminRow: + capacity, activeRuns, completed in 30 days), zones }. Every prop is lazy, parallel and Live on `AdminPulse`, so an event reloads each through its own query |
| POST | /admin/deliveries/{id}/assign · /pickup · /deliver { cash?, receivedBy? } · /fail { reason } · /note { note } | → Back() + flash. A failure increments `attempts` once (repeat posts are no-ops); notes and reasons are capped at 200 characters |
| POST | /admin/riders · /admin/riders/{id} · /admin/zones · /admin/zones/{id} | rider/zone CRUD → Back() + flash. Riders accept `capacity` (1–30, default 6); updates are partial, so `{ isActive }` alone toggles availability |
| GET | /admin/credit?status= | `admin/Credit` { plans, statusFilter } |
| GET | /admin/care?tab=&show=&page= | `admin/Care` { tab, show (`open` default = needs a decision, or `all`), warrantyClaims, insuranceClaims, repairs } - each queue is a numbered page of 25 (newest first); only the open tab is queried, the others are empty pages |
| POST | /admin/care/warranty-claims/{id} · /insurance-claims/{id} · /repairs/{id} | decide/update → Back() + flash (+ CareDecisionEmail job) |
| GET | /admin/support | `admin/Support` { tickets, view }. The response clock (SlaDue, `overdue` filter, overview "Support tickets past due") runs only while a ticket is received or open and has no first response (`firstResponseAt` null), like the ticket-escalation cron; answered, waiting-on-customer, resolved and closed tickets are never overdue and show Answered / Clock paused |
| GET | /admin/support/{number} | `admin/Support` { ticket, messages } |
| POST | /admin/support/{id}/reply · /{id} | reply (reopens) / decide (resolve/close) → Back() + flash |
| GET | /admin/reviews | `admin/Reviews` { reviews } |
| POST | /admin/reviews/{id} | hide/unhide → Back() |

## Auth & uploads

| Method | Path | Auth | Result |
|---|---|---|---|
| GET / POST | /login, /register, /logout, /email/verify(+confirm), /email/verification-notification | guest/signed in | `Auth/Login` { returnUrl }, `Auth/Register`, `Auth/VerifyEmail`; redirects + flash as before |
| POST | /uploads/presign · /uploads/{id}/confirm | verified | JSON as before (product images, avatars); PNG/JPEG/WebP/GIF only (no PDF), 5 MB. The presigned PUT signs the declared `Content-Length`; confirm re-checks the stored size and type and reads the first bytes for the file signature. 422 ValidationProblem on a bad type/size or a body that is not the declared image (the asset is discarded) |

## Runwick jobs, dashboard and demo triggers

| Method | Path | Auth | Result |
|---|---|---|---|
| GET | /runwick | Runwick cookie | Runwick dashboard (Jobs by default); signed out → `/runwick-login?ReturnUrl=%2Frunwick` |
| GET / POST | /runwick-login | anonymous | HTML sign-in form (POST rate limited with the `auth` policy); accepts `Admin` / `Admin@12345` in Development only. Elsewhere the app refuses to start until `RUNWICK_ADMIN_USERNAME` / `RUNWICK_ADMIN_PASSWORD` are set to other values |
| POST | /runwick-logout | Runwick cookie | Sign out and redirect to `/`; a cross-origin `Origin` is refused with 403 |
| * | /runwick-api/v1/... | Runwick cookie | Runwick management API; mutations need `X-Runwick-Antiforgery` |
| GET | /demo/jobs | anonymous | JSON catalog of demo job kinds, queue/pool configuration and trigger URLs (mapped only when `RUNWICK_DEMO_ENABLED`; default on in Development only) |
| POST | /demo/jobs/{kind} | anonymous | Dispatch one sleep job; `kind` is `100ms`, `500ms`, `2s`, `10s` or `30s`; 202 JSON { jobId, operationId, batchId, dashboard } |
| POST | /demo/jobs/{kind}/batch?count=100 | anonymous | Dispatch 1–100 sleep jobs (default 100); 202 JSON { batchId, requested, accepted, jobs[] } |

### Runwick jobs and queues

All background work runs on Runwick; the old `Jobs` outbox table is dropped by the `DropLegacyJobs` migration. Features
stage jobs with `IJobOutbox.StageAsync(db, job)` (Runwick.Postgres `IPostgresJobDispatcher.StageAsync(job, NpgsqlTransaction, DispatchOptions, ct)`)
inside the EF transaction of the business write, so the job commits or rolls back with it; work decided without a write
uses `DispatchAsync`. Payloads carry ids, never rendered content.

| Queue | Job (Runwick name) | Uniqueness | Retry |
|---|---|---|---|
| mail | `mail.verification` (user id; the link is minted by the handler), `mail.order-paid`, `mail.order-shipped`, `mail.order-cancelled`, `mail.order-refund` (order id), `mail.installment-paid` (installment id), `mail.installment-reminder` (plan, kind reminder/overdue, day), `mail.installment-statement` (user, day), `mail.ticket-reply` (ticket id), `mail.care-decision` (kind + id) | verification while active; one-shot facts within a 2–30 day window; ticket replies and care decisions are not unique (each is its own event) | 5 executions, backoff 10 s to 10 min |
| events | `events.realtime-push` (SignalR method, payload, group/user), `events.delete-assets` (storage keys) | none | 3 / 5 executions |
| cron | `cron.scheduled` (schedule name) | serialized per schedule | 1 execution |

Schedules (`CronJobs.Schedule`, misfire RunOnce, overlap Serialize): sales-rollup, bnpl-reminders, bnpl-overdue,
bnpl-statement (one email per customer across all their plans), ticket-escalation, order-timeout (cancels stale unpaid
orders in batches; stock release, alerts and the cancel email commit together), prune-product-views, orphan-assets,
prune-unverified. The `prune-jobs` schedule is gone; Runwick retention replaces it.

The Runwick cookie is independent of `.vis.auth` and grants only Runwick management permissions. The dashboard and
management API are mapped outside the app's Inertia pipeline; demo triggers opt out of the storefront antiforgery
middleware because they are intentionally open. Demo jobs never touch business data.

## Realtime (StoreHub, `/realtime`)

| Event | Audience | Payload |
|---|---|---|
| OrderChanged | `admins` group | { orderNumber, status, paymentStatus } |
| Notification | one user / `admins` group | { title, body, href } |
| OrderTimelineUpdated | owning user | { orderNumber } |
| StockAlert | `admins` group | { productId, stock } |

The same deliveries also keep the live-props channels (Inertia.Net 1.6) in step, published by the worker after the raw
sends: the shared `notifications` prop (and the `account/Alerts` page) listens for `Notification` on
`private:users.{userId}` (200 ms throttle), admin dashboards listen for `AdminPulse` on `private:admins` (authorized by
the admin role claim; the `admin/Index` KPI strip — `kpis`/`revenueSeries`/`lowStock`/`attention`, the `admin/Analytics`
deferred props, `admin/Orders` `orders`, `admin/Deliveries` `deliveries`/`delivered`/`deliveredToday`/`riders`/`zones` and the `admin/Order` detail
props; all of these are lazy props - `Prop(...).Live(...)` - so an event runs only the affected prop's query), and `orders/Track` / `account/Order` timelines listen for `OrderTimelineUpdated` on the public
`public:orders.{orderNumber}` channel (a link holder may join; the page reload still authorizes its own data reads and
publishes carry no values). Publish points remove the NotificationsCache/StoreCache entries before enqueueing, so each
live reload reads fresh rows. The hub is mapped at `/inertia-live`; pages without these listeners never connect.


## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers stage or dispatch Runwick jobs and return after the commit. Workers deliver with retries; HTTP success does not promise immediate delivery. Runwick schedules materialize `cron.scheduled` jobs, and the Runwick worker pools run them alongside the mail, events and (when `RUNWICK_DEMO_ENABLED`) demo sleep jobs. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.


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

CLI: `dotnet run --project src/TechLandier -- --create-admin` prompts for name, email and a hidden password, migrates,
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

The full paginated inbox remains `/account/alerts`: 25 per page via `ToPageAsync`, bound from the `page` and
`perPage` query parameters through `PageRequest` (unsupported values fall back to page 1); the visible page and the
unread count reload themselves when a `Notification` event arrives on the owner's channel. Existing read-all and
individual read endpoints are unchanged.
Admin notifications for order changes, stock alerts and operational pulses are committed with their queued push, so
an offline administrator can read them later. Customer order/care/support alerts continue to use the existing Alerts table.

## Security headers, guest support and rate limits

- Every response carries `X-Content-Type-Options: nosniff`, `X-Frame-Options: DENY`, `Referrer-Policy: strict-origin-when-cross-origin`, `Permissions-Policy` and `Cross-Origin-Opener-Policy: same-origin`; HSTS (365 days, includeSubDomains) is added outside Development. HTML responses add a `Content-Security-Policy` built by `Infrastructure/SecurityHeaders.cs`: `script-src 'self' 'nonce-{per request}' 'sha256-{inline theme script of app.html}'` (Inertia.Net stamps the nonce on the page-data script and Vite tags through `InertiaOptions.CspNonce`), `style-src 'self' 'unsafe-inline' fonts.googleapis.com`, `font-src fonts.gstatic.com`, `img-src` / `connect-src` with the object-storage origins from config, same-origin `ws(s)` for `/realtime` and `/inertia-live`, `frame-ancestors 'none'`, `object-src 'none'`, `base-uri 'self'`, `form-action 'self'`, plus `upgrade-insecure-requests` outside Development. In Development the Vite dev server origin (and its HMR socket) is added. The Runwick dashboard is mapped ahead of this pipeline and is not covered.
- `POST /support` (public guest complaint, JSON/Inertia form { name, email, subject, category, message, orderNumber? }): limited to 5 per 10 minutes per client IP (`guest-support` policy). Name 100, email 254, subject 150, message 4000 and order number 40 characters at most (server validation; also on ticket create and replies). A known order number links the ticket to that order and its customer only when the email matches the order customer's email (case-insensitive); otherwise it stays a guest ticket under the email given, with no order.
- Account and admin route groups apply the `writes` rate limit (120 per minute per user, mutations only: GET/HEAD are never counted). A rejected Inertia request is answered with `Back()` + the error `rateLimit` + an error toast; other clients get a bare 429.
- `RealtimeOriginMiddleware` checks `Origin` against the request origin for both SignalR hubs, `/realtime` and `/inertia-live`.
- Signed-in `/account`, `/admin` and `/checkout` pages set `encryptHistory`; `POST /logout` clears the history key.

## Email, PDFs and unsubscribe

- **Templates** (`Infrastructure/Emails.cs`): verify, order-paid (items, totals, address, receipt number, "Download receipt (PDF)"), order-shipped, order-cancelled, order-refund, installment-paid, installment-reminder, installment-overdue, installment-statement (one per user across active plans, a "Download statement (PDF)" link per plan), ticket-reply (includes the staff reply text) and care-decision. One responsive table layout in the TechLandier brand: hidden preheader, wordmark, one primary button, `color-scheme` meta with `prefers-color-scheme` and `[data-ogsc]` dark styles, phone styles, a plain-text twin for each, every interpolated value HTML-encoded, money through `Pricing.Show`. Handlers build an `EmailContext` (store identity from `StoreSettings` plus the public base URL) through `EmailContexts`.
- **Footers:** verify says why it was sent. Order, ticket and care mail are transactional: reason plus a link to `/account/preferences`, no unsubscribe. Installment mail (paid, reminder, overdue, statement) is preference-backed (`EmailInstallmentReminders`) and adds the reason, the preferences link and an Unsubscribe link.
- **One-click unsubscribe (RFC 8058):** `GET /email/unsubscribe?token=` shows a confirm page and changes nothing (link scanners prefetch). `POST /email/unsubscribe?token=` (the page's button, or the mail client's one-click POST) sets `EmailInstallmentReminders` to false; idempotent, anonymous, exempt from antiforgery (`PipelineSetup` skips `/email/unsubscribe`). The token is a Data Protection time-limited protector (purpose `TechLandier.Email.Unsubscribe.v1`, 60 days) over user id and mail kind; a forged or expired token answers 400. `Mailer` sends `List-Unsubscribe: <url>` and `List-Unsubscribe-Post: List-Unsubscribe=One-Click` in the Maileroo `headers` object on those messages only.
- **Dev capture:** mail to the seeded demo addresses is written to `{DATA_DIR}/captured-mail/<id>.txt` and `<id>.html` and never sent.
- **Dev preview (Development only, not mapped elsewhere):** `GET /dev/emails` lists every template; `GET /dev/emails/{name}` renders it with sample data and `?format=text` returns the plain-text twin.
- **PDFs** use QuestPDF (Community license, set at startup; system fonts off, Lato embedded in the package). They are generated per request, streamed with `Content-Disposition: inline` and `Cache-Control: private, no-store`, and never stored. Client links are plain `<a href download>`: the account order page, the guest tracking page and each installment plan card (customer views only).

## Order plan summary and prices

- Every order DTO (OrderDetail, AccountOrderRow, AdminOrderRow) carries `plan: { count, paid } | null` (`PlanBrief`): the installment count and how many are paid, null when the order is not on a plan. Clients show a plan order as "On plan · paid/count paid", never a bare "Paid".
- `ProductCard`/`ProductDetail` send `compareAtPrice` = the list `price` while a discount applies (else null), so the strike-through, the percent and the saving always agree. The `CompareAtPrice` column is no longer shown.
- Device insurance is priced on the price paid per unit (`Pricing.Premium` / `Pricing.Coverage`: 15% premium, 90% payout), at checkout and when attached later from a delivered order.
