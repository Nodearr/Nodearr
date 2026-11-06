using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Nodearr.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Update;

namespace Nodearr.Api.V5.Update
{
    [V5ApiController]
    public class UpdateController : Controller
    {
        private readonly ICheckUpdateService _checkUpdateService;
        private readonly IUpdateInstallability _installability;
        private readonly IPendingUpdate _pendingUpdate;
        private readonly IConfigFileProvider _configFileProvider;

        public UpdateController(ICheckUpdateService checkUpdateService,
                                IUpdateInstallability installability,
                                IPendingUpdate pendingUpdate,
                                IConfigFileProvider configFileProvider)
        {
            _checkUpdateService = checkUpdateService;
            _installability = installability;
            _pendingUpdate = pendingUpdate;
            _configFileProvider = configFileProvider;
        }

        // What the last check found. It does not check again: the Check for updates task does, every
        // 6 hours, or when started from this page.
        [HttpGet]
        [Produces("application/json")]
        public Ok<UpdateStatusResource> GetStatus()
        {
            var status = _checkUpdateService.Status();
            var available = status.Available;
            var kind = _installability.Kind(available);

            return TypedResults.Ok(new UpdateStatusResource
            {
                CurrentVersion = CheckUpdateService.CurrentVersion.ToString(),
                Branch = _configFileProvider.Branch,
                UpdateCheck = _configFileProvider.UpdateCheck,
                ContactNodearr = _configFileProvider.ContactNodearr,
                UpdateAutomatically = _configFileProvider.UpdateAutomatically,
                Outcome = status.Outcome,
                LastChecked = status.LastChecked,
                Available = available?.ToResource(kind),
                AvailableSince = status.AvailableSince,
                InstallKind = kind,
                WaitingSince = available == null ? null : _pendingUpdate.WaitingSince(available.SemanticVersion)
            });
        }
    }
}
