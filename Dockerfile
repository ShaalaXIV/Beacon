FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
WORKDIR /src

COPY NuGet.Config global.json Directory.Build.props ./
COPY src/Beacon.Shared/Beacon.Shared.csproj src/Beacon.Shared/packages.lock.json src/Beacon.Shared/
COPY src/Beacon.Server/Beacon.Server.csproj src/Beacon.Server/packages.lock.json src/Beacon.Server/
RUN dotnet restore src/Beacon.Server/Beacon.Server.csproj --locked-mode

COPY src/Beacon.Shared/ src/Beacon.Shared/
COPY src/Beacon.Server/ src/Beacon.Server/
RUN dotnet publish src/Beacon.Server/Beacon.Server.csproj \
    -c Release \
    --no-restore \
    --no-self-contained \
    -p:UseAppHost=false \
    -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-noble-chiseled@sha256:70d6f993bf715a031f027832a19cfb7f894df66c8b5eb40be0aaee820ad5d119 AS runtime
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://0.0.0.0:8080 \
    DOTNET_EnableDiagnostics=0 \
    BEACON_HEALTH_URL=http://127.0.0.1:8080/health

COPY --from=build /out/ ./

USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "Beacon.Server.dll", "--health-check"]
ENTRYPOINT ["dotnet", "Beacon.Server.dll"]
