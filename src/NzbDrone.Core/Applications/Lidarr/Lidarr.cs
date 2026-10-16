using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Lidarr
{
    // Lidarr's library is artists, their albums, and the albums' tracks, each track a file. It
    // grabs and imports a whole album at once (ADR 0020).
    public class Lidarr : ApplicationBase<LidarrSettings>
    {
        public Lidarr(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
            : base(proxy, configFileProvider, logger)
        {
        }

        public override string Name => "Lidarr";
        protected override Version MinimumVersion => new(2, 0, 0, 0);
        protected override string CategoryField => "musicCategory";

        protected override IReadOnlyDictionary<string, bool> WebhookEvents => new Dictionary<string, bool>
        {
            { "onGrab", false },
            { "onReleaseImport", true },
            { "onUpgrade", true },
            { "onRename", true },
            { "onArtistAdd", true },
            { "onArtistDelete", true },
            { "onAlbumDelete", true },
            { "onHealthIssue", false },
            { "onHealthRestored", false },
            { "onDownloadFailure", false },
            { "onImportFailure", false },
            { "onTrackRetag", false },
            { "onApplicationUpdate", false }
        };

        public override ApplicationCatalog FetchCatalog()
        {
            var catalog = new ApplicationCatalog();

            foreach (var profile in _proxy.Get<List<ArrQualityProfileResource>>(Settings, "/qualityprofile"))
            {
                catalog.QualityProfiles.Add(profile.ToCatalog());
            }

            var artists = _proxy.Get<List<LidarrArtistResource>>(Settings, "/artist");

            catalog.Artists.AddRange(artists.Select(a => a.ToCatalog()));
            catalog.Albums.AddRange(_proxy.Get<List<LidarrAlbumResource>>(Settings, "/album").Select(a => a.ToCatalog()));

            // Lidarr lists tracks and track files only for an artist or an album, so two requests per artist.
            foreach (var artist in artists)
            {
                AddTrackFiles(catalog, artist);
            }

            return catalog;
        }

        public override ApplicationCatalog FetchTitle(int arrId)
        {
            var catalog = new ApplicationCatalog();
            var artist = GetOrNull<LidarrArtistResource>($"/artist/{arrId}");

            if (artist == null)
            {
                return catalog;
            }

            catalog.Artists.Add(artist.ToCatalog());
            catalog.Albums.AddRange(_proxy.Get<List<LidarrAlbumResource>>(Settings, $"/album?artistId={arrId}").Select(a => a.ToCatalog()));
            AddTrackFiles(catalog, artist);

            return catalog;
        }

        // The artist's track files, each with the track it is. A file Lidarr holds for more than one
        // track is named after the first.
        private void AddTrackFiles(ApplicationCatalog catalog, LidarrArtistResource artist)
        {
            var tracks = _proxy.Get<List<LidarrTrackResource>>(Settings, $"/track?artistId={artist.Id}")
                .Where(t => t.TrackFileId > 0)
                .GroupBy(t => t.TrackFileId)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.MediumNumber).ThenBy(t => t.AbsoluteTrackNumber).First());

            foreach (var file in _proxy.Get<List<LidarrTrackFileResource>>(Settings, $"/trackfile?artistId={artist.Id}"))
            {
                var catalogFile = file.ToCatalog(artist.Path);

                if (tracks.TryGetValue(file.Id, out var track))
                {
                    catalogFile.TrackTitle = track.Title;
                    catalogFile.TrackNumber = track.AbsoluteTrackNumber;
                    catalogFile.DiscNumber = track.MediumNumber;
                }

                catalog.TrackFiles.Add(catalogFile);
            }
        }
    }
}
