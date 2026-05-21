# Nodearr

Nodearr is a self-hosted trusted peer-to-peer media file discovery and transfer service for the Servarr ecosystem. This repository is currently at the minimal project setup stage: a .NET host skeleton with Node/npm commands for local development.

## Prerequisites

- .NET SDK 8.0 or newer
- Node.js and npm

## Build

```sh
npm run build
```

This delegates to:

```sh
dotnet build src/Nodearr.sln
```

## Run

```sh
dotnet run --project src/Nodearr.Host/Nodearr.Host.csproj
```

When running locally, the minimal health endpoint is available at:

```text
GET /health
```
