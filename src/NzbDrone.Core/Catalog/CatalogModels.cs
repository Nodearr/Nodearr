using System;
using System.Collections.Generic;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Catalog
{
    // The local catalog is a copy of what each configured Arr app says it has. Rows are keyed by
    // the application and the Arr app's own IDs, so relationships survive a refresh unchanged.
    public abstract class CatalogModel : ModelBase
    {
        public int ApplicationId { get; set; }
        public int ArrId { get; set; }

        // Set when a refresh no longer finds the item in the Arr app. Cleared if it comes back.
        public DateTime? MissingSince { get; set; }
    }

    public class CatalogQualityProfile : CatalogModel
    {
        public string Name { get; set; }
        public bool UpgradeAllowed { get; set; }
        public int CutoffQualityId { get; set; }
        public string AllowedQualities { get; set; }

        // The profile's order of qualities, as QualityRanking stores it. Empty until the profile is next read.
        public string QualityRanking { get; set; }
    }

    public class CatalogSeries : CatalogModel
    {
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
    }

    public class CatalogEpisode : CatalogModel
    {
        public int ArrSeriesId { get; set; }
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
        public int? AbsoluteEpisodeNumber { get; set; }

        // Set only where the scene numbers the episode differently from TVDB.
        public int? SceneSeasonNumber { get; set; }
        public int? SceneEpisodeNumber { get; set; }
        public string Title { get; set; }

        // The day it aired where it aired, as yyyy-MM-dd.
        public string AirDate { get; set; }
        public DateTime? AirDateUtc { get; set; }
        public bool Monitored { get; set; }

        // Zero when the episode has no file. Several episodes share one ID for a multi-episode file.
        public int ArrEpisodeFileId { get; set; }
    }

    public class CatalogMovie : CatalogModel
    {
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string Title { get; set; }
        public string SortTitle { get; set; }
        public int Year { get; set; }
        public string Path { get; set; }
        public int QualityProfileId { get; set; }
        public bool Monitored { get; set; }
        public int ArrMovieFileId { get; set; }

        // Whisparr's, which Radarr's movies leave empty. ItemType says the item is adult content
        // at all: "movie" for a film or "scene" for a scene. Whisparr searches for a scene by its
        // studio, release date, title and code, and knows it by IDs from StashDB and ThePornDB.
        public string ItemType { get; set; }
        public string Studio { get; set; }
        public string ReleaseDate { get; set; }
        public string Code { get; set; }
        public string ForeignId { get; set; }
        public string StashId { get; set; }
        public string TpdbId { get; set; }

        public bool IsAdult => ItemType.IsNotNullOrWhiteSpace();
    }

    public abstract class CatalogFile : CatalogModel
    {
        // The path as the Arr app sees it. Nodearr's own view of it goes through the path mappings.
        public string Path { get; set; }
        public string RelativePath { get; set; }
        public long Size { get; set; }
        public DateTime? DateAdded { get; set; }
        public string SceneName { get; set; }
        public string ReleaseGroup { get; set; }
        public string Quality { get; set; }
        public string QualitySource { get; set; }
        public int Resolution { get; set; }
        public string Languages { get; set; }
        public string CustomFormats { get; set; }
        public int CustomFormatScore { get; set; }
        public string VideoCodec { get; set; }
        public string VideoDynamicRange { get; set; }
        public string AudioCodec { get; set; }
        public double AudioChannels { get; set; }
        public string RunTime { get; set; }
    }

    public class CatalogEpisodeFile : CatalogFile
    {
        public int ArrSeriesId { get; set; }
        public int SeasonNumber { get; set; }
    }

    public class CatalogMovieFile : CatalogFile
    {
        public int ArrMovieId { get; set; }
        public string Edition { get; set; }
    }

    // Everything one Arr app reported in one refresh.
    public class ApplicationCatalog
    {
        public List<CatalogQualityProfile> QualityProfiles { get; } = new();
        public List<CatalogSeries> Series { get; } = new();
        public List<CatalogEpisode> Episodes { get; } = new();
        public List<CatalogEpisodeFile> EpisodeFiles { get; } = new();
        public List<CatalogMovie> Movies { get; } = new();
        public List<CatalogMovieFile> MovieFiles { get; } = new();
    }

    // Raised after an application's catalog was refreshed successfully.
    public class CatalogRefreshedEvent : IEvent
    {
        public CatalogRefreshedEvent(int applicationId)
        {
            ApplicationId = applicationId;
        }

        public int ApplicationId { get; }
    }

    public class CatalogSyncStatus : ModelBase
    {
        public int ApplicationId { get; set; }
        public DateTime? LastSyncStarted { get; set; }
        public DateTime? LastSyncCompleted { get; set; }
        public string LastSyncError { get; set; }
    }
}
