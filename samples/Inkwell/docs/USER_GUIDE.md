# Using Inkwell administration

## Demo accounts and the first administrator

Run these commands from the Inkwell directory:

```sh
dotnet run --project src/Inkwell -- --seed-demo
dotnet run --project src/Inkwell -- --create-admin
```

Seeding is optional. Fresh demo databases make `inertia.demo42.00000@yopmail.com` an administrator;
demo passwords are `Password@12345`. Rerunning resumes the seeder and updates demo credentials once
without deleting live content or restoring deliberately removed administrator privileges.

On an existing database, the team-management migration leaves existing users as non-admins.
Bootstrap an administrator with `--create-admin`: enter a name, unique email and hidden password.
The command creates a verified administrator and exits; it does not overwrite or promote an existing email.
Inside the app container, use `dotnet Inkwell.dll --create-admin` or `--seed-demo`.

## Manage the team

Sign in as an administrator and select **Team management** from the account menu, or open `/admin/team`.
Add an administrator with a name, unique email and password of 12–128 characters.
Remove another administrator to revoke their privilege while retaining their author account and content.
You cannot remove yourself. Public sign-up cannot grant administrator access, and authorization checks
the current database flag on each request.

## Settings and notifications

Open `/settings` for profile, appearance, notification preferences and digest scheduling.
Inkwell is a publishing application: its settings concern authors and readers; it has no store hours,
delivery regions, installment plans, insurance claims or repairs.

The account header's speaker button enables notification chimes for incoming live notifications.
This preference is opt-in in the browser. Interact with the page after a reload if browser audio is locked.
The control does not request operating-system notification permission or replay all old notifications.
Workers must run for queued realtime delivery; consult [DEPLOY.md](DEPLOY.md) before separating workers
or adding web replicas.

See [SOCIAL_FEATURES.md](SOCIAL_FEATURES.md) for feeds, follows, recommendations and digests,
and [CONTRACT.md](CONTRACT.md) for team authorization and notification contracts.
