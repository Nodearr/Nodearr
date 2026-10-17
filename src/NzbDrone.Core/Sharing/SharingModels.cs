using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Sharing
{
    public enum MediaKind
    {
        Tv = 0,
        Movie = 1,

        // An album from Lidarr. Its files are the album's tracks (ADR 0020).
        Music = 2
    }

    // A series, a movie or an artist in the local catalog, named the way the catalog keys it.
    public readonly record struct TitleKey(MediaKind Kind, int ApplicationId, int ArrId);

    // What a share list can hold, at the levels the Arr apps monitor: a whole series, one season of
    // it, or one episode; a whole artist, or one album of it; a movie, whole. A whole series,
    // season or artist takes in what arrives later. ArrId is the series, the movie or the artist.
    // EpisodeArrId is an episode, or, for an artist, an album.
    public readonly record struct ShareKey(MediaKind Kind, int ApplicationId, int ArrId, int? SeasonNumber = null, int? EpisodeArrId = null)
    {
        public static ShareKey Series(int applicationId, int seriesArrId) => new(MediaKind.Tv, applicationId, seriesArrId);
        public static ShareKey Season(int applicationId, int seriesArrId, int seasonNumber) => new(MediaKind.Tv, applicationId, seriesArrId, seasonNumber);
        public static ShareKey Episode(int applicationId, int seriesArrId, int episodeArrId) => new(MediaKind.Tv, applicationId, seriesArrId, null, episodeArrId);
        public static ShareKey Movie(int applicationId, int movieArrId) => new(MediaKind.Movie, applicationId, movieArrId);
        public static ShareKey Artist(int applicationId, int artistArrId) => new(MediaKind.Music, applicationId, artistArrId);
        public static ShareKey Album(int applicationId, int artistArrId, int albumArrId) => new(MediaKind.Music, applicationId, artistArrId, null, albumArrId);

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

        // Set on the list that what is sent to one friend goes on (SendService).
        public int? SentToPeerId { get; set; }
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

    // The albums of one artist, for choosing among them.
    public class ShareableAlbum
    {
        public int AlbumArrId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public string AlbumType { get; set; }

        // Only an album Lidarr holds every track of is offered.
        public bool IsWhole { get; set; }
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

        // A file from Whisparr: adult content, which only an adult search finds. With it, a film
        // or a scene's ItemType, and what Whisparr searches for a scene by and knows it by. Left
        // out for every other file, so an offer without them is the same as it ever was.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? Adult { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ItemType { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Studio { get; set; }

        // As yyyy-MM-dd.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ReleaseDate { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Code { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ForeignId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string StashId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string TpdbId { get; set; }

        // The name the release had when the Arr app grabbed it, when the Arr app kept one.
        public string SceneName { get; set; }
        public string Edition { get; set; }

        // An album from Lidarr: its artist, the MusicBrainz IDs Lidarr knows both by, the kind of
        // release, and its tracks, in Lidarr's order. Title, Year and ReleaseDate are the album's,
        // and the quality and size are the whole album's. Left out for every other file.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Artist { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ForeignArtistId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string ForeignAlbumId { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string AlbumType { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<OfferTrack> Tracks { get; set; }

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

        // The file's BLAKE3 hash, where this node has worked it out. Two peers whose files have the
        // same hash hold the same bytes.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Blake3 { get; set; }

        // A hash of everything above, set when the offer is sealed. It changes when anything said
        // about the file does.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Fingerprint { get; set; }
    }

    // One track of an offered album. Like a file, it says nothing of its name or where it lives,
    // only which track it is, as an episode's title and number are sent. Its ID is what a transfer
    // asks for.
    public class OfferTrack
    {
        public string Id { get; set; }
        public string Extension { get; set; }
        public long Size { get; set; }
        public string Quality { get; set; }

        // The track's title, its number on its disc, and the disc, where Lidarr knows them.
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Title { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Number { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public int? Disc { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Blake3 { get; set; }
    }

    // What this node last sent one peer: the version, and each file's fingerprint by its ID.
    public class OfferRecord : ModelBase
    {
        public int PeerId { get; set; }
        public string Version { get; set; }

        // "id fingerprint" pairs, one to a line.
        public string Fingerprints { get; set; }
        public DateTime Sent { get; set; }

        public Dictionary<string, string> GetFingerprints()
        {
            return (Fingerprints ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Split(' '))
                .Where(parts => parts.Length == 2)
                .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        }

        public static string Write(IEnumerable<OfferItem> items)
        {
            return string.Join("\n", items.Select(i => $"{i.Id} {i.Fingerprint}"));
        }
    }

    // Everything one peer is offered at one moment. The version changes when the contents do.
    public class Offer
    {
        public string Version { get; set; }
        public List<OfferItem> Items { get; set; } = new();

        // Sets each item's fingerprint, orders the items, and works out the version from the
        // fingerprints alone. A node that kept each file's fingerprint can work out the same
        // version from what it holds, and so check that what it holds is what was offered.
        public static Offer Seal(List<OfferItem> items)
        {
            foreach (var item in items)
            {
                item.Fingerprint = null;
                item.Fingerprint = Hex(JsonSerializer.SerializeToUtf8Bytes(item));
            }

            var ordered = items.OrderBy(i => i.Id, StringComparer.Ordinal).ToList();

            return new Offer { Version = VersionOf(ordered.Select(i => (i.Id, i.Fingerprint))), Items = ordered };
        }

        public static string VersionOf(IEnumerable<(string Id, string Fingerprint)> files)
        {
            var lines = files.OrderBy(f => f.Id, StringComparer.Ordinal).Select(f => $"{f.Id} {f.Fingerprint}\n");

            return Hex(Encoding.UTF8.GetBytes(string.Concat(lines)));
        }

        private static string Hex(byte[] data)
        {
            return Convert.ToHexString(SHA256.HashData(data))[..16].ToLowerInvariant();
        }
    }
}
