using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public interface INodearrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
        IHttpRequestBuilderFactory SkyHookTvdb { get; }
    }

    public class NodearrCloudRequestBuilder : INodearrCloudRequestBuilder
    {
        public NodearrCloudRequestBuilder()
        {
            Services = new HttpRequestBuilder("https://services.nodearr.com/v1/")
                .CreateFactory();

            SkyHookTvdb = new HttpRequestBuilder("https://skyhook.nodearr.com/v1/tvdb/{route}/{language}/")
                .SetSegment("language", "en")
                .CreateFactory();
        }

        public IHttpRequestBuilderFactory Services { get; }

        public IHttpRequestBuilderFactory SkyHookTvdb { get; }
    }
}
