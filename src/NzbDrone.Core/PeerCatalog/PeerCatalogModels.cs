using System;
using System.Collections.Generic;
using System.Linq;
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

        public List<int> GetEpisodeNumbers()
        {
            return (EpisodeNumbers ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
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
                DateAdded = offer.DateAdded
            };
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
