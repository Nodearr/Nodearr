using System.Collections.Generic;

namespace NzbDrone.Core.Nodes.Transport
{
    public class FileTransferRequest
    {
        // Chosen by the caller, and used to ask about the transfer afterwards.
        public string Id { get; set; }
        public NodeAddress Peer { get; set; }

        // The peer's own name for the file.
        public string File { get; set; }

        // Where the bytes are written. The file stays here, verified, when the transfer completes.
        public string Destination { get; set; }

        // The hash an earlier attempt was told. With it, what is already at the destination is
        // kept and the transfer carries on from there, as long as the peer's file is still the same.
        public string ExpectedHash { get; set; }
    }

    public enum FileTransferState
    {
        Connecting,
        Receiving,
        Completed,
        Failed
    }

    public class FileTransferProgress
    {
        public string Id { get; set; }
        public FileTransferState State { get; set; }
        public long BytesDone { get; set; }
        public long Size { get; set; }

        // The BLAKE3 hash the sender gave before it sent anything.
        public string Hash { get; set; }

        // Set when the transfer failed. The transport's own word for what went wrong.
        public string FailureKind { get; set; }
        public string FailureMessage { get; set; }
    }

    // What this node answers when another node asks for a file.
    public class FileGrant
    {
        // Where the file is on this machine. Null when the request is refused.
        public string Path { get; set; }

        // NotAuthorized, Revoked or NotFound.
        public string Refusal { get; set; }

        public static FileGrant Allow(string path) => new() { Path = path };
        public static FileGrant Refuse(string reason) => new() { Refusal = reason };
    }

    // Decides, for every request, whether a node may have a file. The transport has verified who
    // is asking.
    public delegate FileGrant FileSourceHandler(string remoteNodeId, string file);

    // Moves files between nodes. File data only ever travels over a direct path (ADR 0011).
    public interface IFileTransport
    {
        void Start(FileTransferRequest request);
        List<FileTransferProgress> List();

        // Stops a transfer and forgets it. What it wrote stays where it is.
        void Cancel(string id);

        // Cuts every file this node is sending to another node.
        void Disconnect(string nodeId);
        void SetSource(FileSourceHandler handler);
    }
}
