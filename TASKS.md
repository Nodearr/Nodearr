# Nodearr tasks

The milestones Nodearr started from, all done. What is open now is in the V1 plan, under [What is still open](docs/plans/v1-implementation-plan.md#what-is-still-open).

## Milestones

Each was built in a slice of the [V1 plan](docs/plans/v1-implementation-plan.md).

- [x] **M1: Configure Arr app connections and import library metadata from Sonarr/Radarr.** S3. Whisparr and Lidarr since.
- [x] **M2: Normalize imported Arr data into Nodearr catalog entries.** S3.
- [x] **M3: Add node identity and local peer pairing.** S4.
- [x] **M4: Sync catalogs between two Nodearr instances.** S5, over the peer transport from the start rather than local HTTP.
- [x] **M5: Show local catalog, peer catalog, and missing/available comparisons in the UI.** S5.
- [x] **M6: Add profile-aware candidate matching.** S6, and upgrades by each Arr app's own quality profile since.
- [x] **M7: Add content hashing for exact byte identity and verification.** S7: BLAKE3, and background hashing since.
- [x] **M8: Add transfer job model.** S7.
- [x] **M9: Add first direct file transfer provider.** S7, on iroh.
- [x] **M10: Expose Nodearr as a Newznab-compatible indexer.** S6.
- [x] **M11: Expose Nodearr as an Arr-compatible download client.** S7, as SABnzbd.
- [x] **M12: Real direct P2P discovery and transfer, using iroh.** S2 and S4, and run between machines on different networks since (ADR 0011).
