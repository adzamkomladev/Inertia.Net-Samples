# syntax=docker/dockerfile:1
# Production image: web app, SSR (Node, supervised by the app), jobs and crons. Port 8080, health /health,
# volume /data. Uses external PostgreSQL (for example a Coolify database) via DATABASE_URL (see docs/DEPLOY.md).

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/). Bun is build-only.
FROM oven/bun:1-alpine AS web
WORKDIR /src
COPY src/TechLandier/package.json src/TechLandier/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/TechLandier/vite.config.ts src/TechLandier/tsconfig.json src/TechLandier/components.json ./
COPY src/TechLandier/ClientApp ./ClientApp
# vue-tsc runs in dev and CI (`bun run build`); the image only bundles.
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend: SDK only in this stage.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api
WORKDIR /src
COPY src/TechLandier/TechLandier.csproj ./
RUN dotnet restore -r linux-musl-x64
COPY src/TechLandier/ ./
# RID-specific framework-dependent publish keeps only the native assets Alpine needs.
RUN dotnet publish -c Release -r linux-musl-x64 --self-contained false -o /app --no-restore /p:UseAppHost=false

# 3. Runtime: Alpine ASP.NET + Node for the managed SSR process. ICU and tzdata stay because the storefront
# formats money and business hours in named time zones.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache icu-libs tzdata nodejs \
    && mkdir -p /data \
    && chown -R $APP_UID:$APP_UID /data
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/wwwroot/build ./wwwroot/build
COPY --from=web /src/ssr ./ssr

# /data holds the Data Protection keys and captured mail.
VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DATA_DIR=/data \
    Inertia__SsrExecutable=node \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# Node is already present for SSR. start-period covers migrations on first boot.
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=3 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "TechLandier.dll"]
