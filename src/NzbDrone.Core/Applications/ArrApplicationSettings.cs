using System.Collections.Generic;
using System.Text.Json.Serialization;
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

        // Whether friends offered everything are offered this app's library too, when they become
        // friends or when the app is added. Each friend can be changed on Catalog > Sharing.
        [FieldDefinition(7, Label = "ApplicationShareWithNewFriends", Type = FieldType.Checkbox, HelpText = "ApplicationShareWithNewFriendsHelpText")]
        public bool ShareWithNewFriends { get; set; } = true;

        // Null only for an existing app whose RSS choice has not been read yet. Keep that marker
        // in saved settings until registration sync can reach the app, or the owner makes a choice.
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        [FieldDefinition(8, Label = "ApplicationFetchAutomatically", Type = FieldType.Checkbox, HelpText = "ApplicationFetchAutomaticallyHelpText")]
        public bool? FetchNewFilesAutomatically { get; set; } = true;

        [FieldDefinition(9, Label = "ApplicationIncludeUpgrades", Type = FieldType.Checkbox, HelpText = "ApplicationIncludeUpgradesHelpText")]
        public bool IncludeAutomaticUpgrades { get; set; }

        // Not a setting: kept out of what is saved and shown. Lidarr's API is /api/v1.
        string IArrConnection.ApiRoute => ApiRoute;

        protected virtual string ApiRoute => "/api/v3";

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
