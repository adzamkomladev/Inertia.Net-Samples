# syntax=docker/dockerfile:1

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/)
FROM oven/bun:1-debian AS web
WORKDIR /src/src/SvelteMongoBlog
COPY src/SvelteMongoBlog/package.json src/SvelteMongoBlog/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/SvelteMongoBlog/vite.config.ts src/SvelteMongoBlog/tsconfig.json src/SvelteMongoBlog/svelte.config.js ./
COPY src/SvelteMongoBlog/ClientApp ./ClientApp
# Typecheck (svelte-check) runs in dev/CI; this image only needs the hardened production build.
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/src/SvelteMongoBlog
COPY src/SvelteMongoBlog/SvelteMongoBlog.csproj ./
RUN dotnet restore
COPY src/SvelteMongoBlog/ ./
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false

FROM mongo:8 AS mongo-tools

# 3. Runtime: ASP.NET + the bun binary that runs `ssr/ssr.js` (the SSR process Inertia.Net starts).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=mongo-tools /usr/bin/mongodump /usr/local/bin/mongodump
RUN apt-get update && apt-get install -y --no-install-recommends libicu-dev tzdata libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/src/SvelteMongoBlog/wwwroot/build ./wwwroot/build
COPY --from=web /src/src/SvelteMongoBlog/ssr ./ssr

# /data holds Data Protection keys and temporary backups; MongoDB stores database data separately.
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    App__DataDir=/data \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# No curl in the image; bun is already here for SSR.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bun", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "SvelteMongoBlog.dll"]
