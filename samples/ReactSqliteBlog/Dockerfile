# syntax=docker/dockerfile:1
FROM oven/bun:1-debian AS web
WORKDIR /src/src/ReactSqliteBlog
COPY src/ReactSqliteBlog/package.json src/ReactSqliteBlog/bun.lock ./
RUN bun install --frozen-lockfile
COPY src/ReactSqliteBlog/ ./
RUN bun run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/ReactSqliteBlog/ReactSqliteBlog.csproj src/ReactSqliteBlog/
RUN dotnet restore src/ReactSqliteBlog/ReactSqliteBlog.csproj
COPY src/ReactSqliteBlog/ src/ReactSqliteBlog/
RUN dotnet publish src/ReactSqliteBlog/ReactSqliteBlog.csproj -c Release -o /app --no-restore
COPY --from=web /src/src/ReactSqliteBlog/wwwroot/build /app/wwwroot/build
COPY --from=web /src/src/ReactSqliteBlog/ssr /app/ssr

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
# SSR bundle is self-contained: only the bun binary is needed at runtime (the app runs `bun ssr/ssr.js`).
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
RUN apt-get update \
 && apt-get install -y --no-install-recommends curl \
 && rm -rf /var/lib/apt/lists/* \
 && mkdir -p /data && chown app:app /data
WORKDIR /app
COPY --from=build --chown=app:app /app ./
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true
USER app
VOLUME /data
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "ReactSqliteBlog.dll"]
