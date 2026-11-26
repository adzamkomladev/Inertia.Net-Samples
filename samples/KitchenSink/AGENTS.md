# KitchenSink

This is an independently shareable Inertia.Net sample: the upstream inertiajs/demo-v3 Vue UI on .NET 10, SQLite and
Inertia.Net 1.9.5. Paths below are relative to this sample root.

- The Vue pages, layouts and shadcn components are copied from inertiajs/demo-v3. Keep component names, prop names and
  route URLs stable; the page smoke tests and Playwright suite depend on them.
- Features live in `src/KitchenSink/Features/`. Routes own HTTP/Inertia responses; `CrmService` owns CRM queries and
  pagination. Keep JSON prop names in the snake_case shape the pages expect (DTO attributes in `CrmModels.cs`).
- `ClientApp/wayfinder/` is a hand-written stand-in for Wayfinder. Add a route there when a page starts using a new
  endpoint; do not generate PHP files.
- Use Inertia.Net's prop helpers (`Defer`, `Optional`, `Merge`, `Once`, `Scroll`, `Prop(...).Live(...)`), session state
  and antiforgery middleware rather than ad-hoc implementations. QUERY is a safe method and skips antiforgery.
- The live transport is SignalR, not Reverb/Echo, and lives on the public `demo` channel. Publish through `IInertiaLive`;
  adding a listener means adding a `.Live(...)` to the prop and a test for the page metadata.
- Always await asynchronous work. Background bursts use `LiveTickBurstService`; daily resets use `DemoResetScheduler`.
  Do not use `Task.Run` or unobserved fire-and-forget tasks.
- Use Bun and the committed `bun.lock`. No alternate lockfiles.

Checks from this sample root:

```text
dotnet test tests/KitchenSink.Tests
dotnet build tests/KitchenSink.BrowserTests
RUN_BROWSER_TESTS=1 dotnet test tests/KitchenSink.BrowserTests
```

From `src/KitchenSink`: `bun run typecheck` and `bun run build`. Update `docs/CONTRACT.md` when a route, prop, live
event or the demo reset behavior changes. Report actual checks and remaining risk.
