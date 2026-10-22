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
The additive TeamManagement migration defaults existing users to non-admin; use the CLI to bootstrap an admin.

`dotnet run --project src/Inkwell -- --create-admin` migrates, prompts for name, email and a hidden password, creates
a verified admin and exits (nonzero on validation failure). New demo databases mark `inertia.demo42.00000@yopmail.com`
as an admin. Demo passwords are `Password@12345`; a one-time credentials checkpoint updates seeded passwords on resume
without changing live accounts, removing content or regranting removed administrator privileges.

The account header includes an opt-in speaker control for existing SignalR Notification events. Browser audio unlocks
through user interaction; no operating-system notification permission is required. Blog profile, appearance, notification
and digest scheduling settings remain in `/settings`; store/delivery/repair concepts do not apply to Inkwell.
