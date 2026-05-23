using System.Collections.Generic;

namespace Nodearr.Api.V3.Episodes
{
    public class EpisodesMonitoredResource
    {
        public List<int> EpisodeIds { get; set; }
        public bool Monitored { get; set; }
    }
}
