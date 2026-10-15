using FluentValidation;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Applications.Prowlarr
{
    public class ProwlarrSettingsValidator : AbstractValidator<ProwlarrSettings>
    {
        public ProwlarrSettingsValidator()
        {
            RuleFor(c => c.NodearrUrl).IsValidUrl();
            RuleFor(c => c.BaseUrl).IsValidUrl();
            RuleFor(c => c.ApiKey).NotEmpty();
        }
    }

    // Prowlarr is told where Nodearr is, and nothing is read back from it: it holds no library.
    public class ProwlarrSettings : IProviderConfig, IArrConnection
    {
        private static readonly ProwlarrSettingsValidator Validator = new();

        [FieldDefinition(0, Label = "NodearrServer", HelpText = "ProwlarrNodearrServerHelpText", Placeholder = "http://localhost:6633")]
        public string NodearrUrl { get; set; } = "http://localhost:6633";

        [FieldDefinition(1, Label = "ApplicationServer", HelpText = "ApplicationServerHelpText")]
        public string BaseUrl { get; set; } = "http://localhost:9696";

        [FieldDefinition(2, Label = "ApiKey", Privacy = PrivacyLevel.ApiKey, HelpText = "ProwlarrApiKeyHelpText")]
        public string ApiKey { get; set; }

        [FieldDefinition(3, Label = "ApplicationAuthUsername", HelpText = "ApplicationAuthUsernameHelpText", Privacy = PrivacyLevel.UserName, Advanced = true)]
        public string AuthUsername { get; set; }

        [FieldDefinition(4, Label = "ApplicationAuthPassword", HelpText = "ApplicationAuthPasswordHelpText", Type = FieldType.Password, Privacy = PrivacyLevel.Password, Advanced = true)]
        public string AuthPassword { get; set; }

        string IArrConnection.ApiRoute => "/api/v1";

        public NzbDroneValidationResult Validate()
        {
            return new NzbDroneValidationResult(Validator.Validate(this));
        }
    }
}
