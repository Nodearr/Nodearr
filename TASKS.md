# Nodearr Tasks

## Current status

The repository is at the early foundation stage. Current work should stay focused on documentation, architecture, and narrow implementation milestones that build toward the Arr-compatible peer availability and transfer flow.

## Milestones

- [ ] **M1: Configure Arr app connections and import library metadata from Sonarr/Radarr.**
- [ ] **M2: Normalize imported Arr data into Nodearr catalog entries.**
- [ ] **M3: Add node identity and local peer pairing.**
- [ ] **M4: Sync catalogs between two Nodearr instances over local HTTP or another simple local transport.**
- [ ] **M5: Show local catalog, peer catalog, and missing/available comparisons in the UI.**
- [ ] **M6: Add profile-aware candidate matching.**
- [ ] **M7: Add content hashing for exact byte identity and verification.**
- [ ] **M8: Add transfer job model.**
- [ ] **M9: Add first direct file transfer provider.**
- [ ] **M10: Expose Nodearr as a Torznab/Newznab-compatible indexer.**
- [ ] **M11: Expose Nodearr as an Arr-compatible download client.**
- [ ] **M12: Replace local/LAN peer transport with real direct P2P discovery and transfer, likely using Iroh.**
