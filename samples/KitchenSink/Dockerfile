# syntax=docker/dockerfile:1
# One image: the ASP.NET app and the Vite-built Vue bundle. Port 8080, volume /data.

FROM oven/bun:1-alpine AS web
WORKDIR /src
COPY src/KitchenSink/package.json src/KitchenSink/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/KitchenSink/vite.config.ts src/KitchenSink/tsconfig.json src/KitchenSink/components.json ./
COPY src/KitchenSink/ClientApp ./ClientApp
RUN bun --bun vite build

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS api
WORKDIR /src
COPY src/KitchenSink/KitchenSink.csproj ./
RUN dotnet restore -r linux-musl-x64
COPY src/KitchenSink/ ./
RUN dotnet publish -c Release -r linux-musl-x64 --self-contained false -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
RUN apk add --no-cache icu-libs tzdata \
    && mkdir -p /data \
    && chown -R $APP_UID:$APP_UID /data
WORKDIR /app
COPY --from=api /app ./
COPY --from=web /src/wwwroot/build ./wwwroot/build

VOLUME /data
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    App__DataDir=/data \
    DATABASE_PATH=/data/kitchen-sink.db \
    Demo__GlobalRateLimit=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
EXPOSE 8080
USER $APP_UID

HEALTHCHECK --interval=15s --timeout=5s --start-period=30s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/health >/dev/null 2>&1 || exit 1

ENTRYPOINT ["dotnet", "KitchenSink.dll"]
