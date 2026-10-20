using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.HealthCheck
{
    public class HealthCheck : ModelBase
    {
        // Where to read about what a check found: the troubleshooting page of the user guide on
        // nodearr.com. It was Servarr's wiki, which has no page for Nodearr.
        private const string GuidePage = "https://nodearr.com/guide/troubleshooting/";

        private static readonly Regex CleanFragmentRegex = new Regex("[^a-z ]", RegexOptions.Compiled);

        // The section of that page for each of Nodearr's own checks, by its heading in
        // docs/user-guide/troubleshooting.md. Other checks, inherited from Sonarr, keep the
        // fragment they give, which leads to the top of the page.
        private static readonly Dictionary<HealthCheckReason, string> GuideSections = new()
        {
            [HealthCheckReason.LibraryNotFound] = "#friends-are-refused-your-files",
            [HealthCheckReason.LibraryNotReadable] = "#friends-are-refused-your-files",
            [HealthCheckReason.LibraryFilesMissing] = "#friends-are-refused-your-files",
            [HealthCheckReason.DownloadFolderNotWritable] = "#the-arr-app-does-not-import-what-arrived",
            [HealthCheckReason.DownloadFolderNotShared] = "#the-arr-app-does-not-import-what-arrived",
            [HealthCheckReason.PeerRelayOnly] = "#relay-only",
            [HealthCheckReason.PeerNetworkVaryingPort] = "#relay-only",
            [HealthCheckReason.PeerNetworkNoUdp] = "#a-friend-cannot-be-reached",
            [HealthCheckReason.PeerTransportNotRunning] = "#a-friend-cannot-be-reached",
            [HealthCheckReason.PairAttempts] = "#pairing-keeps-being-turned-away",
        };

        public Type Source { get; set; }
        public HealthCheckResult Type { get; set; }
        public HealthCheckReason Reason { get; set; }
        public string Message { get; set; }
        public HttpUri WikiUrl { get; set; }

        public HealthCheck()
        {
        }

        public HealthCheck(Type source)
        {
            Source = source;
            Type = HealthCheckResult.Ok;
        }

        public HealthCheck(Type source, HealthCheckResult type, HealthCheckReason reason, string message, string wikiFragment = null)
        {
            Source = source;
            Type = type;
            Reason = reason;
            Message = message;
            WikiUrl = MakeWikiUrl(wikiFragment ?? GuideSections.GetValueOrDefault(reason) ?? MakeWikiFragment(message));
        }

        private static string MakeWikiFragment(string message)
        {
            return "#" + CleanFragmentRegex.Replace(message.ToLower(), string.Empty).Replace(' ', '-');
        }

        private static HttpUri MakeWikiUrl(string fragment)
        {
            return new HttpUri(GuidePage) + new HttpUri(fragment);
        }
    }

    public enum HealthCheckResult
    {
        Ok = 0,
        Notice = 1,
        Warning = 2,
        Error = 3
    }

    public enum HealthCheckReason
    {
        AppDataLocation,
        DownloadClientCheckNoneAvailable,
        DownloadClientCheckUnableToCommunicate,
        DownloadClientRemovesCompletedDownloads,
        DownloadClientRootFolder,
        DownloadClientSorting,
        DownloadClientStatusAllClients,
        DownloadClientStatusSingleClient,
        DownloadFolderNotShared,
        DownloadFolderNotWritable,
        ImportListRootFolderMissing,
        ImportListRootFolderMultipleMissing,
        ImportListStatusAllUnavailable,
        ImportListStatusUnavailable,
        ImportMechanismEnableCompletedDownloadHandlingIfPossible,
        ImportMechanismEnableCompletedDownloadHandlingIfPossibleMultiComputer,
        ImportMechanismHandlingDisabled,
        IndexerDownloadClient,
        IndexerJackettAll,
        IndexerLongTermStatusAllUnavailable,
        IndexerLongTermStatusUnavailable,
        IndexerRssNoIndexersAvailable,
        IndexerRssNoIndexersEnabled,
        IndexerSearchNoAutomatic,
        IndexerSearchNoAvailableIndexers,
        IndexerSearchNoInteractive,
        IndexerStatusAllUnavailable,
        IndexerStatusUnavailable,
        LibraryFilesMissing,
        LibraryNotFound,
        LibraryNotReadable,
        MinimumApiKeyLength,
        MountSeries,
        NotificationStatusAll,
        NotificationStatusSingle,
        Package,
        PairAttempts,
        PeerNetworkNoUdp,
        PeerNetworkVaryingPort,
        PeerRelayOnly,
        PeerTransportNotRunning,
        ProxyBadRequest,
        ProxyFailed,
        ProxyResolveIp,
        RecycleBinUnableToWrite,
        RemotePathMappingBadDockerPath,
        RemotePathMappingDockerFolderMissing,
        RemotePathMappingDownloadPermissionsEpisode,
        RemotePathMappingFileRemoved,
        RemotePathMappingFilesBadDockerPath,
        RemotePathMappingFilesGenericPermissions,
        RemotePathMappingFilesLocalWrongOSPath,
        RemotePathMappingFilesWrongOSPath,
        RemotePathMappingFolderPermissions,
        RemotePathMappingGenericPermissions,
        RemotePathMappingImportEpisodeFailed,
        RemotePathMappingLocalFolderMissing,
        RemotePathMappingLocalWrongOSPath,
        RemotePathMappingRemoteDownloadClient,
        RemotePathMappingWrongOSPath,
        RemovedSeriesMultiple,
        RemovedSeriesSingle,
        RootFolderEmpty,
        RootFolderMissing,
        RootFolderMultipleMissing,
        ServerNotification,
        SystemTime,
        UpdateAvailable,
        UpdateStartupNotWritable,
        UpdateStartupTranslocation,
        UpdateUiNotWritable
    }
}
