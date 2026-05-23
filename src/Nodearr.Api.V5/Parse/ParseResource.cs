using Nodearr.Api.V5.CustomFormats;
using Nodearr.Api.V5.Episodes;
using Nodearr.Api.V5.Series;
using Nodearr.Http.REST;
using NzbDrone.Core.Languages;
using NzbDrone.Core.Parser.Model;

namespace Nodearr.Api.V5.Parse;

public class ParseResource : RestResource
{
    public string? Title { get; set; }
    public ParsedEpisodeInfo? ParsedEpisodeInfo { get; set; }
    public SeriesResource? Series { get; set; }
    public List<EpisodeResource>? Episodes { get; set; }
    public List<Language>? Languages { get; set; }
    public List<CustomFormatResource>? CustomFormats { get; set; }
    public int CustomFormatScore { get; set; }
}
