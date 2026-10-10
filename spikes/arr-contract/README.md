# S1 spike: the Arr contract

Proves that Sonarr, Radarr and Prowlarr accept Nodearr as a Newznab indexer plus a SABnzbd-compatible download client, all the way to a real import. The result and the findings are in [ADR 0010](../../docs/adr/0010-arr-contract-newznab-indexer-and-sabnzbd-download-client.md).

**This harness runs only on the `v1/s1-s2-spikes` branch.** It needs both halves of the stub, and S6 replaced the indexer half with the real indexer. The same ground is covered on later branches by the scripts in `acceptance/`.

## What is here

- `docker-compose.yml`: Sonarr, Radarr and Prowlarr, pinned by version and digest, sharing one `/downloads` folder.
- `Run-ArrContractSpike.ps1`: starts everything fresh, configures the apps through their APIs, runs nine scenarios and checks them.
- The stub endpoints themselves are in `src/Nodearr.Http/ArrContractSpike/`. They are spike code: candidates come from a JSON file and a "transfer" is a slow local file copy.

## Run it

Needs PowerShell 7, Docker, ffmpeg and a built Nodearr.

```sh
dotnet build src/Nodearr.sln
```

```sh
pwsh spikes/arr-contract/Run-ArrContractSpike.ps1
```

It takes about three minutes and exits non-zero if any scenario fails. Nodearr runs on the host on port 8789. The containers publish on 127.0.0.1 only, on ports 18989, 17878 and 19696, and reach Nodearr through `host.docker.internal`.

Add `-KeepRunning` to leave Nodearr and the containers up for inspection. To remove everything, including the generated API keys and media:

```sh
pwsh spikes/arr-contract/Run-ArrContractSpike.ps1 -Down
```

## What it writes

Everything goes in `.run/`, which is ignored by git:

- `.env`: random API keys for this run's containers and Nodearr.
- `peer-library/`: two synthetic ffmpeg test-pattern videos, about 26 MB in total.
- `results.json`: each scenario, its evidence, and the requests Nodearr received during it.
- `transcript.jsonl`: every request the Arr apps made to Nodearr, without the API key.

## Notes

- The series and movies are real catalog entries so the apps can look them up. The files are test patterns.
- Sonarr and Radarr need internet access to fetch series and movie metadata when the items are added.
- Each run pulls about 50 MB of media into Docker volumes through the imports. The volumes are removed at the end unless `-KeepRunning` is set.
