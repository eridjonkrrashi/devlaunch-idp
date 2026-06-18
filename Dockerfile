# ── Stage 1: build frontend ──────────────────────────────────────────────────
FROM node:22-alpine AS frontend-build
WORKDIR /frontend
COPY devlaunch-frontend/package*.json ./
RUN npm ci
COPY devlaunch-frontend/ ./
RUN npm run build
# output lands in ../DevLaunch.Api/wwwroot — but we work from /frontend so adjust:
# vite.config sets outDir: '../DevLaunch.Api/wwwroot'
# In Docker the context is repo root, so output is at /DevLaunch.Api/wwwroot
# We'll copy from that location in the publish stage.

# ── Stage 2: build API ────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["DevLaunch.Api/DevLaunch.Api.csproj", "DevLaunch.Api/"]
RUN dotnet restore "DevLaunch.Api/DevLaunch.Api.csproj"
COPY . .
# Copy pre-built frontend static assets
COPY --from=frontend-build /DevLaunch.Api/wwwroot /src/DevLaunch.Api/wwwroot
WORKDIR "/src/DevLaunch.Api"
RUN dotnet build "DevLaunch.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "DevLaunch.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "DevLaunch.Api.dll"]
