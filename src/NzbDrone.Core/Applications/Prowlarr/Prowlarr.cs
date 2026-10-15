using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using FluentValidation.Results;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Applications.Prowlarr
{
    // Prowlarr is downstream of Nodearr (ADR 0007). Nodearr adds itself to it as an indexer, so it
    // can be searched there with the user's other indexers, and reads nothing from it.
    //
    // Prowlarr copies its indexers into every Sonarr and Radarr it manages, as "Nodearr (Prowlarr)".
    // Such a copy is not pinned to Nodearr's download client, so a Nodearr grab through it could go
    // to a real SABnzbd and fail (ADR 0010, finding 6). Nodearr's indexer in Prowlarr therefore uses a
    // sync profile with RSS and both kinds of search off: the copies are there and switched off, and
    // Sonarr and Radarr go on using the indexer Nodearr adds to them directly, pinned to it.
    public class Prowlarr : IApplication
    {
        // The name Nodearr's indexer and sync profile carry inside Prowlarr, as in Sonarr and Radarr.
        public const string RemoteName = "Nodearr";

        private const string GenericNewznab = "Generic Newznab";

        private static readonly Version MinimumVersion = new(1, 0, 0);

        private readonly IArrV3Proxy _proxy;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly Logger _logger;

        public Prowlarr(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
        {
            _proxy = proxy;
            _configFileProvider = configFileProvider;
            _logger = logger;
        }

        public string Name => "Prowlarr";
        public Type ConfigContract => typeof(ProwlarrSettings);
        public ProviderMessage Message => new(
            "Nodearr adds itself to Prowlarr as an indexer, to be searched there with your others. The copies Prowlarr makes of it in Sonarr and Radarr are switched off: add Sonarr and Radarr to Nodearr as well, for Nodearr to work with them.",
            ProviderMessageType.Info);

        public ProviderDefinition Definition { get; set; }
        private ProwlarrSettings Settings => (ProwlarrSettings)Definition.Settings;

        public IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                yield return new ApplicationDefinition
                {
                    SyncLevel = ApplicationSyncLevel.FullSync,
                    Implementation = GetType().Name,
                    Settings = new ProwlarrSettings { NodearrUrl = $"http://localhost:{_configFileProvider.Port}" }
                };
            }
        }

        public ValidationResult Test()
        {
            var failures = new List<ValidationFailure>();

            try
            {
                failures.AddIfNotNull(_proxy.TestConnection(Settings, Name, MinimumVersion));
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Warn(ex, "API Key is invalid");
                failures.Add(new ValidationFailure("ApiKey", "API Key is invalid"));
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.NotFound)
            {
                _logger.Warn(ex, "Prowlarr not found");
                failures.Add(new ValidationFailure("BaseUrl", "Prowlarr URL is invalid, Nodearr cannot connect to Prowlarr. Is it running, and is a URL base missing?"));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to complete application test");
                failures.Add(new ValidationFailure("BaseUrl", $"Unable to complete application test, cannot connect to Prowlarr. {ex.Message}"));
            }

            return new ValidationResult(failures);
        }

        public void SyncRegistration(bool force)
        {
            var profileId = EnsureSyncProfile(force);

            try
            {
                var existing = FindOurIndexer();

                if (existing == null)
                {
                    var indexer = NewIndexer();
                    Apply(indexer, profileId);

                    _logger.Info("Adding Nodearr as indexer to {0}", Definition.Name);
                    _proxy.Post(Settings, "/indexer", indexer);

                    return;
                }

                if (((ApplicationDefinition)Definition).SyncLevel != ApplicationSyncLevel.FullSync)
                {
                    return;
                }

                if (!Apply(existing, profileId) && !force)
                {
                    return;
                }

                _logger.Info("Updating Nodearr's indexer in {0}", Definition.Name);
                _proxy.Put(Settings, $"/indexer/{existing.Value<int>("id")}", existing);
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.BadRequest)
            {
                // Prowlarr asks Nodearr for its capabilities when the indexer is saved, and answers
                // 400 with the reasons when it cannot.
                throw new ApplicationSyncException($"Prowlarr rejected Nodearr as an indexer: {ReadValidationErrors(ex.Response.Content)}", ex);
            }
        }

        public void RemoveRegistration()
        {
            var indexer = FindOurIndexer();

            if (indexer != null)
            {
                _logger.Info("Removing Nodearr as indexer from {0}", Definition.Name);
                _proxy.Delete(Settings, $"/indexer/{indexer.Value<int>("id")}");
            }

            var profile = FindOurSyncProfile();

            if (profile != null)
            {
                try
                {
                    _proxy.Delete(Settings, $"/appprofile/{profile.Value<int>("id")}");
                }
                catch (HttpException ex)
                {
                    // Prowlarr keeps a profile something else still uses. That is the user's to remove.
                    _logger.Debug(ex, "Prowlarr kept the Nodearr sync profile");
                }
            }
        }

        // Prowlarr holds no library. There is nothing to read from it.
        public ApplicationCatalog FetchCatalog()
        {
            return new ApplicationCatalog();
        }

        public ApplicationCatalog FetchTitle(int arrId)
        {
            return new ApplicationCatalog();
        }

        // Prowlarr never imports, so it has no need to see Nodearr's download folder.
        public FolderVisibility LookForFolder(string folder, string marker)
        {
            return new FolderVisibility { Found = true, LookedAt = folder };
        }

        public object RequestAction(string stage, IDictionary<string, string> query)
        {
            return null;
        }

        public override string ToString()
        {
            return GetType().Name;
        }

        private int EnsureSyncProfile(bool force)
        {
            var wanted = new JObject
            {
                ["name"] = RemoteName,
                ["enableRss"] = false,
                ["enableAutomaticSearch"] = false,
                ["enableInteractiveSearch"] = false,
                ["minimumSeeders"] = 1
            };

            var existing = FindOurSyncProfile();

            if (existing == null)
            {
                _logger.Info("Adding the Nodearr sync profile to {0}", Definition.Name);

                return _proxy.Post(Settings, "/appprofile", wanted).Value<int>("id");
            }

            var changed = false;

            foreach (var property in wanted.Properties())
            {
                changed |= !JToken.DeepEquals(existing[property.Name], property.Value);
                existing[property.Name] = property.Value;
            }

            if (changed || force)
            {
                _proxy.Put(Settings, $"/appprofile/{existing.Value<int>("id")}", existing);
            }

            return existing.Value<int>("id");
        }

        private JObject FindOurSyncProfile()
        {
            return _proxy.GetArray(Settings, "/appprofile").OfType<JObject>().FirstOrDefault(p => p.Value<string>("name") == RemoteName);
        }

        private JObject FindOurIndexer()
        {
            return _proxy.GetArray(Settings, "/indexer").OfType<JObject>()
                .FirstOrDefault(i => i.Value<string>("implementation") == "Newznab" && i.Value<string>("name") == RemoteName);
        }

        private JObject NewIndexer()
        {
            var schemas = _proxy.GetArray(Settings, "/indexer/schema").OfType<JObject>().Where(s => s.Value<string>("implementation") == "Newznab").ToList();
            var schema = schemas.FirstOrDefault(s => s.Value<string>("name") == GenericNewznab || s.Value<string>("definitionName") == GenericNewznab) ?? schemas.FirstOrDefault();

            if (schema == null)
            {
                throw new ApplicationSyncException("Prowlarr offers no Generic Newznab indexer to add Nodearr as.", null);
            }

            schema["id"] = 0;
            schema["name"] = RemoteName;
            schema["tags"] = new JArray();

            return schema;
        }

        // Returns whether anything visible changed. Prowlarr masks the API key when read back, so it
        // is always written and never compared.
        private bool Apply(JObject indexer, int profileId)
        {
            var properties = new Dictionary<string, JToken>
            {
                { "enable", true },
                { "appProfileId", profileId }
            };

            var fields = new Dictionary<string, JToken>
            {
                { "baseUrl", Settings.NodearrUrl.TrimEnd('/') + "/newznab" },
                { "apiPath", "/api" }
            };

            var changed = false;

            foreach (var property in properties)
            {
                changed |= !JToken.DeepEquals(indexer[property.Key], property.Value);
                indexer[property.Key] = property.Value;
            }

            var indexerFields = indexer["fields"]?.OfType<JObject>().ToList() ?? new List<JObject>();

            foreach (var field in fields)
            {
                var target = indexerFields.FirstOrDefault(f => f.Value<string>("name") == field.Key);

                if (target != null)
                {
                    changed |= !JToken.DeepEquals(target["value"], field.Value);
                    target["value"] = field.Value;
                }
            }

            var apiKey = indexerFields.FirstOrDefault(f => f.Value<string>("name") == "apiKey");

            if (apiKey != null)
            {
                apiKey["value"] = _configFileProvider.ApiKey;
            }

            return changed;
        }

        private static string ReadValidationErrors(string content)
        {
            try
            {
                var messages = JArray.Parse(content).Select(e => e.Value<string>("errorMessage")).Where(m => m.IsNotNullOrWhiteSpace());

                return string.Join(" ", messages);
            }
            catch (JsonException)
            {
                return content;
            }
        }
    }
}
