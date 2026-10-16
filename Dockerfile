# Portable (framework-dependent, no RID) publish: the same output runs on x64 and arm64, and the
# file builds with both the classic builder and buildx.
FROM node:24-bookworm-slim AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY external/PKHeX/ external/PKHeX/
COPY src/PKForge.Sync.Protocol/PKForge.Sync.Protocol.csproj src/PKForge.Sync.Protocol/
COPY src/PKForge.Sync.Server/PKForge.Sync.Server.csproj src/PKForge.Sync.Server/
RUN dotnet restore src/PKForge.Sync.Server/PKForge.Sync.Server.csproj
COPY src/ src/
RUN dotnet publish src/PKForge.Sync.Server/PKForge.Sync.Server.csproj -c Release --no-restore -p:SkipWebBuild=true -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web/dist /app/wwwroot
COPY LICENSE THIRD-PARTY-NOTICES.md /app/
COPY docker/admin-command.sh /usr/local/bin/pkforge-sync-admin
RUN chmod +x /usr/local/bin/pkforge-sync-admin \
 && for c in create-user reset-password list-users force-resync; do ln -s /usr/local/bin/pkforge-sync-admin /usr/local/bin/$c; done \
 && mkdir /data && chown app:app /data
ENV DATA_DIR=/data ASPNETCORE_HTTP_PORTS=8080
VOLUME /data
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "PKForge.Sync.Server.dll"]
