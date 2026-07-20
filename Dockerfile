FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["NuGet.Config", "./"]
COPY ["src/Indexarr.Web/Indexarr.Web.csproj", "src/Indexarr.Web/"]
RUN dotnet restore "src/Indexarr.Web/Indexarr.Web.csproj" --configfile ./NuGet.Config

COPY . .
WORKDIR /src/src/Indexarr.Web
RUN dotnet publish "Indexarr.Web.csproj" -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

LABEL org.opencontainers.image.title="Indexarr" \
      org.opencontainers.image.description="Self-hosted dashboard to monitor, protect, and automate Prowlarr indexers." \
      org.opencontainers.image.source="https://github.com/gabryk91/Indexarr" \
      org.opencontainers.image.url="https://github.com/gabryk91/Indexarr" \
      org.opencontainers.image.icon="https://raw.githubusercontent.com/gabryk91/Indexarr/main/src/Indexarr.Web/wwwroot/images/indexarr_logo.png" \
      org.opencontainers.image.documentation="https://github.com/gabryk91/Indexarr/blob/main/README.md"

RUN apt-get update \
    && apt-get install -y --no-install-recommends wget \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
ENV TZ=Europe/Rome
ENV Indexarr__ConfigPath=/config
ENV Indexarr__BackupPath=/backups
ENV Indexarr__LogsPath=/logs
ENV Indexarr__Automation__Enabled=true
ENV Indexarr__Automation__IntervalMinutes=60

RUN mkdir -p /config /backups /logs

COPY --from=build /app/publish .

EXPOSE 8080
VOLUME ["/config", "/backups", "/logs"]

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/readyz || exit 1

ENTRYPOINT ["dotnet", "Indexarr.Web.dll"]
