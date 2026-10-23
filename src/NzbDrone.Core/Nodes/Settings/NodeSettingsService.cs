using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Transfers;

namespace NzbDrone.Core.Nodes.Settings
{
    public interface INodeSettingsService
    {
        // What is wrong with these settings, if anything. Nothing is saved.
        List<ValidationFailure> Validate(NodeSettings settings);

        // Saves what the environment does not fix, and restarts the peer transport if it has to.
        NodeSettingsSaved Save(NodeSettings settings);
    }

    public class NodeSettingsService : INodeSettingsService
    {
        public const int MaxNameLength = 64;
        public const int MinDirectTimeout = 5;
        public const int MaxDirectTimeout = 120;
        public const int MinUploadLimit = 16;
        public const int MinDownloadLimit = 16;

        private static readonly TimeSpan RestartWait = TimeSpan.FromSeconds(20);

        private readonly INodeSettingsProvider _provider;
        private readonly IConfigService _configService;
        private readonly IPeerTransport _transport;
        private readonly ITransferRepository _transfers;
        private readonly IEventAggregator _eventAggregator;
        private readonly IRelayProbe _relayProbe;
        private readonly Logger _logger;

        public NodeSettingsService(INodeSettingsProvider provider, IConfigService configService, IPeerTransport transport, ITransferRepository transfers, IEventAggregator eventAggregator, IRelayProbe relayProbe, Logger logger)
        {
            _provider = provider;
            _configService = configService;
            _transport = transport;
            _transfers = transfers;
            _eventAggregator = eventAggregator;
            _relayProbe = relayProbe;
            _logger = logger;
        }

        public List<ValidationFailure> Validate(NodeSettings settings)
        {
            var current = _provider.Get();
            var fixedNames = _provider.FixedByEnvironment();
            var failures = new List<ValidationFailure>();

            // A setting the environment fixes is not the user's to get wrong.
            void Check(string name, Func<string> problem)
            {
                if (fixedNames.Contains(name))
                {
                    return;
                }

                var message = problem();

                if (message != null)
                {
                    failures.Add(new ValidationFailure(name, message));
                }
            }

            Check(nameof(NodeSettings.NodeName), () =>
            {
                var name = settings.NodeName ?? string.Empty;

                if (name.Length > MaxNameLength)
                {
                    return $"A name can be at most {MaxNameLength} characters";
                }

                return name.Any(char.IsControl) ? "A name cannot contain line breaks or other control characters" : null;
            });

            Check(nameof(NodeSettings.Relay), () => Enum.IsDefined(settings.Relay) ? null : "Choose one of the relay options");

            Check(nameof(NodeSettings.RelayUrl), () =>
            {
                // The relay the environment fixes is the one used, whatever was sent.
                var relay = fixedNames.Contains(nameof(NodeSettings.Relay)) ? current.Relay : settings.Relay;

                if (relay != RelayMode.Custom)
                {
                    return null;
                }

                if (settings.RelayUrl.IsNullOrWhiteSpace())
                {
                    return "A relay of your own needs its address";
                }

                if (!Uri.TryCreate(settings.RelayUrl.Trim(), UriKind.Absolute, out var url) || url.Scheme is not ("http" or "https") || url.Host.Length == 0)
                {
                    return "A relay address looks like https://relay.example.com";
                }

                // Asked only when it is new, so a relay that is down for a moment does not stop the
                // rest of the page being saved.
                var unchanged = current.Relay == RelayMode.Custom && settings.RelayUrl.Trim() == (current.RelayUrl ?? string.Empty).Trim();

                return unchanged ? null : _relayProbe.Check(url);
            });

            Check(nameof(NodeSettings.Port), () =>
            {
                if (settings.Port == 0)
                {
                    return null;
                }

                if (settings.Port is < 1024 or > 65535)
                {
                    return "Use 0 to let the system choose, or a port from 1024 to 65535";
                }

                // The port in use now is taken by this node's own transport, which is about to let go of it.
                return settings.Port != current.Port && !IsUdpPortFree(settings.Port)
                    ? $"UDP port {settings.Port} is already in use on this machine"
                    : null;
            });

            Check(nameof(NodeSettings.DirectTimeoutSeconds), () =>
                settings.DirectTimeoutSeconds is < MinDirectTimeout or > MaxDirectTimeout
                    ? $"Between {MinDirectTimeout} and {MaxDirectTimeout} seconds"
                    : null);

            Check(nameof(NodeSettings.UploadLimitKbps), () =>
                settings.UploadLimitKbps != 0 && settings.UploadLimitKbps < MinUploadLimit
                    ? $"Use 0 for no limit, or at least {MinUploadLimit} KB/s"
                    : null);

            Check(nameof(NodeSettings.DownloadLimitKbps), () =>
                settings.DownloadLimitKbps != 0 && settings.DownloadLimitKbps < MinDownloadLimit
                    ? $"Use 0 for no limit, or at least {MinDownloadLimit} KB/s"
                    : null);

            Check(nameof(NodeSettings.DownloadFolder), () => CheckDownloadFolder(settings.DownloadFolder, current.DownloadFolder));

            // Sonarr's own words for its RSS Sync Interval.
            Check(nameof(NodeSettings.CatalogSyncInterval), () =>
                settings.CatalogSyncInterval == 0 || settings.CatalogSyncInterval is >= NodeSettingsDefaults.MinCatalogSyncInterval and <= NodeSettingsDefaults.MaxCatalogSyncInterval
                    ? null
                    : $"Must be between {NodeSettingsDefaults.MinCatalogSyncInterval} and {NodeSettingsDefaults.MaxCatalogSyncInterval} or 0 to disable");

            return failures;
        }

        public NodeSettingsSaved Save(NodeSettings settings)
        {
            var before = _provider.Get();
            var fixedNames = _provider.FixedByEnvironment();
            var result = new NodeSettingsSaved();

            // Writes one setting, unless the environment fixes it.
            void Write<T>(string name, T was, T wanted, Action<T> write)
            {
                if (EqualityComparer<T>.Default.Equals(was, wanted))
                {
                    return;
                }

                if (fixedNames.Contains(name))
                {
                    result.Ignored.Add(name);

                    return;
                }

                write(wanted);
            }

            Write(nameof(NodeSettings.NodeName), before.NodeName ?? string.Empty, (settings.NodeName ?? string.Empty).Trim(), v => _configService.NodeName = v);
            Write(nameof(NodeSettings.Relay), before.Relay, settings.Relay, v => _configService.TransportRelay = v.ToString().ToLowerInvariant());
            Write(nameof(NodeSettings.RelayUrl), before.RelayUrl ?? string.Empty, (settings.RelayUrl ?? string.Empty).Trim(), v => _configService.TransportRelayUrl = v);
            Write(nameof(NodeSettings.Port), before.Port, settings.Port, v => _configService.TransportPort = v);
            Write(nameof(NodeSettings.PortMapping), before.PortMapping, settings.PortMapping, v => _configService.TransportPortMapping = v);
            Write(nameof(NodeSettings.DirectTimeoutSeconds), before.DirectTimeoutSeconds, settings.DirectTimeoutSeconds, v => _configService.TransferDirectTimeout = v);
            Write(nameof(NodeSettings.DownloadFolder), before.DownloadFolder ?? string.Empty, (settings.DownloadFolder ?? string.Empty).Trim(), v => _configService.TransferDownloadFolder = v);
            Write(nameof(NodeSettings.UploadLimitKbps), before.UploadLimitKbps, settings.UploadLimitKbps, v => _configService.TransferUploadLimit = v);
            Write(nameof(NodeSettings.DownloadLimitKbps), before.DownloadLimitKbps, settings.DownloadLimitKbps, v => _configService.TransferDownloadLimit = v);
            Write(nameof(NodeSettings.CatalogSyncInterval), before.CatalogSyncInterval, settings.CatalogSyncInterval, v => _configService.CatalogSyncInterval = v);

            var after = _provider.Get();

            // The task schedule takes a new interval when told the configuration changed.
            if (before.CatalogSyncInterval != after.CatalogSyncInterval)
            {
                _eventAggregator.PublishEvent(new ConfigSavedEvent());
            }

            result.Settings = after;
            result.TransportRunning = _transport.IsRunning;

            // The name and the download folder are read when they are needed. The rest is given
            // to the transport when it starts, so it has to start again to take them.
            var transportChanged = before.Relay != after.Relay ||
                                   (before.RelayUrl ?? string.Empty) != (after.RelayUrl ?? string.Empty) ||
                                   before.Port != after.Port ||
                                   before.PortMapping != after.PortMapping ||
                                   before.DirectTimeoutSeconds != after.DirectTimeoutSeconds ||
                                   before.UploadLimitKbps != after.UploadLimitKbps ||
                                   before.DownloadLimitKbps != after.DownloadLimitKbps;

            if (transportChanged && _transport.IsRunning)
            {
                _logger.Info("Restarting the peer transport to apply new settings");
                _transport.Restart();

                result.TransportRestarted = true;
                result.TransportRunning = WaitForTransport();

                if (!result.TransportRunning)
                {
                    _logger.Error("The peer transport did not start again with the new settings");
                }
            }

            return result;
        }

        private bool WaitForTransport()
        {
            var deadline = DateTime.UtcNow.Add(RestartWait);

            while (DateTime.UtcNow < deadline)
            {
                if (_transport.IsRunning)
                {
                    return true;
                }

                Thread.Sleep(250);
            }

            return _transport.IsRunning;
        }

        private string CheckDownloadFolder(string wanted, string current)
        {
            wanted = (wanted ?? string.Empty).Trim();

            if (wanted == (current ?? string.Empty).Trim())
            {
                return null;
            }

            // Transfers under way know where they are by this folder. Moving it would strand them.
            if (_transfers.Active().Count > 0)
            {
                return "Wait for the transfers under way to finish, or remove them, before moving the download folder";
            }

            if (wanted.Length == 0)
            {
                return null;
            }

            if (!Path.IsPathFullyQualified(wanted))
            {
                return "Give the whole path, from the root of the disk";
            }

            if (Path.GetPathRoot(wanted) == Path.GetFullPath(wanted))
            {
                return "Choose a folder, not the root of a disk";
            }

            try
            {
                Directory.CreateDirectory(wanted);

                var probe = Path.Combine(wanted, ".nodearr-write-test-" + Guid.NewGuid().ToString("N")[..8]);

                File.WriteAllText(probe, string.Empty);
                File.Delete(probe);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return "Nodearr cannot write to this folder: " + ex.Message;
            }

            return null;
        }

        private static bool IsUdpPortFree(int port)
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

                socket.Bind(new IPEndPoint(IPAddress.Any, port));

                return true;
            }
            catch (SocketException)
            {
                return false;
            }
        }
    }
}
