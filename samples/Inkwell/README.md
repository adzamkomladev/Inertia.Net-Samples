# Inkwell

A multi-user publishing platform: write essays in markdown, follow writers, get a personal feed, see how your posts
read. .NET 10 Minimal APIs, Inertia.Net 1.5.1 and React 19 with server-side rendering, on SQLite. One container.

## Features

See the [administration guide](docs/USER_GUIDE.md) for demo access, terminal admin creation, team management,
settings and notification sound.

- Accounts with email verification and password reset (ASP.NET Core Identity), Precognition-validated forms.
- Markdown editor with drafts, cover images (S3-compatible storage) and up to five topics.
- Latest, **For you** (ranked from your likes, comments and reads) and **Following** feeds; people directory and
  profiles; likes, reposts, bookmarks, archives, threaded responses.
- SQLite FTS5 search ranked with bm25 and highlighted snippets.
- Cookie-free page-view analytics with salted daily hashes.
- Live counters and notifications over SignalR; weekly and daily recommendation emails.

## How it uses Inertia.Net 1.5.1

| Feature | Where |
|---|---|
| `Scroll` + keyset cursors | Feeds, people, profiles, bookmarks (`<InfiniteScroll>` with a virtualized list) |
| `Defer` with groups | Post responses; analytics summary and charts |
| `.Parallel()` | Analytics props, each loader on its own `DbContext` from `IDbContextFactory<AppDb>` |
| `.Cache(key, ttl, staleFor, tags)` | Analytics props, keyed by user, post, range and window; tagged per user/post and served stale while one request refreshes |
| `IInertiaCache` invalidation | Creating, updating or deleting a post removes that author's analytics entries immediately |
| `Once` | Shared `appName` |
| `MapInertia` | `/posts/create` |
| SSR + SSR cache with `StaleFor` | Public post list and post pages; an expired render is served while one request refreshes it. Private pages render on the client |
| Precognition | Login and registration |
| Vite prefetch, lazy XSRF cookie refresh, DevTools | `InertiaSetup.cs` |

## Run it locally

Prerequisites: .NET 10 SDK, Bun 1.3+, an S3-compatible endpoint (only for uploads).

```sh
cp .env.example .env
bun install --cwd src/Inkwell
bun run --cwd src/Inkwell dev              # Vite
dotnet watch --project src/Inkwell         # http://localhost:5100
```

- Demo data: `dotnet run --project src/Inkwell -- --seed-demo` creates 5,000 users and 40,000 posts. Use an empty database if previously seeded with different counts.
- With `EMAIL_API_KEY` empty, emails go to the console.
- Local S3: `docker compose -f docker-compose.dev.yml --profile local-storage up -d` (RustFS on :9000).
- Checks: `bun run --cwd src/Inkwell build` (typecheck + client + SSR) and `dotnet test`.
- Migrations: `dotnet ef migrations add <Name> --project src/Inkwell -o Data/Migrations`.

## Live demo

[https://inkwell.yebi.africa](https://inkwell.yebi.africa) runs this sample with demo data.

- Admin: `inertia.demo42.00000@yopmail.com`
- Password: `Password@12345`

## Deploy

One image, a `/data` volume, port 8080. See [docs/DEPLOY.md](docs/DEPLOY.md) for a plain VPS, Coolify and Dokploy.

## Layout

```
src/Inkwell/
  Program.cs                composition root and CLI flags
  Features/<Feature>/       <Feature>Feature.cs (routes, HTTP only) + <Feature>Service.cs (queries, rules)
  Data/                     AppDb, entities, migrations, demo seeder
  Jobs/, Cron/              durable job queue and TickerQ schedules
  Infrastructure/           setup, storage, email, realtime
  ClientApp/                React pages, layouts, components (app.tsx, ssr.tsx)
tests/Inkwell.Tests/
```

More on feeds, recommendations and the daily digest: [docs/SOCIAL_FEATURES.md](docs/SOCIAL_FEATURES.md).


AI workflow setup: [docs/ai/README.md](docs/ai/README.md). Feature conventions: [AGENTS.md](AGENTS.md). Optional worker/cron tuning: [docs/DEPLOY.md](docs/DEPLOY.md).


### Create an administrator from the terminal

```powershell
dotnet run --project src/Inkwell -- --create-admin
```

Enter the name, email and password when prompted (password input is hidden). Existing accounts are never overwritten.
Use the team screen to add or remove administrators. Demo passwords are `Password@12345` and demo emails use `@yopmail.com`.
Rerunning `--seed-demo` updates old seeded credentials once without resetting live data. Migrations apply automatically.

Team management is at `/admin/team` (also in the admin account menu). On existing databases, bootstrap an admin with the command above. On fresh demo databases the first demo user, `inertia.demo42.00000@yopmail.com`, is an admin. Enable notification chimes with the speaker button.
