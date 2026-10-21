# syntax=docker/dockerfile:1
# One image runs everything: the web app, SSR (bun, supervised by the app), the job worker and the crons.
# Port 8080, health check /health, persistent volume at /data (SQLite database + Data Protection keys).

FROM oven/bun:1-debian AS web
WORKDIR /src
COPY src/Inkwell/package.json src/Inkwell/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/Inkwell/vite.config.ts src/Inkwell/tsconfig.json src/Inkwell/components.json ./
COPY src/Inkwell/ClientApp ./ClientApp
# tsc runs in dev and CI (`bun run build`); the image only bundles.
RUN bun --bun vite build && bun --bun vite build --ssr

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api
WORKDIR /src
COPY src/Inkwell/Inkwell.csproj ./
RUN dotnet restore
COPY src/Inkwell/ ./
RUN dotnet publish -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/wwwroot/build ./wwwroot/build
COPY --from=web /src/ssr ./ssr
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DATABASE_URL="Data Source=/data/app.db;Default Timeout=30;Foreign Keys=True" \
    DATA_PROTECTION_KEYS_PATH=/data/keys \
    DOTNET_gcServer=0
EXPOSE 8080
USER $APP_UID

# No curl in the image; bun is already here for SSR. start-period covers migrations on first boot.
HEALTHCHECK --interval=15s --timeout=5s --start-period=60s --retries=3 \
  CMD ["bun", "-e", "fetch('http://127.0.0.1:8080/health').then(r=>process.exit(r.ok?0:1)).catch(()=>process.exit(1))"]

ENTRYPOINT ["dotnet", "Inkwell.dll"]
