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
