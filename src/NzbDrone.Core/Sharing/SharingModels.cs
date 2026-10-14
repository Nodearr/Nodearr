using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Sharing
{
    public enum MediaKind
    {
        Tv = 0,
        Movie = 1
    }

    // A series or a movie in the local catalog, named the way the catalog keys it.
    public readonly record struct TitleKey(MediaKind Kind, int ApplicationId, int ArrId);

    // What a share list can hold, at the levels Sonarr monitors: a whole series, one season of
    // it, or one episode. A whole series or season takes in episodes that arrive later. A movie
    // is always whole. ArrId is the series or the movie.
    public readonly record struct ShareKey(MediaKind Kind, int ApplicationId, int ArrId, int? SeasonNumber = null, int? EpisodeArrId = null)
    {
        public static ShareKey Series(int applicationId, int seriesArrId) => new(MediaKind.Tv, applicationId, seriesArrId);
        public static ShareKey Season(int applicationId, int seriesArrId, int seasonNumber) => new(MediaKind.Tv, applicationId, seriesArrId, seasonNumber);
        public static ShareKey Episode(int applicationId, int seriesArrId, int episodeArrId) => new(MediaKind.Tv, applicationId, seriesArrId, null, episodeArrId);
        public static ShareKey Movie(int applicationId, int movieArrId) => new(MediaKind.Movie, applicationId, movieArrId);

        public bool IsWhole => SeasonNumber == null && EpisodeArrId == null;
        public TitleKey Title => new(Kind, ApplicationId, ArrId);

        public static implicit operator ShareKey(TitleKey title) => new(title.Kind, title.ApplicationId, title.ArrId);
    }

    // A named set of series and movies offered to chosen peers. The one list with IsHidden set is
    // different: what is on it is offered to nobody, whatever else says otherwise.
    public class ShareList : ModelBase
    {
        public string Name { get; set; }
        public bool IsHidden { get; set; }
    }

    public class ShareListItem : ModelBase
    {
        public int ShareListId { get; set; }
        public MediaKind MediaKind { get; set; }
        public int ApplicationId { get; set; }
        public int ArrId { get; set; }

        // Both null for a whole series or a movie.
        public int? SeasonNumber { get; set; }
        public int? EpisodeArrId { get; set; }

        public ShareKey Key => new(MediaKind, ApplicationId, ArrId, SeasonNumber, EpisodeArrId);
    }

    public class ShareListPeer : ModelBase
    {
        public int ShareListId { get; set; }
        public int PeerId { get; set; }
    }

    // A share list with its contents, as the API and the UI work with it.
    public class ShareListDetails
    {
        public ShareList List { get; set; }
        public List<ShareKey> Items { get; set; } = new();
        public List<int> PeerIds { get; set; } = new();
    }

    // The seasons and episodes of one series, for choosing among them.
    public class ShareableSeason
    {
        public int SeasonNumber { get; set; }
        public List<ShareableEpisode> Episodes { get; set; } = new();
    }

    public class ShareableEpisode
    {
        public int EpisodeArrId { get; set; }
        public int EpisodeNumber { get; set; }
        public string Title { get; set; }
        public bool HasFile { get; set; }
    }

    // One series or movie that can be put on a share list.
    public class ShareableTitle
    {
        public TitleKey Key { get; set; }
        public string ApplicationName { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public int Files { get; set; }
    }

    // One file as a peer is told about it. It says what the file is and how good it is, and
    // nothing about where it lives.
    public class OfferItem
    {
        public string Id { get; set; }
        public MediaKind Kind { get; set; }
        public int TvdbId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public int SeasonNumber { get; set; }
        public List<int> EpisodeNumbers { get; set; } = new();
        public List<string> EpisodeTitles { get; set; } = new();

        // The day each episode aired, as yyyy-MM-dd, where the Arr app knows it. Sonarr asks for
        // daily shows by day and not by number.
        public List<string> AirDates { get; set; } = new();

        // For a series the scene numbers differently from TVDB: the season and episodes as the
        // scene has them, which is how Sonarr searches for them and reads them in a title. Left
        // out for every other file, so an offer without them is the same as it ever was.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? SceneSeasonNumber { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<int> SceneEpisodeNumbers { get; set; }

        // The name the release had when the Arr app grabbed it, when the Arr app kept one.
        public string SceneName { get; set; }
        public string Edition { get; set; }

        // Such as .mkv. A transfer has to write a file the Arr app will recognise.
        public string Extension { get; set; }
        public string Quality { get; set; }
        public string QualitySource { get; set; }
        public int Resolution { get; set; }
        public long Size { get; set; }
        public string Languages { get; set; }
        public string CustomFormats { get; set; }
        public string ReleaseGroup { get; set; }
        public string VideoCodec { get; set; }
        public string VideoDynamicRange { get; set; }
        public string AudioCodec { get; set; }
        public double AudioChannels { get; set; }
        public DateTime? DateAdded { get; set; }
    }

    // Everything one peer is offered at one moment. The version changes when the contents do.
    public class Offer
    {
        public string Version { get; set; }
        public List<OfferItem> Items { get; set; } = new();
    }
}
