using System;
using System.Collections.Generic;
using System.Net;
using FluentValidation.Results;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Localization;

namespace NzbDrone.Core.ImportLists.Nodearr
{
    public interface INodearrV3Proxy
    {
        List<NodearrSeries> GetSeries(NodearrSettings settings);
        List<NodearrProfile> GetQualityProfiles(NodearrSettings settings);
        List<NodearrProfile> GetLanguageProfiles(NodearrSettings settings);
        List<NodearrRootFolder> GetRootFolders(NodearrSettings settings);
        List<NodearrTag> GetTags(NodearrSettings settings);
        ValidationFailure Test(NodearrSettings settings);
    }

    public class NodearrV3Proxy : INodearrV3Proxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly ILocalizationService _localizationService;

        public NodearrV3Proxy(IHttpClient httpClient, ILocalizationService localizationService, Logger logger)
        {
            _httpClient = httpClient;
            _localizationService = localizationService;
            _logger = logger;
        }

        public List<NodearrSeries> GetSeries(NodearrSettings settings)
        {
            return Execute<NodearrSeries>("/api/v3/series", settings);
        }

        public List<NodearrProfile> GetQualityProfiles(NodearrSettings settings)
        {
            return Execute<NodearrProfile>("/api/v3/qualityprofile", settings);
        }

        public List<NodearrProfile> GetLanguageProfiles(NodearrSettings settings)
        {
            return Execute<NodearrProfile>("/api/v3/languageprofile", settings);
        }

        public List<NodearrRootFolder> GetRootFolders(NodearrSettings settings)
        {
            return Execute<NodearrRootFolder>("api/v3/rootfolder", settings);
        }

        public List<NodearrTag> GetTags(NodearrSettings settings)
        {
            return Execute<NodearrTag>("/api/v3/tag", settings);
        }

        public ValidationFailure Test(NodearrSettings settings)
        {
            try
            {
                GetSeries(settings);
            }
            catch (HttpException ex)
            {
                if (ex.Response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    _logger.Error(ex, "API Key is invalid");
                    return new ValidationFailure("ApiKey", _localizationService.GetLocalizedString("ImportListsValidationInvalidApiKey"));
                }

                if (ex.Response.HasHttpRedirect)
                {
                    _logger.Error(ex, "Nodearr returned redirect and is invalid");
                    return new ValidationFailure("BaseUrl", _localizationService.GetLocalizedString("ImportListsNodearrValidationInvalidUrl"));
                }

                _logger.Error(ex, "Unable to connect to import list.");
                return new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("ImportListsValidationUnableToConnectException", new Dictionary<string, object> { { "exceptionMessage", ex.Message } }));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unable to connect to import list.");
                return new ValidationFailure(string.Empty, _localizationService.GetLocalizedString("ImportListsValidationUnableToConnectException", new Dictionary<string, object> { { "exceptionMessage", ex.Message } }));
            }

            return null;
        }

        private List<TResource> Execute<TResource>(string resource, NodearrSettings settings)
        {
            if (settings.BaseUrl.IsNullOrWhiteSpace() || settings.ApiKey.IsNullOrWhiteSpace())
            {
                return new List<TResource>();
            }

            var baseUrl = settings.BaseUrl.TrimEnd('/');

            var request = new HttpRequestBuilder(baseUrl).Resource(resource)
                .Accept(HttpAccept.Json)
                .SetHeader("X-Api-Key", settings.ApiKey)
                .Build();

            var response = _httpClient.Get(request);

            if ((int)response.StatusCode >= 300)
            {
                throw new HttpException(response);
            }

            var results = JsonConvert.DeserializeObject<List<TResource>>(response.Content);

            return results;
        }
    }
}
