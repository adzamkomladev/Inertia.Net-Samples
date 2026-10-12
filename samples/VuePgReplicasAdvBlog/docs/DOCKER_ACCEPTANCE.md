# Advanced sample acceptance — 2026-10-12

## Build and integration checks

- AppHost and all production projects build with zero warnings and errors on .NET SDK 10.0.401.
- Final isolated backend integration suite: **61/61 passed**, with `DOTNET_PROCESSOR_COUNT=2` to bound test parallelism. Coverage includes separate Generic Host worker execution, MainApp/Realtime route separation, shared cookies, queue claims, cron leases, durable view processing and retry idempotency.
- Vue typecheck, client/SSR production builds, realtime reducer checks and social-toggle checks passed.
- A temporary forbidden MainApp-to-Realtime project reference was rejected by the MSBuild boundary guard; the probe was removed.
- The ignored environment configuration retains the reference sample's service credentials. Running Worker reported the three required Maileroo settings present without printing their values.

## Aspire 13.6.1

The actual AppHost ran PostgreSQL 18, Redis, the one-shot migrator, three MainApp processes, two Worker processes, two Realtime processes and the YARP gateway. Web resources passed database/Redis health checks; workers had no HTTP endpoints. PostgreSQL contained all 11 cron lease baselines.

The HTTP acceptance script passed signup on each app, the unverified-user publication guard, database verification bypass, signin on another app, authenticated access across every app, post creation/read/edit/delete, likes, comments, bookmarks, ownership checks, durable view jobs, public/private realtime events, email-job processing and logout. Acceptance users were removed afterwards.

Routing checks observed app1–app3 and realtime1–realtime2, independent sticky cookies, successful SignalR negotiation/long-poll transport and a shared HTTP rate budget: ten missing-antiforgery probes were allowed through to 419, followed by two 429 responses.

Aspire was stopped through the pinned CLI. Its project processes, PostgreSQL, Redis, gateway and tunnel containers were stopped and removed; named database/cache volumes remain.

## Docker Compose

The final separate MainApp, Worker and Realtime images built successfully. Default deployment ran three apps, two workers and two realtime hosts. The same complete HTTP acceptance flow passed. A rendered HTML page also returned successfully.

Scaling with `--scale app=5 --scale worker=3 --scale realtime=3` produced eleven healthy role containers. Routing checks observed app1–app5 and realtime1–realtime3; SignalR affinity and the shared rate budget passed.

Image checks confirmed Worker exposes no ports, MainApp contains no TickerQ scheduler assembly, and Realtime contains neither MainApp nor TickerQ assemblies. Only Worker includes the PostgreSQL 18 backup client (18.6 in this build).

## Test scope

Email verification was bypassed in the database as authorized. Reserved demo addresses capture mail locally; external Maileroo delivery and S3 backup/upload delivery were not exercised. These checks establish functional replica behavior, not a throughput benchmark. A suite run alongside the scaled deployment had one raw SignalR test transport expire and a database-fixture cleanup failure; run the integration suite separately from the scaled stack.

Completed shutdown with `docker compose down` without deleting volumes. Final checks found no advanced Compose containers or Aspire/app processes remaining. Disposable integration databases were also removed.

## File-based AppHost follow-up

Converted the orchestrator to root-level `apphost.cs`, with pinned SDK/package directives and stable path-based project resources. Removed the AppHost project/settings files and its solution entry; updated CLI discovery and run instructions. Retained the user-secrets identity and existing PostgreSQL/Redis volumes.

The file-based AppHost builds successfully, and the remaining solution builds with zero warnings/errors. Its live run reached healthy 3-app/2-worker/2-realtime resources and passed the complete HTTP acceptance flow, including signup/signin, post mutations, shared authentication, queued views, email jobs and realtime delivery. Test accounts were removed. The backend integration suite and Docker image checks from above were not rerun for this orchestration-only conversion.

Restart testing found that a launch without a profile could regenerate the database parameter instead of loading its saved value. The AppHost now explicitly loads user secrets with environment overrides. The local Aspire database credential was reconciled with the saved parameter without deleting database contents; original authentication rules were restored. The corrected plain `dotnet run --file apphost.cs` launch passed the complete HTTP acceptance flow. A subsequent plain .NET restart reused the credential and passed all routing, independent affinity, SignalR transport and shared rate-budget checks. Managed resources were stopped afterwards; named volumes remain.

## SSR port isolation follow-up — 2026-10-12

The user's Aspire log exposed a false positive in the earlier readiness checks: all host-run MainApp processes used SSR port 13714. A running React SQLite sample owned that port, so Inertia.Net could report its health endpoint as healthy while the Vue SSR processes repeatedly exited. Checking only `data-page` also accepted client-side fallback HTML.

AppHost now allocates a private TCP endpoint per MainApp and injects `Inertia__SsrPort`. MainApp configures `Ssr.Url` with it; the Vue SSR entry reads Inertia.Net's `INERTIA_SSR_PORT` at runtime. One production bundle works across host replicas and containers. The routing acceptance check now requires the rendered Vue heading on every replica.

Verified using the user's installed `aspire run`: app1/app2/app3 each started one healthy Bun SSR process on ports 18372/18374/18376 while the unrelated React sample retained port 13714. All three returned rendered Vue pages; SSR logs showed no crash/restart loop. Frontend typechecking, client/SSR builds, development SSR rendering and eight runtime configuration tests passed. Signup, database verification, signin across replicas, post CRUD, social interactions, durable view processing, realtime delivery, logout, affinity and the shared rate limit passed. The test accounts were removed.

The sample's Aspire processes and test containers were stopped, retaining database and Redis volumes. The unrelated React sample was left running. The full backend suite and Docker image builds were not repeated for this SSR port fix.

## Feature library boundary follow-up — 2026-10-12

Replaced the catch-all Blog project with Common and twelve feature libraries. MainApp now owns endpoint binding, Inertia rendering and deferred props, scroll metadata, flash messages, redirects, JSON responses, login request models and theme cookies. Features accept explicit user IDs and desired state, return plain data and retain their transactions, validation and ownership checks. Each HTTP application owns cookie configuration; Auth supplies password, verification-token and principal logic. ServiceDefaults remains separate, with health endpoint mapping in the web applications.

MainApp is the only production project referencing Inertia.Net. A temporary library project with that package was rejected by the new MSBuild guard and removed. Assembly boundary tests verify that all libraries have no Inertia dependency and features have no HTTP request/response or cookie-handler dependency. Published Worker and Realtime images contain no Inertia or MainApp assembly; MainApp contains no Worker, Realtime or TickerQ assembly. Worker exposes no ports.

The final solution builds with **zero warnings/errors**, formatting verification passes, and the final isolated backend suite passed **72/72**, with no skipped tests. All three Docker images built successfully after retrying transient NuGet download failures. Both Compose and the file-based Aspire 13.6.1 deployment passed the complete HTTP acceptance flow on three MainApp, two Worker and two Realtime replicas. These flows covered signup/signin, the unverified-user guard, database verification, post CRUD, social interactions, ownership checks, durable view jobs, email-job processing, public/private realtime delivery and logout. Acceptance accounts were removed. Running Docker Worker confirmed all three Maileroo settings present without exposing values.

Both deployments also passed Vue SSR on every app, separate realtime affinity, SignalR negotiation/long polling and a shared HTTP rate budget. Compose was brought down; Aspire and its remaining owned containers were stopped and removed. Final checks found no running sample application/SSR processes or containers. Database/cache volumes were retained and the unrelated React SSR process was left running.

Verification was bypassed in the database as authorized; demo mail was captured locally. External Maileroo delivery and S3 delivery were not exercised. These are functional checks, not a throughput benchmark.

