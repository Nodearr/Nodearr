# syntax=docker/dockerfile:1.7

# Nodearr in one image: the web UI, the .NET application and the peer transport sidecar.
#
#   docker build -t nodearr .
#   docker buildx build --platform linux/amd64,linux/arm64 -t nodearr .
#
# The three build stages share nothing, so they build side by side. They run on the machine doing
# the build, whatever the image is for: the web UI and the .NET application come out the same for
# every architecture, and the sidecar is cross-compiled. Only the last stage is the target's own.

# --- The web UI -----------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM node:20.11.1-bookworm AS frontend
WORKDIR /build

RUN corepack enable && corepack prepare yarn@4.15.0 --activate

COPY package.json yarn.lock .yarnrc.yml tsconfig.json ./
RUN --mount=type=cache,target=/root/.yarn/berry/cache \
    yarn install --immutable

COPY frontend ./frontend
RUN yarn build --env production

# --- The peer transport sidecar ---------------------------------------------------------------
# A statically linked musl binary for the target architecture, cross-compiled with Zig, as the
# release packages' Linux sidecars are (build/Build-Transport.ps1).
FROM --platform=$BUILDPLATFORM rust:1-bookworm AS transport
ARG TARGETARCH
WORKDIR /build/transport

RUN apt-get update && apt-get install -y --no-install-recommends python3-venv && rm -rf /var/lib/apt/lists/* && \
    python3 -m venv /opt/zigbuild && /opt/zigbuild/bin/pip install --no-cache-dir cargo-zigbuild==0.23.4
ENV PATH=/opt/zigbuild/bin:$PATH

COPY transport/Cargo.toml transport/Cargo.lock ./
COPY transport/src ./src
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    --mount=type=cache,target=/build/transport/target,id=transport-target-$TARGETARCH \
    case "$TARGETARCH" in \
      amd64) target=x86_64-unknown-linux-musl ;; \
      arm64) target=aarch64-unknown-linux-musl ;; \
      arm) target=armv7-unknown-linux-musleabihf ;; \
      *) echo "No sidecar target for $TARGETARCH" && exit 1 ;; \
    esac && \
    rustup target add "$target" && \
    cargo zigbuild --release --locked --target "$target" && \
    cp "target/$target/release/nodearr-transport" /nodearr-transport

# --- The application --------------------------------------------------------------------------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /build

# The release version, such as 0.1.0, which the release workflow passes. Left empty, the build
# numbers itself as a development build does.
ARG VERSION=

# The repository's own global.json pins one SDK patch level for development. The image's SDK is
# used here as it comes, so that file is left out.
# .editorconfig sets which analyzer rules are errors. Without it every rule is.
COPY .editorconfig ./
COPY Logo ./Logo
COPY src ./src
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet build src/Nodearr.sln --configuration Release -p:Platform=Posix --nologo --verbosity minimal \
      ${VERSION:+-p:AssemblyVersion=${VERSION%%-*}.0}

# --- What runs --------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl gosu libsqlite3-0 tini && \
    rm -rf /var/lib/apt/lists/*

COPY --from=backend /build/_output/net10.0 /app/bin
COPY --from=frontend /build/_output/UI /app/bin/UI
COPY --from=transport /nodearr-transport /app/bin/nodearr-transport
COPY docker/entrypoint.sh /usr/local/bin/nodearr-entrypoint
RUN chmod 0755 /usr/local/bin/nodearr-entrypoint /app/bin/nodearr-transport

# /config holds the database and the node's key. Losing it loses the node's identity and every
# trust it has. /downloads is where transfers land, and must be the same folder Sonarr and
# Radarr see as /downloads.
ENV HOME=/config \
    PUID=1000 \
    PGID=1000 \
    Nodearr__Server__Port=6633 \
    Nodearr__Server__BindAddress=* \
    Nodearr__Transport__DownloadFolder=/downloads \
    Nodearr__Transport__Port=6633

VOLUME ["/config", "/downloads"]

# The web UI and the API the Arr apps call, and the UDP port other nodes connect to.
EXPOSE 6633/tcp 6633/udp

HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:6633/ping || exit 1

ENTRYPOINT ["tini", "--", "nodearr-entrypoint"]
CMD ["dotnet", "/app/bin/Nodearr.dll", "-nobrowser", "-data=/config"]
