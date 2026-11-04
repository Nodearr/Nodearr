using System;
using System.Collections.Generic;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Statistics
{
    // What a failure on the statistics page means, and where the troubleshooting guide says more.
    public static class FailureGuide
    {
        private const string GuidePage = "https://nodearr.com/guide/troubleshooting/";

        // What the transport, or the tracker, says of an upload that stopped, from the sending
        // side: the friend's node is the other node here.
        private static readonly Dictionary<string, string> SentExplanations = new()
        {
            { "NoDirectRoute", "The two nodes could not reach each other directly, and Nodearr never sends files through a relay." },
            { "DirectPathLost", "The direct connection to the friend's node was lost part way through." },
            { "NotAuthorized", "The file is no longer shared with that friend." },
            { "Revoked", "Trust between this node and the friend's node ended." },
            { "NotFound", "The file could not be read here. It may have moved, or Nodearr may not be allowed to open it." },
            { "VerificationFailed", "The friend's node said what arrived did not match the file's hash. It asks again, and the hash is worked out afresh." },
            { "Stopped", "The friend's node went away or cancelled, or this node's transport restarted while it was sending." }
        };

        private static readonly Dictionary<string, string> Sections = new()
        {
            { "NoDirectRoute", "#relay-only" },
            { "DirectPathLost", "#relay-only" },
            { "ConnectionLost", "#a-friend-cannot-be-reached" },
            { "PeerUnreachable", "#a-friend-cannot-be-reached" }
        };

        public static string Explain(TransferDirection direction, string failure)
        {
            if (direction == TransferDirection.Sent)
            {
                return SentExplanations.TryGetValue(failure ?? string.Empty, out var text) ? text : "The upload did not finish.";
            }

            return TransferFailureText.Explain(Enum.TryParse<TransferFailure>(failure, out var parsed) ? parsed : TransferFailure.Failed);
        }

        public static string GuideUrl(TransferDirection direction, string failure)
        {
            if (Sections.TryGetValue(failure ?? string.Empty, out var section))
            {
                return GuidePage + section;
            }

            var own = (direction, failure) switch
            {
                (TransferDirection.Sent, "NotFound") => "#friends-are-refused-your-files",
                (TransferDirection.Sent, "NotAuthorized") => "#a-friend-sees-nothing-of-yours",
                (TransferDirection.Fetched, "DiskError") => "#the-arr-app-does-not-import-what-arrived",
                _ => "#transfers-fail"
            };

            return GuidePage + own;
        }
    }
}
