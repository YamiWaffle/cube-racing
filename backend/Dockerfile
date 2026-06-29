FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first — Docker caches this layer until .csproj files change
COPY src/CubeRacing.API/CubeRacing.API.csproj             src/CubeRacing.API/
COPY src/CubeRacing.Application/CubeRacing.Application.csproj src/CubeRacing.Application/
COPY src/CubeRacing.Domain/CubeRacing.Domain.csproj        src/CubeRacing.Domain/
COPY src/CubeRacing.Infrastructure/CubeRacing.Infrastructure.csproj src/CubeRacing.Infrastructure/

RUN dotnet restore src/CubeRacing.API/CubeRacing.API.csproj

COPY src/ src/

RUN dotnet publish src/CubeRacing.API/CubeRacing.API.csproj \
    -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "CubeRacing.API.dll"]
