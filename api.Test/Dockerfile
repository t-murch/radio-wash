FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build

WORKDIR /src

# Copy project files
COPY api/RadioWash.Api.csproj api/
COPY api.Test/RadioWash.Api.Test.csproj api.Test/

# Restore dependencies
RUN dotnet restore api.Test/RadioWash.Api.Test.csproj

# Copy source code
COPY api/ api/
COPY api.Test/ api.Test/

# Build the test project
RUN dotnet build api.Test/RadioWash.Api.Test.csproj --no-restore

# Run integration tests
ENTRYPOINT ["dotnet", "test", "api.Test/RadioWash.Api.Test.csproj", "--filter", "FullyQualifiedName~Integration", "--logger", "console", "--verbosity", "normal", "--no-build"]