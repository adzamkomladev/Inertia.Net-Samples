# syntax=docker/dockerfile:1

# 1. Frontend: client bundle (wwwroot/build) + SSR bundle (ssr/)
FROM oven/bun:1-debian AS web
WORKDIR /src/src/VuePgBlog
COPY src/VuePgBlog/package.json src/VuePgBlog/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/VuePgBlog/vite.config.ts src/VuePgBlog/tsconfig.json src/VuePgBlog/components.json ./
COPY src/VuePgBlog/ClientApp ./ClientApp
# Typecheck (vue-tsc) runs in dev/CI; under bun in this image it can't resolve .vue files, so build only.
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

# 2. Backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src/src/VuePgBlog
COPY src/VuePgBlog/VuePgBlog.csproj ./
RUN dotnet restore
COPY src/VuePgBlog/ ./
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false

# 3. Runtime: ASP.NET + the bun binary that runs `ssr/ssr.js` (the SSR process Inertia.Net starts).
FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/src/VuePgBlog/wwwroot/build ./wwwroot/build
COPY --from=web /src/src/VuePgBlog/ssr ./ssr

# /data holds Data Protection keys and captured mail -> mount persistent storage here.
RUN apt-get update && apt-get install -y --no-install-recommends postgresql-client \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    App__DataDir=/data \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# No curl in the image; bun is already here for SSR.
HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD ["bun", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "VuePgBlog.dll"]
