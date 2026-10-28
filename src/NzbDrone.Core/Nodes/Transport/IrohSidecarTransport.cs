using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using Microsoft.Extensions.Options;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes.Settings;

namespace NzbDrone.Core.Nodes.Transport
{
    // Runs the nodearr-transport sidecar and talks to it over its loopback API (plan decision 4).
    // The sidecar holds the node key and the iroh endpoint. If it stops, it is started again.
    public class IrohSidecarTransport : IPeerTransport, IFileTransport, IHandle<ApplicationStartedEvent>, IHandle<ApplicationShutdownRequested>
    {
        private const string TokenHeader = "x-nodearr-token";
        private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IEventAggregator _eventAggregator;
        private readonly TransportOptions _options;
        private readonly INodeSettingsProvider _settings;
        private readonly Logger _logger;
        private readonly object _lock = new();
        private readonly CancellationTokenSource _shutdown = new();

        private IncomingMessageHandler _handler;
        private FileSourceHandler _fileSource;
        private List<string> _blocked = new();
        private Process _process;
        private HttpClient _client;
        private NodeAddress _localAddress;
        private Thread _supervisor;
        private volatile bool _restarting;

        public IrohSidecarTransport(IAppFolderInfo appFolderInfo, IEventAggregator eventAggregator, IOptions<TransportOptions> options, INodeSettingsProvider settings, Logger logger)
        {
            _appFolderInfo = appFolderInfo;
            _eventAggregator = eventAggregator;
            _options = options.Value;
            _settings = settings;
            _logger = logger;
        }

        public bool IsRunning => _client != null && _process is { HasExited: false };

        public void Handle(ApplicationStartedEvent message)
        {
            lock (_lock)
            {
                if (_supervisor != null)
                {
                    return;
                }

                _supervisor = new Thread(Supervise) { IsBackground = true, Name = "transport-sidecar" };
                _supervisor.Start();
            }
        }

        public void Handle(ApplicationShutdownRequested message)
        {
            _shutdown.Cancel();
            Stop();
        }

        public void Restart()
        {
            // The supervisor sees the process go and starts it again, with the settings as they are now.
            _restarting = true;
            Stop();
        }

        public NodeAddress GetLocalAddress()
        {
            var client = RequireClient();

            try
            {
                var identity = client.GetFromJsonAsync<IdentityResponse>("/v1/identity", JsonOptions).GetAwaiter().GetResult();

                _localAddress = new NodeAddress { NodeId = identity.NodeId, RelayUrl = identity.RelayUrl, IpAddresses = identity.IpAddrs ?? new List<string>() };
            }
            catch (HttpRequestException ex)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }

            return _localAddress;
        }

        public byte[] Request(NodeAddress peer, byte[] body, TimeSpan timeout)
        {
            var client = RequireClient();
            var request = new RequestBody
            {
                NodeId = peer.NodeId,
                RelayUrl = peer.RelayUrl,
                IpAddrs = peer.IpAddresses ?? new List<string>(),
                Body = Convert.ToBase64String(body),
                TimeoutMs = (long)timeout.TotalMilliseconds
            };

            RequestResponse response;

            try
            {
                using var http = client.PostAsJsonAsync("/v1/request", request, JsonOptions).GetAwaiter().GetResult();

                http.EnsureSuccessStatusCode();
                response = http.Content.ReadFromJsonAsync<RequestResponse>(JsonOptions).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }

            if (response.Error != null)
            {
                var failure = Enum.TryParse<TransportFailure>(response.Error.Kind, out var parsed) ? parsed : TransportFailure.Failed;

                throw new TransportException(failure, response.Error.Message);
            }

            return Convert.FromBase64String(response.Body);
        }

        public ConnectionProbe Probe(NodeAddress peer, TimeSpan waitForDirect)
        {
            var client = RequireClient();
            var request = new ProbeBody
            {
                NodeId = peer.NodeId,
                RelayUrl = peer.RelayUrl,
                IpAddrs = peer.IpAddresses ?? new List<string>(),
                WaitMs = (long)waitForDirect.TotalMilliseconds
            };

            ProbeResponse response;

            try
            {
                using var http = client.PostAsJsonAsync("/v1/probe", request, JsonOptions).GetAwaiter().GetResult();

                http.EnsureSuccessStatusCode();
                response = http.Content.ReadFromJsonAsync<ProbeResponse>(JsonOptions).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }

            if (response.Error != null)
            {
                var failure = Enum.TryParse<TransportFailure>(response.Error.Kind, out var parsed) ? parsed : TransportFailure.Failed;

                throw new TransportException(failure, response.Error.Message);
            }

            return new ConnectionProbe
            {
                Path = response.Path switch { "direct" => ConnectionPath.Direct, "relay" => ConnectionPath.Relay, _ => ConnectionPath.Unknown },
                RemoteAddress = response.Remote
            };
        }

        public NetworkStatus GetNetworkStatus()
        {
            var client = RequireClient();

            try
            {
                var network = client.GetFromJsonAsync<NetworkResponse>("/v1/network", JsonOptions).GetAwaiter().GetResult();

                return new NetworkStatus
                {
                    RelayUrl = network.RelayUrl,
                    IpAddresses = network.IpAddrs ?? new List<string>(),
                    Udp = network.Udp,
                    MappingVaries = network.MappingVaries,
                    PublicAddress = network.PublicAddress,
                    CaptivePortal = network.CaptivePortal
                };
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }
        }

        public void SetBlocked(IEnumerable<string> nodeIds)
        {
            lock (_lock)
            {
                _blocked = nodeIds.ToList();
            }

            if (IsRunning)
            {
                PushBlocked(_client);
            }
        }

        public void SetHandler(IncomingMessageHandler handler)
        {
            _handler = handler;
        }

        public void SetSource(FileSourceHandler handler)
        {
            _fileSource = handler;
        }

        public void Start(FileTransferRequest request)
        {
            var body = new StartTransferBody
            {
                Id = request.Id,
                NodeId = request.Peer.NodeId,
                RelayUrl = request.Peer.RelayUrl,
                IpAddrs = request.Peer.IpAddresses ?? new List<string>(),
                File = request.File,
                Dest = request.Destination,
                ExpectedHash = request.ExpectedHash
            };

            Send(client => client.PostAsJsonAsync("/v1/transfers", body, JsonOptions));
        }

        public List<FileTransferProgress> List()
        {
            var client = RequireClient();

            try
            {
                var states = client.GetFromJsonAsync<List<TransferState>>("/v1/transfers", JsonOptions).GetAwaiter().GetResult();

                return states.ConvertAll(s => new FileTransferProgress
                {
                    Id = s.Id,
                    State = Enum.TryParse<FileTransferState>(s.State, true, out var state) ? state : FileTransferState.Failed,
                    BytesDone = s.BytesDone,
                    Size = s.Size,
                    Hash = s.Blake3,
                    FailureKind = s.Failure?.Kind,
                    FailureMessage = s.Failure?.Message
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }
        }

        public List<FileUploadProgress> ListUploads()
        {
            var client = RequireClient();

            try
            {
                var states = client.GetFromJsonAsync<List<UploadStateBody>>("/v1/uploads", JsonOptions).GetAwaiter().GetResult();

                return states.ConvertAll(s => new FileUploadProgress
                {
                    Id = s.Id,
                    RemoteNodeId = s.RemoteNodeId,
                    File = s.File,
                    Path = s.Path,
                    State = Enum.TryParse<Transfers.UploadState>(s.State, true, out var state) ? state : Transfers.UploadState.Failed,
                    BytesSent = s.BytesSent,
                    Size = s.Size,
                    Attempts = s.Attempts,
                    LastFailureKind = s.LastFailure?.Kind,
                    LastFailureMessage = s.LastFailure?.Message,
                    FailureKind = s.Failure?.Kind,
                    FailureMessage = s.Failure?.Message
                });
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }
        }

        public void Cancel(string id)
        {
            // 404 is fine: the transport had already forgotten it.
            Send(client => client.DeleteAsync("/v1/transfers/" + Uri.EscapeDataString(id)), allowNotFound: true);
        }

        public HashedFile Hash(string path, HashedFile known)
        {
            var client = RequireClient();

            try
            {
                using var response = client.PostAsJsonAsync("/v1/hash", new HashBody { Path = path, Known = known }, JsonOptions, _shutdown.Token).GetAwaiter().GetResult();

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    _logger.Debug("Unable to hash {0}: {1}", path, response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

                    return null;
                }

                response.EnsureSuccessStatusCode();

                return response.Content.ReadFromJsonAsync<HashedFile>(JsonOptions).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }
        }

        public void StopUpload(string id, string reason)
        {
            // 404 is fine: the upload had already ended.
            Send(client => client.PostAsJsonAsync("/v1/uploads/" + Uri.EscapeDataString(id) + "/stop", new StopUploadBody { Reason = reason }, JsonOptions), allowNotFound: true);
        }

        public void Disconnect(string nodeId)
        {
            Send(client => client.PostAsJsonAsync("/v1/disconnect", new DisconnectBody { NodeId = nodeId }, JsonOptions));
        }

        private void Send(Func<HttpClient, System.Threading.Tasks.Task<HttpResponseMessage>> send, bool allowNotFound = false)
        {
            var client = RequireClient();

            try
            {
                using var response = send(client).GetAwaiter().GetResult();

                if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                {
                    return;
                }

                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not responding: " + ex.Message);
            }
        }

        private HttpClient RequireClient()
        {
            var client = _client;

            if (client == null || !IsRunning)
            {
                throw new TransportException(TransportFailure.Unavailable, "The peer transport is not running");
            }

            return client;
        }

        private void Supervise()
        {
            var delay = TimeSpan.FromSeconds(2);

            while (!_shutdown.IsCancellationRequested)
            {
                try
                {
                    if (!Start())
                    {
                        // No binary. Nothing will change until the next start.
                        return;
                    }

                    delay = TimeSpan.FromSeconds(2);
                    Pump();
                }
                catch (Exception ex)
                {
                    // Stopping Nodearr cancels the wait for the next message, which arrives here as
                    // an exception. It has to be caught like any other: one that leaves this thread
                    // takes the whole process with it, and a restart from the settings page is a
                    // stop followed by a start in the same process.
                    if (!_shutdown.IsCancellationRequested && !_restarting)
                    {
                        _logger.Warn(ex, "The peer transport stopped unexpectedly");
                    }
                }

                if (_shutdown.IsCancellationRequested)
                {
                    // The sidecar was stopped by the shutdown itself. Nothing is left to tidy or tell.
                    return;
                }

                Stop();
                _eventAggregator.PublishEvent(new PeerTransportStateChangedEvent());

                if (_restarting)
                {
                    // Asked for. Start again at once, and do not count it against the transport.
                    _restarting = false;
                    delay = TimeSpan.FromSeconds(2);

                    continue;
                }

                if (_shutdown.Token.WaitHandle.WaitOne(delay))
                {
                    return;
                }

                delay = TimeSpan.FromSeconds(Math.Min(delay.TotalSeconds * 2, 30));
            }
        }

        private bool Start()
        {
            var binary = FindBinary();

            if (binary == null)
            {
                _logger.Warn("The peer transport binary (nodearr-transport) was not found. Peer features are unavailable.");
                _eventAggregator.PublishEvent(new PeerTransportStateChangedEvent());

                return false;
            }

            NodeKeyFile.ApplyPendingRestore(_appFolderInfo.AppDataFolder, _logger);

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            var startInfo = new ProcessStartInfo(binary)
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add("--data-dir");
            startInfo.ArgumentList.Add(_appFolderInfo.AppDataFolder);

            // Read at every start, so a restart is all it takes to apply a change.
            var settings = _settings.Get();

            startInfo.ArgumentList.Add("--relay");
            startInfo.ArgumentList.Add(settings.Relay.ToString().ToLowerInvariant());

            if (settings.Relay == RelayMode.Custom && settings.RelayUrl.IsNotNullOrWhiteSpace())
            {
                startInfo.ArgumentList.Add("--relay-url");
                startInfo.ArgumentList.Add(settings.RelayUrl.Trim());
            }

            if (!settings.PortMapping)
            {
                startInfo.ArgumentList.Add("--no-port-mapping");
            }

            if (settings.Port > 0)
            {
                startInfo.ArgumentList.Add("--bind-port");
                startInfo.ArgumentList.Add(settings.Port.ToString());
            }

            if (settings.DirectTimeoutSeconds > 0)
            {
                startInfo.ArgumentList.Add("--direct-timeout-secs");
                startInfo.ArgumentList.Add(settings.DirectTimeoutSeconds.ToString());
            }

            if (settings.UploadLimitKbps > 0)
            {
                startInfo.ArgumentList.Add("--upload-limit-kbps");
                startInfo.ArgumentList.Add(settings.UploadLimitKbps.ToString());
            }

            if (settings.TotalUploadLimitKbps > 0)
            {
                startInfo.ArgumentList.Add("--total-upload-limit-kbps");
                startInfo.ArgumentList.Add(settings.TotalUploadLimitKbps.ToString());
            }

            if (settings.DownloadLimitKbps > 0)
            {
                startInfo.ArgumentList.Add("--download-limit-kbps");
                startInfo.ArgumentList.Add(settings.DownloadLimitKbps.ToString());
            }

            // The token never appears on the command line, where other local users could read it.
            startInfo.Environment["NODEARR_TRANSPORT_TOKEN"] = token;

            var process = Process.Start(startInfo);

            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data.IsNotNullOrWhiteSpace())
                {
                    _logger.Info("transport: {0}", e.Data);
                }
            };
            process.BeginErrorReadLine();

            // The sidecar's first line says where its API is.
            var readyLine = process.StandardOutput.ReadLineAsync();

            if (!readyLine.Wait(TimeSpan.FromSeconds(20)) || readyLine.Result == null)
            {
                process.Kill(true);
                throw new InvalidOperationException("The peer transport did not start");
            }

            var ready = JsonSerializer.Deserialize<ReadyLine>(readyLine.Result, JsonOptions);
            var client = new HttpClient { BaseAddress = new Uri("http://" + ready.Api), Timeout = Timeout.InfiniteTimeSpan };

            client.DefaultRequestHeaders.Add(TokenHeader, token);

            _process = process;
            _client = client;

            PushBlocked(client);
            _logger.Info("Peer transport started. Node ID: {0}", ready.NodeId);
            _eventAggregator.PublishEvent(new PeerTransportStateChangedEvent());

            return true;
        }

        // Hands each incoming message to the handler and returns its answer, until the sidecar goes away.
        private void Pump()
        {
            var client = _client;

            while (!_shutdown.IsCancellationRequested && _process is { HasExited: false })
            {
                using var response = client.GetAsync("/v1/incoming?wait_ms=20000", _shutdown.Token).GetAwaiter().GetResult();

                if (response.StatusCode == HttpStatusCode.NoContent)
                {
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var incoming = response.Content.ReadFromJsonAsync<IncomingMessage>(JsonOptions).GetAwaiter().GetResult();

                ThreadPool.QueueUserWorkItem(_ => Answer(client, incoming));
            }
        }

        private void Answer(HttpClient client, IncomingMessage incoming)
        {
            var answer = new AnswerBody { Reject = true };

            try
            {
                if (incoming.Kind == "transfer")
                {
                    var grant = _fileSource?.Invoke(incoming.RemoteNodeId, incoming.File);

                    answer = grant?.Path != null ? new AnswerBody { Path = grant.Path, Hash = grant.Hash } : new AnswerBody { Reject = true, Reason = grant?.Refusal };
                }
                else
                {
                    var reply = _handler?.Invoke(incoming.RemoteNodeId, Convert.FromBase64String(incoming.Body));

                    if (reply != null)
                    {
                        answer = new AnswerBody { Body = Convert.ToBase64String(reply) };
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to handle a request from node {0}", incoming.RemoteNodeId);
            }

            try
            {
                client.PostAsJsonAsync($"/v1/incoming/{incoming.Id}", answer, JsonOptions).GetAwaiter().GetResult().Dispose();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to answer a message from node {0}", incoming.RemoteNodeId);
            }
        }

        private void PushBlocked(HttpClient client)
        {
            List<string> blocked;

            lock (_lock)
            {
                blocked = _blocked.ToList();
            }

            try
            {
                client.PutAsJsonAsync("/v1/blocked", new BlockedBody { NodeIds = blocked }, JsonOptions).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            }
            catch (HttpRequestException ex)
            {
                _logger.Warn(ex, "Unable to give the block list to the peer transport");
            }
        }

        private void Stop()
        {
            var process = _process;

            _client = null;
            _process = null;

            if (process == null)
            {
                return;
            }

            try
            {
                // Closing stdin asks the sidecar to exit. Kill it if it does not.
                process.StandardInput.Close();

                if (!process.WaitForExit(3000))
                {
                    process.Kill(true);
                }
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Unable to stop the peer transport cleanly");
            }
            finally
            {
                process.Dispose();
            }
        }

        private string FindBinary()
        {
            var name = OsInfo.IsWindows ? "nodearr-transport.exe" : "nodearr-transport";
            var candidates = new[]
            {
                _options.BinaryPath,
                Path.Combine(AppContext.BaseDirectory, name)
            };

            return candidates.FirstOrDefault(c => c.IsNotNullOrWhiteSpace() && File.Exists(c));
        }

        private class ReadyLine
        {
            public string Api { get; set; }
            public string NodeId { get; set; }
        }

        private class IdentityResponse
        {
            public string NodeId { get; set; }
            public string RelayUrl { get; set; }
            public List<string> IpAddrs { get; set; }
        }

        private class RequestBody
        {
            public string NodeId { get; set; }
            public string RelayUrl { get; set; }
            public List<string> IpAddrs { get; set; }
            public string Body { get; set; }
            public long TimeoutMs { get; set; }
        }

        private class ProbeBody
        {
            public string NodeId { get; set; }
            public string RelayUrl { get; set; }
            public List<string> IpAddrs { get; set; }
            public long WaitMs { get; set; }
        }

        private class ProbeResponse
        {
            public string Path { get; set; }
            public string Remote { get; set; }
            public RequestError Error { get; set; }
        }

        private class NetworkResponse
        {
            public string RelayUrl { get; set; }
            public List<string> IpAddrs { get; set; }
            public bool? Udp { get; set; }
            public bool? MappingVaries { get; set; }
            public string PublicAddress { get; set; }
            public bool? CaptivePortal { get; set; }
        }

        private class RequestResponse
        {
            public string Body { get; set; }
            public RequestError Error { get; set; }
        }

        private class RequestError
        {
            public string Kind { get; set; }
            public string Message { get; set; }
        }

        private class IncomingMessage
        {
            public long Id { get; set; }
            public string RemoteNodeId { get; set; }

            // "message" or "transfer".
            public string Kind { get; set; }
            public string Body { get; set; }
            public string File { get; set; }
        }

        private class AnswerBody
        {
            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Body { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Path { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public HashedFile Hash { get; set; }
            public bool Reject { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string Reason { get; set; }
        }

        private class StartTransferBody
        {
            public string Id { get; set; }
            public string NodeId { get; set; }
            public string RelayUrl { get; set; }
            public List<string> IpAddrs { get; set; }
            public string File { get; set; }
            public string Dest { get; set; }
            public string ExpectedHash { get; set; }
        }

        private class TransferState
        {
            public string Id { get; set; }
            public string State { get; set; }
            public long BytesDone { get; set; }
            public long Size { get; set; }
            public string Blake3 { get; set; }
            public TransferFailureBody Failure { get; set; }
        }

        private class UploadStateBody
        {
            public string Id { get; set; }
            public string RemoteNodeId { get; set; }
            public string File { get; set; }
            public string Path { get; set; }
            public string State { get; set; }
            public long BytesSent { get; set; }
            public long Size { get; set; }
            public int Attempts { get; set; }
            public TransferFailureBody LastFailure { get; set; }
            public TransferFailureBody Failure { get; set; }
        }

        private class TransferFailureBody
        {
            public string Kind { get; set; }
            public string Message { get; set; }
        }

        private class HashBody
        {
            public string Path { get; set; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public HashedFile Known { get; set; }
        }

        private class StopUploadBody
        {
            public string Reason { get; set; }
        }

        private class DisconnectBody
        {
            public string NodeId { get; set; }
        }

        private class BlockedBody
        {
            public List<string> NodeIds { get; set; }
        }
    }
}
