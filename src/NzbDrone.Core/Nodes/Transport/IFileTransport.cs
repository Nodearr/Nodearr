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

        // The other node has no free upload slot. It has said where this transfer is in its line.
        Waiting,
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

        // While waiting: the place in the other node's line, counting from 1.
        public int? QueuePosition { get; set; }
    }

    // One file this node is sending to another node, as the transport reports it.
    public class FileUploadProgress
    {
        public string Id { get; set; }
        public string RemoteNodeId { get; set; }

        // The name the other node asked for the file by, and where the file is here.
        public string File { get; set; }
        public string Path { get; set; }
        public Transfers.UploadState State { get; set; }
        public long BytesSent { get; set; }
        public long Size { get; set; }
        public int Attempts { get; set; }
        public string LastFailureKind { get; set; }
        public string LastFailureMessage { get; set; }
        public string FailureKind { get; set; }
        public string FailureMessage { get; set; }

        // While waiting for a free upload slot: its place in line, counting from 1.
        public int? QueuePosition { get; set; }
    }

    // A file's BLAKE3 hash, and the version of the file it belongs to, as the transport saw it.
    public class HashedFile
    {
        public long Size { get; set; }

        // Nanoseconds since 1970, as the transport's filesystem reports it. Only the transport
        // compares it, so it never has to agree with .NET's idea of the time.
        public long ModifiedNs { get; set; }
        public string Blake3 { get; set; }
    }

    // What this node answers when another node asks for a file.
    public class FileGrant
    {
        // Where the file is on this machine. Null when the request is refused.
        public string Path { get; set; }

        // The hash worked out earlier, if there is one. The transport uses it only if the file has
        // not changed since, and otherwise hashes the file before sending.
        public HashedFile Hash { get; set; }

        // NotAuthorized, Revoked or NotFound.
        public string Refusal { get; set; }

        public static FileGrant Allow(string path, HashedFile hash = null) => new() { Path = path, Hash = hash };
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

        // What this node is sending now, and what it finished sending in the last few minutes.
        List<FileUploadProgress> ListUploads();

        // Stops a transfer and forgets it. What it wrote stays where it is.
        void Cancel(string id);

        // Cuts one file this node is sending, telling the other node why: NotAuthorized, Revoked
        // or NotFound, as for a refused request.
        void StopUpload(string id, string reason);

        // Cuts every file this node is sending to another node.
        void Disconnect(string nodeId);
        void SetSource(FileSourceHandler handler);

        // Works out a file's hash. Given what it was told last time, the transport answers without
        // reading the file if the file has not changed. Null when the file cannot be read. This can
        // take as long as reading the whole file.
        HashedFile Hash(string path, HashedFile known);
    }
}
