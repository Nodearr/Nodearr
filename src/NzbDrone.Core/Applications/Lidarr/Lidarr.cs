using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;

namespace NzbDrone.Core.Applications.Lidarr
{
    // Lidarr's library is artists, their albums, and the albums' tracks, each track a file. It
    // grabs and imports a whole album at once (ADR 0020).
    public class Lidarr : ApplicationBase<LidarrSettings>, ILibraryAdder, IPosterSource
    {
        public Lidarr(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger, IApplicationsRepository applicationsRepository)
            : base(proxy, configFileProvider, logger, applicationsRepository)
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

            AddQualityProfiles(catalog);

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
            var allTracks = _proxy.Get<List<LidarrTrackResource>>(Settings, $"/track?artistId={artist.Id}");
            foreach (var album in catalog.Albums.Where(a => a.ArrArtistId == artist.Id))
            {
                var selectedTracks = allTracks.Where(t => t.AlbumId == album.ArrId).ToList();
                if (selectedTracks.Count > 0)
                {
                    album.TrackCount = selectedTracks.Count;
                }

                album.Tracks = JsonSerializer.Serialize(selectedTracks
                    .OrderBy(t => t.MediumNumber).ThenBy(t => t.AbsoluteTrackNumber)
                    .Select(t => new CatalogAlbumTrack
                    {
                        Title = t.Title,
                        ForeignRecordingId = t.ForeignRecordingId,
                        Number = int.TryParse(t.TrackNumber, out var number) ? number : t.AbsoluteTrackNumber,
                        Disc = t.MediumNumber,
                        Duration = t.Duration,
                        TrackFileId = t.TrackFileId
                    }).ToList());
            }

            var tracks = allTracks
                .Where(t => t.TrackFileId > 0)
                .GroupBy(t => t.TrackFileId)
                .ToDictionary(g => g.Key, g => g.OrderBy(t => t.MediumNumber).ThenBy(t => t.AbsoluteTrackNumber).First());

            foreach (var file in _proxy.Get<List<LidarrTrackFileResource>>(Settings, $"/trackfile?artistId={artist.Id}"))
            {
                var catalogFile = file.ToCatalog(artist.Path);

                if (tracks.TryGetValue(file.Id, out var track))
                {
                    catalogFile.TrackTitle = track.Title;
                    catalogFile.ForeignRecordingId = track.ForeignRecordingId;
                    catalogFile.TrackNumber = int.TryParse(track.TrackNumber, out var number) ? number : track.AbsoluteTrackNumber;
                    catalogFile.DiscNumber = track.MediumNumber;
                }

                catalog.TrackFiles.Add(catalogFile);
            }
        }

        public LibraryAddChoices GetAddChoices()
        {
            return LibraryAdd.Choices(_proxy, Settings, true);
        }

        // A whole artist is monitored with its new albums; otherwise only the albums sent. Lidarr
        // reads an artist's albums from its metadata server after adding it, so they are waited for.
        public string AddAndSearch(LibraryAddRequest request)
        {
            var artist = request.ExistingArrId.HasValue ? GetOrNull<JObject>($"/artist/{request.ExistingArrId}") : null;
            var added = false;

            if (artist == null)
            {
                var found = _proxy.GetArray(Settings, $"/artist/lookup?term=lidarr:{request.ForeignArtistId}").OfType<JObject>().FirstOrDefault()
                            ?? throw new LibraryAddException("Lidarr could not find the artist on MusicBrainz");

                if (found.Value<int?>("id") is > 0)
                {
                    artist = _proxy.Get<JObject>(Settings, $"/artist/{found.Value<int>("id")}");
                }
                else
                {
                    var choices = GetAddChoices();

                    LibraryAdd.CheckOptions(choices, request, Name);

                    if (choices.MetadataProfiles.All(p => p.Id != request.MetadataProfileId))
                    {
                        throw new LibraryAddException("Lidarr has no such metadata profile");
                    }

                    found["qualityProfileId"] = request.QualityProfileId;
                    found["metadataProfileId"] = request.MetadataProfileId;
                    found["rootFolderPath"] = request.RootFolderPath;
                    found["monitored"] = true;
                    found["monitorNewItems"] = request.Whole ? "all" : "none";
                    found["addOptions"] = new JObject { ["monitor"] = request.Whole ? "all" : "none", ["searchForMissingAlbums"] = false };

                    artist = _proxy.Post(Settings, "/artist", found);
                    added = true;
                }
            }

            var id = artist.Value<int>("id");

            if (!added)
            {
                artist["monitored"] = true;

                if (request.Whole)
                {
                    artist["monitorNewItems"] = "all";
                }

                _proxy.Put(Settings, $"/artist/{id}", artist);
            }

            var wanted = request.Albums.Select(a => a.ToLowerInvariant()).ToHashSet();
            var albums = LibraryAdd.WaitFor(
                () => _proxy.GetArray(Settings, $"/album?artistId={id}").OfType<JObject>().ToList(),
                list => list.Count > 0 &&
                        (request.Whole || wanted.All(w => list.Any(a => ForeignId(a) == w))) &&
                        !(added && LibraryAdd.IsRunning(_proxy, Settings, "RefreshArtist")));

            // Lidarr leaves an artist added without monitoring its albums unmonitored itself, and
            // then its monitored albums are not searched for or upgraded on their own.
            if (added)
            {
                var now = _proxy.Get<JObject>(Settings, $"/artist/{id}");

                if (!now.Value<bool>("monitored"))
                {
                    now["monitored"] = true;
                    _proxy.Put(Settings, $"/artist/{id}", now);
                }
            }

            var albumIds = albums
                .Where(a => request.Whole || wanted.Contains(ForeignId(a)))
                .Select(a => a.Value<int>("id"))
                .ToList();

            if (albumIds.Count > 0)
            {
                _proxy.PutMany(Settings, "/album/monitor", new JObject { ["albumIds"] = new JArray(albumIds), ["monitored"] = true });
            }

            string what;

            if (request.Whole)
            {
                // The albums the friend offers, rather than every album of the artist, so that one
                // send is not a search of every indexer for each album.
                var offered = request.OfferedAlbums.Select(a => a.ToLowerInvariant()).ToHashSet();
                var search = albums
                    .Where(a => offered.Contains(ForeignId(a)) && (!IsComplete(a) || request.UpgradeAlbums.Contains(ForeignId(a), StringComparer.OrdinalIgnoreCase)))
                    .Select(a => a.Value<int>("id"))
                    .ToList();

                if (offered.Count == 0)
                {
                    _proxy.Post(Settings, "/command", new JObject { ["name"] = "ArtistSearch", ["artistId"] = id });
                }
                else if (search.Count > 0)
                {
                    _proxy.Post(Settings, "/command", new JObject { ["name"] = "AlbumSearch", ["albumIds"] = new JArray(search) });
                }

                what = "the whole artist";
            }
            else
            {
                if (albumIds.Count > 0)
                {
                    _proxy.Post(Settings, "/command", new JObject { ["name"] = "AlbumSearch", ["albumIds"] = new JArray(albumIds) });
                }

                what = albumIds.Count == 1
                    ? albums.First(a => a.Value<int>("id") == albumIds[0]).Value<string>("title")
                    : $"{albumIds.Count} albums";
            }

            // An album Lidarr does not list is one its metadata profile leaves out, such as a single.
            var missing = request.Whole ? 0 : wanted.Count - albumIds.Count;
            var note = missing > 0 ? $". Lidarr does not list {missing} of the albums sent: its metadata profile may leave that kind of release out" : string.Empty;

            return $"{(added ? "Added to" : "Already in")} Lidarr, monitoring {what}, and searching{note}";
        }

        // Lidarr has every track of it. An album it says nothing about is not.
        private static bool IsComplete(JObject album)
        {
            var statistics = album["statistics"];
            var tracks = statistics?.Value<int?>("trackCount") ?? 0;

            return tracks > 0 && (statistics?.Value<int?>("trackFileCount") ?? 0) >= tracks;
        }

        private static string ForeignId(JObject album)
        {
            return album.Value<string>("foreignAlbumId")?.ToLowerInvariant() ?? string.Empty;
        }

        // An album's own cover, or its artist's picture where Lidarr has no cover for it.
        public byte[] GetPoster(PosterSubject subject, int arrId)
        {
            if (subject == PosterSubject.Artist)
            {
                return PosterImages.FromMediaCover(_proxy, Settings, $"artist/{arrId}/poster-500.jpg");
            }

            if (subject != PosterSubject.Album)
            {
                return null;
            }

            var cover = PosterImages.FromMediaCover(_proxy, Settings, $"album/{arrId}/cover-500.jpg");

            if (cover != null)
            {
                return cover;
            }

            var album = GetOrNull<JObject>($"/album/{arrId}");

            return album == null ? null : PosterImages.FromMediaCover(_proxy, Settings, $"artist/{album.Value<int>("artistId")}/poster-500.jpg");
        }
    }
}
