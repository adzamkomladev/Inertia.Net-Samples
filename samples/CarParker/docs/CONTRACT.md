# Route & prop contract

Frontend types live in `ClientApp/types/index.d.ts`. JSON is camelCase. All non-GET requests need the antiforgery
header (`X-XSRF-TOKEN` from the `XSRF-TOKEN` cookie); Inertia's router adds it automatically, `fetch` callers must add it.

Route handler methods (in the feature `.cs` route files) compose the Inertia response; services return plain data/outcomes.

`MONGO_MODE=single` (default outside production Compose) executes transaction callbacks in a session without
cross-document rollback. `MONGO_MODE=replica` requires a replica set or sharded cluster and wraps those callbacks
in transactions. This includes booking writes, refunds, lot creation, registration, upload cleanup and demo seeding.
Single-document guards remain atomic in either mode; single-mode failures may require partial-data repair.
Routes and props are unchanged. See DEPLOY.md for connection settings.

## Public / storefront

| Method | Path | Auth | Result |
|---|---|---|---|
| GET | / | public | `Home` { hero + lot cards + how-it-works } |
| GET | /lots?q=&type=&maxPrice=&sort=&page= | public | `lots/Index` { lots: Page<LotCard> (Scroll, wrapper `data`, totals/lastPage from Inertia.Net.MongoDB), filters: { q, type, maxPrice, sort } } |
| GET | /lots/{id} | public | `lots/Show` { lot, spots: SpotView[], tariff, stats } — spot map is live via SignalR |
| GET | /lots/{id}/book?spot=&windowType=&date=&timeFrom=&timeTo=&daysOfWeek=&weeks= | verified | `lots/Book` { lot, tariff, spots, spotsWindow, prefillSpot, vehicles: VehicleView[] (the signed-in user's saved plates; the default prefills the plate) }. The window query uses the POST's lot-local fields. With a valid window (inside the lot's hours) `spots[].state` is the bay's state for that window: `blocked`, `booked` when a Scheduled/Active occurrence of a Confirmed reservation overlaps any slot, else `free`; `spotsWindow` echoes it (`single\|date\|timeFrom\|timeTo` or `recurring\|timeFrom\|timeTo\|daysOfWeek\|weeks`). Without one, `spots` are the live states and `spotsWindow` is null. The page sends the window on a debounced partial reload (`only: ['spots','spotsWindow']`); two reads (lot's confirmed reservations, then one occurrences `$in` + overlap query) |
| GET | /about, /policies | public | `about/Policy` { page: "about"\|"policies" } |
| GET | /error | public | `Error` { status } |

## Customer (auth + verified)

| Method | Path | Result |
|---|---|---|
| GET | /account | `account/Index` { account: { kpis, nextVisit, activeSession (with windowEndUtc and lotPhone: past the window end the card shows the overstay and a call-the-lot link), unreadAlerts } } |
| GET | /account/bookings?view=upcoming\|past&page= | `account/Bookings` { bookings: BookingView[] (20 per page), page, hasMore, view }. `upcoming` (default): confirmed bookings not yet ended, soonest next visit first; `past`: cancelled, completed or ended, latest end first (index `user_end`) |
| POST | /account/bookings | body { lotId, spotId, windowType, date?, timeFrom?, timeTo?, daysOfWeek?, weeks?, startAtUtc?, endAtUtc?, plateNo?, insurance, method, attemptId?, note? } → booking + payment + toast. `date`/`timeFrom`/`timeTo`/`daysOfWeek` are read on the lot's clock (`Lot.TimeZoneId`) and stored as UTC instants; a weekly 09:00 stays 09:00 local across DST, and a time inside a spring-forward gap moves on by the gap's real length (02:15 on Lord Howe's 30-minute change becomes 02:45) and a repeated fall-back time resolves to standard time, the later instant (client `zonedToUtc` and server `LotTime.ToUtc` agree). `startAtUtc`/`endAtUtc` remain for API clients when `date` is absent. Any bay that is not blocked and has no overlapping booking can be reserved (a car parked now or another day's booking does not block it); in replica mode the booking transaction first bumps `Spot.BookingVersion` and re-checks overlaps after that write, so concurrent bookings of one bay conflict on the spot document and the loser (even one that stalled) returns the `spotId` error (409 path); single mode, with no transactions, keeps a best-effort 30 s lease (`Spot.BookingLockUntil`) |
| GET | /account/bookings/{id} | `account/Booking` { booking: BookingView (with `lotTimeZoneId`), payment, refund, sessions, policy, cancelPreview: { rule, amountCents, paid, nextStartAtUtc } \| null }. `cancelPreview` is what cancelling now would request under RefundRules (null once nothing is left to cancel; `amountCents` 0 when unpaid) |
| GET | /account/bookings/{id}/receipt.pdf | PDF attachment `receipt-{id8}.pdf` (application/pdf, generated per request, `Cache-Control: private, no-store`); owner or admin, 404 missing, 403 another user's booking |
| GET | /account/bookings/{id}/refund-receipt.pdf | PDF attachment `refund-receipt-{id8}.pdf`; same authorization; 404 when the booking has no refund |
| GET | /dev/emails, /dev/emails/{name}?format=text | Development only (unmapped elsewhere): index of every email template, rendered HTML or plain text with sample data |
| POST | /account/bookings/{id}/cancel | → refund requested per rule matrix + toast; refused only while a session is open on this booking's own windows |
| POST | /account/bookings/{id}/pay | body { method } → payment paid (mock gateway) + toast |
| GET | /account/payments?page= | `account/Payments` { payments (20 per page, newest first; a paid charge reads `refund_pending` while its booking's cancellation refund is requested or approved, `refunded` once paid out), refunds (those of the payments on the page), page, hasMore } |
| GET | /account/insurance | `account/Insurance` { policies + claims } |
| POST | /account/insurance/{policyId}/claims | body { kind, description, incidentOn } → Back() |
| GET | /account/alerts | `account/Alerts` { alerts } |
| POST | /account/alerts/read | marks all read → Back() |

## Guard console (role `guard`)

| Method | Path | Result |
|---|---|---|
| GET | /guard?lot= | `guard/Console` { lots, lotId, lotTimeZoneId, spots, queue (today's occurrences), openSessions (each with `windowEndUtc`, the booked window's end, where overstay grace starts), exitedSessions }; every time shows on the lot's clock |
| POST | /guard/check-in | body { spotId, plateNo } → Back() + toast; validates occurrence window ±15 min; the bay may be Booked or Free (a bay with another booking ahead is returned to Booked, not Free, by checkout, no-show and cancel) |
| POST | /guard/check-out | body { sessionId } → Back() + toast |
| POST | /guard/sessions/{id}/resolve | body { note } → resolves overstay + toast |
| GET | /guard/board | `guard/Console` { boardOnly: true, lots, openSessions, exitedSessions } |
| GET | /guard/incidents?lot=&status= | `guard/Incidents` { lots, filterLot, filterStatus, incidents } |
| POST | /guard/incidents | body { lotId, kind, plateNo?, description } → Back() |
| POST | /guard/incidents/{id}/resolve | body { note } → Back() |

## Admin (role `admin`)

| Method | Path | Result |
|---|---|---|
| GET | /admin | `admin/Index` { overview: kpis + needs-attention counters } |
| GET / POST | /admin/lots | `admin/Lots` { lots } / create → Back() + toast |
| GET | /admin/lots/{id} | `admin/Lot` { lot, spots } |
| PUT | /admin/lots/{id} | update tariffs/details → Back() + toast |
| POST | /admin/lots/{id}/spots/{spotId}/override | body { state, note } → Back() (manual block/clear) |
| GET | /admin/bookings?q=&status= | `admin/Bookings` { bookings, query, statusFilter }; literal case-insensitive customer name/email, lot or plate search before newest-100 limit |
| GET | /admin/bookings/{id} | `admin/Booking` { booking, payment, occurrences, sessions } |
| POST | /admin/bookings/{id}/cancel | admin cancellation → Back() + toast |
| GET | /admin/sessions?q= | `admin/Sessions` { openSessions, exitedSessions, query }; literal case-insensitive plate, lot or bay search before newest-100 exit limit |
| POST | /admin/sessions/{id}/force-close | body { note } → Back() |
| GET | /admin/refunds | `admin/Refunds` { pending, decided } |
| POST | /admin/refunds/{id}/approve | body { amountCents } → Back() + toast |
| POST | /admin/refunds/{id}/pay | commits the refund, then queues the `mail.refund-paid` job → Back() + toast |
| POST | /admin/refunds/{id}/reject | body { note } → Back() + toast |
| GET | /admin/insurance | `admin/Insurance` { policies, claims } |
| POST | /admin/claims/{id}/approve · /pay · /reject | → Back() + toast (pay and reject queue the `mail.claim-decision` job) |
| GET | /admin/incidents?lot=&status= | `admin/Incidents` { lots, filterLot, filterStatus, incidents } |
| GET | /admin/users?q=&role= | `admin/Users` { users: AdminUserRow[], query, roleFilter }; literal case-insensitive name/email search and role filter before newest-200 limit |
| POST | /admin/users | body { name, email, password }; creates an unverified customer (role always customer), hashes password, validates and rejects duplicate emails → Back() + toast |
| GET | /admin/users/{id} | `admin/User` { detail: { user: AdminUserRow, bookings: BookingView[] } }; newest 100 account bookings, no credentials; 404 for missing account |
| POST | /admin/users/{id}/verify | toggle email verification → Back() |
| GET | /admin/analytics?days=7\|30\|90&lot= | `admin/Analytics` { days, lotFilter, lots, kpis: Defer group `kpis` (.Cache `lots:{lotId}:{days}:{prop}` 60 s), series: Defer group `charts` (.Cache), topLots: Defer group `charts` (.Cache), refundMix: Defer group `charts` (.Cache) } |

## Auth & account

| Method | Path | Auth | Result |
|---|---|---|---|
| GET / POST | /login, /register, /logout, /email/verify(+confirm), /email/verification-notification | guest/signed in | `Auth/Login`, `Auth/Register`, `Auth/VerifyEmail`; redirects + flash/toast as before. A successful login goes to a safe local `returnUrl` when one was requested, otherwise to the role home: `/guard` for guards, `/admin` for admins, `/lots` for customers |
| GET / PUT | /account/profile | verified | `account/Preferences` shared { profile, preferences, vehicles } |
| GET / PUT | /account/preferences | verified | theme cookie + notification toggles → Back() + toast |
| PUT | /account/vehicles | verified | body { vehicles: [{ plate, label?, isDefault }] } replaces the saved list (max 5, plates uppercased and unique, exactly one default) → Back() + toast; list errors under `vehicles` |
| POST | /uploads/presign · /uploads/{id}/confirm | signed in | JSON as before (avatar uploads); PNG/JPEG/WebP/GIF only (max 5 MB), the presigned PUT signs the exact Content-Length, confirm checks size, type and magic bytes and deletes a mismatching object; 422 ValidationProblem on bad type/size; 429 problem JSON when rate limited |
| GET | /email/unsubscribe/promo?token= | anonymous | data-protected promo unsubscribe (token TTL 180 days) |

## Runwick operations

| Method | Path | Auth | Result |
|---|---|---|---|
| GET / POST | /runwick, /runwick-api/v1/... | Runwick cookie | Dashboard and management API (separate `Admin` / `Admin@12345` login) |
| GET / POST | /runwick-login, /runwick-logout | anonymous / Runwick cookie | Sign-in form and sign-out |
| GET | /demo/jobs | admin | Demo job catalog and trigger URLs; mapped only when `RUNWICK_DEMO_ENABLED` (default: Development only) |
| POST | /demo/jobs/{kind} · /demo/jobs/{kind}/batch?count=100 | admin | Dispatch one or up to 100 side-effect-free sleep jobs (needs the antiforgery header) |

The dashboard login is rate limited (the `auth` policy), and `POST /runwick-logout` refuses a browser `Origin` that is not this host. Outside
Development the host refuses to start while the dashboard still uses `Admin` / `Admin@12345`: set `RUNWICK_ADMIN_USERNAME` and
`RUNWICK_ADMIN_PASSWORD`.

### Jobs

Runwick runs every background job; the Mongo `jobs` collection and its custom worker are gone (the collection is dropped at startup).
Payloads carry ids only; handlers load the rows and render the email, so a retry always mails current content.

| Job (name) | Queue | Dispatched by | Uniqueness | Executions |
|---|---|---|---|---|
| `SendVerificationEmail` (`mail.verification`) | mail | register, resend | while active, per user | 5 |
| `SessionEndedEmail` (`mail.session-ended`) | mail | session close (handler checks `EmailOnCheckout`) | 30 days, per session | 5 |
| `RefundPaidEmail` (`mail.refund-paid`) | mail | admin pays a refund | 30 days, per refund | 5 |
| `BookingConfirmedEmail` (`mail.booking-confirmed`) | mail | a booking commits (`BookingsService.CreateAsync`); skipped if cancelled before it runs | 30 days, per reservation | 5 |
| `ReservationReminderEmail` (`mail.reservation-reminder`) | mail | `reservation-sweeper` cron, one batch | 2 days, per occurrence | 5 |
| `ClaimDecisionEmail` (`mail.claim-decision`) | mail | claim paid / rejected | 30 days, per claim and decision | 5 |
| `DeleteAssets` (`events.delete-assets`) | events | rejected upload, `orphan-assets` cron | none (deleting a key twice is harmless) | 5 |
| `RealtimePush` (`events.realtime-push`) | events | every realtime trigger and alert | none | 3 |
| `ScheduledCronJob` (`cron.scheduled`) | cron (1 at a time) | Runwick schedules | per schedule occurrence | 1 |
| `DemoSleep*` | demo-* | `/demo/jobs` | none | 2 (only when the demo is enabled) |

**Consistency trade-off.** Runwick uses SQLite and cannot join a MongoDB transaction. A service commits its Mongo write first and dispatches the job
right after. A crash (or Runwick outage) between the two loses that one job, never the business write; the business write is never rolled back by a
failed dispatch. Realtime dispatch is best effort (logged, never fails the request); mail and cleanup dispatch errors surface to the caller.
Delivery is at least once, so handlers are idempotent: alerts use a unique `sourceKey`, the reminder handler skips an occurrence whose alert exists,
cron sweeps are deduplicated by alert markers, and uniqueness keys make a repeated dispatch reuse the first job. Upload cleanup dispatches the object
deletion before removing the asset row, so a failure in between leaves a row the orphan cron retries.

## Realtime (ParkHub, `/realtime`)

| Event | Audience | Payload |
|---|---|---|
| SpotChanged | All | { lotId, spotId, state } |
| LotPulse | All | { lotId } |
| BoardChanged | `staff` group (admin + guard) | { kind: "session"\|"refund"\|"incident"\|"booking"\|"claim" } |
| Notification | one user or `staff` | { message, href } |

## Live props (Inertia.Net.Live.SignalR, `/inertia-live`)

The live transport of Inertia.Net 1.6 refreshes the props this table's events used to reload by hand:

| Prop | Channel | Event |
|---|---|---|
| shared `notifications` + `account/Alerts` alerts | `private:users.{userId}` | `Notification` (throttle 200 ms) |
| guard/Console `lots`,`spots`,`queue`,`openSessions` (+ board's `exitedSessions`) | `private:staff` | `BoardChanged` |
| admin/Index `overview`; admin/Sessions `openSessions`,`exitedSessions`; admin/Refunds `pending`,`decided`; admin/Insurance `policies`,`claims`; admin/Bookings `bookings`; incidents pages `incidents` | `private:staff` | `BoardChanged` |
| admin/Analytics `kpis`,`series`,`topLots`,`refundMix` (deferred + cached) | `private:staff` | `BoardChanged` |

Analytics cache tags are narrow: `kpis` entries carry `analytics:kpis` and `analytics:kpis:{lotId}` (all lots = empty guid), `refundMix` carries
`analytics:refunds`, `series`/`topLots` carry `analytics:rollup`. A booking, payment, cancellation or session in one lot drops that lot's and the all-lots KPIs; refund
requests/decisions drop the refund mix and every KPI view; lot create/edit and the rollup cron drop the rollup charts; a spot block changes none of them.
The signed-in principal lookup behind the auth cookie is cached 30 s per user (`PrincipalCache`) and dropped on verification, profile, preference and role changes.

Publishes ride the Runwick `RealtimePush` job (`events` queue). The handler sends the raw events above and additionally publishes on
the live channels — per-user notification channels for every user and staff recipient (their cache entries are removed
before enqueue, so each reload reads fresh rows), and one `BoardChanged` publish on the staff channel per board event.
The staff channel authorizes by role claims at subscribe time; demoted staff therefore re-authorize on reconnect, matching
the raw hub's connection-time group join. Storefront `lots`/`lots` chapters and the `lot` page keep the raw `SpotChanged`
pulses: those pages hold Scroll props, which cannot be live. Pages without these listeners never open `/inertia-live`.


## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers dispatch Runwick jobs after the write and return. Runwick delivers with retries; HTTP success does not promise immediate delivery. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.


## Team, operating hours and claim detail

- GET `/admin/team` → `admin/Team` { members }; POST creates { name, email, password, role: admin|guard }.
  POST `/admin/team/{id}/remove` demotes another staff account to customer, preserving bookings. Self-removal and
  duplicate email creation are rejected. Passwords must contain 12–128 characters. Admin policy protects all three routes.
- `dotnet run --project src/CarParker -- --create-admin` prompts for name, email and a hidden password and exits.
- `LotInput` / `LotCard` add `open24Hours`, `opens`, `closes`, `timeZoneId`; `LotCard` also returns `isActive`.
- `LotInput` / `LotCard` add `arrivalNotes` (≤ 280 chars, blank stored as null): entrance and height-limit tips shown on the lot page and checkout.
  Defaults preserve existing 24-hour lots. Hours apply daily in the configured zone; new single and recurring booking
  windows must fit within one open day. Existing bookings are preserved. Overnight windows require 24-hour access.
  Lot creation/editing and customer lot/booking screens expose these hours.
- Lot time: storage and the API stay UTC; everything a driver sees or enters for a lot (booking inputs, lot hours, booking
  summary and detail, account home, receipts, reminder and session emails) reads on the lot's clock. `BookingView`,
  account `nextVisit`/`upcoming`/`activeSession` rows carry `lotTimeZoneId`; recurring `pattern.timeFrom/timeTo` are
  lot-local. Demo lots seed as `America/New_York`; existing lots keep their stored zone (default `UTC`).
- GET `/admin/claims/{id}` → `admin/Claim` { detail: { claim, reservationId, coverageCents, premiumCents, email,
  decidedAt, paidAt } }; missing claims return 404. Existing approve/pay/reject routes remain authoritative.
- Notification sound is opt-in through the header speaker control. Staff realtime deliveries select current staff user IDs,
  preventing demoted staff from receiving private pushes through an existing socket.
- `--seed-demo` uses `inertia.demo42.carparker.{admin|guard|guard2|customer|customer2|customer3}@yopmail.com`,
  password `Password@12345`; a one-time credential phase updates only demo users on resumed databases.

Admin lot and team directories, customer booking history and guard session boards filter their already-scoped loaded rows locally. Creation forms use a focus-trapped right side panel with Escape dismissal. Admin sessions expose details and force-close confirmation in that panel.

## In-app notification center

Every authenticated page shares `notifications: { unreadCount, items }` (latest 10, newest first); guests receive null.
The summary is cached per user for 5 minutes fresh / 1 minute stale and invalidated by every alert create or read
path; it is UI data and never authorizes access. The bell opens a keyboard-accessible panel in every signed-in shell.
A queued live notification refreshes this prop and the inbox when open; reconnect also refreshes it. Counts and
mark-read operations always use the authenticated user, never a user ID supplied by the client. Partial reloads
preserve in-progress navigation and forms.

`POST /notifications/read-all` marks every alert owned by the current user read. The full inbox stays at
`/account/alerts`. New alerts are unread by default. Operational alerts persist for administrators (and guards when
the destination is a guard route); refund payouts and claim decisions have unique per-user source keys so job retries
do not duplicate inbox entries. Internal sweep markers are mapped to human-readable descriptions in page props.
Admins can open `/account` using Customer dashboard and return using Admin console, without changing identity or role.
