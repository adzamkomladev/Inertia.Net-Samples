# CarParker

Rent a parking bay for one window or every week, get through the gate by plate, claim on Spot Cover if something
happens. .NET 10 Minimal APIs, Inertia.Net 1.4 and Svelte 5 with server-side rendering, on MongoDB.

## The domain

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

Prerequisites: .NET 10 SDK, Bun, Docker. MongoDB must be a replica set (transactions); the dev compose file starts a
single-node one on port 27018.

```sh
cp .env.example .env
docker compose -f docker-compose.dev.yml up -d mongo
bun install --cwd src/CarParker
bun run --cwd src/CarParker dev                                   # Vite
dotnet watch --project src/CarParker --urls http://localhost:5199
```

Seed demo data with `dotnet run --project src/CarParker -- --seed-demo`. Logins (password `CarParker-42-demo!`):

| Role | Email |
|---|---|
| Admin | `admin@carparker.test` |
| Guard | `guard@carparker.test` |
| Customer | `customer@carparker.test` |

- Uploads need an S3-compatible endpoint in `.env`; without an email key, Development logs mail.
- Checks: `bun run --cwd src/CarParker build` (svelte-check + client + SSR) and `dotnet test` (uses a replica set
  on `localhost:27017`, or `INERTIA_TEST_CONNECTION`).

## Deploy

Compose (app + Mongo replica set) or the image alone against your own replica set, on a VPS, Coolify or Dokploy:
see [docs/DEPLOY.md](docs/DEPLOY.md).

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

Schedules (UTC): backup 03:00 (mongodump to S3, keep 14) · prune jobs 03:30 · orphan uploads hourly · prune
unverified 04:00 · policy expiry 04:00 · lot rollup every 15 min · reminders 07:05 · no-show and overstay checks
every 10 min. `CRON_ENABLED=false` turns them off.


AI workflow setup: [docs/ai/README.md](docs/ai/README.md). Feature conventions: [AGENTS.md](AGENTS.md). Optional worker/cron tuning: [docs/DEPLOY.md](docs/DEPLOY.md).

For the booking UI and queued live refresh check, start a disposable demo-seeded app, open it with playwright-cli from this sample directory, then run `playwright-cli run-code --filename=scripts/check-booking-browser.js`. The check signs in as demo customer/admin, temporarily blocks and clears a free bay, exercises date/time controls, and captures desktop/tablet/mobile/dark screenshots.
