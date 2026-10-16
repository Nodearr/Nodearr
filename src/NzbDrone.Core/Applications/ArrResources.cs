using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Catalog;

namespace NzbDrone.Core.Applications
{
    // The parts of Sonarr's and Radarr's v3 API resources that the catalog keeps.
    public class ArrQualityProfileResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public bool UpgradeAllowed { get; set; }
        public int Cutoff { get; set; }
        public List<ArrQualityProfileItemResource> Items { get; set; } = new();

        public CatalogQualityProfile ToCatalog()
        {
            return new CatalogQualityProfile
            {
                ArrId = Id,
                Name = Name,
                UpgradeAllowed = UpgradeAllowed,
                CutoffQualityId = Cutoff,
                AllowedQualities = string.Join(", ", Items.SelectMany(i => i.AllowedNames())),
                QualityRanking = ToRanking().ToStored()
            };
        }

        // The Arr apps list a profile's items from the least wanted to the most. An item is one
        // quality or a group of them, and the cutoff names either a quality or a group.
        public QualityRanking ToRanking()
        {
            var ranking = new QualityRanking { UpgradeAllowed = UpgradeAllowed };

            for (var rank = 0; rank < Items.Count; rank++)
            {
                var item = Items[rank];
                var members = item.Quality != null ? new List<ArrQualityResource> { item.Quality } : item.Items.Select(i => i.Quality).Where(q => q != null).ToList();

                foreach (var quality in members)
                {
                    ranking.Qualities.Add(new RankedQuality { Name = quality.Name, Rank = rank, Allowed = item.Allowed });

                    if (quality.Id == Cutoff)
                    {
                        ranking.CutoffRank = rank;
                    }
                }

                if (item.Quality == null && item.Id == Cutoff)
                {
                    ranking.CutoffRank = rank;
                }
            }

            return ranking;
        }
    }

    public class ArrQualityProfileItemResource
    {
        // A group's own ID. A plain quality has none, and its quality's ID is what counts.
        public int Id { get; set; }
        public string Name { get; set; }
        public bool Allowed { get; set; }
        public ArrQualityResource Quality { get; set; }
        public List<ArrQualityProfileItemResource> Items { get; set; } = new();

        public IEnumerable<string> AllowedNames()
        {
            if (!Allowed)
            {
                return Enumerable.Empty<string>();
            }

            // A group lists its member qualities. A plain item is one quality.
            return Quality != null ? new[] { Quality.Name } : Items.Select(i => i.Quality?.Name).Where(n => n != null);
        }
    }

    public class ArrQualityResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public int Resolution { get; set; }
    }

    public class ArrQualityModelResource
    {
        public ArrQualityResource Quality { get; set; }
    }

    public class ArrNamedResource
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class ArrMediaInfoResource
    {
        public string VideoCodec { get; set; }
        public string VideoDynamicRangeType { get; set; }
        public string AudioCodec { get; set; }
        public double AudioChannels { get; set; }
        public string RunTime { get; set; }
    }

    public abstract class ArrFileResource
    {
        public int Id { get; set; }
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public long Size { get; set; }
        public DateTime? DateAdded { get; set; }
        public string SceneName { get; set; }
        public string ReleaseGroup { get; set; }
        public ArrQualityModelResource Quality { get; set; }
        public List<ArrNamedResource> Languages { get; set; } = new();
        public List<ArrNamedResource> CustomFormats { get; set; } = new();
        public int CustomFormatScore { get; set; }
        public ArrMediaInfoResource MediaInfo { get; set; }

        protected void Fill(CatalogFile file)
        {
            file.ArrId = Id;
            file.Path = Path;
            file.RelativePath = RelativePath;
            file.Size = Size;
            file.DateAdded = DateAdded?.ToUniversalTime();
            file.SceneName = SceneName;
            file.ReleaseGroup = ReleaseGroup;
            file.Quality = Quality?.Quality?.Name;
            file.QualitySource = Quality?.Quality?.Source;
            file.Resolution = Quality?.Quality?.Resolution ?? 0;
            file.Languages = string.Join(", ", (Languages ?? new()).Select(l => l.Name));
            file.CustomFormats = string.Join(", ", (CustomFormats ?? new()).Select(c => c.Name));
            file.CustomFormatScore = CustomFormatScore;
            file.VideoCodec = MediaInfo?.VideoCodec;
            file.VideoDynamicRange = MediaInfo?.VideoDynamicRangeType;
            file.AudioCodec = MediaInfo?.AudioCodec;
            file.AudioChannels = MediaInfo?.AudioChannels ?? 0;
            file.RunTime = MediaInfo?.RunTime;
        }
    }

    public class SonarrSeriesResource
    {
        public int Id { get; set; }
        public int TvdbId { get; set; }
        public int TvMazeId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string Title { get; set; }
        public string SortTitle { get; set; }
        public int Year { get; set; }
        public string SeriesType { get; set; }
        public string Path { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }
        public SonarrSeriesStatisticsResource Statistics { get; set; }

        public CatalogSeries ToCatalog()
        {
            return new CatalogSeries
            {
                ArrId = Id,
                TvdbId = TvdbId,
                TvMazeId = TvMazeId,
                TmdbId = TmdbId,
                ImdbId = ImdbId,
                Title = Title,
                SortTitle = SortTitle,
                Year = Year,
                SeriesType = SeriesType,
                Path = Path,
                QualityProfileId = QualityProfileId,
                Monitored = Monitored
            };
        }
    }

    public class SonarrSeriesStatisticsResource
    {
        public int EpisodeFileCount { get; set; }
    }

    public class SonarrEpisodeResource
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public int? AbsoluteEpisodeNumber { get; set; }

        // From XEM, for a series the scene numbers differently. Sonarr searches by these.
        public int? SceneSeasonNumber { get; set; }
        public int? SceneEpisodeNumber { get; set; }
        public string Title { get; set; }

        // The day it aired where it aired, as yyyy-MM-dd. Sonarr searches for daily shows by this,
        // and it is not always the day in AirDateUtc.
        public string AirDate { get; set; }
        public DateTime? AirDateUtc { get; set; }
        public bool Monitored { get; set; }
        public int EpisodeFileId { get; set; }

        public CatalogEpisode ToCatalog()
        {
            return new CatalogEpisode
            {
                ArrId = Id,
                ArrSeriesId = SeriesId,
                SeasonNumber = SeasonNumber,
                EpisodeNumber = EpisodeNumber,
                AbsoluteEpisodeNumber = AbsoluteEpisodeNumber,
                SceneSeasonNumber = SceneSeasonNumber,
                SceneEpisodeNumber = SceneEpisodeNumber,
                Title = Title,
                AirDate = AirDate,
                AirDateUtc = AirDateUtc?.ToUniversalTime(),
                Monitored = Monitored,
                ArrEpisodeFileId = EpisodeFileId
            };
        }
    }

    public class SonarrEpisodeFileResource : ArrFileResource
    {
        public int SeriesId { get; set; }
        public int SeasonNumber { get; set; }

        public CatalogEpisodeFile ToCatalog()
        {
            var file = new CatalogEpisodeFile { ArrSeriesId = SeriesId, SeasonNumber = SeasonNumber };
            Fill(file);

            return file;
        }
    }

    public class RadarrMovieResource
    {
        public int Id { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string Title { get; set; }
        public string SortTitle { get; set; }
        public int Year { get; set; }
        public string Path { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }
        public RadarrMovieFileResource MovieFile { get; set; }

        public CatalogMovie ToCatalog()
        {
            return new CatalogMovie
            {
                ArrId = Id,
                TmdbId = TmdbId,
                ImdbId = ImdbId,
                Title = Title,
                SortTitle = SortTitle,
                Year = Year,
                Path = Path,
                QualityProfileId = QualityProfileId,
                Monitored = Monitored,
                ArrMovieFileId = MovieFile?.Id ?? 0
            };
        }
    }

    // Whisparr v3 (Eros) is built on Radarr: a film or a scene is a movie, with one file. What it
    // adds is what Whisparr searches for a scene by, and the IDs it knows it by.
    public class WhisparrMovieResource : RadarrMovieResource
    {
        public string ItemType { get; set; }
        public string StudioTitle { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public string Code { get; set; }
        public string ForeignId { get; set; }
        public string StashId { get; set; }
        public string TpdbId { get; set; }

        public new CatalogMovie ToCatalog()
        {
            var movie = base.ToCatalog();

            // Whatever Whisparr says, this is adult content. Only a scene or a film is expected, by
            // name or by its number in Whisparr's ItemType, where Scene is 1.
            movie.ItemType = string.Equals(ItemType, "scene", StringComparison.OrdinalIgnoreCase) || ItemType == "1" ? "scene" : "movie";
            movie.Studio = StudioTitle;
            movie.ReleaseDate = ReleaseDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            movie.Code = Code;
            movie.ForeignId = ForeignId;
            movie.StashId = StashId;
            movie.TpdbId = TpdbId;

            return movie;
        }
    }

    // Whisparr v2 is built on Sonarr: a site is a series, and a scene is an episode with a release
    // date and no episode number. Nodearr keeps each scene as Whisparr v3 keeps one, a movie marked
    // as a scene with the site as its studio, so both versions are searched and compared alike.
    public class WhisparrV2SiteResource
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Path { get; set; }
        public int QualityProfileId { get; set; }
        public int TvdbId { get; set; }
    }

    public class WhisparrV2SceneResource
    {
        public int Id { get; set; }
        public int SeriesId { get; set; }

        // The scene's ID in Whisparr v2's metadata, in the field Sonarr keeps TVDB's in.
        public int TvdbId { get; set; }
        public int EpisodeFileId { get; set; }
        public string Title { get; set; }

        // As yyyy-MM-dd.
        public string ReleaseDate { get; set; }
        public bool Monitored { get; set; }

        // The file, in the same shape as Radarr's, which Whisparr v2 sends when asked for it.
        public RadarrMovieFileResource EpisodeFile { get; set; }

        public CatalogMovie ToCatalog(WhisparrV2SiteResource site)
        {
            var released = DateTime.TryParse(ReleaseDate, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal, out var day) ? day : (DateTime?)null;

            return new CatalogMovie
            {
                ArrId = Id,
                Title = Title,
                SortTitle = Title?.ToLowerInvariant(),
                Year = released?.Year ?? 0,
                Path = site.Path,
                QualityProfileId = site.QualityProfileId,
                Monitored = Monitored,
                ArrMovieFileId = EpisodeFile?.Id ?? EpisodeFileId,
                ItemType = "scene",
                Studio = site.Title,
                ReleaseDate = released?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),

                // Only another Whisparr v2 knows a scene by this number.
                ForeignId = TvdbId > 0 ? $"whisparr2:{TvdbId}" : null
            };
        }
    }

    public class RadarrMovieFileResource : ArrFileResource
    {
        public int MovieId { get; set; }
        public string Edition { get; set; }

        public CatalogMovieFile ToCatalog()
        {
            var file = new CatalogMovieFile { ArrMovieId = MovieId, Edition = Edition };
            Fill(file);

            return file;
        }
    }

    // Lidarr's v1 API. An artist holds albums, and an album's tracks are files of their own.
    public class LidarrArtistResource
    {
        public int Id { get; set; }
        public string ForeignArtistId { get; set; }
        public string ArtistName { get; set; }
        public string SortName { get; set; }
        public string Path { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }

        public CatalogArtist ToCatalog()
        {
            return new CatalogArtist
            {
                ArrId = Id,
                ForeignArtistId = ForeignArtistId,
                Name = ArtistName,
                SortName = SortName,
                Path = Path,
                QualityProfileId = QualityProfileId,
                Monitored = Monitored
            };
        }
    }

    public class LidarrAlbumResource
    {
        public int Id { get; set; }
        public int ArtistId { get; set; }

        // The MusicBrainz release group, which every edition of the album shares.
        public string ForeignAlbumId { get; set; }
        public string Title { get; set; }
        public string AlbumType { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public bool Monitored { get; set; }
        public LidarrAlbumStatisticsResource Statistics { get; set; }

        public CatalogAlbum ToCatalog()
        {
            var released = ReleaseDate?.ToUniversalTime();

            return new CatalogAlbum
            {
                ArrId = Id,
                ArrArtistId = ArtistId,
                ForeignAlbumId = ForeignAlbumId,
                Title = Title,
                AlbumType = AlbumType,
                ReleaseDate = released?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                Year = released?.Year ?? 0,
                Monitored = Monitored,
                TrackCount = Statistics?.TrackCount ?? 0,
                TrackFileCount = Statistics?.TrackFileCount ?? 0
            };
        }
    }

    public class LidarrAlbumStatisticsResource
    {
        public int TrackCount { get; set; }
        public int TrackFileCount { get; set; }
    }

    // One track of an album's release, and the file Lidarr holds for it, if any.
    public class LidarrTrackResource
    {
        public int TrackFileId { get; set; }
        public int AbsoluteTrackNumber { get; set; }
        public int MediumNumber { get; set; }
        public string Title { get; set; }
    }

    public class LidarrTrackFileResource : ArrFileResource
    {
        public int ArtistId { get; set; }
        public int AlbumId { get; set; }

        // Lidarr sends only the full path. The part below the artist's folder stands in for the
        // relative path Sonarr and Radarr send.
        public CatalogTrackFile ToCatalog(string artistPath)
        {
            var file = new CatalogTrackFile { ArrArtistId = ArtistId, ArrAlbumId = AlbumId };
            Fill(file);

            if (file.RelativePath == null && file.Path != null && artistPath != null)
            {
                var root = artistPath.TrimEnd('/', '\\');

                if (file.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase) && file.Path.Length > root.Length + 1)
                {
                    file.RelativePath = file.Path.Substring(root.Length + 1);
                }
            }

            return file;
        }
    }
}
