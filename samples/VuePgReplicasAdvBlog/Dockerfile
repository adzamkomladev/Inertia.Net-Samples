# syntax=docker/dockerfile:1
FROM oven/bun:1-debian AS web
WORKDIR /src/apps/MainApp
COPY apps/MainApp/package.json apps/MainApp/bun.lock ./
RUN bun install --frozen-lockfile
COPY apps/MainApp/vite.config.ts apps/MainApp/tsconfig.json apps/MainApp/components.json ./
COPY apps/MainApp/ClientApp ./ClientApp
RUN bun --bun vite build && bun --bun vite build --ssr ClientApp/ssr.ts

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props Directory.Build.targets ./
COPY lib/Common/Common.csproj lib/Common/
COPY lib/Auth/Auth.csproj lib/Auth/
COPY lib/Posts/Posts.csproj lib/Posts/
COPY lib/Comments/Comments.csproj lib/Comments/
COPY lib/Likes/Likes.csproj lib/Likes/
COPY lib/Uploads/Uploads.csproj lib/Uploads/
COPY lib/Social/Social.csproj lib/Social/
COPY lib/Preferences/Preferences.csproj lib/Preferences/
COPY lib/Analytics/Analytics.csproj lib/Analytics/
COPY lib/Discovery/Discovery.csproj lib/Discovery/
COPY lib/Digests/Digests.csproj lib/Digests/
COPY lib/Email/Email.csproj lib/Email/
COPY lib/Search/Search.csproj lib/Search/
COPY lib/ServiceDefaults/ServiceDefaults.csproj lib/ServiceDefaults/
COPY apps/MainApp/MainApp.csproj apps/MainApp/
COPY apps/Worker/Worker.csproj apps/Worker/
COPY apps/Realtime/Realtime.csproj apps/Realtime/
RUN dotnet restore apps/MainApp && dotnet restore apps/Worker && dotnet restore apps/Realtime
COPY lib/ lib/
COPY apps/ apps/
RUN dotnet publish apps/MainApp -c Release -o /out/main --no-restore /p:UseAppHost=false \
    && dotnet publish apps/Worker -c Release -o /out/worker --no-restore /p:UseAppHost=false \
    && dotnet publish apps/Realtime -c Release -o /out/realtime --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get -o Acquire::Retries=3 update \
    && apt-get -o Acquire::Retries=3 install -y --no-install-recommends curl ca-certificates \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data && chown $APP_UID:$APP_UID /data
WORKDIR /app
ENV DOTNET_ENVIRONMENT=Production ASPNETCORE_ENVIRONMENT=Production App__DataDir=/data DOTNET_gcServer=0

FROM runtime AS web-runtime
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://127.0.0.1:8080/health || exit 1

FROM web-runtime AS mainapp
COPY --from=build /out/main ./
COPY --from=web /usr/local/bin/bun /usr/local/bin/bun
COPY --from=web /src/apps/MainApp/wwwroot/build ./wwwroot/build
COPY --from=web /src/apps/MainApp/ssr ./ssr
USER $APP_UID
ENTRYPOINT ["dotnet", "MainApp.dll"]

FROM runtime AS worker
# Match the database major version; only Worker runs backups.
RUN curl --fail --retry 3 https://www.postgresql.org/media/keys/ACCC4CF8.asc -o /usr/share/keyrings/postgresql.asc \
    && . /etc/os-release \
    && echo "deb [signed-by=/usr/share/keyrings/postgresql.asc] https://apt.postgresql.org/pub/repos/apt $VERSION_CODENAME-pgdg main" > /etc/apt/sources.list.d/postgresql.list \
    && apt-get -o Acquire::Retries=3 update \
    && apt-get -o Acquire::Retries=3 install -y --no-install-recommends postgresql-client-18 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Worker.dll"]

FROM web-runtime AS realtime
COPY --from=build /out/realtime ./
USER $APP_UID
ENTRYPOINT ["dotnet", "Realtime.dll"]
