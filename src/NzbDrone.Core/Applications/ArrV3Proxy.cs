using System;
using System.Net.Http;
using FluentValidation.Results;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Applications
{
    // Where an Arr app is and how to sign in to it.
    public interface IArrConnection
    {
        string BaseUrl { get; }
        string ApiKey { get; }
        string AuthUsername { get; }
        string AuthPassword { get; }

        // /api/v3 for Sonarr and Radarr, /api/v1 for Prowlarr.
        string ApiRoute { get; }
    }

    public interface IArrV3Proxy
    {
        ValidationFailure TestConnection(IArrConnection settings, string appName, Version minimumVersion, Version belowVersion = null);
        T Get<T>(IArrConnection settings, string resource)
            where T : new();
        JArray GetArray(IArrConnection settings, string resource);
        JObject Post(IArrConnection settings, string resource, JObject body);
        JObject Put(IArrConnection settings, string resource, JObject body);

        // A change to many things at once, which the Arr apps answer with a list of them, or nothing.
        void PutMany(IArrConnection settings, string resource, JObject body);
        void Delete(IArrConnection settings, string resource);
    }

    // Sonarr, Radarr and Prowlarr share one API shape for everything Nodearr touches, under their own routes.
    public class ArrV3Proxy : IArrV3Proxy
    {
        private readonly IHttpClient _httpClient;

        public ArrV3Proxy(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public ValidationFailure TestConnection(IArrConnection settings, string appName, Version minimumVersion, Version belowVersion = null)
        {
            var status = JObject.Parse(Execute(settings, "/system/status", HttpMethod.Get, null));
            var reportedName = status.Value<string>("appName");
            var reportedVersion = status.Value<string>("version");

            if (reportedName.IsNotNullOrWhiteSpace() && !reportedName.Equals(appName, StringComparison.OrdinalIgnoreCase))
            {
                return new ValidationFailure("BaseUrl", $"This URL belongs to {reportedName}, not {appName}");
            }

            if (!Version.TryParse(reportedVersion, out var version))
            {
                return new ValidationFailure(string.Empty, $"Failed to parse {appName} version: {reportedVersion}");
            }

            if (version < minimumVersion)
            {
                return new ValidationFailure(string.Empty, $"{appName} version should be at least {minimumVersion.ToString(3)}. Version reported is {reportedVersion}");
            }

            // Whisparr v2 and v3 call themselves the same, and only the version tells them apart.
            if (belowVersion != null && version >= belowVersion)
            {
                return new ValidationFailure(string.Empty, $"This is {appName} {reportedVersion}. Add it as {appName} v{version.Major}.");
            }

            return null;
        }

        public T Get<T>(IArrConnection settings, string resource)
            where T : new()
        {
            return Json.Deserialize<T>(Execute(settings, resource, HttpMethod.Get, null));
        }

        public JArray GetArray(IArrConnection settings, string resource)
        {
            return JArray.Parse(Execute(settings, resource, HttpMethod.Get, null));
        }

        public JObject Post(IArrConnection settings, string resource, JObject body)
        {
            return JObject.Parse(Execute(settings, resource, HttpMethod.Post, body));
        }

        public JObject Put(IArrConnection settings, string resource, JObject body)
        {
            return JObject.Parse(Execute(settings, resource, HttpMethod.Put, body));
        }

        public void PutMany(IArrConnection settings, string resource, JObject body)
        {
            Execute(settings, resource, HttpMethod.Put, body);
        }

        public void Delete(IArrConnection settings, string resource)
        {
            Execute(settings, resource, HttpMethod.Delete, null);
        }

        private string Execute(IArrConnection settings, string resource, HttpMethod method, JObject body)
        {
            var requestBuilder = new HttpRequestBuilder(settings.BaseUrl.TrimEnd('/'))
                .Resource(settings.ApiRoute + resource)
                .Accept(HttpAccept.Json)
                .SetHeader("X-Api-Key", settings.ApiKey);

            if (settings.AuthUsername.IsNotNullOrWhiteSpace() || settings.AuthPassword.IsNotNullOrWhiteSpace())
            {
                requestBuilder.NetworkCredential = new BasicNetworkCredential(settings.AuthUsername, settings.AuthPassword);
            }

            var request = requestBuilder.Build();

            request.Headers.ContentType = "application/json";
            request.Method = method;
            request.AllowAutoRedirect = true;

            if (body != null)
            {
                request.SetContent(body.ToString());
            }

            var response = _httpClient.Execute(request);

            if ((int)response.StatusCode >= 300)
            {
                throw new HttpException(response);
            }

            return response.Content;
        }
    }
}
