using System;
using System.Collections.Generic;

namespace NzbDrone.Core.Update
{
    // A newer release, as a signed manifest described it, with the package for this platform when
    // the manifest has one.
    public class UpdatePackage
    {
        // Without the pre-release tag, for the update history.
        public Version Version { get; set; }
        public SemanticVersion SemanticVersion { get; set; }
        public DateTime ReleaseDate { get; set; }
        public string Branch { get; set; }
        public string Changelog { get; set; }
        public string Summary { get; set; }
        public List<UpdateManifestChanges> Changes { get; set; } = new List<UpdateManifestChanges>();
        public string Image { get; set; }

        // The package for this platform: null when the manifest has none for it.
        public string FileName { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Hash { get; set; }
    }
}
