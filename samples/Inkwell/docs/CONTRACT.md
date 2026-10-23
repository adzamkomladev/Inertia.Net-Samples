# Inkwell feature contracts

Feature routes own HTTP and Inertia responses. Services return POCOs/records; types live with the owning feature. Existing URLs, prop names and authorization policies remain stable. See docs/SOCIAL_FEATURES.md for social behavior.


## Background delivery and feature boundaries

Services return records/data; routes and response filters own HTTP/Inertia conversion. Realtime triggers persist jobs and return after enqueueing. Workers deliver with retries; HTTP success does not promise immediate delivery. Upload confirmation checks storage metadata before making an asset ready; rejected-object cleanup is queued in the presigned-upload samples. Existing route URLs and prop names remain unchanged.


## Administrator team and notification sound

GET `/admin/team` renders `Admin/Team` with { members: [{ id, name, email }] }. POST accepts { name, email, password }
and creates a verified administrator through Identity. POST `/admin/team/{id}/remove` removes another administrator's
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

`GET /notifications` renders `Notifications/Index { items }` (latest 100). `POST /notifications/read-all` marks all
owned rows read. The additive NotificationInbox migration creates the Notifications table with a user/time index and
user-delete cascade. Likes and comments commit notifications in the same transaction as the action, independently of
email preferences; repeated likes and self-activity do not create duplicate/self notifications. Existing queued realtime
delivery remains responsible for live toasts and inbox refreshes. Historical activity is not backfilled.
