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

namespace NzbDrone.Core.Applications
{
    public abstract class ApplicationBase<TSettings> : IApplication
        where TSettings : ArrApplicationSettings, new()
    {
        // The name Nodearr's download client and indexer carry inside the Arr app.
        public const string RemoteName = "Nodearr";

        private const int AnimeCategory = 5070;

        private static readonly HashSet<string> SecretFields = new() { "apiKey" };

        protected readonly IArrV3Proxy _proxy;
        protected readonly IConfigFileProvider _configFileProvider;
        protected readonly Logger _logger;

        protected ApplicationBase(IArrV3Proxy proxy, IConfigFileProvider configFileProvider, Logger logger)
        {
            _proxy = proxy;
            _configFileProvider = configFileProvider;
            _logger = logger;
        }

        public abstract string Name { get; }
        protected abstract Version MinimumVersion { get; }

        // The download client field that holds the category: tvCategory in Sonarr, movieCategory in Radarr.
        protected abstract string CategoryField { get; }

        public abstract ApplicationCatalog FetchCatalog();

        public Type ConfigContract => typeof(TSettings);
        public virtual ProviderMessage Message => null;
        public ProviderDefinition Definition { get; set; }
        protected TSettings Settings => (TSettings)Definition.Settings;

        public virtual IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                yield return new ApplicationDefinition
                {
                    SyncLevel = ApplicationSyncLevel.FullSync,
                    Implementation = GetType().Name,
                    Settings = new TSettings()
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
                _logger.Warn(ex, "{0} not found", Name);
                failures.Add(new ValidationFailure("BaseUrl", $"{Name} URL is invalid, Nodearr cannot connect to {Name}. Is it running, and is a URL base missing?"));
            }
            catch (JsonException ex)
            {
                _logger.Warn(ex, "Unable to parse the response from {0}", Name);
                failures.Add(new ValidationFailure("BaseUrl", $"Unable to parse the response from {Name}. {ex.Message}"));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Unable to complete application test");
                failures.Add(new ValidationFailure("BaseUrl", $"Unable to complete application test, cannot connect to {Name}. {ex.Message}"));
            }

            return new ValidationResult(failures);
        }

        public void SyncRegistration(bool force)
        {
            var nodearr = new Uri(Settings.NodearrUrl.TrimEnd('/') + "/");
            var apiKey = _configFileProvider.ApiKey;

            var downloadClient = EnsureProvider("download client",
                "/downloadclient",
                "Sabnzbd",
                new Dictionary<string, JToken>
                {
                    { "host", nodearr.Host },
                    { "port", nodearr.Port },
                    { "useSsl", nodearr.Scheme == Uri.UriSchemeHttps },
                    { "urlBase", (nodearr.AbsolutePath.Trim('/') + "/sabnzbd").Trim('/') },
                    { "apiKey", apiKey },
                    { CategoryField, Settings.DownloadCategory }
                },
                new Dictionary<string, JToken>
                {
                    { "enable", true },
                    { "removeCompletedDownloads", true },
                    { "removeFailedDownloads", true }
                },
                force);

            // Pinning matters: a Nodearr NZB is a ticket only Nodearr can redeem.
            EnsureProvider("indexer",
                "/indexer",
                "Newznab",
                new Dictionary<string, JToken>
                {
                    { "baseUrl", Settings.NodearrUrl.TrimEnd('/') + "/newznab" },
                    { "apiPath", "/api" },
                    { "apiKey", apiKey },
                    { "categories", new JArray(Settings.SyncCategories) },

                    // Sonarr only. It lets anime be found by ID, season and episode like everything
                    // else, which is the one way Nodearr matches. Radarr has no such fields.
                    { "animeCategories", new JArray(AnimeCategory) },
                    { "animeStandardFormatSearch", true }
                },
                new Dictionary<string, JToken>
                {
                    { "enableRss", false },
                    { "enableAutomaticSearch", true },
                    { "enableInteractiveSearch", true },
                    { "downloadClientId", downloadClient.Value<int>("id") }
                },
                force);
        }

        public FolderVisibility LookForFolder(string folder, string marker)
        {
            var nodearrHost = new Uri(Settings.NodearrUrl.TrimEnd('/') + "/").Host;
            var normalized = WithForwardSlashes(folder);
            var asTheAppSeesIt = folder;

            // The app's own remote path mappings say where it finds this download client's folders.
            foreach (var mapping in _proxy.GetArray(Settings, "/remotepathmapping").OfType<JObject>())
            {
                var remote = mapping.Value<string>("remotePath");
                var local = mapping.Value<string>("localPath");

                if (!string.Equals(mapping.Value<string>("host"), nodearrHost, StringComparison.OrdinalIgnoreCase) || remote.IsNullOrWhiteSpace() || local.IsNullOrWhiteSpace())
                {
                    continue;
                }

                if (normalized.StartsWith(WithForwardSlashes(remote), StringComparison.OrdinalIgnoreCase))
                {
                    asTheAppSeesIt = WithForwardSlashes(local) + normalized[WithForwardSlashes(remote).Length..];

                    break;
                }
            }

            var listing = _proxy.Get<JObject>(Settings, $"/filesystem?path={Uri.EscapeDataString(asTheAppSeesIt)}&includeFiles=false&allowFoldersWithoutTrailingSlashes=true");
            var found = listing["directories"]?.OfType<JObject>().Any(d => d.Value<string>("name") == marker) ?? false;

            return new FolderVisibility { Found = found, LookedAt = asTheAppSeesIt };
        }

        // One spelling for a folder, whichever system wrote it down.
        private static string WithForwardSlashes(string path)
        {
            return path.Replace('\\', '/').TrimEnd('/') + "/";
        }

        private JObject EnsureProvider(string what, string resource, string implementation, Dictionary<string, JToken> fields, Dictionary<string, JToken> properties, bool force)
        {
            bool IsOurs(JObject provider) => provider.Value<string>("implementation") == implementation && provider.Value<string>("name") == RemoteName;

            try
            {
                var existing = _proxy.GetArray(Settings, resource).OfType<JObject>().FirstOrDefault(IsOurs);

                if (existing == null)
                {
                    var schema = _proxy.GetArray(Settings, resource + "/schema").OfType<JObject>().First(p => p.Value<string>("implementation") == implementation);

                    schema["name"] = RemoteName;
                    Apply(schema, fields, properties);
                    _logger.Info("Adding Nodearr as {0} to {1}", what, Definition.Name);

                    return _proxy.Post(Settings, resource, schema);
                }

                if (((ApplicationDefinition)Definition).SyncLevel != ApplicationSyncLevel.FullSync)
                {
                    return existing;
                }

                if (!Apply(existing, fields, properties) && !force)
                {
                    return existing;
                }

                _logger.Info("Updating Nodearr's {0} in {1}", what, Definition.Name);

                return _proxy.Put(Settings, $"{resource}/{existing.Value<int>("id")}", existing);
            }
            catch (HttpException ex) when (ex.Response.StatusCode == HttpStatusCode.BadRequest)
            {
                // Arr tests a provider when it is saved and answers 400 with the reasons.
                throw new ApplicationSyncException($"{Name} rejected Nodearr as its {what}: {ReadValidationErrors(ex.Response.Content)}", ex);
            }
        }

        // Returns whether anything visible changed. Arr masks secrets when read back, so they are
        // always written and never compared.
        private static bool Apply(JObject provider, Dictionary<string, JToken> fields, Dictionary<string, JToken> properties)
        {
            var changed = false;

            foreach (var property in properties)
            {
                changed |= !JToken.DeepEquals(provider[property.Key], property.Value);
                provider[property.Key] = property.Value;
            }

            var providerFields = provider["fields"]?.OfType<JObject>().ToList() ?? new List<JObject>();

            foreach (var field in fields)
            {
                var target = providerFields.FirstOrDefault(f => f.Value<string>("name") == field.Key);

                if (target == null)
                {
                    continue;
                }

                if (!SecretFields.Contains(field.Key))
                {
                    changed |= !JToken.DeepEquals(target["value"], field.Value);
                }

                target["value"] = field.Value;
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

        public virtual object RequestAction(string action, IDictionary<string, string> query)
        {
            return null;
        }

        public override string ToString()
        {
            return GetType().Name;
        }
    }
}
