using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using Nodearr.Http.REST;
using NzbDrone.Core.Languages;

namespace Nodearr.Api.V3.Profiles.Languages
{
    [V3ApiController("languageprofile/schema")]
    [Obsolete("Deprecated")]
    public class LanguageProfileSchemaController : RestController<LanguageProfileResource>
    {
        [HttpGet]
        [Produces("application/json")]
        public LanguageProfileResource GetSchema()
        {
            return new LanguageProfileResource
            {
                Id = 1,
                Name = "Deprecated",
                UpgradeAllowed = true,
                Cutoff = Language.English,
                Languages = new List<LanguageProfileItemResource>
                {
                    new LanguageProfileItemResource
                    {
                        Language = Language.English,
                        Allowed = true
                    }
                }
            };
        }

        [NonAction]
        public override Results<Ok<LanguageProfileResource>, NotFound> GetResourceByIdWithErrorHandler(int id)
        {
            return base.GetResourceByIdWithErrorHandler(id);
        }

        protected override LanguageProfileResource GetResourceById(int id)
        {
            throw new NotImplementedException();
        }
    }
}
