using System;
using System.Collections.Generic;
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

        public TitleKey Key => new(MediaKind, ApplicationId, ArrId);
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
        public List<TitleKey> Items { get; set; } = new();
        public List<int> PeerIds { get; set; } = new();
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
        public string ReleaseTitle { get; set; }
        public string Quality { get; set; }
        public string QualitySource { get; set; }
        public int Resolution { get; set; }
        public long Size { get; set; }
        public string Languages { get; set; }
        public string CustomFormats { get; set; }
        public string ReleaseGroup { get; set; }
        public string VideoCodec { get; set; }
        public string AudioCodec { get; set; }
        public DateTime? DateAdded { get; set; }
    }

    // Everything one peer is offered at one moment. The version changes when the contents do.
    public class Offer
    {
        public string Version { get; set; }
        public List<OfferItem> Items { get; set; } = new();
    }
}
