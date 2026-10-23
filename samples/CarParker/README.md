# CarParker

Rent a parking bay for one window or every week, get through the gate by plate, claim on Spot Cover if something
happens. .NET 10 Minimal APIs, Inertia.Net 1.4 and Svelte 5 with server-side rendering, on MongoDB.

## The domain

See the [user and admin guide](docs/USER_GUIDE.md) for MongoDB mode selection, lot hours, booking/claim details,
team management, terminal admin creation and notification sound.

- **Lots and bays.** Each lot has levels and sections of numbered bays (`B-07`) typed `standard | compact |
  handicap | covered | ev`, each `free | booked | occupied | blocked`. The lot page draws a live floor plan.
- **Bookings.** One window, or a weekly pattern materialised into up to 8 occurrences. Claiming a bay is a
  conditional update (`free → booked`), so two shoppers can't both win it; the loser gets a 409. Price is the hourly
  rate capped per 24 h, plus optional **Spot Cover** (12%, minimum $2).
- **Gate.** Guards check plates in and out against the window (±15 min). Overstays alert the customer and staff;
  bookings with no arrival after 30 min become no-shows and release the bay.
- **Money.** Mocked card / mobile money / pay-at-gate payments. Cancelling requests a refund by rule (≥24 h full,
  ≥4 h half, later none); admins approve, pay or reject.
- **Spot Cover claims**, an **alerts inbox** pushed over SignalR, guard **incident** logs, and **daily lot rollups**
  (occupancy, revenue, sessions) behind the admin analytics.

## How it uses Inertia.Net 1.4

| Feature | Where |
|---|---|
| `Scroll` | Lots listing (`<InfiniteScroll>`) |
| `Defer` + `.Parallel()` + `.Cache(key, ttl)` | Admin analytics: KPIs, series, top lots, refund mix. The Mongo driver is thread-safe, so the loaders run concurrently; cache keys carry lot and range |
| `MapInertia` | `/about`, `/policies` |
| `Once` | Shared `appName` |
| Precognition | Registration, booking, lot create/edit |
| SSR + SSR cache | All public pages |
| Vite prefetch, lazy XSRF cookie refresh, DevTools | `InertiaSetup.cs` |

## Run it locally

Prerequisites: .NET 10 SDK, Bun, Docker. MongoDB can be standalone or a replica set; the dev compose file starts a
single-node replica set on port 27018.

```sh
cp .env.example .env
docker compose -f docker-compose.dev.yml up -d mongo
bun install --cwd src/CarParker
bun run --cwd src/CarParker dev                                   # Vite
dotnet watch --project src/CarParker --urls http://localhost:5199
```

Seed demo data with `dotnet run --project src/CarParker -- --seed-demo`. Logins (password `Password@12345`):

Live demo: [https://carparker.yebi.africa](https://carparker.yebi.africa).

| Role | Email |
|---|---|
| Admin | `inertia.demo42.carparker.admin@yopmail.com` |
| Guard | `inertia.demo42.carparker.guard@yopmail.com` |
| Customer | `inertia.demo42.carparker.customer@yopmail.com` |

- Uploads need an S3-compatible endpoint in `.env`; without an email key, Development logs mail.
- Checks: `bun run --cwd src/CarParker build` (svelte-check + client + SSR) and `dotnet test` (uses a replica set
  on `localhost:27017`, or `INERTIA_TEST_CONNECTION`).

## Deploy

Compose (app + Mongo replica set) or the image alone against standalone MongoDB or a replica set, on a VPS, Coolify or Dokploy:
see [docs/DEPLOY.md](docs/DEPLOY.md).

## MongoDB mode

Set `MONGO_MODE=single` for standalone MongoDB (also the default outside production Compose), with a URL such as
`mongodb://localhost:27017/?directConnection=true`. Remove `replicaSet` from standalone connection URLs.
Set `MONGO_MODE=replica` for multi-document transactions, with a URL such as
`mongodb://localhost:27018/?replicaSet=rs0&directConnection=true`. Production Compose defaults to replica mode.
The aliases `replica-set` and `replicaset` also work. Invalid modes fail at startup; replica mode rejects a standalone server.
This applies to web requests, `--seed-demo` and `--create-admin`; no reseed is required to change mode.

Single mode preserves single-document conditional updates, including the bay claim, but writes across collections
cannot roll back together. A failed booking, refund or job enqueue can leave partial data requiring repair.
Use replica mode when those operations must be atomic. There is no automatic fallback on transaction errors.

Integration tests default to replica mode on port 27017. To test a disposable standalone server, set
`INERTIA_TEST_CONNECTION=mongodb://localhost:27019/?directConnection=true` and `INERTIA_TEST_MONGO_MODE=single`
before running `dotnet test tests/CarParker.Tests`. Each fixture creates and drops its own test database.

## Layout

```
src/CarParker/
  Features/<Feature>/   <Feature>.cs (routes, HTTP only) + <Feature>Service.cs (queries, rules, realtime)
  Data/                 AppDb (collections, indexes), demo seeder
  Jobs/, Cron/          durable job queue; TickerQ schedules with a database lease
  Infrastructure/       setup, storage, email, realtime hub
  ClientApp/            Svelte pages, layouts, components (app.ts, ssr.ts)
tests/CarParker.Tests/
```

Schedules (UTC): prune jobs 03:30 · orphan uploads hourly · prune
unverified 04:00 · policy expiry 04:00 · lot rollup every 15 min · reminders 07:05 · no-show and overstay checks
every 10 min. `CRON_ENABLED=false` turns them off.


AI workflow setup: [docs/ai/README.md](docs/ai/README.md). Feature conventions: [AGENTS.md](AGENTS.md). Optional worker/cron tuning: [docs/DEPLOY.md](docs/DEPLOY.md).

For the booking UI and queued live refresh check, start a disposable demo-seeded app, open it with playwright-cli from this sample directory, then run `playwright-cli run-code --filename=scripts/check-booking-browser.js`. The check signs in as demo customer/admin, temporarily blocks and clears a free bay, exercises date/time controls, and captures desktop/tablet/mobile/dark screenshots.


### Create an administrator from the terminal

```powershell
dotnet run --project src/CarParker -- --create-admin
```

Enter the name, email and password when prompted (password input is hidden). Existing accounts are never overwritten.
Use the team screen to add or remove administrators and guards. Demo passwords are `Password@12345` and demo emails use `@yopmail.com`.
Rerunning `--seed-demo` updates old seeded credentials once without resetting live data. Migrations apply automatically.

Team management is at `/admin/team`. Configure daily opening hours and time zones under Lots & spots; new bookings respect these hours. Open insurance claim details from Spot Cover. Enable notification chimes with the speaker button.

### Console UI verification

Against a disposable, demo-seeded local app, open `http://localhost:5199` with `playwright-cli` from this sample root,
then run `playwright-cli run-code --filename=scripts/check-console-browser.js`. This signs in as all three demo roles,
creates customer/staff accounts and lots, force-closes a demo session, checks search and panel behavior, and saves
light/dark desktop/mobile/tablet screenshots under `.playwright/shots/`. Never point this mutation check at live data.

The admin People directory supports name/email search and role filters, customer creation, and account detail pages.
Lot/staff creation, session details, guard incident reports and customer claim forms open in side panels. On mobile,
open the current-section menu to navigate the admin or customer console.
