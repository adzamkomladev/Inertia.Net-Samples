# syntax=docker/dockerfile:1
# One image runs everything: the web app, SSR (Node, supervised by the app), the job worker and the crons.
# Port 8080, health check /health, persistent volume at /data (SQLite database + Data Protection keys).

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/). Bun is build-only.
FROM oven/bun:1-alpine AS web
WORKDIR /src
COPY src/Inkwell/package.json src/Inkwell/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/Inkwell/vite.config.ts src/Inkwell/tsconfig.json src/Inkwell/components.json ./
COPY src/Inkwell/ClientApp ./ClientApp
# tsc runs in dev and CI (`bun run build`); the image only bundles.
RUN bun --bun vite build && bun --bun vite build --ssr

# 2. Backend: SDK only in this stage.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api
WORKDIR /src
COPY src/Inkwell/Inkwell.csproj ./
RUN dotnet restore -r linux-musl-x64
COPY src/Inkwell/ ./
# RID-specific framework-dependent publish keeps only the native assets Alpine needs.
RUN dotnet publish -c Release -r linux-musl-x64 --self-contained false -o /app --no-restore /p:UseAppHost=false

# 3. Runtime: Alpine ASP.NET + Node for the managed SSR process. ICU and tzdata stay because the samples
# use culture-specific formatting and named time zones.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache icu-libs tzdata nodejs \
    && mkdir -p /data \
    && chown -R $APP_UID:$APP_UID /data
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/wwwroot/build ./wwwroot/build
COPY --from=web /src/ssr ./ssr

VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DATABASE_URL="Data Source=/data/app.db;Default Timeout=30;Foreign Keys=True" \
    DATA_PROTECTION_KEYS_PATH=/data/keys \
    Inertia__SsrExecutable=node \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# Node is already present for SSR. start-period covers migrations on first boot.
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=3 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "Inkwell.dll"]
