# syntax=docker/dockerfile:1
# Production image: web app, SSR (Node, supervised by the app), jobs and crons. Port 8080, health /health.
# A replica deployment also needs REDIS_URL for Data Protection, SignalR and the Inertia cache and a
# MongoDB replica set in DATABASE_URL (see docs/DEPLOY.md and docs/REPLICAS.md).

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/). Bun is build-only.
FROM oven/bun:1-alpine AS web
WORKDIR /src/src/CarParkerReplicas
COPY src/CarParkerReplicas/package.json src/CarParkerReplicas/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/CarParkerReplicas/vite.config.ts src/CarParkerReplicas/tsconfig.json src/CarParkerReplicas/svelte.config.js ./
COPY src/CarParkerReplicas/ClientApp ./ClientApp
# Typecheck (svelte-check) runs in dev/CI; this image only needs the hardened production build.
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend: SDK only in this stage.
FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api
WORKDIR /src/src/CarParkerReplicas
COPY src/CarParkerReplicas/CarParkerReplicas.csproj ./
RUN dotnet restore -r linux-musl-x64
COPY src/CarParkerReplicas/ ./
# RID-specific framework-dependent publish keeps only the native assets Alpine needs.
RUN dotnet publish -c Release -r linux-musl-x64 --self-contained false -o /app --no-restore /p:UseAppHost=false

# 3. Runtime: Alpine ASP.NET + Node for the managed SSR process. ICU and tzdata stay because the samples
# use culture-specific money formatting and named time zones. libstdc++, libgcc and zlib are the native dependencies of
# QuestPDF (PDF receipts); no fontconfig or font packages are needed because the PDFs use the font embedded in QuestPDF.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache icu-libs tzdata nodejs libstdc++ libgcc zlib \
    && mkdir -p /data \
    && chown -R $APP_UID:$APP_UID /data
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/src/CarParkerReplicas/wwwroot/build ./wwwroot/build
COPY --from=web /src/src/CarParkerReplicas/ssr ./ssr

# /data is the file Data Protection fallback when Redis is absent and the app's durable local storage.
VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    App__DataDir=/data \
    Inertia__SsrExecutable=node \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# Node is already present for SSR. Degraded SSR/cache still answers 200; MongoDB or critical Redis
# failures answer 503 and remove the replica from the proxy pool.
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=3 \
  CMD ["node", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "CarParkerReplicas.dll"]
