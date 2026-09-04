# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY SuperScanner.slnx ./
COPY src/ ./src/
RUN dotnet restore src/SuperScanner.Api/SuperScanner.Api.csproj --locked-mode
RUN dotnet publish src/SuperScanner.Api/SuperScanner.Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN mkdir -p /app/tmp && chown -R "$APP_UID:$APP_UID" /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0 \
    TMPDIR=/app/tmp
USER $APP_UID
COPY --from=build --chown=$APP_UID:$APP_UID /app/publish ./
EXPOSE 8080
ENTRYPOINT ["dotnet", "SuperScanner.Api.dll"]
