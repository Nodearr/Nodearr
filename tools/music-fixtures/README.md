# CC0 music fixtures

The manifest pins artist, release-group and release MusicBrainz IDs, ZIP SHA256 checksums and song counts. Both albums are available through Lidarr's metadata service and have been resolved and imported by Lidarr 3.1.0.4875. The creator's linked pages explicitly dedicate the music to [CC0](https://creativecommons.org/publicdomain/zero/1.0/). Source downloads remain external; no audio is committed.

| Artist | Album | Songs | Creator's source and licence |
| --- | --- | --- | --- |
| Loyalty Freak Music | Lofi Ftw | 9 | [Source](https://loyaltyfreakmusic.com/music/lofi-ftw/) |
| Komiku | The adventure goes on, vol.1 | 13 | [Source](https://loyaltyfreakmusic.com/music/the-adventure-goes-on-vol-1/) |

Komiku's other albums were tried as a third fixture and turned down:

- **CHIEN SNOWBOARD 96' Demo Edition Official Soundtrack.** Lidarr reads a release's quality from the first bitrate-like number anywhere in its title. "CHIEN SNOWBOARD 96' … [MP3 320]" therefore parses as MP3-96, is refused as too large, and is never grabbed. That is Lidarr's parser, and it would happen with any indexer.
- **It's time for adventure.** The creator's CC0 download for that page is a different album, *It's time for adventure vol 2*. Its audio does not match the MusicBrainz release, so Lidarr will not import it automatically.
- **Fuzzy Trip in Remixland Vol.2.** The creator notes that its samples come from old records and may draw Content ID claims, so it is not clearly free of copyright.

`cover.svg` is original geometric artwork created for this repository, dedicated to CC0. Browser captures substitute it for these albums and artists; other unverified music covers get a placeholder. The downloaded archives' cover images are not used. Music rights do not automatically establish rights to every image returned by a metadata service.

See [the UI kit guide](../../docs/guides/ui-test.md#optional-lidarr-artistalbum-checks-68) for setup and checks. The setup verifies ZIP hashes before importing, tags audio with the pinned release's metadata, and uses Lidarr's manual import API to create fixture state. Product retrieval still uses complete album downloads through Nodearr's normal indexer and transfer pipeline.
