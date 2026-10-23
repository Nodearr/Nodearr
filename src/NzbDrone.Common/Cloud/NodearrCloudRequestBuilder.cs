using NzbDrone.Common.Http;

namespace NzbDrone.Common.Cloud
{
    public interface INodearrCloudRequestBuilder
    {
        IHttpRequestBuilderFactory Services { get; }
    }

    public class NodearrCloudRequestBuilder : INodearrCloudRequestBuilder
    {
        public NodearrCloudRequestBuilder()
        {
            Services = new HttpRequestBuilder("https://services.nodearr.com/v1/")
                .CreateFactory();
        }

        public IHttpRequestBuilderFactory Services { get; }
    }
}
