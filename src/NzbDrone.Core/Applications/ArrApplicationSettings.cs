using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Applications
{
    public class ArrApplicationSettingsValidator : AbstractValidator<ArrApplicationSettings>
    {
        public ArrApplicationSettingsValidator()
        {
            RuleFor(c => c.NodearrUrl).IsValidUrl();
            RuleFor(c => c.BaseUrl).IsValidUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
            RuleFor(c => c.DownloadCategory).NotEmpty();
            RuleFor(c => c.SyncCategories).NotEmpty();
        }
    }

    public abstract class ArrApplicationSettings : IProviderConfig, IArrConnection
    {
        private static readonly ArrApplicationSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "NodearrServer", HelpText = "NodearrServerHelpText", Placeholder = "http://localhost:6633")]
        public string NodearrUrl { get; set; } = "http://localhost:6633";

        [FieldDefinition(1, Label = "ApplicationServer", HelpText = "ApplicationServerHelpText")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2, Label = "ApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "ApplicationApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(3, Label = "ApplicationAuthUsername", HelpText = "ApplicationAuthUsernameHelpText", Privacy = PrivacyLevel.UserName, Advanced = true)]
        public string AuthUsername { get; set; }

        [FieldDefinition(4, Label = "ApplicationAuthPassword", HelpText = "ApplicationAuthPasswordHelpText", Type = FieldType.Password, Privacy = PrivacyLevel.Password, Advanced = true)]
        public string AuthPassword { get; set; }

        [FieldDefinition(5, Label = "ApplicationSettingsDownloadCategory", HelpText = "ApplicationSettingsDownloadCategoryHelpText", Advanced = true)]
        public string DownloadCategory { get; set; }

        [FieldDefinition(6, Label = "ApplicationSettingsSyncCategories", Type = FieldType.Select, SelectOptions = typeof(NewznabCategoryFieldConverter), Advanced = true, HelpText = "ApplicationSettingsSyncCategoriesHelpText")]
        public IEnumerable<int> SyncCategories { get; set; }

        // Not a setting: kept out of what is saved and shown.
        string IArrConnection.ApiRoute => "/api/v3";

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
