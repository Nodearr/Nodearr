using Nodearr.Http;
using NzbDrone.Core.Configuration;

namespace Nodearr.Api.V3.Config
{
    [V3ApiController("config/downloadclient")]
    public class DownloadClientConfigController : ConfigController<DownloadClientConfigResource>
    {
        public DownloadClientConfigController(IConfigService configService)
            : base(configService)
        {
        }

        protected override DownloadClientConfigResource ToResource(IConfigService model)
        {
            return DownloadClientConfigResourceMapper.ToResource(model);
        }
    }
}
