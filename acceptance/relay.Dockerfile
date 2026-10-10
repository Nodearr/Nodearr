# syntax=docker/dockerfile:1.7

# An iroh relay for the S8 acceptance run only. It stands in for a public relay, so that the
# script can take away the direct route between two nodes and show that no file data goes
# through the relay. Same version as the iroh the transport sidecar is built on.
#
#   docker build -t nodearr-test-relay -f acceptance/relay.Dockerfile acceptance

FROM rust:1-bookworm AS build
RUN --mount=type=cache,target=/usr/local/cargo/registry \
    cargo install iroh-relay --version =1.3.0 --features server --locked --root /out

FROM debian:bookworm-slim
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /out/bin/iroh-relay /usr/local/bin/iroh-relay

# Plain HTTP on 3340, and its counters on 9090.
EXPOSE 3340 9090
ENTRYPOINT ["iroh-relay", "--dev"]
