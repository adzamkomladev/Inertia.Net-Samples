# KitchenSink contracts

The Vue pages are the upstream inertiajs/demo-v3 UI. Keep component names, prop names and route URLs stable;
the pages, the page smoke tests and the Playwright suite rely on them.

## Shared props

| Key | Shape | Notes |
|---|---|---|
| `name` | string | App name from `App:Name` |
| `auth.user` | `{ id, name, email, email_verified_at, created_at, updated_at } \| null` | Loaded from the database per request |
| `sidebarOpen` | bool | `sidebar_state` cookie, default true |
| `queryMethod` | bool | `Demo:QueryMethod`; hides the QUERY demos |
| `demoResetAt` | ISO timestamp \| null | Live prop on `public:demo`, event `DemoDataReset` |

`errors` and `flash` are supplied by Inertia.Net's session state store.

## CRM

| Method | Path | Result |
|---|---|---|
| GET | `/dashboard` | `Crm/Dashboard`; `recentActivity` now, stats deferred |
| GET | `/contacts?search=&favorite=&cursor=` | `Contacts/Index`; `contacts` Scroll prop, 15 per page |
| GET | `/contacts/create` | `Contacts/Create`; `organizations` |
| POST | `/contacts` | Create and redirect to show |
| GET | `/contacts/{id}` | `Contacts/Show`; `notes` deferred |
| GET | `/contacts/{id}/edit` | `Contacts/Edit`; `contact`, `organizations` |
| PUT/PATCH | `/contacts/{id}` | Update and redirect to show |
| DELETE | `/contacts/{id}` | Delete and redirect to index |
| POST | `/contacts/{id}/favorite` | Toggle and `Back()` |
| POST | `/contacts/{id}/notes` | Create note and `Back()` |
| GET | `/organizations?search=&page=` | `Organizations/Index`; `organizations.data` + `meta.links` |
| GET | `/organizations/{id}` | `Organizations/Show`; `contacts` Scroll prop |
| PUT/PATCH | `/organizations/{id}` | Rename and `Back()` |

All CRM routes require authentication. JSON bodies and form posts are both accepted.

## Showcase

`/features/forms/*`, `/features/navigation/*`, `/features/data-loading/*`, `/features/prefetching/*`,
`/features/state/*`, `/features/layouts/*`, `/features/events/*`, `/features/errors/*` and `/features/http/*`
follow the upstream page list. Notable protocol behavior:

- `QUERY /features/navigation/links` and `/query-visits` are safe reads and skip antiforgery validation.
- `GET /features/events/location-event/deploy` resolves to asset version `new-asset-version-hash`; the Inertia
  middleware answers a current client with `409`, `X-Inertia-Location` and the new version.
- `POST /features/forms/precognition` answers Precognition requests with `204` + `Precognition-Success: true`
  or `422` + `Precognition: true`, limited to `Precognition-Validate-Only`.
- `GET /features/data-loading/big-integers` uses `$bigint` when `preserve` is not `0`; with
  `?preserve=0` it sends a JSON number so the browser rounds it.
- `POST /features/data-loading/live-props/votes|reactions|ticks` return `204`; ticks run on a background queue.

## Live props

All listeners are on the public `demo` channel:

| Event | Props |
|---|---|
| `VoteCast` | `votes`, `yourVote` (reload; the event also carries `visitor`/`framework` for the log) |
| `ReactionAdded` | `reactions` (carried values) |
| `CounterTicked` | `ticks`, `slowTicks` (slowTicks throttled to 3 s) |
| `DemoDataReset` | shared `demoResetAt` |

`demo:reset` is `dotnet run --project src/KitchenSink -- --reset-demo`. It replaces contacts, organizations,
notes and live data in one transaction, keeps the shared user, then publishes `DemoDataReset`.
