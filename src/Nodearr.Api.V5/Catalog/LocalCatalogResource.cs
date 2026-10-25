using NzbDrone.Core.Applications;
using NzbDrone.Core.Catalog;

namespace Nodearr.Api.V5.Catalog;

public class LocalCatalogSummaryResource
{
    public int ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string? Implementation { get; set; }
    public int Series { get; set; }
    public int Episodes { get; set; }
    public int EpisodeFiles { get; set; }
    public int Movies { get; set; }
    public int MovieFiles { get; set; }
    public int Artists { get; set; }
    public int Albums { get; set; }
    public int TrackFiles { get; set; }
    public int MissingFiles { get; set; }
    public DateTime? LastSyncStarted { get; set; }
    public DateTime? LastSyncCompleted { get; set; }
    public string? LastSyncError { get; set; }

    public static LocalCatalogSummaryResource FromModel(CatalogApplicationSummary summary)
    {
        return new LocalCatalogSummaryResource
        {
            ApplicationId = summary.ApplicationId,
            ApplicationName = summary.ApplicationName,
            Implementation = summary.Implementation,
            Series = summary.Series,
            Episodes = summary.Episodes,
            EpisodeFiles = summary.EpisodeFiles,
            Movies = summary.Movies,
            MovieFiles = summary.MovieFiles,
            Artists = summary.Artists,
            Albums = summary.Albums,
            TrackFiles = summary.TrackFiles,
            MissingFiles = summary.MissingFiles,
            LastSyncStarted = summary.LastSyncStarted,
            LastSyncCompleted = summary.LastSyncCompleted,
            LastSyncError = summary.LastSyncError
        };
    }
}

// One file in the local catalog, with the media it belongs to.
public class LocalCatalogFileResource
{
    public int Id { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public int ApplicationId { get; set; }
    public string? ApplicationName { get; set; }

    public string? Title { get; set; }
    public int Year { get; set; }

    // The title its poster belongs to: GET catalog/posters/local/{applicationId}/{posterSubject}/{posterArrId}.
    public PosterSubject PosterSubject { get; set; }
    public int PosterArrId { get; set; }

    public int TvdbId { get; set; }
    public int TmdbId { get; set; }
    public string? ImdbId { get; set; }

    // A multi-episode file lists every episode it contains.
    public int? SeasonNumber { get; set; }
    public List<int> EpisodeNumbers { get; set; } = new();
    public List<string> EpisodeTitles { get; set; } = new();

    public string? Path { get; set; }
    public string? RelativePath { get; set; }

    // The same file as Nodearr reaches it, after path mappings, and whether Nodearr can open it.
    // Readable is true only when Access is.
    public string? LocalPath { get; set; }
    public CatalogFileAccess Access { get; set; }
    public bool Readable { get; set; }

    public long Size { get; set; }
    public string? Quality { get; set; }

    // Proper, Repack or Real, as the Arr app recorded it. Null for a first release.
    public string? Revision { get; set; }
    public int Resolution { get; set; }
    public string? Languages { get; set; }
    public string? CustomFormats { get; set; }
    public string? ReleaseGroup { get; set; }
    public string? SceneName { get; set; }
    public string? VideoCodec { get; set; }
    public string? AudioCodec { get; set; }
    public DateTime? DateAdded { get; set; }
    public DateTime? MissingSince { get; set; }

    public static LocalCatalogFileResource FromFile(CatalogFile file, string mediaType)
    {
        return new LocalCatalogFileResource
        {
            Id = file.Id,
            MediaType = mediaType,
            ApplicationId = file.ApplicationId,
            Path = file.Path,
            RelativePath = file.RelativePath,
            Size = file.Size,
            Quality = file.Quality,
            Revision = RevisionLabel.For(file.RevisionVersion, file.RevisionReal, file.RevisionIsRepack),
            Resolution = file.Resolution,
            Languages = file.Languages,
            CustomFormats = file.CustomFormats,
            ReleaseGroup = file.ReleaseGroup,
            SceneName = file.SceneName,
            VideoCodec = file.VideoCodec,
            AudioCodec = file.AudioCodec,
            DateAdded = file.DateAdded,
            MissingSince = file.MissingSince
        };
    }
}

// A series or movie in the local catalog, for the Posters view.
public class LocalCatalogTitleResource
{
    public int ApplicationId { get; set; }
    public string? ApplicationName { get; set; }
    public string MediaType { get; set; } = string.Empty;
    public PosterSubject PosterSubject { get; set; }
    public int PosterArrId { get; set; }
    public string? Title { get; set; }
    public int Year { get; set; }
    public int Files { get; set; }
}
