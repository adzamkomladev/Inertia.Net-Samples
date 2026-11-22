# Inkwell feature contracts

Feature routes own HTTP and Inertia responses. Services return POCOs/records; types live with the owning feature. Existing URLs, prop names and authorization policies remain stable. See docs/SOCIAL_FEATURES.md for social behavior.

## Pagination

Inertia.Net 1.6.3 pagination: routes bind `PageRequest`/`CursorRequest` (query `page`/`perPage`/`cursor`, clamped by
configured defaults), services page with the `Inertia.Net.EntityFrameworkCore` helpers, and paged props keep their
names and `data` wrappers.

- Latest feed `/posts` and topic pages `ToCursorPageAsync(r => r.CreatedAt, r => r.Id, descending)`; search
  `POST... GET /posts?q=` uses the delegate `Pagination.PaginateAsync` with FTS5 `bm25` ordering and
  `LIMIT/OFFSET` (rank has no keyset); both surface `Scroll` metadata via the page value itself.
- Social timelines (`/me/posts`, `/me/archives`, `/bookmarks`, `/users/{id}`) keyset over (repost-at/createdAt, id)
  newest first; the people directory `/users` keyset ascending over `Id`.
- Comment pages are `CursorPage<CommentDto>`: the optional `comments` prop is a page object
  (`data`/`nextCursor`/`hasMore`), the drawer merges it, and `GET /posts/{id}/comments?cursor=` answers JSON
  `{ data, nextCursor, hasMore }`.
- A malformed or foreign cursor loads the first page (Debug-logged, values not exposed).

## Runwick operations

| Method | Path | Auth | Result |
|---|---|---|---|
| GET / POST | /runwick, /runwick-api/v1/... | Runwick cookie | Dashboard and management API (separate `Admin` / `Admin@12345` login) |
| GET / POST | /runwick-login, /runwick-logout | anonymous / Runwick cookie | Sign-in form and sign-out |
| GET | /demo/jobs | anonymous | Demo job catalog and trigger URLs (mapped only when `RUNWICK_DEMO_ENABLED`; default on in Development only) |
| GET | /ping | anonymous | 204 heartbeat the client uses for its offline indicator (unnamed; the route generator skips it) |
| POST | /demo/jobs/{kind} · /demo/jobs/{kind}/batch?count=100 | anonymous | Dispatch one or up to 100 side-effect-free sleep jobs (same flag) |

All background work runs on Runwick; the old `Jobs` outbox table is dropped by the `DropLegacyJobs` migration.
Queues: `cron` (`cron.scheduled`), `mail` (`mail.verification`, `mail.comment`, `mail.like`, `mail.weekly-digest`,
`mail.recommendation-digest`) and `events` (`events.post-changed`, `events.post-removed`, `events.analytics-reload`).
Payloads carry ids only.
Work caused by a database write (like, comment, view, digest enqueue) is staged with `ISqliteJobDispatcher.StageAsync` in
the same SQLite transaction, so it commits or rolls back with the write; work with no write (verification resend, cron
fan-out) is dispatched directly. Uniqueness: verification per user while active, like mail per (post, 15-minute window), weekly
digest per (user, ISO week) for 8 days, recommendation digest per delivery. View counts are recorded inline in the
request transaction (no job); interest profiles are rebuilt lazily and in batches of dirty profiles by cron.

`events.analytics-reload` folds a burst of owner dashboard reloads into one trailing publish (playbook: at most one
trailing "something changed" event per second per channel — the first event of a burst publishes at once and
`RealtimeDelivery` dispatches the trailing job, unique while it is queued).

## Auth POSTs render the next page inline

An Inertia visit that completes a write whose landing page is known renders that page directly from the POST (the
playbook's "return the next page from the POST"): `POST /login` (no returnUrl) renders the feed at url `/posts`,
`POST /register` renders `Auth/VerifyEmail` at `/email/verify`, `POST /logout` renders `Auth/Login` at `/login`, and
`POST /posts` (publish) and `PUT /posts/{id}` (update) render `Posts/Show` at the post's own url. `UrlResolver`
resolves the page url through `Infrastructure/PageUrl`, and the render re-issues the antiforgery cookie for the new
identity (`PageUrl.RefreshXsrf`). Plain form posts and error outcomes keep the redirect responses. Rate limits are
sized for a room behind one shared address (×100 of a single user's budget); the per-identity limits (`writes`) and
the account lockout remain the abuse control.

## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers stage Runwick jobs and return after commit. Workers deliver with retries; HTTP success does not promise immediate delivery. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.


## Administrator team and notification sound

GET `/admin/team` renders `Admin/Team` with { members: [{ id, name, email }] }. POST accepts { name, email, password }
and creates a verified administrator through Identity. POST `/admin/team/promote` accepts { email } and gives an
existing, email-verified writer admin access (unknown, unverified or already-admin emails return an `email` error).
POST `/admin/team/{id}/remove` removes another administrator's
privilege while preserving the author account and content. Self-removal, duplicate emails and passwords outside
12–128 characters are rejected. The `admin` policy queries current `AppUser.IsAdmin` on every request. Public registration
never assigns this flag. Shared `auth.user` adds `isAdmin`, and admin users see Team management in their account menu.
The shared auth prop is UI data cached per user for 3 minutes fresh / 1 minute stale; profile, preference, email
verification and admin-role writes invalidate it immediately, while authorization keeps using the current database
policy. The additive TeamManagement migration defaults existing users to non-admin; use the CLI to bootstrap an admin.

`dotnet run --project src/Inkwell -- --create-admin` migrates, prompts for name, email and a hidden password, creates
a verified admin and exits (nonzero on validation failure). New demo databases mark `inertia.demo42.00000@yopmail.com`
as an admin. Demo passwords are `Password@12345`; a one-time credentials checkpoint updates seeded passwords on resume
without changing live accounts, removing content or regranting removed administrator privileges.

The account header includes an opt-in speaker control for existing SignalR Notification events. Browser audio unlocks
through user interaction; no operating-system notification permission is required. Blog profile, appearance, notification
and digest scheduling settings remain in `/settings`; store/delivery/repair concepts do not apply to Inkwell.

## In-app notification center

Every authenticated page shares `notifications: { unreadCount, items }` (latest 10, newest first); guests receive null.
The summary is cached per user for 5 minutes fresh / 1 minute stale and invalidated by every create or read path; it is
UI data and never authorizes access. The bell opens a keyboard-accessible panel in every signed-in shell. A queued live
notification refreshes this prop and the inbox when open; reconnect also refreshes it. Counts and mark-read operations
always use the authenticated user, never a user ID supplied by the client. Partial reloads preserve in-progress
navigation and forms.

The summary and the inbox rows are Inertia live props (Inertia.Net 1.6): the pages declare a listener for the
`Notification` event on the per-user private channel `users.{userId}` (throttle 200 ms), and every publish point
(notification creation, mark-read elsewhere) removes the cache entry before queuing, so each refresh reads fresh rows.
The raw `/realtime` hub keeps the `Notification` message for the toast and opt-in speaker only.

`GET /notifications` renders `Notifications/Index { items }` (latest 100). Each item is `{ id, kind: 'like'|'comment'|'notice', title, href, at, readAt, actor: { id, name, avatarUrl } | null, postTitle | null }`; the client groups consecutive rows of one kind on one post (`Ann and 2 others liked …`). The `NotificationActors` migration adds `Kind`, `ActorId` (set null on user delete) and `PostId` (cascade) and backfills kind/post from existing rows. `POST /notifications/read-all` marks all
owned rows read. The additive NotificationInbox migration creates the Notifications table with a user/time index and
user-delete cascade. Likes and comments commit notifications in the same transaction as the action, independently of
email preferences; repeated likes and self-activity do not create duplicate/self notifications. Existing queued realtime
delivery remains responsible for live toasts and inbox refreshes. Historical activity is not backfilled.

## Profiles, author cards and topic browsing

`AppUser.Bio` (nullable, at most 280 characters) is added by the additive ProfileBio migration. PUT `/settings/profile`
accepts an optional `bio`; it is trimmed and blank clears it. `Settings/Index` adds `profile.bio`. Every user summary
(`/users` rows, `Users/Show { user }` and the post author card) adds `bio` and `joinedAt` (UTC). The demo seeder fills
bios in a resumable `bios` checkpoint, so existing demo databases receive them on the next `--seed-demo`.

`Posts/Show` adds the optional `author` prop: the author's user summary with the viewer's follow state. It and the
optional `comments` prop (a `CursorPage`, `data`/`nextCursor`/`hasMore`, 50 per page newest first) are never sent on
the first visit; the client asks for each with a partial reload (`WhenVisible`, 600 px / 400 px ahead) as the reader
scrolls toward it, so a visit that ends at the article runs no comment or profile queries. Posting a comment reloads
only `comments`.

Pages served to a signed-in user set Inertia `encryptHistory`, so page props kept in browser history are unreadable
after the session's history key is cleared on sign-out.

GET `/posts?topic=<topic>` filters the latest feed to one topic, keeping the keyset cursor. The topic is normalized
with the post-topic rules; an invalid value is ignored (all posts) rather than rejected. `Posts/Index` adds
`topic` (the active topic or null, always sent) and `topics: [{ topic, count }]`, the 12 most used topics on public
posts. Topic counts are a cached prop shared by all visitors (10 minutes fresh / 5 minutes stale) and are not
invalidated by writes: popularity is an aggregate where that lag is acceptable.

## Email

Every message uses one layout (`Infrastructure/EmailTemplates.cs`): paper background, serif wordmark and headings, one
terracotta action, hidden preheader, dark-mode and phone styles, and a plain-text twin. Optional mail (comment, like,
weekly stats, daily recommendations) carries a footer reason, an Email settings link, a visible Unsubscribe link and the
RFC 8058 headers `List-Unsubscribe: <url>` + `List-Unsubscribe-Post: List-Unsubscribe=One-Click`. Verification mail is
transactional and has neither.

| Method | Path | Auth | Result |
|---|---|---|---|
| GET | /email/unsubscribe?token= | anonymous | Confirmation page with a POST button; changes nothing (link scanners are safe) |
| POST | /email/unsubscribe?token= | anonymous, no antiforgery | Turns the token's preference off (comments, likes, weekly, daily); 400 page for a bad/expired token |
| GET | /email/unsubscribe/daily?token= | anonymous | Redirects old daily-digest links to the page above |
| GET | /dev/emails, /dev/emails/{name}[?format=text] | Development only | Every template rendered with sample data |

Tokens are Data Protection payloads `userId|expiresUnix|kind` (60 days); old daily-digest tokens are still accepted.
Like emails are batched: a like stages `LikeNotification(PostId, Window)` (15-minute windows, delayed 15 minutes,
unique per post and window), and the handler mails the window's likes that are still unread in the app, as one
"Ann and 3 others liked …" message. Comment emails link to `/posts/{id}#comments` and quote a markdown-stripped excerpt.
