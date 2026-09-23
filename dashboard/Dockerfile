# syntax=docker/dockerfile:1

# Build context is the repo root (Render Root Directory empty,
# Dockerfile Path dashboard/Dockerfile) so the factory catalog
# (agents.json) ships into the runtime image.

# ---- Stage 1: build the Vue SPA ----
FROM node:22-alpine AS web
WORKDIR /web
COPY dashboard/src/web/package.json dashboard/src/web/package-lock.json ./
RUN npm ci
COPY dashboard/src/web/ ./
RUN npm run build

# ---- Stage 2: publish the .NET app ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS api
WORKDIR /src
COPY dashboard/src/Api/ ./
RUN dotnet publish -c Release -o /app/publish

# ---- Stage 3: runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=api /app/publish ./
# Built SPA becomes the static web root.
COPY --from=web /web/dist ./wwwroot
# Factory workflow catalog: FactoryCatalog.Locate probes /app first.
COPY agents.json ./agents.json

ENV PORT=10000
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "Api.dll"]
