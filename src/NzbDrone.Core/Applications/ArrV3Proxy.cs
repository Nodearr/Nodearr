using System;
using System.Net.Http;
using FluentValidation.Results;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Applications
{
    public interface IArrV3Proxy
    {
        ValidationFailure TestConnection(ArrApplicationSettings settings, string appName, Version minimumVersion);
        T Get<T>(ArrApplicationSettings settings, string resource)
            where T : new();
        JArray GetArray(ArrApplicationSettings settings, string resource);
        JObject Post(ArrApplicationSettings settings, string resource, JObject body);
        JObject Put(ArrApplicationSettings settings, string resource, JObject body);
        void Delete(ArrApplicationSettings settings, string resource);
    }

    // Sonarr and Radarr share the same v3 API shape for everything Nodearr touches.
    public class ArrV3Proxy : IArrV3Proxy
    {
        private const string ApiRoute = "/api/v3";

        private readonly IHttpClient _httpClient;

        public ArrV3Proxy(IHttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public ValidationFailure TestConnection(ArrApplicationSettings settings, string appName, Version minimumVersion)
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

            return null;
        }

        public T Get<T>(ArrApplicationSettings settings, string resource)
            where T : new()
        {
            return Json.Deserialize<T>(Execute(settings, resource, HttpMethod.Get, null));
        }

        public JArray GetArray(ArrApplicationSettings settings, string resource)
        {
            return JArray.Parse(Execute(settings, resource, HttpMethod.Get, null));
        }

        public JObject Post(ArrApplicationSettings settings, string resource, JObject body)
        {
            return JObject.Parse(Execute(settings, resource, HttpMethod.Post, body));
        }

        public JObject Put(ArrApplicationSettings settings, string resource, JObject body)
        {
            return JObject.Parse(Execute(settings, resource, HttpMethod.Put, body));
        }

        public void Delete(ArrApplicationSettings settings, string resource)
        {
            Execute(settings, resource, HttpMethod.Delete, null);
        }

        private string Execute(ArrApplicationSettings settings, string resource, HttpMethod method, JObject body)
        {
            var requestBuilder = new HttpRequestBuilder(settings.BaseUrl.TrimEnd('/'))
                .Resource(ApiRoute + resource)
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
