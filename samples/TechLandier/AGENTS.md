# TechLandier

This is an independently shareable Inertia.Net sample: Vue 3, PostgreSQL, and S3-compatible object storage. Do not add dependencies on sibling samples or standardize its providers with theirs. Paths below are relative to this sample root.

- Read the affected code and trace callers, tests, seeders, registrations and frontend types before editing. Preserve unrelated user changes and local .env files.
- Features live in `src/TechLandier/Features/<Feature>/`. Routes own MapGet/MapPost and HTTP/Inertia responses (Render, Back, Location, flash, InertiaProps and prop modifiers). Services own business logic, database queries, durable job enqueueing and realtime triggers, returning POCOs/records rather than IResult.
- Put DTOs/entities/records/enums/static literals in feature Models files; validation routines in Validators; conversions in Mappers; feature utilities in Utils. Keep declarative DataAnnotations on input models. If there are multiple files of a kind, group them under Models/, Services/, Validators/, Mappers/ or Utils/. Do not create empty files or forwarding wrappers to satisfy a naming convention.
- Prefer direct, readable LINQ projections and the existing data API. Avoid speculative query abstractions, overfetching and raw SQL. Retain documented SQL only where provider-specific search or atomic concurrency guarantees require it; test that guarantee before replacing it.
- Queue work whose result the response does not need, including emails, cleanup and realtime delivery. Await validation and the business commit. Use the existing durable queue/outbox, transaction/session support, retry policy and idempotent handlers. Never use Task.Run or unobserved fire-and-forget tasks in requests.
- Always await asynchronous work in the method or delegate that starts it. Use async/await (return await for Task<T>/ValueTask<T> results); never forward a Task/ValueTask directly, including in routes, callbacks, services, jobs, infrastructure and tests. Keep intentional parallel work concurrent and await Task.WhenAll. Preserve cancellation, disposal scopes and exception propagation; never use async void outside required event-handler contracts.
- Keep cron to actual wall-clock/periodic maintenance. Event-driven work belongs in the queue. Preserve lease fencing and catch-up behavior. Worker concurrency is Jobs:Concurrency (JOBS_CONCURRENCY); Jobs:Enabled (JOBS_ENABLED) and CRON_ENABLED let a web replica opt out. See docs/DEPLOY.md before changing topology.
- Preserve object storage, upload ownership/type/size checks and backup storage. Keep .env.example limited to required connection/secret/provider values; document optional tuning in docs/DEPLOY.md.
- Reuse local shadcn components and their native backers. Use accessible styled date/time/select controls, never native date/time inputs or HTML selects in application forms. Preserve keyboard focus, touch targets and reduced-motion support. Prevent page overflow; hide scrollbar chrome without disabling scrolling.
- Use Inertia useForm for mutations; use existing Precognition support when live server validation helps. Valibot is the only permitted frontend schema validator, and is unnecessary for forms already covered by server validation. Do not add Zod.
- Prefer VueUse before custom reactive browser utilities. Use defineModel for component v-model and useTemplateRef for template references. Use computed only for pure derivation; keep conversions in small named functions. Use watch only for actual external side effects.
- Keep hooks/composables small and focused. Reuse the existing ones before adding another. Micro-interactions should communicate pending, success, error and selection states without blocking the user.
- Use Bun and the committed bun.lock. No alternate lockfiles. Verify UI in playwright-cli on desktop/mobile, exercise changed controls and inspect screenshots and console/network errors. Never claim browser verification from a build alone.
- Remove only files proven unrelated or obsolete. Never delete .env, persisted storage or another person's untracked files as cleanup.

Checks from this sample root:

```text
dotnet test tests/TechLandier.Tests
node scripts/ai/sync.mjs --check
node --test scripts/ai/sync.test.mjs
```

From `src/TechLandier`: `bun run typecheck`, `bun run build` and relevant `check:*` scripts in package.json. Database integration tests need a disposable PostgreSQL instance; see README.md. Update docs/CONTRACT.md when route, prop, job or realtime contracts change. Report actual checks, skips and remaining risk.

AI setup and available workflows: [docs/ai/README.md](docs/ai/README.md). Inertia adapter APIs: [Inertia.Net](https://github.com/adzamkomladev/Inertia.Net); check the installed package version before adopting an API from main.

