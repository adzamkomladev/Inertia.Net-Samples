# syntax=docker/dockerfile:1
# TechLandierDistributed images:
#   mainapp  - Inertia/Vue, routes and the migrate/seed CLI (no Bun, jobs or cron)
#   worker   - PostgreSQL queue consumers, job handlers and TickerQ crons
#   realtime - SignalR hub connections on /realtime
#   ssr      - Bun-only Inertia SSR bundle, selected through Inertia:SsrUrls
#   migrate  - the mainapp image with --migrate (see docker-compose.yml)

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/)
FROM oven/bun:1-debian AS web
WORKDIR /src/apps/MainApp
COPY apps/MainApp/package.json apps/MainApp/bun.lock ./
RUN bun install --frozen-lockfile
COPY apps/MainApp/vite.config.ts apps/MainApp/tsconfig.json apps/MainApp/components.json ./
COPY apps/MainApp/ClientApp ./ClientApp
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Build.targets ./
COPY lib/TechLandier.Core/TechLandier.Core.csproj lib/TechLandier.Core/
COPY lib/TechLandier.Caching/TechLandier.Caching.csproj lib/TechLandier.Caching/
COPY apps/MainApp/TechLandierDistributed.MainApp.csproj apps/MainApp/
COPY apps/Worker/TechLandierDistributed.Worker.csproj apps/Worker/
COPY apps/Realtime/TechLandierDistributed.Realtime.csproj apps/Realtime/
RUN dotnet restore apps/MainApp/TechLandierDistributed.MainApp.csproj \
    && dotnet restore apps/Worker/TechLandierDistributed.Worker.csproj \
    && dotnet restore apps/Realtime/TechLandierDistributed.Realtime.csproj
COPY lib/ lib/
COPY apps/ apps/
RUN dotnet publish apps/MainApp/TechLandierDistributed.MainApp.csproj -c Release -o /out/main --no-restore /p:UseAppHost=false \
    && dotnet publish apps/Worker/TechLandierDistributed.Worker.csproj -c Release -o /out/worker --no-restore /p:UseAppHost=false \
    && dotnet publish apps/Realtime/TechLandierDistributed.Realtime.csproj -c Release -o /out/realtime --no-restore /p:UseAppHost=false

# 3. Runtime base (curl is used by the health checks; the images do not need Bun except ssr)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get -o Acquire::Retries=3 update \
    && apt-get -o Acquire::Retries=3 install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data && chown $APP_UID:$APP_UID /data
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production \
    ASPNETCORE_ENVIRONMENT=Production \
    App__DataDir=/data \
    DOTNET_gcServer=0

FROM runtime AS web-runtime
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1

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

# 4. Standalone SSR pool. One bundle, three containers; no app binaries or node_modules.
FROM oven/bun:1-debian AS ssr
WORKDIR /app
COPY --from=web /src/apps/MainApp/ssr ./ssr
ENV NODE_ENV=production \
    INERTIA_SSR_HOST=0.0.0.0 \
    INERTIA_SSR_PORT=13714
EXPOSE 13714
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
    CMD ["bun", "-e", "fetch('http://127.0.0.1:13714/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]
ENTRYPOINT ["bun", "ssr/ssr.js"]
