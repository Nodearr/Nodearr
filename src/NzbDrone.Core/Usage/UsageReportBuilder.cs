using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Applications;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Nodes.Settings;
using NzbDrone.Core.Nodes.Transport;
using NzbDrone.Core.Sharing;
using NzbDrone.Core.Statistics;
using NzbDrone.Core.Transfers;
using NzbDrone.Core.Update;

namespace NzbDrone.Core.Usage
{
    public interface IUsageReportBuilder
    {
        // The report as it would be sent at that moment, UTC.
        UsageReport Build(DateTime now);
    }

    // Builds the usage report from what this node keeps anyway: the statistics roll-up (#132), the
    // trusted friends, the sharing rules and the settings. It reads nothing it would not otherwise,
    // apart from each Arr app's version, which it asks the app for.
    public class UsageReportBuilder : IUsageReportBuilder
    {
        public const string IdDayKey = "usagereportday";
        public const string IdKey = "usagereportid";

        public static readonly TimeSpan Week = TimeSpan.FromDays(7);
        public static readonly TimeSpan ActiveWithin = TimeSpan.FromDays(30);

        // Why a transfer failed, by the names the report may carry: TransferFailure for what was
        // fetched, and the transport's words for what was sent. Anything else is Other.
        public static readonly HashSet<string> FailureReasons = new(
            Enum.GetNames<TransferFailure>().Where(n => n != nameof(TransferFailure.None)).Concat(new[] { "Stopped", "Other" }),
            StringComparer.Ordinal);

        private static readonly Regex LanguagePattern = new(@"^[a-z]{2,3}(-[A-Z]{2})?$", RegexOptions.Compiled);

        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigRepository _configRepository;
        private readonly IMainDatabase _database;
        private readonly IRuntimeInfo _runtimeInfo;
        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IDiskProvider _diskProvider;
        private readonly IDeploymentInfoProvider _deploymentInfo;
        private readonly ITransferStatisticRepository _transferStatistics;
        private readonly IConnectionStatisticRepository _connectionStatistics;
        private readonly ITransferRepository _transfers;
        private readonly IApplicationFactory _applications;
        private readonly IArrV3Proxy _arrProxy;
        private readonly IPeerRepository _peers;
        private readonly IStatisticsTitles _titles;
        private readonly ISharingService _sharing;
        private readonly ISentTitleRepository _sent;
        private readonly INodeSettingsProvider _nodeSettings;
        private readonly IPeerTransport _transport;
        private readonly IContainerEnvironment _container;
        private readonly ILocalizationService _localization;
        private readonly ICached<string> _appVersions;
        private readonly Logger _logger;
        private readonly object _idLock = new();

        public UsageReportBuilder(IConfigFileProvider configFileProvider,
                                  IConfigRepository configRepository,
                                  IMainDatabase database,
                                  IRuntimeInfo runtimeInfo,
                                  IAppFolderInfo appFolderInfo,
                                  IDiskProvider diskProvider,
                                  IDeploymentInfoProvider deploymentInfo,
                                  ITransferStatisticRepository transferStatistics,
                                  IConnectionStatisticRepository connectionStatistics,
                                  ITransferRepository transfers,
                                  IApplicationFactory applications,
                                  IArrV3Proxy arrProxy,
                                  IPeerRepository peers,
                                  IStatisticsTitles titles,
                                  ISharingService sharing,
                                  ISentTitleRepository sent,
                                  INodeSettingsProvider nodeSettings,
                                  IPeerTransport transport,
                                  IContainerEnvironment container,
                                  ILocalizationService localization,
                                  ICacheManager cacheManager,
                                  Logger logger)
        {
            _configFileProvider = configFileProvider;
            _configRepository = configRepository;
            _database = database;
            _runtimeInfo = runtimeInfo;
            _appFolderInfo = appFolderInfo;
            _diskProvider = diskProvider;
            _deploymentInfo = deploymentInfo;
            _transferStatistics = transferStatistics;
            _connectionStatistics = connectionStatistics;
            _transfers = transfers;
            _applications = applications;
            _arrProxy = arrProxy;
            _peers = peers;
            _titles = titles;
            _sharing = sharing;
            _sent = sent;
            _nodeSettings = nodeSettings;
            _transport = transport;
            _container = container;
            _localization = localization;
            _appVersions = cacheManager.GetCache<string>(GetType(), "appVersions");
            _logger = logger;
        }

        // For testing: the runtime identifier and .NET version, which are otherwise this process's.
        public string Platform { get; set; } = RuntimeInformation.RuntimeIdentifier;
        public string RuntimeVersion { get; set; } = Environment.Version.ToString();

        public static JsonSerializerOptions JsonOptions { get; } = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            WriteIndented = true,

            // The same bytes on every platform.
            NewLine = "\n"
        };

        public static string ToJson(UsageReport report)
        {
            return JsonSerializer.Serialize(report, JsonOptions);
        }

        public UsageReport Build(DateTime now)
        {
            var trusted = _peers.All().Where(p => p.State == PeerState.Trusted).ToList();
            var applications = _applications.All().Where(a => a.Enable).ToList();
            var settings = _nodeSettings.Get();

            var report = new UsageReport
            {
                Id = IdFor(now),
                Nodearr = Nodearr(),
                Active = IsActive(now),
                Apps = applications.Select(App).OrderBy(a => a.Kind, StringComparer.Ordinal).ThenBy(a => a.Version, StringComparer.Ordinal).ToList(),
                Friends = UsageBuckets.Friends(trusted.Count),
                SharedTitles = SharedTitles(applications),
                Past7Days = PastWeek(now),
                Network = Network(now, trusted, settings),
                Features = Features(trusted, applications, settings),
                Language = Language()
            };

            return report;
        }

        // A random ID kept for the UTC day it was made on, and replaced by a new one on the next.
        // It is kept in the database only so that a restart the same day doesn't count twice.
        public string IdFor(DateTime now)
        {
            var day = now.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            lock (_idLock)
            {
                var storedId = _configRepository.Get(IdKey)?.Value;

                if (_configRepository.Get(IdDayKey)?.Value == day && Guid.TryParse(storedId, out var id))
                {
                    return id.ToString("D");
                }

                var newId = Guid.NewGuid().ToString("D");

                _configRepository.Upsert(IdKey, newId);
                _configRepository.Upsert(IdDayKey, day);

                return newId;
            }
        }

        private UsageNodearr Nodearr()
        {
            return new UsageNodearr
            {
                Version = BuildInfo.NodearrVersion,

                // The branch is free text in config.xml. Anything but the one Nodearr publishes is
                // only "other", so a name someone gave their own can't be sent.
                Branch = _configFileProvider.Branch == "main" ? "main" : "other",
                Platform = Platform,
                Runtime = RuntimeVersion,
                Database = _database.DatabaseType == DatabaseType.PostgreSQL ? "postgres" : "sqlite",
                Install = Install()
            };
        }

        public string Install()
        {
            if (_deploymentInfo.PackageUpdateMechanism == UpdateMechanism.Docker || _configFileProvider.UpdateMechanism == UpdateMechanism.Docker)
            {
                return "docker";
            }

            if (_deploymentInfo.IsExternalUpdateMechanism)
            {
                return "package";
            }

            var startupFolder = _appFolderInfo.StartUpFolder;

            if (startupFolder.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                             .Any(part => part.Equals("_output", StringComparison.OrdinalIgnoreCase)))
            {
                return "development";
            }

            if (OsInfo.IsWindows)
            {
                // The Windows installer leaves its uninstaller beside the program.
                if (_diskProvider.FileExists(Path.Combine(startupFolder, "unins000.exe")))
                {
                    return _runtimeInfo.IsWindowsService ? "installerService" : "installerTray";
                }

                return "zip";
            }

            return "tarball";
        }

        // Moved a file or answered a grab in the past 30 days, as Sonarr's "active" is whether
        // anything was added to its history in the past month.
        private bool IsActive(DateTime now)
        {
            if (_transferStatistics.Between(StatisticsHours.HourOf(now - ActiveWithin), now).Any(s => s.Transfers > 0))
            {
                return true;
            }

            return _transfers.Active().Count > 0;
        }

        private UsageApp App(ApplicationDefinition definition)
        {
            return new UsageApp
            {
                Kind = Kind(definition.Implementation),
                Version = AppVersion(definition)
            };
        }

        public static string Kind(string implementation)
        {
            return implementation switch
            {
                "Sonarr" => "sonarr",
                "Radarr" => "radarr",
                "Lidarr" => "lidarr",
                "WhisparrV2" or "WhisparrV3" or "Whisparr" => "whisparr",
                "Prowlarr" => "prowlarr",
                _ => "other"
            };
        }

        // Major and minor only. Asked once an hour at most, so showing the report and sending it
        // soon after don't ask twice.
        private string AppVersion(ApplicationDefinition definition)
        {
            if (definition.Settings is not IArrConnection connection)
            {
                return "unknown";
            }

            return _appVersions.Get($"{definition.Id}|{connection.BaseUrl}", () => AskVersion(definition.Name, connection), TimeSpan.FromHours(1));
        }

        private string AskVersion(string name, IArrConnection connection)
        {
            try
            {
                var version = _arrProxy.GetVersion(connection);

                return version == null ? "unknown" : $"{version.Major}.{Math.Max(0, version.Minor)}";
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not ask {0} for its version for the usage report", name);

                return "unknown";
            }
        }

        private SortedDictionary<string, string> SharedTitles(List<ApplicationDefinition> applications)
        {
            var shared = _titles.SharedTitles();

            return new SortedDictionary<string, string>(
                applications
                    .Select(a => (Kind: Kind(a.Implementation), a.Id))
                    .Where(a => a.Kind is "sonarr" or "radarr" or "lidarr" or "whisparr")
                    .GroupBy(a => a.Kind)
                    .ToDictionary(g => g.Key, g => UsageBuckets.Titles(g.Sum(a => (long)shared.GetValueOrDefault(a.Id)))),
                StringComparer.Ordinal);
        }

        private UsagePastWeek PastWeek(DateTime now)
        {
            var rows = _transferStatistics.Between(StatisticsHours.HourOf(now - Week), now);
            var from = now - Week;

            return new UsagePastWeek
            {
                Sent = Direction(rows.Where(r => r.Direction == TransferDirection.Sent)),
                Fetched = Direction(rows.Where(r => r.Direction == TransferDirection.Fetched)),
                Sends = UsageBuckets.Count(_sent.All().Count(s => s.Created >= from && s.Created <= now))
            };
        }

        private static UsageDirection Direction(IEnumerable<TransferStatistic> rows)
        {
            var list = rows.ToList();

            return new UsageDirection
            {
                Finished = UsageBuckets.Count(list.Where(r => r.IsFinished).Sum(r => (long)r.Counted)),
                Failed = UsageBuckets.Count(list.Where(r => !r.IsFinished).Sum(r => (long)r.Counted)),
                Data = UsageBuckets.Data(list.Sum(r => r.Bytes)),
                Failures = new SortedDictionary<string, string>(
                    list.Where(r => !r.IsFinished)
                        .GroupBy(r => FailureReasons.Contains(r.Failure) ? r.Failure : "Other")
                        .Select(g => (Reason: g.Key, Count: g.Sum(r => (long)r.Counted)))
                        .Where(g => g.Count > 0)
                        .ToDictionary(g => g.Reason, g => UsageBuckets.Count(g.Count)),
                    StringComparer.Ordinal)
            };
        }

        private UsageNetwork Network(DateTime now, List<Peer> trusted, NodeSettings settings)
        {
            var friendIds = trusted.Select(p => p.NodeId).ToHashSet(StringComparer.Ordinal);
            var checks = _connectionStatistics.Between(StatisticsHours.HourOf(now - Week), now)
                .Where(c => friendIds.Contains(c.PeerNodeId))
                .GroupBy(c => c.PeerNodeId)
                .Select(g => (Direct: g.Sum(c => c.Direct), RelayOnly: g.Sum(c => c.RelayOnly), None: g.Sum(c => c.None)))
                .ToList();

            NetworkStatus status = null;

            try
            {
                status = _transport.GetNetworkStatus();
            }
            catch (TransportException)
            {
                // Not running, so nothing is known about the network.
            }

            return new UsageNetwork
            {
                FriendsDirect = UsageBuckets.Friends(checks.Count(c => c.Direct > 0)),
                FriendsRelayOnly = UsageBuckets.Friends(checks.Count(c => c.Direct == 0 && c.RelayOnly > 0)),
                FriendsUnreachable = UsageBuckets.Friends(checks.Count(c => c.Direct == 0 && c.RelayOnly == 0 && c.None > 0)),
                PortVaries = status?.MappingVaries,
                Udp = status?.Udp,
                Relay = settings.Relay switch
                {
                    RelayMode.Custom => "own",
                    RelayMode.Disabled => "off",
                    _ => "n0"
                },
                DockerDesktop = _container.IsDockerDesktop
            };
        }

        private UsageFeatures Features(List<Peer> trusted, List<ApplicationDefinition> applications, NodeSettings settings)
        {
            var lists = _sharing.All();

            return new UsageFeatures
            {
                ShareLists = lists.Any(l => !l.List.IsHidden && l.Items.Count > 0 && l.PeerIds.Count > 0),
                HiddenTitles = lists.Any(l => l.List.IsHidden && l.Items.Count > 0),
                ShareEverything = trusted.Any(p => p.ShareAll),
                FetchAutomatically = applications.Any(a => a.Settings is ArrApplicationSettings arr && arr.FetchNewFilesAutomatically != false),
                UploadLimit = settings.UploadLimitKbps > 0,
                TotalUploadLimit = settings.TotalUploadLimitKbps > 0,
                DownloadLimit = settings.DownloadLimitKbps > 0,
                MaxUploads = settings.MaxUploads switch
                {
                    <= 0 => "none",
                    NodeSettingsDefaults.MaxUploads => "default",
                    _ => "other"
                }
            };
        }

        private string Language()
        {
            var language = _localization.GetLanguageIdentifier();

            return language != null && LanguagePattern.IsMatch(language) ? language : "other";
        }
    }
}
