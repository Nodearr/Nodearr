using System;
using System.Collections.Generic;
using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.ImportLists.Nodearr
{
    public class NodearrSettingsValidator : AbstractValidator<NodearrSettings>
    {
        public NodearrSettingsValidator()
        {
            RuleFor(c => c.BaseUrl).ValidRootUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    public class NodearrSettings : ImportListSettingsBase<NodearrSettings>
    {
        private static readonly NodearrSettingsValidator Validator = new();

        public NodearrSettings()
        {
            ApiKey = "";
            ProfileIds = Array.Empty<int>();
            LanguageProfileIds = Array.Empty<int>();
            TagIds = Array.Empty<int>();
            RootFolderPaths = Array.Empty<string>();
        }

        [FieldDefinition(0, Label = "ImportListsNodearrSettingsFullUrl", HelpText = "ImportListsNodearrSettingsFullUrlHelpText")]
        public override string BaseUrl { get; set; } = string.Empty;

        [FieldDefinition(1, Label = "ApiKey", HelpText = "ImportListsNodearrSettingsApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(2, Label = "ImportListsNodearrSettingsSyncSeasonMonitoring", HelpText = "ImportListsNodearrSettingsSyncSeasonMonitoringHelpText", Type = FieldType.Checkbox)]
        public bool SyncSeasonMonitoring { get; set; }

        [FieldDefinition(3, Type = FieldType.Select, SelectOptionsProviderAction = "getProfiles", Label = "QualityProfiles", HelpText = "ImportListsNodearrSettingsQualityProfilesHelpText")]
        public IEnumerable<int> ProfileIds { get; set; }

        [FieldDefinition(4, Type = FieldType.Select, SelectOptionsProviderAction = "getTags", Label = "Tags", HelpText = "ImportListsNodearrSettingsTagsHelpText")]
        public IEnumerable<int> TagIds { get; set; }

        [FieldDefinition(5, Type = FieldType.Select, SelectOptionsProviderAction = "getRootFolders", Label = "RootFolders", HelpText = "ImportListsNodearrSettingsRootFoldersHelpText")]
        public IEnumerable<string> RootFolderPaths { get; set; }

        // TODO: Remove this eventually, no translation added as deprecated
        [FieldDefinition(6, Type = FieldType.Select, SelectOptionsProviderAction = "getLanguageProfiles", Label = "Language Profiles", HelpText = "Language Profiles from the source instance to import from")]
        public IEnumerable<int> LanguageProfileIds { get; set; }

        public override NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
