# Image de production de Kokora : construction (Node + .NET SDK) puis exécution (ASP.NET, Linux).
# docker compose up -d --build   (voir README, section « Mise en ligne »)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update \
 && apt-get install -y --no-install-recommends ca-certificates curl gnupg \
 && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*
WORKDIR /src

# Dépendances d'abord (mises en cache tant que les fichiers de projet ne changent pas)
COPY Directory.Build.props Kokora.slnx ./
COPY src/Kokora.Domain/Kokora.Domain.csproj src/Kokora.Domain/
COPY src/Kokora.Application/Kokora.Application.csproj src/Kokora.Application/
COPY src/Kokora.Infrastructure/Kokora.Infrastructure.csproj src/Kokora.Infrastructure/
COPY src/Kokora.Web/Kokora.Web.csproj src/Kokora.Web/package.json src/Kokora.Web/package-lock.json src/Kokora.Web/
RUN cd src/Kokora.Web && npm ci --no-audit --no-fund
RUN dotnet restore src/Kokora.Web/Kokora.Web.csproj

COPY src/ src/
RUN dotnet publish src/Kokora.Web/Kokora.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Données persistantes (téléversements, clés de chiffrement et VAPID) dans le volume /data
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    Storage__DataPath=/data \
    Storage__UploadsPath=/data/uploads \
    TZ=Africa/Dakar
RUN mkdir -p /data/uploads && chown -R app:app /data
USER app
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Kokora.Web.dll"]
