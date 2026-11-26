# syntax=docker/dockerfile:1

# ---- UI -------------------------------------------------------------------
# The UI and .NET build run on the build machine's own architecture (fast);
# the output is architecture-neutral, so only the final stage is per-platform.
FROM --platform=$BUILDPLATFORM node:22-alpine AS ui
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ---- Backend --------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY src/ src/
COPY --from=ui /app/frontend/dist frontend/dist
RUN dotnet publish src/Issuarr.Host/Issuarr.Host.csproj -c Release -o /out --nologo

# ---- Runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
LABEL org.opencontainers.image.title="Issuarr" \
      org.opencontainers.image.description="Self-hosted manager for magazines and comics" \
      org.opencontainers.image.source="https://github.com/hazarduz/issuarr"
WORKDIR /app/issuarr/bin
COPY --from=build /out ./

ENV ISSUARR_DATA=/config
VOLUME ["/config"]
EXPOSE 8788

ENTRYPOINT ["dotnet", "Issuarr.dll"]
