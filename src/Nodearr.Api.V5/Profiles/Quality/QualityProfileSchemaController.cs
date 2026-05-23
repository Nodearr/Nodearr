using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Profiles.Qualities;

namespace Nodearr.Api.V5.Profiles.Quality
{
    [V5ApiController("qualityprofile/schema")]
    public class QualityProfileSchemaController : Controller
    {
        private readonly IQualityProfileService _profileService;

        public QualityProfileSchemaController(IQualityProfileService profileService)
        {
            _profileService = profileService;
        }

        [HttpGet]
        [Produces("application/json")]
        public Ok<QualityProfileResource> GetSchema()
        {
            var qualityProfile = _profileService.GetDefaultProfile(string.Empty);

            return TypedResults.Ok(qualityProfile.ToResource());
        }
    }
}
