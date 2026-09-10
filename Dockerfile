# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for dependency resolution
COPY ["Lab-ai-assist.slnx", "."]
COPY ["src/LabAi.Domain/LabAi.Domain.csproj", "src/LabAi.Domain/"]
COPY ["src/LabAi.Application/LabAi.Application.csproj", "src/LabAi.Application/"]
COPY ["src/LabAi.Infrastructure/LabAi.Infrastructure.csproj", "src/LabAi.Infrastructure/"]
COPY ["src/LabAi.Web/LabAi.Web.csproj", "src/LabAi.Web/"]
COPY ["tests/LabAi.Tests/LabAi.Tests.csproj", "tests/LabAi.Tests/"]
COPY ["Directory.Build.props", "."]

RUN dotnet restore "src/LabAi.Web/LabAi.Web.csproj"

# Copy source code and build
COPY . .
RUN dotnet publish "src/LabAi.Web/LabAi.Web.csproj" -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Create non-root user for security (Debian uses useradd, not adduser)
RUN useradd -r -s /bin/false appuser && \
    mkdir -p /app/data /app/logs && \
    chown -R appuser:appuser /app

USER appuser

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=5s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1

# Set environment variables
ENV ASPNETCORE_URLS=http://+:8080
ENV DOTNET_ENVIRONMENT=Production

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "LabAi.Web.dll"]
