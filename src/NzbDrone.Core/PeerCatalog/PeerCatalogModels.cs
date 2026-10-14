using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerCatalog
{
    // How a file a peer offers compares with what the local Arr apps hold.
    public enum PeerItemStatus
    {
        // The series or movie is not in any local Arr app.
        NotInLibrary = 0,

        // It is in a local Arr app, without a file.
        Missing = 1,

        // There is a local file, and the peer's is better.
        Upgrade = 2,

        // There is a local file at least as good.
        Have = 3
    }

    // One file a trusted peer has offered. The rows for a peer are replaced as a whole whenever
    // its catalog changes, so anything the peer stops offering disappears.
    public class PeerCatalogItem : ModelBase
    {
        public int PeerId { get; set; }

        // The peer's own name for the file. It means nothing here except when asking for the file.
        public string RemoteId { get; set; }

        // The name the Arr apps know the file by. See CandidateIdProvider.
        public string CandidateId { get; set; }
        public MediaKind MediaKind { get; set; }
        public int TvdbId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string Title { get; set; }
        public int Year { get; set; }
        public int SeasonNumber { get; set; }
        public string EpisodeNumbers { get; set; }
        public string EpisodeTitles { get; set; }

        // The day each episode aired, as yyyy-MM-dd, comma separated. Empty when the peer did not say.
        public string AirDates { get; set; }

        // As the scene numbers the episodes, for a series it numbers differently from TVDB. Null
        // and empty otherwise. One number for each in EpisodeNumbers, comma separated.
        public int? SceneSeasonNumber { get; set; }
        public string SceneEpisodeNumbers { get; set; }
        public string SceneName { get; set; }
        public string Edition { get; set; }
        public string Extension { get; set; }

        // What the Arr apps are shown. Worked out here from the fields the peer sent.
        public string ReleaseTitle { get; set; }
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
        public PeerItemStatus LocalStatus { get; set; }

        // As the peer sent them: a hash of what it said about the file, which the catalog's
        // version is made from, and the file's BLAKE3 hash where the peer has worked it out.
        public string Fingerprint { get; set; }
        public string Blake3 { get; set; }

        public List<int> GetEpisodeNumbers()
        {
            return (EpisodeNumbers ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        }

        public List<string> GetAirDates()
        {
            return (AirDates ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        public bool HasSceneNumbers => SceneSeasonNumber.HasValue && SceneEpisodeNumbers.IsNotNullOrWhiteSpace();

        public List<int> GetSceneEpisodeNumbers()
        {
            return (SceneEpisodeNumbers ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        }

        // The season and episodes a release title for this file says. Sonarr reads a title's
        // numbers as the scene's for a series that has scene numbers.
        public int TitleSeasonNumber => HasSceneNumbers ? SceneSeasonNumber.Value : SeasonNumber;

        public List<int> GetTitleEpisodeNumbers()
        {
            return HasSceneNumbers ? GetSceneEpisodeNumbers() : GetEpisodeNumbers();
        }

        // Scene numbers from another node are kept only when they fit the file: a sensible season,
        // and one number for each of its episodes.
        private static bool AreSceneNumbers(OfferItem offer)
        {
            return offer.SceneSeasonNumber is >= 0 and < 10000 &&
                   offer.SceneEpisodeNumbers is { Count: > 0 } numbers &&
                   numbers.Count == (offer.EpisodeNumbers?.Count ?? 0) &&
                   numbers.All(n => n is >= 0 and < 100000);
        }

        // A date from another node ends up in a release title and a folder name, so only a real
        // date in the one expected spelling is kept.
        private static bool IsAirDate(string value)
        {
            return value is { Length: 10 } &&
                   DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        public static PeerCatalogItem FromOffer(int peerId, OfferItem offer)
        {
            return new PeerCatalogItem
            {
                PeerId = peerId,
                RemoteId = offer.Id,
                MediaKind = offer.Kind,
                TvdbId = offer.TvdbId,
                TmdbId = offer.TmdbId,
                ImdbId = offer.ImdbId,
                Title = offer.Title,
                Year = offer.Year,
                SeasonNumber = offer.SeasonNumber,
                EpisodeNumbers = string.Join(",", offer.EpisodeNumbers ?? new List<int>()),
                EpisodeTitles = string.Join(" + ", offer.EpisodeTitles ?? new List<string>()),
                AirDates = string.Join(",", (offer.AirDates ?? new List<string>()).Where(IsAirDate)),
                SceneSeasonNumber = AreSceneNumbers(offer) ? offer.SceneSeasonNumber : null,
                SceneEpisodeNumbers = AreSceneNumbers(offer) ? string.Join(",", offer.SceneEpisodeNumbers) : null,
                SceneName = offer.SceneName,
                Edition = offer.Edition,
                Extension = offer.Extension,
                Quality = offer.Quality,
                QualitySource = offer.QualitySource,
                Resolution = offer.Resolution,
                Size = offer.Size,
                Languages = offer.Languages,
                CustomFormats = offer.CustomFormats,
                ReleaseGroup = offer.ReleaseGroup,
                VideoCodec = offer.VideoCodec,
                VideoDynamicRange = offer.VideoDynamicRange,
                AudioCodec = offer.AudioCodec,
                AudioChannels = offer.AudioChannels,
                DateAdded = offer.DateAdded,
                Fingerprint = IsHex(offer.Fingerprint, 16) ? offer.Fingerprint : null,
                Blake3 = IsHex(offer.Blake3, 64) ? offer.Blake3 : null
            };
        }

        // Hashes from another node end up compared and stored, so only lowercase hex of the
        // expected length is kept.
        private static bool IsHex(string value, int length)
        {
            return value != null && value.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
        }
    }

    public class PeerCatalogSummary
    {
        public int PeerId { get; set; }
        public string PeerName { get; set; }
        public string CatalogVersion { get; set; }
        public DateTime? CatalogSyncedAt { get; set; }
        public string CatalogError { get; set; }
        public int NotInLibrary { get; set; }
        public int Missing { get; set; }
        public int Upgrade { get; set; }
        public int Have { get; set; }
    }

    public class PeerCatalogSyncCommand : Command
    {
        // Syncs every trusted peer when not set.
        public int? PeerId { get; set; }

        public override bool SendUpdatesToClient => true;
        public override bool UpdateScheduledTask => !PeerId.HasValue;
        public override string CompletionMessage => "Completed";
    }

    // A trusted peer said its offer changed. Its catalog should be fetched again.
    public class PeerCatalogChangedEvent : IEvent
    {
        public PeerCatalogChangedEvent(int peerId)
        {
            PeerId = peerId;
        }

        public int PeerId { get; }
    }
}
