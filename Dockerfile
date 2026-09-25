# =============================================================================
# Kromic Commerce — Production Dockerfile
# =============================================================================
#
# Build strategy: multi-stage build
#   Stage 1 (build)   — SDK image, restores packages, compiles, publishes
#   Stage 2 (runtime) — ASP.NET runtime image only (no SDK, no EF CLI tools)
#
# Migration strategy:
#   The application owns database migrations.
#   Set App__Migrate=true in the Render environment to auto-migrate on deploy.
#   The Dockerfile does NOT run `dotnet ef database update`.
#   The container ENTRYPOINT simply starts the .NET application.
#
# =============================================================================

# --- Stage 1: Build & Publish ------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source

# Copy solution and project files first (better layer caching)
COPY KromicCommerce.sln global.json ./
COPY src/KromicCommerce.Domain/KromicCommerce.Domain.csproj               src/KromicCommerce.Domain/
COPY src/KromicCommerce.Shared/KromicCommerce.Shared.csproj               src/KromicCommerce.Shared/
COPY src/KromicCommerce.Contracts/KromicCommerce.Contracts.csproj         src/KromicCommerce.Contracts/
COPY src/KromicCommerce.Application/KromicCommerce.Application.csproj     src/KromicCommerce.Application/
COPY src/KromicCommerce.Infrastructure/KromicCommerce.Infrastructure.csproj src/KromicCommerce.Infrastructure/
COPY src/KromicCommerce.Api/KromicCommerce.Api.csproj                     src/KromicCommerce.Api/

# Restore NuGet packages (cached unless .csproj files change)
RUN dotnet restore src/KromicCommerce.Api/KromicCommerce.Api.csproj

# Copy remaining source
COPY src/ src/

# Publish the API in Release configuration
RUN dotnet publish src/KromicCommerce.Api/KromicCommerce.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

# --- Stage 2: Runtime --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Create a non-root user for security
RUN addgroup --system --gid 1001 kromic \
    && adduser --system --uid 1001 --ingroup kromic kromic

# Copy the published output from the build stage
COPY --from=build --chown=kromic:kromic /app/publish .

# Switch to non-root user
USER kromic

# Expose HTTP port (Render maps this to HTTPS externally)
EXPOSE 8080

# Render sets PORT env var; configure Kestrel to listen on it
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

# =============================================================================
# ENTRYPOINT — starts the .NET application.
# The application itself runs Database.MigrateAsync() when App__Migrate=true.
# No EF CLI tools are required or present in this image.
# =============================================================================
ENTRYPOINT ["dotnet", "KromicCommerce.Api.dll"]
