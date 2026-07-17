# syntax=docker/dockerfile:1
#
# Multi-stage build producing a single self-contained ASP.NET Core image that
# serves the React SPA + REST API + SignalR hub, with the imported SQLite DBs
# baked in. Mirrors build-release.ps1, but for a container.

# ── Stage 1: build the React / Vite frontend ─────────────────────────────
FROM node:22-alpine AS frontend
WORKDIR /src/ClientApp
# Install deps first so this layer is cached until the lockfile changes.
COPY creaturegame.Web/ClientApp/package.json creaturegame.Web/ClientApp/package-lock.json ./
RUN npm ci
COPY creaturegame.Web/ClientApp/ ./
RUN npm run build
# -> /src/ClientApp/dist  (index.html + hashed assets)

# ── Stage 2: build & publish the .NET app ────────────────────────────────
# Pinned to the exact SDK from global.json (9.0.200, rollForward: disable) —
# the floating :9.0 tag would resolve to a newer patch and fail the pin.
FROM mcr.microsoft.com/dotnet/sdk:9.0.200 AS backend
WORKDIR /src
COPY . .
# Overlay the built SPA into wwwroot, which already holds audio/ + sprites/.
COPY --from=frontend /src/ClientApp/dist/ ./creaturegame.Web/wwwroot/
# The .csproj copies pokemon.db/moves.db/items.db into the publish output.
RUN dotnet publish creaturegame.Web -c Release -o /app/publish

# ── Stage 3: runtime image ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=backend /app/publish ./
# Fly terminates TLS at the edge and forwards plain HTTP to this port;
# must match internal_port in fly.toml. `+` binds all interfaces.
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "creaturegame.Web.dll"]
