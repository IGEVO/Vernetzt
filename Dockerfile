# syntax=docker/dockerfile:1
# Help documentation build stage
FROM node:25-alpine AS help
WORKDIR /src/user-docs
COPY user-docs/package.json user-docs/package-lock.json ./
RUN npm ci
RUN npm audit --audit-level=high
COPY user-docs/ ./
RUN npm run build:ci

# Vue build stage
FROM node:25-alpine AS frontend
WORKDIR /src/Igevo.Vernetzt.WebApp
COPY Igevo.Vernetzt.WebApp/package.json Igevo.Vernetzt.WebApp/package-lock.json ./
RUN npm ci
RUN npm audit --audit-level=high
COPY Igevo.Vernetzt.WebApp/ ./
COPY --from=help /src/user-docs/site ./wwwroot/help
RUN npm run build

# .NET build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG APP_VERSION=0.0.0
ARG APP_GIT_HASH=local

# Copy project files for restore
COPY Igevo.Vernetzt.Core/Igevo.Vernetzt.Core.csproj Igevo.Vernetzt.Core/
COPY Igevo.Vernetzt.Infrastructure/Igevo.Vernetzt.Infrastructure.csproj Igevo.Vernetzt.Infrastructure/
COPY Igevo.Vernetzt.ServiceDefaults/Igevo.Vernetzt.ServiceDefaults.csproj Igevo.Vernetzt.ServiceDefaults/
COPY Igevo.Vernetzt.Server/Igevo.Vernetzt.Server.csproj Igevo.Vernetzt.Server/

RUN dotnet restore ./Igevo.Vernetzt.Server/Igevo.Vernetzt.Server.csproj

# Copy all source code and publish
COPY . .
RUN dotnet publish Igevo.Vernetzt.Server/Igevo.Vernetzt.Server.csproj -c Release -o /app/publish --no-restore \
    -p:SkipVueBuild=true \
    -p:Version=${APP_VERSION} \
    -p:InformationalVersion=${APP_VERSION}+${APP_GIT_HASH}

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ARG APP_GIT_HASH=local

# Apply pending OS security updates (the base image may lag behind) and install curl for health checks
RUN apt-get update \
    && apt-get upgrade -y --no-install-recommends \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Configure environment
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV VERNETZT_GIT_HASH=${APP_GIT_HASH}
# User the app runs as (set at runtime, see docker/entrypoint.sh)
ENV PUID=1654
ENV PGID=1654

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Copy published application
COPY --from=build /app/publish .
COPY --from=frontend /src/Igevo.Vernetzt.WebApp/dist ./wwwroot

# Persistent storage: SQLite database (/app/data, must be a local disk), uploads (/app/uploads),
# database backups (/app/backups). Mount each as its own volume, see docs/HOSTING.md.
RUN mkdir -p /app/App_Data /app/data /app/uploads /app/backups \
    && chown "$APP_UID:$APP_UID" /app/App_Data /app/data /app/uploads /app/backups
VOLUME ["/app/data", "/app/uploads", "/app/backups"]

# Starts as root only to fix volume ownership, then drops to PUID:PGID
COPY --chmod=755 docker/entrypoint.sh /usr/local/bin/entrypoint.sh
ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
CMD ["dotnet", "Igevo.Vernetzt.Server.dll"]
