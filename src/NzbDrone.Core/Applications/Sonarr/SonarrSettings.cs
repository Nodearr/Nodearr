using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Applications.Sonarr
{
    public class SonarrSettingsValidator : AbstractValidator<SonarrSettings>
    {
        public SonarrSettingsValidator()
        {
            RuleFor(c => c.NodearrUrl).IsValidUrl();
            RuleFor(c => c.BaseUrl).IsValidUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class SonarrSettings : IApplicationSettings
    {
        private static readonly SonarrSettingsValidator Validator = new();

        public SonarrSettings()
        {
            NodearrUrl = "http://localhost:8989";
            BaseUrl = "http://localhost:8989";
            SyncCategories = new[] { 5000, 5010, 5020, 5030, 5040, 5045, 5050, 5090 };
            AnimeSyncCategories = new[] { 5070 };
            SyncAnimeStandardFormatSearch = true;
        }

        [FieldDefinition(0, Label = "NodearrServer", HelpText = "NodearrServerHelpText", Placeholder = "http://localhost:8989")]
        public string NodearrUrl { get; set; }

        [FieldDefinition(1, Label = "ApplicationSonarrServer", HelpText = "ApplicationSonarrServerHelpText", Placeholder = "http://localhost:8989")]
        public string BaseUrl { get; set; }

        [FieldDefinition(2, Label = "ApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "ApplicationSonarrApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(3, Label = "ApplicationAuthUsername", HelpText = "ApplicationAuthUsernameHelpText", Privacy = PrivacyLevel.UserName, Advanced = true)]
        public string AuthUsername { get; set; }

        [FieldDefinition(4, Label = "ApplicationAuthPassword", HelpText = "ApplicationAuthPasswordHelpText", Type = FieldType.Password, Privacy = PrivacyLevel.Password, Advanced = true)]
        public string AuthPassword { get; set; }

        [FieldDefinition(5, Label = "ApplicationSettingsSyncCategories", Type = FieldType.Select, SelectOptions = typeof(NewznabCategoryFieldConverter), Advanced = true, HelpText = "ApplicationSettingsSyncCategoriesHelpText")]
        public IEnumerable<int> SyncCategories { get; set; }

        [FieldDefinition(6, Label = "ApplicationSettingsAnimeSyncCategories", Type = FieldType.Select, SelectOptions = typeof(NewznabCategoryFieldConverter), Advanced = true, HelpText = "ApplicationSettingsAnimeSyncCategoriesHelpText")]
        public IEnumerable<int> AnimeSyncCategories { get; set; }

        [FieldDefinition(7, Label = "ApplicationSettingsSyncAnimeStandardFormatSearch", Type = FieldType.Checkbox, HelpText = "ApplicationSettingsSyncAnimeStandardFormatSearchHelpText", Advanced = true)]
        public bool SyncAnimeStandardFormatSearch { get; set; }

        [FieldDefinition(8, Type = FieldType.Checkbox, Label = "ApplicationSettingsSyncRejectBlocklistedTorrentHashes", HelpText = "ApplicationSettingsSyncRejectBlocklistedTorrentHashesHelpText", Advanced = true)]
        public bool SyncRejectBlocklistedTorrentHashesWhileGrabbing { get; set; }

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
