# syntax=docker/dockerfile:1

#FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
FROM ghcr.io/jdx/mise:2026.10.15-debian AS build

ARG GITHUB_ACTOR

WORKDIR /src
ENV MISE_NO_ENV=1 \
    MISE_TRUSTED_CONFIG_PATHS=/src

RUN apt-get update \
    && apt-get install -y --no-install-recommends libicu76 \
    && rm -rf /var/lib/apt/lists/*

# Restore first so NuGet packages remain cached when only application code changes.
COPY mise.toml NuGet.Config src/ui.csproj ./
RUN mise install && mise reshim
RUN --mount=type=secret,id=github_packages_token \
    NuGetPackageSourceCredentials_github="Username=${GITHUB_ACTOR};Password=$(cat /run/secrets/github_packages_token)" \
    dotnet restore

COPY src/ ./
RUN dotnet publish ui.csproj --no-restore --configuration Release --output /app/publish
RUN --mount=type=secret,id=github_packages_token \
    NuGetPackageSourceCredentials_github="Username=${GITHUB_ACTOR};Password=$(cat /run/secrets/github_packages_token)" \
    dotnet ef migrations bundle --project ui.csproj --configuration Release --self-contained -r linux-x64 --output /app/migrator

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.14-noble-chiseled-extra AS migrator
WORKDIR /app
COPY --from=build /app/migrator .
USER $APP_UID
ENTRYPOINT ["/app/migrator"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14 AS app
WORKDIR /app
COPY --from=build /app/publish .
COPY LICENSE /usr/share/licenses/jobdeck-ui/LICENSE
COPY third_party/licenses/nuget/ /usr/share/licenses/jobdeck-ui/third_party/nuget/
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    DOTNET_RUNNING_IN_CONTAINER=true
USER $APP_UID
ENTRYPOINT ["dotnet", "ui.dll"]
