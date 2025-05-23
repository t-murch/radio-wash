# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy csproj and restore as distinct layers (better caching)
COPY ["RadioWash.Api.csproj", "./"]
RUN dotnet restore "RadioWash.Api.csproj"

# Copy everything else and build
COPY [".", "./"]
WORKDIR "/src"
RUN dotnet build "RadioWash.Api.csproj" -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish "RadioWash.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Final stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Add healthcheck
HEALTHCHECK --interval=30s --timeout=3s \
  CMD curl -f http://localhost:80/healthz || exit 1

# Create non-root user for security
RUN adduser --disabled-password --gecos "" appuser
USER appuser

ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80
# Copy published app and set entry point
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "RadioWash.Api.dll"]
