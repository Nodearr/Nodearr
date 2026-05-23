using Nodearr.Http.REST;
using NzbDrone.Core.HealthCheck;

namespace Nodearr.Api.V5.Health;

public class HealthResource : RestResource
{
    public string? Source { get; set; }
    public HealthCheckResult Type { get; set; }
    public HealthCheckReason Reason { get; set; }
    public string? Message { get; set; }
    public string? WikiUrl { get; set; }
}

public static class HealthResourceMapper
{
    public static HealthResource ToResource(this HealthCheck model)
    {
        return new HealthResource
        {
            Id = model.Id,
            Source = model.Source.Name,
            Type = model.Type,
            Reason = model.Reason,
            Message = model.Message,
            WikiUrl = model.WikiUrl.FullUri
        };
    }

    public static List<HealthResource> ToResource(this IEnumerable<HealthCheck> models)
    {
        return models.Select(ToResource).ToList();
    }
}
