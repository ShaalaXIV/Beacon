FROM mcr.microsoft.com/dotnet/sdk:10.0.302@sha256:72dd743782f2ae7e5476fd64f6a460045e3998dc862218b80e6944cba79a01b0 AS build
WORKDIR /src

COPY NuGet.Config global.json Directory.Build.props ./
COPY src/Compass.Shared/Compass.Shared.csproj src/Compass.Shared/packages.lock.json src/Compass.Shared/
COPY src/Compass.Server/Compass.Server.csproj src/Compass.Server/packages.lock.json src/Compass.Server/
RUN dotnet restore src/Compass.Server/Compass.Server.csproj --locked-mode

COPY src/Compass.Shared/ src/Compass.Shared/
COPY src/Compass.Server/ src/Compass.Server/
RUN dotnet publish src/Compass.Server/Compass.Server.csproj \
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
    COMPASS_HEALTH_URL=http://127.0.0.1:8080/health

COPY --from=build /out/ ./

USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD ["dotnet", "Compass.Server.dll", "--health-check"]
ENTRYPOINT ["dotnet", "Compass.Server.dll"]
