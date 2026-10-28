using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Transfers
{
    public enum TransferState
    {
        // Waiting for a free slot, or for the next attempt.
        Queued = 0,
        Downloading = 1,

        // Verified and moved into place.
        Completed = 2,
        Failed = 3
    }

    // Why a transfer failed. The names are shown to people in the Arr apps' history, so they are
    // part of what Nodearr promises.
    public enum TransferFailure
    {
        None = 0,

        // The nodes could only reach each other through a relay. Files never go that way.
        NoDirectRoute = 1,
        DirectPathLost = 2,
        ConnectionLost = 3,
        PeerUnreachable = 4,

        // The other node no longer shares this file with this node.
        NotAuthorized = 5,

        // Trust between the two nodes has ended, from either side.
        Revoked = 6,
        Blocked = 7,
        NotFound = 8,

        // What arrived did not match the hash the sender gave first.
        VerificationFailed = 9,
        DiskError = 10,
        Failed = 11
    }

    // One file being fetched from a trusted node, from the moment an Arr app hands over the ticket
    // until the Arr app has imported the result and asked for it to be removed.
    public class TransferJob : ModelBase
    {
        // What the Arr app knows the download by: SABnzbd's nzo_id.
        public string DownloadId { get; set; }
        public string CandidateId { get; set; }
        public int PeerId { get; set; }
        public string PeerNodeId { get; set; }

        // The peer's own name for the file.
        public string RemoteId { get; set; }
        public MediaKind MediaKind { get; set; }

        // The release title, which names the folder and the file the Arr app imports.
        public string Title { get; set; }

        // Set when the Arr app asked for the episode by the day it aired. The title is then
        // written with that day, which is the only title Sonarr accepts for such a search.
        public string AirDate { get; set; }
        public string Extension { get; set; }

        // The Arr app's download category, such as tv or movies.
        public string Category { get; set; }
        public long Size { get; set; }
        public long BytesDone { get; set; }
        public TransferState State { get; set; }
        public TransferFailure Failure { get; set; }
        public string FailureMessage { get; set; }

        // The BLAKE3 hash the sender gave before sending. A later attempt resumes only against it.
        public string Hash { get; set; }

        // The hash the file was offered with, where the node had worked it out. Any trusted node
        // offering a file with this hash has the same bytes, and can be fetched from instead.
        public string Blake3 { get; set; }

        // The nodes this transfer moved away from, comma separated. It does not go back to them.
        public string TriedPeerIds { get; set; }

        // An album's tracks, as JSON, in the order they are fetched. Null for a single file. One
        // part is fetched at a time, PartIndex is the one under way, and RemoteId and Hash are
        // its. The album's Size and BytesDone count every part (ADR 0020).
        public string Parts { get; set; }
        public int PartIndex { get; set; }

        // Where the finished file is: a folder holding the one file, or an album's tracks.
        public string OutputPath { get; set; }
        public int Attempts { get; set; }
        public DateTime? NextAttempt { get; set; }
        public DateTime Added { get; set; }
        public DateTime? Started { get; set; }
        public DateTime? Ended { get; set; }

        // While downloading: the other node has no free upload slot, and this is the place in its
        // line it gave, counting from 1. Null once it sends, and for a node too old to say.
        public int? WaitingPosition { get; set; }

        public bool IsWaitingForPeer => State == TransferState.Downloading && WaitingPosition.HasValue;

        public bool IsActive => State is TransferState.Queued or TransferState.Downloading;

        public bool HasParts => Parts.IsNotNullOrWhiteSpace();

        public List<TransferPart> GetParts()
        {
            return HasParts ? JsonSerializer.Deserialize<List<TransferPart>>(Parts) : new List<TransferPart>();
        }

        public void SetParts(List<TransferPart> parts)
        {
            Parts = parts is { Count: > 0 } ? JsonSerializer.Serialize(parts) : null;
        }

        // What the transport knows this attempt by. Each part of an album is a transfer of its own.
        public string TransportId => HasParts ? $"{DownloadId}-{PartIndex + 1}" : DownloadId;

        public HashSet<int> GetTriedPeerIds()
        {
            return (TriedPeerIds ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToHashSet();
        }
    }

    // One track of an album, as the peer offered it.
    public class TransferPart
    {
        public string RemoteId { get; set; }
        public string Extension { get; set; }
        public long Size { get; set; }

        // What the file is called when it arrives, without its extension: Artist - Album - 01 -
        // Title, as Lidarr names its own. Null when the peer did not say the track's title.
        public string Name { get; set; }
    }

    // Raised when any transfer was added, removed, or moved on.
    public class TransfersChangedEvent : IEvent
    {
    }

    // Raised each time an attempt to fetch a file ends, however it ended. A failure of None means
    // the file arrived, which it can only have done over a direct route.
    public class TransferAttemptEndedEvent : IEvent
    {
        public TransferAttemptEndedEvent(int peerId, TransferFailure failure)
        {
            PeerId = peerId;
            Failure = failure;
        }

        public int PeerId { get; }
        public TransferFailure Failure { get; }
    }

    public static class TransferFailureText
    {
        private static readonly Dictionary<TransferFailure, string> Explanations = new()
        {
            { TransferFailure.NoDirectRoute, "The two nodes could not reach each other directly, and Nodearr never sends files through a relay." },
            { TransferFailure.DirectPathLost, "The direct connection to the other node was lost part way through." },
            { TransferFailure.ConnectionLost, "The connection to the other node was lost." },
            { TransferFailure.PeerUnreachable, "The other node could not be reached. It may be offline." },
            { TransferFailure.NotAuthorized, "The other node no longer shares this file with this node." },
            { TransferFailure.Revoked, "Trust between this node and the other node has ended." },
            { TransferFailure.Blocked, "The other node has blocked this node." },
            { TransferFailure.NotFound, "The other node no longer has this file." },
            { TransferFailure.VerificationFailed, "The file that arrived did not match its hash, so it was thrown away." },
            { TransferFailure.DiskError, "The file could not be written here." }
        };

        // Written for someone reading the Arr app's history, who may never have opened Nodearr.
        public static string For(TransferFailure failure, string detail)
        {
            var text = Explanations.TryGetValue(failure, out var explanation) ? explanation : "The transfer did not finish.";

            // For a route that failed the detail is what Nodearr knows about the route. See RouteDiagnosis.
            if (failure is TransferFailure.DiskError or TransferFailure.Failed or TransferFailure.NoDirectRoute or TransferFailure.DirectPathLost && !string.IsNullOrWhiteSpace(detail))
            {
                text += " " + detail.Trim();
            }

            return $"Nodearr transfer failed: {failure}. {text}";
        }

        // What the transport calls a failure, as one of ours.
        public static TransferFailure FromTransport(string kind)
        {
            return kind switch
            {
                "NoRoute" => TransferFailure.PeerUnreachable,
                "RelayedMediaRefused" => TransferFailure.NoDirectRoute,
                _ => Enum.TryParse<TransferFailure>(kind, out var parsed) && parsed != TransferFailure.None ? parsed : TransferFailure.Failed
            };
        }

        // Worth another attempt: the cause may pass, and what arrived so far is kept.
        public static bool IsTransient(TransferFailure failure)
        {
            return failure is TransferFailure.NoDirectRoute or TransferFailure.DirectPathLost or TransferFailure.ConnectionLost or TransferFailure.PeerUnreachable or TransferFailure.Failed;
        }

        // Worth another attempt from nothing. What arrived did not match the hash it was sent
        // with, so none of it is kept. The likely cause is a file that changed on the other node
        // without its size or modified time changing. That node works the hash out afresh once
        // it hears, and the next attempt carries the right one.
        public static bool StartsOver(TransferFailure failure)
        {
            return failure is TransferFailure.VerificationFailed;
        }
    }
}
