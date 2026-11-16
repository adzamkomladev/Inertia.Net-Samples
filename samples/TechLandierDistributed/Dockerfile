# syntax=docker/dockerfile:1
# TechLandierDistributed images:
#   mainapp   - Inertia/Vue, routes, Runwick producer and the migrate/seed CLI (no Node or workers)
#   worker    - PostgreSQL queue consumers, Runwick worker/scheduler and job handlers
#   realtime  - SignalR hub connections on /realtime
#   dashboard - standalone Runwick management dashboard (no worker or scheduler)
#   ssr       - Node-only Inertia SSR bundle, selected through Inertia:SsrUrls
#   migrate   - the mainapp image with --migrate (see docker-compose.yml)

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/). Bun is build-only.
FROM oven/bun:1-alpine AS web
WORKDIR /src/apps/MainApp
COPY apps/MainApp/package.json apps/MainApp/bun.lock ./
RUN bun install --frozen-lockfile
COPY apps/MainApp/vite.config.ts apps/MainApp/tsconfig.json apps/MainApp/components.json ./
COPY apps/MainApp/ClientApp ./ClientApp
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend: SDK only in this stage.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY Directory.Build.props Directory.Build.targets ./
COPY lib/TechLandier.Core/TechLandier.Core.csproj lib/TechLandier.Core/
COPY lib/TechLandier.Caching/TechLandier.Caching.csproj lib/TechLandier.Caching/
COPY apps/MainApp/TechLandierDistributed.MainApp.csproj apps/MainApp/
COPY apps/Worker/TechLandierDistributed.Worker.csproj apps/Worker/
COPY apps/Realtime/TechLandierDistributed.Realtime.csproj apps/Realtime/
COPY apps/Dashboard/TechLandierDistributed.Dashboard.csproj apps/Dashboard/
RUN dotnet restore apps/MainApp/TechLandierDistributed.MainApp.csproj -r linux-musl-x64 \
    && dotnet restore apps/Worker/TechLandierDistributed.Worker.csproj -r linux-musl-x64 \
    && dotnet restore apps/Realtime/TechLandierDistributed.Realtime.csproj -r linux-musl-x64 \
    && dotnet restore apps/Dashboard/TechLandierDistributed.Dashboard.csproj -r linux-musl-x64
COPY lib/ lib/
COPY apps/ apps/
# RID-specific framework-dependent publish keeps only the native assets Alpine needs.
RUN dotnet publish apps/MainApp/TechLandierDistributed.MainApp.csproj -c Release -r linux-musl-x64 --self-contained false -o /out/main --no-restore /p:UseAppHost=false \
    && dotnet publish apps/Worker/TechLandierDistributed.Worker.csproj -c Release -r linux-musl-x64 --self-contained false -o /out/worker --no-restore /p:UseAppHost=false \
    && dotnet publish apps/Realtime/TechLandierDistributed.Realtime.csproj -c Release -r linux-musl-x64 --self-contained false -o /out/realtime --no-restore /p:UseAppHost=false \
    && dotnet publish apps/Dashboard/TechLandierDistributed.Dashboard.csproj -c Release -r linux-musl-x64 --self-contained false -o /out/dashboard --no-restore /p:UseAppHost=false

# 3. Runtime base: Alpine ASP.NET with ICU and time-zone data. No Node here; the SSR bundle has its own image.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
RUN apk add --no-cache icu-libs tzdata \
    && mkdir -p /data \
    && chown $APP_UID:$APP_UID /data
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    App__DataDir=/data \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0

FROM runtime AS web-runtime
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# BusyBox wget is in the base image; a non-2xx answer exits non-zero.
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1

FROM web-runtime AS mainapp
COPY --from=build /out/main ./
COPY --from=web /src/apps/MainApp/wwwroot/build ./wwwroot/build
USER $APP_UID
ENTRYPOINT ["dotnet", "TechLandierDistributed.MainApp.dll"]

FROM runtime AS worker
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "TechLandierDistributed.Worker.dll"]

FROM web-runtime AS realtime
COPY --from=build /out/realtime ./
USER $APP_UID
ENTRYPOINT ["dotnet", "TechLandierDistributed.Realtime.dll"]

FROM web-runtime AS dashboard
COPY --from=build /out/dashboard ./
USER $APP_UID
ENTRYPOINT ["dotnet", "TechLandierDistributed.Dashboard.dll"]

# 4. Standalone SSR pool. One bundle, three containers; no app binaries or node_modules.
# Alpine's nodejs package is much smaller than starting from a Node image.
FROM alpine:3.22 AS ssr
RUN apk add --no-cache nodejs
WORKDIR /app
COPY --from=web /src/apps/MainApp/ssr ./ssr
ENV NODE_ENV=production \
    INERTIA_SSR_HOST=0.0.0.0 \
    INERTIA_SSR_PORT=13714
EXPOSE 13714
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:13714/health || exit 1
ENTRYPOINT ["node", "ssr/ssr.js"]
