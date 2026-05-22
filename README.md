# Nodearr

Nodearr is a self-hosted trusted peer-to-peer media file discovery and transfer service for the Servarr ecosystem. This repository is currently at the minimal project setup stage: a .NET host skeleton and a trimmed Sonarr-style frontend shell.

## Prerequisites

- .NET SDK 10.0.300 or newer
- Node.js and Yarn

## Build

```sh
npm run build
```

This builds the Sonarr-style frontend into `_output/UI`.

Build the backend with:

```sh
dotnet build src/Nodearr.sln
```

## Run

```sh
dotnet run --project src/Nodearr.Host/Nodearr.Host.csproj
```

When running locally, the minimal health and API endpoints are available at:

```text
GET /health
GET /ping
GET /api/v1/system/status
```

The home page is served from `_output/UI/index.html` and calls `/api/v1/system/status`.
