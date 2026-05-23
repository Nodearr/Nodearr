using FluentValidation;
using Nodearr.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.ImportLists;
using NzbDrone.Core.Validation;

namespace Nodearr.Api.V5.Settings;

[V5ApiController("settings/importlist")]
public class ImportListSettingsController : SettingsController<ImportListSettingsResource>
{
    public ImportListSettingsController(IConfigFileProvider configFileProvider,
        IConfigService configService)
        : base(configFileProvider, configService)
    {
        SharedValidator.RuleFor(c => c.ListSyncTag)
                       .ValidId()
                       .WithMessage("Tag must be specified")
                       .When(c => c.ListSyncLevel == ListSyncLevelType.KeepAndTag);
    }

    protected override ImportListSettingsResource ToResource(IConfigFileProvider configFile, IConfigService model)
    {
        return ImportListSettingsResourceMapper.ToResource(model);
    }
}
