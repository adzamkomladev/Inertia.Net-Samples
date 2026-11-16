# Container images: Alpine, multi-stage and the smallest safe runtime

All five samples build production images from Alpine now. The goal is to remove build tooling from the final image
without dropping features: the apps still use EF Core, MongoDB, SQLite, culture-aware money formatting and named time
zones, so the images keep ICU and `tzdata` instead of switching on invariant globalization.

## What the Dockerfiles do

- **Build frontend with Bun, run SSR with Node.** `oven/bun:1-alpine` is a build-only stage. The production stages
  install Alpine's `nodejs` package and set `Inertia__SsrExecutable=node`; a SPA image would need neither.
- **Build the backend in `mcr.microsoft.com/dotnet/sdk:10.0-alpine`.** The SDK never reaches the final image.
- **Run on `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`.** No Debian package installs. `icu-libs`, `tzdata` and
  `nodejs` are the only additions (Node only where the app supervises an SSR process).
- **Publish RID-specific but framework-dependent.** `dotnet restore -r linux-musl-x64` and
  `dotnet publish -r linux-musl-x64 --self-contained false` keep only the native assets Alpine needs. For Inkwell this
  is the difference between shipping every platform's SQLite native library and just `libe_sqlite3.so`.
- **Copy only the publish output and the built assets.** `bin`, `obj`, `node_modules`, tests, local databases, SSR
  build output and `.env` files are already excluded by each sample's `.dockerignore`.
- **Use BusyBox `wget` for platform health checks.** The final images have no `curl`; the app images already install
  Node for SSR, so their health check uses `node -e "fetch(...)"`.
- **TechLandierDistributed SSR** uses `alpine:3.22` plus `apk add --no-cache nodejs`; the bundle is plain JavaScript,
  so a full Node base image is unnecessary.

## Measured sizes

Built locally on 2026-11-16 with the repository Dockerfiles (linux/amd64):

| Image | Size |
| --- | ---: |
| `techlandier-distributed-ssr` (Alpine + nodejs) | 91.9 MB |
| `techlandier-distributed` MainApp | 151 MB |
| `carparker` (Alpine, RID-specific) | 213 MB |
| `carparker-replicas` | 215 MB |
| `techlandier` | 220 MB |
| `inkwell` (Alpine, RID-specific) | 212 MB |

The single-app images include the ASP.NET runtime, ICU, time-zone data, Node for SSR and the full app plus frontend
assets. The old Debian images were approximately 30–40 MB larger before the RID-specific publish.

## Why not Native AOT

The [Native AOT experiment](NATIVE_AOT_2026_10_22.md) found blockers in these full applications:

- Inkwell needs an EF Core compiled model before the native executable can start.
- CarParker fails while creating MongoDB enum serializers without native metadata.
- TechLandier fails in the ASP.NET Request Delegate Generator with `CS8639`.

Native AOT requires removing or reworking those paths. It is not a drop-in image-size switch for these samples, so
the Dockerfiles stay framework-dependent. A future AOT sample should start from a trimmed feature set and add the
required EF compiled model, Mongo serializer registration and Request Delegate Generator fixes.

## Invariant globalization

`InvariantGlobalization=true` is deliberately not set. CarParker and TechLandier format money and business hours in
named time zones, and Inkwell uses culture-aware formatting. Using invariant mode would remove ICU and reduce the
image, but it changes behavior. If a future sample only needs invariant formatting, set it in that project and drop
`icu-libs`/`tzdata` from its runtime stage.
