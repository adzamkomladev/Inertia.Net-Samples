# syntax=docker/dockerfile:1
# Production image: web app, SSR (bun, supervised by the app), jobs and crons. Port 8080, health /health,
# volume /data. Needs PostgreSQL 18 in DATABASE_URL (see docs/DEPLOY.md).

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/)
FROM oven/bun:1-debian AS web
WORKDIR /src
COPY src/TechLandier/package.json src/TechLandier/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/TechLandier/vite.config.ts src/TechLandier/tsconfig.json src/TechLandier/components.json ./
COPY src/TechLandier/ClientApp ./ClientApp
# vue-tsc runs in dev and CI (`bun run build`); the image only bundles.
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/TechLandier/TechLandier.csproj ./
RUN dotnet restore
COPY src/TechLandier/ ./
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false

# 3. Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# pg_dump for the nightly backup cron. It must be at least the server's major version, and Ubuntu ships 16,
# so it comes from the PostgreSQL apt repository.
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl \
 && install -d /usr/share/postgresql-common/pgdg \
 && curl -fsSo /usr/share/postgresql-common/pgdg/apt.postgresql.org.asc https://www.postgresql.org/media/keys/ACCC4CF8.asc \
 && . /etc/os-release \
 && echo "deb [signed-by=/usr/share/postgresql-common/pgdg/apt.postgresql.org.asc] https://apt.postgresql.org/pub/repos/apt $VERSION_CODENAME-pgdg main" > /etc/apt/sources.list.d/pgdg.list \
 && apt-get update \
 && apt-get install -y --no-install-recommends postgresql-client-18 \
 && apt-get purge -y curl && apt-get autoremove -y \
 && rm -rf /var/lib/apt/lists/*
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/wwwroot/build ./wwwroot/build
COPY --from=web /src/ssr ./ssr

# /data holds the Data Protection keys and captured mail.
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DATA_DIR=/data \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# No curl in the image; bun is already here for SSR. start-period covers migrations on first boot.
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=3 \
  CMD ["bun", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "TechLandier.dll"]
