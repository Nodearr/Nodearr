using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Update.Commands;
using NzbDrone.Core.Update.Events;

namespace NzbDrone.Core.Update
{
    public enum UpdateCheckOutcome
    {
        // No check has been made since Nodearr started.
        NotChecked,

        // Check for updates, or Contact nodearr.com, is off.
        Off,

        // Checked: the signature is good, and the manifest says what it says.
        Checked,

        // The manifest could not be fetched.
        Unavailable,

        // The manifest is not signed by the key Nodearr trusts. Nothing in it is used.
        BadSignature
    }

    public class UpdateCheckStatus
    {
        public UpdateCheckOutcome Outcome { get; set; }
        public DateTime? LastChecked { get; set; }

        // The newer release the last good manifest named, if it named one this node may take.
        public UpdatePackage Available { get; set; }

        // When this node first heard of that release.
        public DateTime? AvailableSince { get; set; }
    }

    public interface ICheckUpdateService
    {
        // Asks updates.nodearr.com, if allowed to.
        UpdateCheckStatus Check();

        // What the last check found, without asking again.
        UpdateCheckStatus Status();

        // The newer release, or null.
        UpdatePackage AvailableUpdate();
    }

    public class CheckUpdateService : ICheckUpdateService, IExecute<ApplicationUpdateCheckCommand>, IHandleAsync<ApplicationStartedEvent>
    {
        private const string AvailableVersionKey = "updateavailableversion";
        private const string AvailableSinceKey = "updateavailablesince";
        private const string RolloutKey = "updaterolloutbucket";

        private readonly IUpdatePackageProvider _packageProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IConfigRepository _configRepository;
        private readonly IManageCommandQueue _commandQueue;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private readonly object _sync = new object();

        // One check at a time: two at once would both write what they found.
        private readonly object _checking = new object();

        private UpdateCheckStatus _status = new UpdateCheckStatus { Outcome = UpdateCheckOutcome.NotChecked };

        public CheckUpdateService(IUpdatePackageProvider packageProvider,
                                  IConfigFileProvider configFileProvider,
                                  IConfigRepository configRepository,
                                  IManageCommandQueue commandQueue,
                                  IEventAggregator eventAggregator,
                                  Logger logger)
        {
            _packageProvider = packageProvider;
            _configFileProvider = configFileProvider;
            _configRepository = configRepository;
            _commandQueue = commandQueue;
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        // The version running, which a manifest must be newer than.
        public static SemanticVersion CurrentVersion => SemanticVersion.TryParse(BuildInfo.NodearrVersion, out var version) ? version : SemanticVersion.Parse(BuildInfo.Version.ToString(3));

        // The package for this platform, by the runtime identifier Nodearr was built for.
        public static string Platform => RuntimeInformation.RuntimeIdentifier;

        public UpdateCheckStatus Status()
        {
            lock (_sync)
            {
                return _status;
            }
        }

        public UpdatePackage AvailableUpdate()
        {
            return Status().Available;
        }

        public UpdateCheckStatus Check()
        {
            lock (_checking)
            {
                return CheckOnce();
            }
        }

        private UpdateCheckStatus CheckOnce()
        {
            UpdateCheckStatus status;

            if (!_configFileProvider.ContactNodearr || !_configFileProvider.UpdateCheck)
            {
                _logger.Debug("Not checking for updates: {0} is off", _configFileProvider.ContactNodearr ? "Check for updates" : "Contact nodearr.com");
                status = new UpdateCheckStatus { Outcome = UpdateCheckOutcome.Off };
            }
            else
            {
                status = Evaluate(_packageProvider.Fetch(_configFileProvider.Branch), DateTime.UtcNow);
            }

            lock (_sync)
            {
                _status = status;
            }

            _eventAggregator.PublishEvent(new UpdateCheckedEvent(status));

            return status;
        }

        public void Execute(ApplicationUpdateCheckCommand message)
        {
            var status = Check();

            // Installing, or waiting for a quiet moment to, is the installer's to decide.
            if (status.Available != null)
            {
                _commandQueue.Push(new ApplicationUpdateCommand(), trigger: CommandTrigger.Scheduled);
            }
        }

        // A check soon after start, so the Updates page has something to say before the first
        // 6 hours are up. Queued once start-up is over: while it runs, the command queue puts back
        // whatever queued commands it finds stored, which would run this one twice.
        public void HandleAsync(ApplicationStartedEvent message)
        {
            _commandQueue.Push(new ApplicationUpdateCheckCommand(), trigger: CommandTrigger.Scheduled);
        }

        public UpdateCheckStatus Evaluate(UpdateManifestResult result, DateTime now)
        {
            var status = new UpdateCheckStatus { LastChecked = now };

            switch (result.Outcome)
            {
                case UpdateManifestOutcome.Unavailable:
                    status.Outcome = UpdateCheckOutcome.Unavailable;

                    // What was known before still stands: a release does not go away because the
                    // manifest could not be reached this time.
                    lock (_sync)
                    {
                        status.Available = _status.Available;
                        status.AvailableSince = _status.AvailableSince;
                    }

                    return status;

                case UpdateManifestOutcome.BadSignature:
                    status.Outcome = UpdateCheckOutcome.BadSignature;
                    return status;
            }

            status.Outcome = UpdateCheckOutcome.Checked;

            var manifest = result.Manifest;
            var current = CurrentVersion;

            // A stale or replayed manifest cannot take a node back.
            if (manifest.Version <= current)
            {
                _logger.Debug("No update: the manifest names {0}, and this is {1}", manifest.Version, current);
                ForgetAvailable();
                return status;
            }

            if (manifest.Rollout < 100 && RolloutBucket() > manifest.Rollout)
            {
                _logger.Debug("Update {0} is going to {1}% of nodes so far, and not yet to this one", manifest.Version, manifest.Rollout);
                ForgetAvailable();
                return status;
            }

            status.Available = ToPackage(manifest);
            status.AvailableSince = AvailableSince(manifest.Version, now);

            _logger.Info("Nodearr {0} is available. This is {1}.", manifest.Version, current);

            return status;
        }

        private static UpdatePackage ToPackage(UpdateManifest manifest)
        {
            var package = new UpdatePackage
            {
                Version = manifest.Version.ToVersion(),
                SemanticVersion = manifest.Version,
                ReleaseDate = manifest.Released,
                Branch = manifest.Branch,
                Changelog = manifest.Changelog,
                Summary = manifest.Summary,
                Changes = manifest.Changes,
                Image = manifest.Image
            };

            if (manifest.Packages.TryGetValue(Platform, out var file))
            {
                package.FileName = file.File;
                package.Url = file.Url;
                package.Size = file.Size;
                package.Hash = file.Sha256;
            }

            return package;
        }

        // A number from 1 to 100, picked once and kept on this node. Never sent anywhere.
        private int RolloutBucket()
        {
            var stored = _configRepository.Get(RolloutKey)?.Value;

            if (int.TryParse(stored, out var bucket) && bucket >= 1 && bucket <= 100)
            {
                return bucket;
            }

            bucket = RandomNumberGenerator.GetInt32(1, 101);
            _configRepository.Upsert(RolloutKey, bucket.ToString(CultureInfo.InvariantCulture));

            return bucket;
        }

        private DateTime AvailableSince(SemanticVersion version, DateTime now)
        {
            var storedVersion = _configRepository.Get(AvailableVersionKey)?.Value;
            var storedSince = _configRepository.Get(AvailableSinceKey)?.Value;

            if (storedVersion == version.ToString() &&
                DateTime.TryParse(storedSince, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var since))
            {
                return since;
            }

            _configRepository.Upsert(AvailableVersionKey, version.ToString());
            _configRepository.Upsert(AvailableSinceKey, now.ToString("O", CultureInfo.InvariantCulture));

            return now;
        }

        private void ForgetAvailable()
        {
            if (_configRepository.Get(AvailableVersionKey) != null)
            {
                _configRepository.Upsert(AvailableVersionKey, string.Empty);
                _configRepository.Upsert(AvailableSinceKey, string.Empty);
            }
        }
    }
}
