# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ARG JULGATE_VERSION=
ARG MATGATE_VERSION=

COPY NuGet.config ./
COPY Matgate/Matgate.csproj Matgate/
RUN --mount=type=secret,id=github_token,required=true \
    token="$(cat /run/secrets/github_token)"; \
    export NuGetPackageSourceCredentials_github="Username=Juloc;Password=${token};ValidAuthenticationTypes=Basic"; \
    dotnet restore Matgate/Matgate.csproj --configfile /src/NuGet.config

COPY Matgate/ Matgate/
RUN version="${JULGATE_VERSION:-$MATGATE_VERSION}"; \
    if [ -n "$version" ]; then \
      dotnet publish Matgate/Matgate.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false /p:Version="$version" /p:InformationalVersion="$version"; \
    else \
      dotnet publish Matgate/Matgate.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ARG JULGATE_BUILD_TAG=local
ARG JULGATE_BUILD_TIME=
ARG MATGATE_BUILD_TAG=
ARG MATGATE_BUILD_TIME=
ENV ASPNETCORE_URLS=http://+:8080
ENV JULGATE_DATA_DIR=/data
ENV MATGATE_DATA_DIR=/data
ENV JULGATE_BUILD_TAG=${JULGATE_BUILD_TAG}
ENV MATGATE_BUILD_TAG=${MATGATE_BUILD_TAG}

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown -R "$APP_UID":0 /data /app \
    && chmod 0700 /data

EXPOSE 8080

COPY --from=build --chown=$APP_UID:0 /app/publish .
RUN set -eu; \
    build_time="${JULGATE_BUILD_TIME:-${MATGATE_BUILD_TIME:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}}"; \
    printf '%s\n' "$build_time" > /app/matgate-build-time.txt; \
    chown "$APP_UID":0 /app/matgate-build-time.txt

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl --fail --silent --show-error http://127.0.0.1:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Matgate.dll"]
