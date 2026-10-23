using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.Localization;

namespace Nodearr.Api.V5.Localization;

[V5ApiController]
public class LocalizationController : RestController<LocalizationResource>
{
    private readonly ILocalizationService _localizationService;

    public LocalizationController(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    protected override LocalizationResource GetResourceById(int id)
    {
        return _localizationService.GetLocalizationDictionary().ToResource();
    }

    [HttpGet]
    [Produces("application/json")]
    public Ok<LocalizationResource> GetLocalization()
    {
        return TypedResults.Ok(GetResourceById(1));
    }
}
