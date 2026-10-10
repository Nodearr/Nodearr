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
                AllowedQualities = string.Join(", ", Items.SelectMany(i => i.AllowedNames()))
            };
        }
    }

    public class ArrQualityProfileItemResource
    {
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
        public string Title { get; set; }
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
                Title = Title,
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
}
