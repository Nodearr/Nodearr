using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Indexers.Newznab;

namespace NzbDrone.Core.Applications.Sonarr
{
    public class Sonarr : ApplicationBase<SonarrSettings>
    {
        public override string Name => "Sonarr";

        private readonly ICached<List<SonarrIndexer>> _schemaCache;
        private readonly ISonarrV3Proxy _sonarrV3Proxy;
        private readonly IConfigFileProvider _configFileProvider;

        public Sonarr(ICacheManager cacheManager,
                      ISonarrV3Proxy sonarrV3Proxy,
                      IConfigFileProvider configFileProvider,
                      IAppIndexerMapService appIndexerMapService,
                      Logger logger)
            : base(appIndexerMapService, logger)
        {
            _schemaCache = cacheManager.GetCache<List<SonarrIndexer>>(GetType());
            _sonarrV3Proxy = sonarrV3Proxy;
            _configFileProvider = configFileProvider;
        }

        public override ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            try
            {
                failures.AddIfNotNull(_sonarrV3Proxy.TestConnection(Settings));
            }
            catch (HttpException ex)
            {
                switch (ex.Response.StatusCode)
                {
                    case HttpStatusCode.Unauthorized:
                        _logger.Warn(ex, "API Key is invalid");
                        failures.AddIfNotNull(new ValidationFailure("ApiKey", "API Key is invalid"));
                        break;
                    case HttpStatusCode.SeeOther:
                    case HttpStatusCode.TemporaryRedirect:
                        _logger.Warn(ex, "Sonarr returned redirect and is invalid");
                        failures.AddIfNotNull(new ValidationFailure("BaseUrl", "Sonarr URL is invalid, Nodearr cannot connect to Sonarr - are you missing a URL base?"));
                        break;
                    case HttpStatusCode.NotFound:
                        _logger.Warn(ex, "Sonarr not found");
                        failures.AddIfNotNull(new ValidationFailure("BaseUrl", "Sonarr URL is invalid, Nodearr cannot connect to Sonarr. Is Sonarr running and accessible? Sonarr v2 is not supported."));
                        break;
                    default:
                        _logger.Warn(ex, "Unable to complete application test");
                        failures.AddIfNotNull(new ValidationFailure("BaseUrl", $"Unable to complete application test, cannot connect to Sonarr. {ex.Message}"));
                        break;
                }
            }
            catch (JsonReaderException ex)
            {
                _logger.Error(ex, "Unable to parse JSON response from Sonarr");
                failures.AddIfNotNull(new ValidationFailure("BaseUrl", $"Unable to parse JSON response from Sonarr. {ex.Message}"));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to complete application test");
                failures.AddIfNotNull(new ValidationFailure("BaseUrl", $"Unable to complete application test, cannot connect to Sonarr. {ex.Message}"));
            }

            return new ValidationResult(failures);
        }

        public override List<AppIndexerMap> GetIndexerMappings()
        {
            var indexers = _sonarrV3Proxy.GetIndexers(Settings)
                .Where(i => i.Implementation is "Newznab" or "Torznab");

            var mappings = new List<AppIndexerMap>();

            foreach (var indexer in indexers)
            {
                var baseUrl = GetFieldValue<string>(indexer, "baseUrl") ?? string.Empty;
                var apiKey = GetFieldValue<string>(indexer, "apiKey");

                if (!baseUrl.StartsWith(Settings.NodearrUrl.TrimEnd('/')) &&
                    apiKey != _configFileProvider.ApiKey)
                {
                    continue;
                }

                var match = AppIndexerRegex.Match(baseUrl);

                if (match.Groups["indexer"].Success && int.TryParse(match.Groups["indexer"].Value, out var indexerId))
                {
                    mappings.Add(new AppIndexerMap { IndexerId = indexerId, RemoteIndexerId = indexer.Id });
                }
            }

            return mappings;
        }

        public override void AddIndexer(IndexerDefinition indexer)
        {
            if (!ShouldSyncIndexer(indexer))
            {
                _logger.Debug("Skipping add for indexer {0} [{1}] because it is not usable by Sonarr", indexer.Name, indexer.Id);
                return;
            }

            _logger.Trace("Adding indexer {0} [{1}]", indexer.Name, indexer.Id);

            var sonarrIndexer = BuildSonarrIndexer(indexer);
            var remoteIndexer = _sonarrV3Proxy.AddIndexer(sonarrIndexer, Settings);

            if (remoteIndexer == null)
            {
                _logger.Debug("Failed to add {0} [{1}]", indexer.Name, indexer.Id);
                return;
            }

            _appIndexerMapService.Insert(new AppIndexerMap { AppId = Definition.Id, IndexerId = indexer.Id, RemoteIndexerId = remoteIndexer.Id });
        }

        public override void RemoveIndexer(int indexerId)
        {
            var appMappings = _appIndexerMapService.GetMappingsForApp(Definition.Id);
            var indexerMapping = appMappings.FirstOrDefault(m => m.IndexerId == indexerId);

            if (indexerMapping != null)
            {
                _sonarrV3Proxy.RemoveIndexer(indexerMapping.RemoteIndexerId, Settings);
                _appIndexerMapService.Delete(indexerMapping.Id);
            }
        }

        public override void UpdateIndexer(IndexerDefinition indexer, bool forceSync = false)
        {
            _logger.Debug("Updating indexer {0} [{1}]", indexer.Name, indexer.Id);

            var appMappings = _appIndexerMapService.GetMappingsForApp(Definition.Id);
            var indexerMapping = appMappings.FirstOrDefault(m => m.IndexerId == indexer.Id);

            if (indexerMapping == null)
            {
                AddIndexer(indexer);
                return;
            }

            var remoteIndexer = _sonarrV3Proxy.GetIndexer(indexerMapping.RemoteIndexerId, Settings);

            if (remoteIndexer == null)
            {
                _appIndexerMapService.Delete(indexerMapping.Id);

                if (ShouldSyncIndexer(indexer))
                {
                    _logger.Debug("Remote indexer not found, re-adding {0} [{1}] to Sonarr", indexer.Name, indexer.Id);
                    AddIndexer(indexer);
                }

                return;
            }

            _logger.Debug("Remote indexer {0} [{1}] found", remoteIndexer.Name, remoteIndexer.Id);

            if (!ShouldSyncIndexer(indexer))
            {
                _sonarrV3Proxy.RemoveIndexer(remoteIndexer.Id, Settings);
                _appIndexerMapService.Delete(indexerMapping.Id);
                return;
            }

            var sonarrIndexer = BuildSonarrIndexer(indexer, indexerMapping.RemoteIndexerId);

            if (!sonarrIndexer.Equals(remoteIndexer) || forceSync)
            {
                _logger.Debug("Syncing remote indexer with current settings");

                sonarrIndexer.Fields.AddRange(remoteIndexer.Fields.Where(f => sonarrIndexer.Fields.All(s => s.Name != f.Name)));
                sonarrIndexer.Tags.UnionWith(remoteIndexer.Tags ?? new HashSet<int>());
                sonarrIndexer.DownloadClientId = remoteIndexer.DownloadClientId;
                sonarrIndexer.SeasonSearchMaximumSingleEpisodeAge = remoteIndexer.SeasonSearchMaximumSingleEpisodeAge;

                _sonarrV3Proxy.UpdateIndexer(sonarrIndexer, Settings);
            }
        }

        private SonarrIndexer BuildSonarrIndexer(IndexerDefinition indexer, int id = 0)
        {
            var cacheKey = Settings.BaseUrl.TrimEnd('/');
            var schemas = _schemaCache.Get(cacheKey, () => _sonarrV3Proxy.GetIndexerSchema(Settings), TimeSpan.FromDays(7));
            var implementation = indexer.Protocol == DownloadProtocol.Usenet ? "Newznab" : "Torznab";
            var schema = schemas.First(i => i.Implementation == implementation);
            var syncFields = new HashSet<string>
            {
                "baseUrl",
                "apiPath",
                "apiKey",
                "categories",
                "animeCategories",
                "animeStandardFormatSearch",
                "minimumSeeders",
                "seedCriteria.seedRatio",
                "seedCriteria.seedTime",
                "seedCriteria.seasonPackSeedTime",
                "rejectBlocklistedTorrentHashesWhileGrabbing"
            };

            if (id == 0)
            {
                syncFields.Add("additionalParameters");
            }

            var sonarrIndexer = new SonarrIndexer
            {
                Id = id,
                Name = $"{indexer.Name} (Nodearr)",
                EnableRss = indexer.Enable && indexer.EnableRss && indexer.SupportsRss,
                EnableAutomaticSearch = indexer.Enable && indexer.EnableAutomaticSearch && indexer.SupportsSearch,
                EnableInteractiveSearch = indexer.Enable && indexer.EnableInteractiveSearch && indexer.SupportsSearch,
                Priority = indexer.Priority,
                Implementation = implementation,
                ConfigContract = schema.ConfigContract,
                Fields = schema.Fields.Where(x => syncFields.Contains(x.Name)).Select(x => x.Clone()).ToList(),
                Tags = new HashSet<int>()
            };

            SetFieldValue(sonarrIndexer, "baseUrl", $"{Settings.NodearrUrl.TrimEnd('/')}/{indexer.Id}/");
            SetFieldValue(sonarrIndexer, "apiPath", "/api");
            SetFieldValue(sonarrIndexer, "apiKey", _configFileProvider.ApiKey);
            SetFieldValue(sonarrIndexer, "categories", JArray.FromObject(GetSyncCategories(indexer)));
            SetFieldValue(sonarrIndexer, "animeCategories", JArray.FromObject(GetAnimeSyncCategories(indexer)));
            SetFieldValue(sonarrIndexer, "animeStandardFormatSearch", Settings.SyncAnimeStandardFormatSearch);

            if (indexer.Protocol == DownloadProtocol.Torrent && indexer.Settings is ITorrentIndexerSettings torrentSettings)
            {
                SetFieldValue(sonarrIndexer, "minimumSeeders", torrentSettings.MinimumSeeders);
                SetFieldValue(sonarrIndexer, "seedCriteria.seedRatio", torrentSettings.SeedCriteria.SeedRatio);
                SetFieldValue(sonarrIndexer, "seedCriteria.seedTime", torrentSettings.SeedCriteria.SeedTime);
                SetFieldValue(sonarrIndexer, "seedCriteria.seasonPackSeedTime", torrentSettings.SeedCriteria.SeasonPackSeedTime);
                SetFieldValue(sonarrIndexer, "rejectBlocklistedTorrentHashesWhileGrabbing", Settings.SyncRejectBlocklistedTorrentHashesWhileGrabbing && torrentSettings.RejectBlocklistedTorrentHashesWhileGrabbing);
            }

            return sonarrIndexer;
        }

        private bool ShouldSyncIndexer(IndexerDefinition indexer)
        {
            if (!indexer.Enable)
            {
                return false;
            }

            if (indexer.Protocol is not DownloadProtocol.Usenet and not DownloadProtocol.Torrent)
            {
                return false;
            }

            if (!indexer.SupportsSearch && !indexer.SupportsRss)
            {
                return false;
            }

            if (!GetSyncCategories(indexer).Any() && !GetAnimeSyncCategories(indexer).Any())
            {
                return false;
            }

            return true;
        }

        private int[] GetSyncCategories(IndexerDefinition indexer)
        {
            return GetCategories(indexer, settings => settings.Categories, Settings.SyncCategories);
        }

        private int[] GetAnimeSyncCategories(IndexerDefinition indexer)
        {
            return GetCategories(indexer, settings => settings.AnimeCategories, Settings.AnimeSyncCategories);
        }

        private static int[] GetCategories(IndexerDefinition indexer, Func<NewznabSettings, IEnumerable<int>> indexerCategories, IEnumerable<int> appCategories)
        {
            var requestedCategories = appCategories?.ToArray() ?? Array.Empty<int>();

            if (indexer.Settings is NewznabSettings newznabSettings)
            {
                return (indexerCategories(newznabSettings) ?? Array.Empty<int>()).Intersect(requestedCategories).ToArray();
            }

            return requestedCategories;
        }

        private static T GetFieldValue<T>(SonarrIndexer indexer, string fieldName)
        {
            var value = indexer.Fields?.FirstOrDefault(x => x.Name == fieldName)?.Value;

            if (value == null)
            {
                return default(T);
            }

            if (value is JValue jValue)
            {
                return jValue.ToObject<T>();
            }

            return (T)Convert.ChangeType(value, typeof(T));
        }

        private static void SetFieldValue(SonarrIndexer indexer, string fieldName, object value)
        {
            var field = indexer.Fields.FirstOrDefault(x => x.Name == fieldName);

            if (field != null)
            {
                field.Value = value;
            }
        }
    }
}
