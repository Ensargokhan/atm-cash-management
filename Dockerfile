# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore first (cached as long as project files do not change)
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/AtmCash.Domain/AtmCash.Domain.csproj src/AtmCash.Domain/
COPY src/AtmCash.Application/AtmCash.Application.csproj src/AtmCash.Application/
COPY src/AtmCash.Infrastructure/AtmCash.Infrastructure.csproj src/AtmCash.Infrastructure/
COPY src/AtmCash.Api/AtmCash.Api.csproj src/AtmCash.Api/
RUN dotnet restore src/AtmCash.Api/AtmCash.Api.csproj

COPY src/ src/
RUN dotnet publish src/AtmCash.Api/AtmCash.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
USER app
ENTRYPOINT ["dotnet", "AtmCash.Api.dll"]
