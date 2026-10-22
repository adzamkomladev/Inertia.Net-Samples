# Route & prop contract

Frontend types live in `ClientApp/types/index.d.ts`. JSON is camelCase. All non-GET requests need the antiforgery
header (`X-XSRF-TOKEN` from the `XSRF-TOKEN` cookie); Inertia's router adds it automatically, `fetch` callers must add it.

Route handler methods (in the feature `.cs` route files) compose the Inertia response; services return plain data/outcomes.

## Public / storefront

| Method | Path | Auth | Result |
|---|---|---|---|
| GET | / | public | `Home` { hero + lot cards + how-it-works } |
| GET | /lots?q=&type=&maxPrice=&sort= | public | `lots/Index` { lots: ScrollPage<LotCard> (Scroll, wrapper `data`), filters: { q, type, maxPrice, sort } } |
| GET | /lots/{id} | public | `lots/Show` { lot, spots: SpotView[], tariff, stats } — spot map is live via SignalR |
| GET | /lots/{id}/book?start=&days=&timeFrom=&timeTo=&weeks=&type= | verified | `lots/Book` { lot, spots, quote, availability } |
| GET | /about, /policies | public | `about/Policy` { page: "about"\|"policies" } |
| GET | /error | public | `Error` { status } |

## Customer (auth + verified)

| Method | Path | Result |
|---|---|---|
| GET | /account | `account/Index` { account: { kpis, nextVisit, activeSession, unreadAlerts } } |
| GET | /account/bookings | `account/Bookings` { bookings: BookingView[] } |
| POST | /account/bookings | body { lotId, spotId?, windowType, startAt?, endAt?, days?, timeFrom?, timeTo?, weeks?, plateNo?, insurance } → booking + payment + toast; 409 on lost race |
| GET | /account/bookings/{id} | `account/Booking` { booking: BookingView, payment, occurrences, sessions, policy } |
| POST | /account/bookings/{id}/cancel | → refund requested per rule matrix + toast |
| POST | /account/bookings/{id}/pay | body { method } → payment paid (mock gateway) + toast |
| GET | /account/payments | `account/Payments` { payments, refunds } |
| GET | /account/insurance | `account/Insurance` { policies + claims } |
| POST | /account/insurance/{policyId}/claims | body { kind, description, incidentOn } → Back() |
| GET | /account/alerts | `account/Alerts` { alerts } |
| POST | /account/alerts/read | marks all read → Back() |

## Guard console (role `guard`)

| Method | Path | Result |
|---|---|---|
| GET | /guard?lot= | `guard/Console` { lots, lotId, spots, queue (today's occurrences), openSessions, exitedSessions } |
| POST | /guard/check-in | body { spotId, plateNo } → Back() + toast; validates occurrence window ±15 min |
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
| GET | /admin/bookings | `admin/Bookings` { bookings } |
| GET | /admin/bookings/{id} | `admin/Booking` { booking, payment, occurrences, sessions } |
| POST | /admin/bookings/{id}/cancel | admin cancellation → Back() + toast |
| GET | /admin/sessions | `admin/Sessions` { openSessions, exitedSessions } |
| POST | /admin/sessions/{id}/force-close | body { note } → Back() |
| GET | /admin/refunds | `admin/Refunds` { pending, decided } |
| POST | /admin/refunds/{id}/approve | body { amountCents } → Back() + toast |
| POST | /admin/refunds/{id}/pay | queues RefundPaidEmail → Back() + toast |
| POST | /admin/refunds/{id}/reject | body { note } → Back() + toast |
| GET | /admin/insurance | `admin/Insurance` { policies, claims } |
| POST | /admin/claims/{id}/approve · /pay · /reject | → Back() + toast (pay queues ClaimDecisionEmail) |
| GET | /admin/incidents?lot=&status= | `admin/Incidents` { lots, filterLot, filterStatus, incidents } |
| GET | /admin/users | `admin/Users` { users } |
| POST | /admin/users/{id}/verify | toggle email verification → Back() |
| GET | /admin/analytics?days=7\|30\|90&lot= | `admin/Analytics` { days, lotFilter, lots, kpis: Defer group `kpis` (.Cache `lots:{lotId}:{days}:{prop}` 60 s), series: Defer group `charts` (.Cache), topLots: Defer group `charts` (.Cache), refundMix: Defer group `charts` (.Cache) } |

## Auth & account

| Method | Path | Auth | Result |
|---|---|---|---|
| GET / POST | /login, /register, /logout, /email/verify(+confirm), /email/verification-notification | guest/signed in | `Auth/Login`, `Auth/Register`, `Auth/VerifyEmail`; redirects + flash/toast as before |
| GET / PUT | /account/profile | verified | `account/Preferences` shared { profile, preferences } |
| GET / PUT | /account/preferences | verified | theme cookie + notification toggles → Back() + toast |
| POST | /uploads/presign · /uploads/{id}/confirm | signed in | JSON as before (avatar uploads); 422 ValidationProblem on bad type/size |
| GET | /email/unsubscribe/promo?token= | anonymous | data-protected promo unsubscribe (token TTL 180 days) |

## Realtime (ParkHub, `/realtime`)

| Event | Audience | Payload |
|---|---|---|
| SpotChanged | All | { lotId, spotId, state } |
| LotPulse | All | { lotId } |
| BoardChanged | `staff` group (admin + guard) | { kind: "session"\|"refund"\|"incident"\|"booking"\|"claim" } |
| Notification | one user or `staff` | { message, href } |


## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers persist jobs and return after enqueueing. Workers deliver with retries; HTTP success does not promise immediate delivery. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.
