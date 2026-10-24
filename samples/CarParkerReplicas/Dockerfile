# syntax=docker/dockerfile:1
# Production image: web app, SSR (bun, supervised by the app), jobs and crons. Port 8080, health /health.
# A replica deployment also needs REDIS_URL for Data Protection, SignalR and the Inertia cache and a
# MongoDB replica set in DATABASE_URL (see docs/DEPLOY.md and docs/REPLICAS.md).

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/)
FROM oven/bun:1-debian AS web
WORKDIR /src/src/CarParkerReplicas
COPY src/CarParkerReplicas/package.json src/CarParkerReplicas/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/CarParkerReplicas/vite.config.ts src/CarParkerReplicas/tsconfig.json src/CarParkerReplicas/svelte.config.js ./
COPY src/CarParkerReplicas/ClientApp ./ClientApp
# Typecheck (svelte-check) runs in dev/CI; this image only needs the hardened production build.
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/src/CarParkerReplicas
COPY src/CarParkerReplicas/CarParkerReplicas.csproj ./
RUN dotnet restore
COPY src/CarParkerReplicas/ ./
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false

# 3. Runtime: ASP.NET + the bun binary that runs `ssr/ssr.js` (the SSR process Inertia.Net starts).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
RUN apt-get update && apt-get install -y --no-install-recommends libicu-dev tzdata libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/src/CarParkerReplicas/wwwroot/build ./wwwroot/build
COPY --from=web /src/src/CarParkerReplicas/ssr ./ssr

# /data is the file Data Protection fallback when Redis is absent and the app's durable local storage.
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    App__DataDir=/data \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# No curl in the image; bun is already here for SSR. Degraded SSR/cache still answers 200; MongoDB or
# critical Redis failures answer 503 and remove the replica from the proxy pool.
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=3 \
  CMD ["bun", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "CarParkerReplicas.dll"]
