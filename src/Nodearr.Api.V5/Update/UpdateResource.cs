using NzbDrone.Core.Update;

namespace Nodearr.Api.V5.Update
{
    // What System > Updates shows: the version running, the last check, and a newer release if the
    // last good manifest named one (ADR 0024).
    public class UpdateStatusResource
    {
        public required string CurrentVersion { get; set; }
        public required string Branch { get; set; }

        // Whether this node asks updates.nodearr.com at all: Check for updates and Contact nodearr.com.
        public bool UpdateCheck { get; set; }
        public bool ContactNodearr { get; set; }
        public bool UpdateAutomatically { get; set; }

        public UpdateCheckOutcome Outcome { get; set; }
        public DateTime? LastChecked { get; set; }

        public UpdateResource? Available { get; set; }
        public DateTime? AvailableSince { get; set; }

        // How this install takes a new version, and so what the page offers.
        public UpdateInstallKind InstallKind { get; set; }

        // When an automatic update began waiting for nothing to be running.
        public DateTime? WaitingSince { get; set; }
    }

    public class UpdateResource
    {
        public required string Version { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string? Changelog { get; set; }
        public string? Summary { get; set; }
        public required List<UpdateChangesResource> Changes { get; set; }

        // The package for this platform, or null when the release has none for it.
        public string? FileName { get; set; }
        public string? Url { get; set; }
        public long Size { get; set; }

        // The release's page, where every package is.
        public required string ReleaseUrl { get; set; }

        // In a container: what to run instead.
        public string? DockerCommand { get; set; }
    }

    public class UpdateChangesResource
    {
        public required string Heading { get; set; }
        public required List<string> Items { get; set; }
    }

    public static class UpdateResourceMapper
    {
        public const string ReleasesUrl = "https://github.com/Nodearr/Nodearr/releases/tag/v";

        public static UpdateResource ToResource(this UpdatePackage model, UpdateInstallKind kind)
        {
            return new UpdateResource
            {
                Version = model.SemanticVersion.ToString(),
                ReleaseDate = model.ReleaseDate,
                Changelog = model.Changelog,
                Summary = model.Summary,
                Changes = model.Changes.Select(c => new UpdateChangesResource { Heading = c.Heading, Items = c.Items }).ToList(),
                FileName = model.FileName,
                Url = model.Url,
                Size = model.Size,
                ReleaseUrl = ReleasesUrl + model.SemanticVersion,
                DockerCommand = kind == UpdateInstallKind.Docker && !string.IsNullOrWhiteSpace(model.Image) ? $"docker pull {model.Image}" : null
            };
        }
    }
}
