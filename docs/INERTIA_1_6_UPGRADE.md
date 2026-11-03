# Inertia.Net 1.6.0 upgrade: live props

All five samples moved from **Inertia.Net 1.5.1 to 1.6.0** on 2026-11-03 and adopted the 1.6.0 headline feature: the
root added the [Inertia v3.11 liveProps protocol](https://github.com/adzamkomladev/Inertia.Net#live-props) and a
SignalR transport package. Frontends moved from `@inertiajs/*` 3.8/3.9 to 3.11 for the client side of the protocol.

## What 1.6.0 adds

- `InertiaProp.Live(LiveEvent)`, `.Live(channel, event, ...)` and `.Throttle(...)`; `Live(value|loader, event)`
  factories. A prop marked `.Live(...)` publishes its listeners in the page's `liveProps` metadata, and the client
  (3.11+) subscribes through a transport and reloads — or applies published values without a request.
- `services.AddInertiaLive()` registers `IInertiaLive`, whose publishes reach subscribers through an
  `IInertiaLiveBroadcaster`; the new `Inertia.Net.Live.SignalR` implements it with `MapInertiaLiveHub()`
  (channel authorization, per-channel groups, its own origin check, `X-Socket-Id` echo).
- Merge, deep-merge and scroll props cannot be live. `Defer`, `Optional`, `Once`, `Rescue` and `Cache` combine freely;
  publishing never evicts a prop's cache entry, so publish points must invalidate first.

## How the samples partition realtime

Every sample already ran a raw `/realtime` SignalR hub fed by the durable job queue, with a client hook that debounced
`router.reload({ only: [...] })`. The upgrade keeps the raw hub for exactly two jobs and hands the rest to the live
protocol, which owns reconnection catch-up, throttling, pause-when-hidden and per-page subscriptions:

| Raw `/realtime` hub keeps | Live `/inertia-live` transport takes |
| --- | --- |
| Toast wording (`Notification` payloads) and the notification sound | The shared `notifications` bell and the alerts page (`private:users.{id}`, 200 ms throttle) |
| Storefront pulses for Scroll-prop pages (Inkwell feed counters/"new posts", CarParker storefront spot/lot cards) | Signed-in dashboards (Inkwell analytics; CarParker guard/admin consoles incl. deferred analytics; TechLandier admin KPIs/delivery/order boards) |
| | Per-order public channels for TechLandier tracking + account order timelines (`public:orders.{number}`) |
| | TechLandierDistributed: same wiring, with the hub hosted by the Realtime tier and publishes made by the Worker through the shared Redis backplane |

Publish decisions follow the protocol's own rule — **never publish values for per-user data** — so every publish is
value-less and each client reloads its own props (the event name only triggers them). Every publish point (the
RealtimeDelivery/RealtimePush job handlers) removes the prop's cache entry before enqueueing, which preserves the 1.5
invariant "reloads always read fresh rows".

Per-sample maps live in each `docs/CONTRACT.md` ("Live props" sections); the upgrade notes below are shared.

## Shared plumbing in each sample

1. Packages: `Inertia.Net.Live.SignalR` 1.6.0 next to `Inertia.Net` 1.6.0 (Testing at 1.6.0 for tests).
2. `AddInertiaLive().AddSignalR(o => o.Authorize(...))` — one authorizer for both things a sample needs:
   `channel.Name == users.{UserIdentifier}` and, where boards exist, the role-claim check for `staff`/`admins`.
3. `app.MapInertiaLiveHub()` — its endpoint carries `DisableAntiforgery()` and its own origin check, so the samples'
   `UseInertiaAntiforgery.ShouldValidate` exempt only the hub paths (`/realtime` untouched, `/inertia-live` added).
4. Client: `ClientApp/lib/inertia-signalr.ts` (the transport file from the library's templates) and
   `createInertiaApp({ live: signalr() })` — `@microsoft/signalr` loads on the first page that declares a listener.
5. Client hooks (`use-realtime.ts` / `realtime.svelte.ts` / `useRealtime.ts`) slim to toasts + Scroll-prop-only
   pulses; every reload branch the live props now owns is deleted.
6. Tests: `HasLive("prop", "private:staff", "Event")` page assertions plus a WebSocket hub test that subscribes and
   receives a publish (Inkwell: `RealtimeTests`; CarParker/Replicas/TechLandier: `LivePropsTests`).

## Sharp edge found and handled

The transport's `AddJsonProtocol` inserts `InertiaLiveJsonContext` at the **front of the hub payload resolver chain**.
On a hub registration whose chain was previously unset, the insert leaves no fallback resolver, so *other* hubs in the
same app (the samples' `BlogHub`/`ParkHub`/`StoreHub`) failed to serialize their own typed payloads (`PostCounters`)
and their connections closed with an error. Each sample therefore appends a `DefaultJsonTypeInfoResolver` behind the
context after `AddSignalR`:

```csharp
services.AddSignalR().AddJsonProtocol(o =>
{
    var chain = o.PayloadSerializerOptions.TypeInfoResolverChain;
    if (!chain.OfType<DefaultJsonTypeInfoResolver>().Any()) chain.Add(new DefaultJsonTypeInfoResolver());
});
```

The samples are not Native AOT builds; if one goes AOT, the reflection fallback is the line to revisit (the context
itself is AOT-annotated, and value-publishing types would then come from the app's registered JSON context).

TechLandierDistributed keeps the layout the sample already mandated — Core stays Inertia-free:

- `TechLandier.Core` gained only `RealtimeNames` string constants (event/channel names both tiers speak).
- MainApp maps listeners with a small `LiveEvents` factory built from those constants, and registers nothing new
  (pages never publish; the Worker does that).
- The Worker gained the packages and publishes inside `RealtimePushHandler` after the raw sends.
- The Realtime tier maps the hub at `/inertia-live` and authorizes by cookie claims; HAProxy gained the
  `/inertia-live` routing rule to the realtime backend.

## Per-sample adoption

| Sample | Live listeners | Publish points |
| --- | --- | --- |
| Inkwell | shared `notifications` (200 ms); `/analytics` + `/posts/{id}/analytics` deferred dashboards on `users.{id}` | `RealtimeDelivery` analytics publishes; notification creation; `MarkAllReadAsync` for the user's other sessions |
| CarParker | shared `notifications` (200 ms), `account/Alerts`; guard/Console + board props, admin/Index/Sessions/Refunds/Insurance/Bookings/incidents, admin/Analytics deferrred on the `staff` channel | `RealtimePushHandler`: per-user chips for notifications; one staff publish per board event |
| CarParkerReplicas | same as CarParker | same; the live hub shares the SignalR Redis backplane |
| TechLandier | shared `notifications` (200 ms), account alerts; admin/Index/Analytics/Orders/Deliveries + admin/Order details on `admins`; Track + account/Order timelines on `public:orders.{number}` | `RealtimePushHandler` (per-user channels, admins pulse, per-order timeline publish) |
| TechLandierDistributed | same as TechLandier | Worker `RealtimePushHandler` through the backplane; hub on the Realtime tier |

## Verification

| Check | Result |
| --- | --- |
| `dotnet build` all five solutions (Release / `-warnaserror` where CarParkerReplicas mandates) | pass, 0 warnings |
| Inkwell `dotnet test` (SQLite, runs locally) | pass, 118 tests (incl. three live-props tests: origin/authorization, end-to-end publish→frame, `HasLive` metadata) |
| Inkwell/CarParker/TechLandier/Replicas/Distributed frontend `bun run typecheck` + `bun run build` | pass |
| CarParker `dotnet test`, TechLandier, Replicas, Distributed tests | **not run**: MongoDB/PostgreSQL/Redis services are not available in this environment; the suites need a disposable instance each (README instructions). The same code paths were exercised in the Inkwell suite (which runs on SQLite) and the builds/type-checks |
| Playwright browse | pending the design pass below (both sides of the change get one verified UI run) |

The existing realtime origin/negotiate tests for the raw hubs were left untouched and still pass (Inkwell), so the
raw side kept its guarantees; the live tests add the new surface without removing them.

## Follow-up

- Feed counter patching stays a custom hook in Inkwell/CarParker by protocol design: Scroll and merge props cannot be
  live, and deep-path value patches into array descendants are not printable through the documented API. If a future
  Inertia release adds those, the remaining hook logic can migrate one-for-one.
- Publishing *with values* for the TechLandier/Inkwell notification summary (worker computes the fresh summary and the
  client applies it without a request) is the natural next step — the current publish asks each client for one
  partial reload; the value form exists in the API and is only wired when a sample wants the extra request saved.
- The upstream package's resolver-chain behavior (sharp edge above) is worth an issue: sample apps with their own
  hubs are the documented "additive" case and their sends silently broke.
