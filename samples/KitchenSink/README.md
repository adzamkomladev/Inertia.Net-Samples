# KitchenSink

A .NET 10 port of [inertiajs/demo-v3](https://github.com/inertiajs/demo-v3), the Inertia.js v3 kitchen-sink demo:
a mini CRM and a feature showcase with the same Vue 3 + TypeScript, Tailwind CSS 4 and shadcn-vue UI.
Built with Inertia.Net 1.9.5, ASP.NET Core Minimal APIs and SQLite.

## What is inside

- **Mini CRM**: dashboard with deferred stats and recent activity, contacts with cursor infinite scroll,
  organizations with pagination, notes, favorites and optimistic updates.
- **Feature showcase**: useForm, the Form component, file uploads, validation and error bags, Precognition,
  optimistic updates, useFormContext, dotted keys, a Wayfinder-style route playground; links and HTTP methods,
  preserve state/scroll, view transitions, history, async requests, instant visits, manual visits, redirects,
  scroll management, URL fragments and QUERY visits; deferred/optional/once/merged/scroll props, polling,
  when-visible, big integers and live props; prefetching; remember, flash data and shared props; persistent,
  nested and layout-prop layouts; head management; global/once/visit/progress/location events; HTTP exceptions
  and network errors; and `useHttp`.
- **Live props**: the poll, reactions, ticks and the demo-reset banner refresh over Inertia.Net's SignalR
  transport (`/inertia-live`), using the same public `demo` channel the upstream demo uses for Reverb.
- **Authentication**: the shared demo login, cookie auth with Inertia redirects, session-backed flash/errors
  and a login throttle.

## How it uses Inertia.Net 1.9.5

| Feature | Where |
|---|---|
| `Defer` and groups | Dashboard stats, contact notes, deferred props demo, query visit matches |
| `Optional` + `WhenVisible` | Optional props, when-visible sections, lazy query results |
| `Merge` / `Prepend` / `MatchOn` | Prop merging demo |
| `Scroll` + infinite-scroll metadata | Contacts, organization members, infinite-scroll demo, cursor pagination |
| `Once` / `Fresh` / `Until` / `As` | Once props demo |
| **Live props** + `IInertiaLive` + SignalR | Poll votes, reactions, ticks, shared `demoResetAt` |
| `PreserveBigIntegers` | Big integers demo (per-request through a JSON element vs. a raw number) |
| `VersionResolver` | Simulated deployment on the location-event page |
| `UseInertiaRedirects()` | Cookie auth sends Inertia visits to login |
| `UseInertiaAntiforgery()` | XSRF cookie + header on every mutation |
| `State.UseSession()` | Flash, errors and the visitor name survive redirects |
| HTTP QUERY | Query visits and the links demo |
| `Back()`, `Location()`, fragment redirects | Navigation demos |
| `UseInertiaDevTools()` | Development page inspector |

The Vue pages come from the upstream demo. Wayfinder's generated files are replaced with a small, typed
`ClientApp/wayfinder` module so the pages keep their `.url()` / `.form()` call shape without PHP code generation.

## Run it locally

Prerequisites: .NET 10 SDK and Bun 1.3+.

```sh
cd samples/KitchenSink
cp .env.example .env
bun install --cwd src/KitchenSink
bun run --cwd src/KitchenSink dev          # Vite on :5173
dotnet run --project src/KitchenSink       # http://localhost:5170
```

The app creates `App_Data/kitchen-sink.db` on first run and seeds the demo login and CRM data.

- Login: `test@example.com`
- Password: `password`

Checks:

```sh
dotnet test tests/KitchenSink.Tests
bun run --cwd src/KitchenSink typecheck
bun run --cwd src/KitchenSink build
```

### Browser tests

The Playwright suite starts the real app on a free port and drives Chromium. It is opt-in:

```sh
dotnet build tests/KitchenSink.BrowserTests
pwsh tests/KitchenSink.BrowserTests/bin/Debug/net10.0/playwright.ps1 install chromium

# PowerShell
$env:RUN_BROWSER_TESTS = "1"; dotnet test tests/KitchenSink.BrowserTests
# bash
RUN_BROWSER_TESTS=1 dotnet test tests/KitchenSink.BrowserTests
```

The backend suite covers the CRM, auth, Precognition, QUERY visits, live-prop broadcasts, rate limits, demo
reset and a page/component smoke test for every showcase page.

## Demo data

The app also resets the demo data daily at 03:00 UTC in-process, so open pages receive the `DemoDataReset`
broadcast and show the reset banner. Set `Demo:DailyResetHourUtc` or `Demo:DailyReset=false` to change or disable it.

The reset command replaces CRM/live data but keeps the shared user signed in:

```sh
dotnet run --project src/KitchenSink -- --reset-demo
```

`--seed-demo` creates the demo data without deleting anything.

## Deploy

One container, port 8080, a `/data` volume holding the SQLite database and Data Protection keys:

```sh
docker compose up --build
```

## Layout

```
src/KitchenSink/
  Program.cs                    composition root and CLI flags
  Features/Auth/                login, logout and the login throttle
  Features/Crm/                 dashboard, contacts, notes and organizations
  Features/Showcase/            one file per showcase category + live props
  Data/                         AppDb, entities, demo seeder and reset
  Infrastructure/               Inertia, auth, pipeline, rate limit, form input
  ClientApp/                    the upstream Vue UI + wayfinder shim
  tests (../tests)              xUnit feature tests and opt-in Playwright tests
```
