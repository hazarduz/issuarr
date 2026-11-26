# syntax=docker/dockerfile:1

# ---- UI -------------------------------------------------------------------
FROM node:22-alpine AS ui
WORKDIR /app/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci
COPY frontend/ ./
RUN npm run build

# ---- Backend --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY src/ src/
COPY --from=ui /app/frontend/dist frontend/dist
RUN dotnet publish src/Issuarr.Host/Issuarr.Host.csproj -c Release -o /out --nologo

# ---- Runtime --------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app/issuarr/bin
COPY --from=build /out ./

ENV ISSUARR_DATA=/config
VOLUME ["/config"]
EXPOSE 8788

ENTRYPOINT ["dotnet", "Issuarr.dll"]
