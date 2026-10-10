using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Nodearr.Http.ArrContractSpike
{
    // S1 spike only. Candidates come from a JSON file in the data folder instead of a peer catalog,
    // and a "transfer" is a throttled local file copy. See docs/adr/0010.
    public class ArrContractSpikeConfig
    {
        public string CompleteDirLocal { get; set; }
        public string CompleteDirReported { get; set; }
        public int TransferSeconds { get; set; } = 6;
        public List<SpikeCandidate> Candidates { get; set; } = new();
    }

    public class SpikeCandidate
    {
        public string Id { get; set; }
        public string Title { get; set; }
        public string Kind { get; set; }
        public int TvdbId { get; set; }
        public int Season { get; set; }
        public int Episode { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public int Category { get; set; }
        public string SourcePath { get; set; }
        public long SizeBytes { get; set; }
        public DateTime PublishedAt { get; set; }
        public string Fail { get; set; }

        public bool IsMovie => string.Equals(Kind, "movie", StringComparison.OrdinalIgnoreCase);

        public long GetSize()
        {
            if (SizeBytes > 0)
            {
                return SizeBytes;
            }

            return SourcePath != null && File.Exists(SourcePath) ? new FileInfo(SourcePath).Length : 0;
        }
    }

    public enum SpikeJobStatus
    {
        Queued,
        Downloading,
        Completed,
        Failed
    }

    public class SpikeJob
    {
        public string NzoId { get; set; }
        public SpikeCandidate Candidate { get; set; }
        public string Category { get; set; }
        public string NzbName { get; set; }
        public SpikeJobStatus Status { get; set; }
        public long TotalBytes { get; set; }
        public long DoneBytes { get; set; }
        public string FailMessage { get; set; }
        public string StorageLocal { get; set; }
        public string StorageReported { get; set; }
        public DateTime AddedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public CancellationTokenSource Cancellation { get; set; }
    }
}
