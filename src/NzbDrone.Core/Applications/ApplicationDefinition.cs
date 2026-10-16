using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Applications
{
    public class ApplicationDefinition : ProviderDefinition
    {
        public ApplicationSyncLevel SyncLevel { get; set; }

        public override bool Enable => SyncLevel is ApplicationSyncLevel.AddOnly or ApplicationSyncLevel.FullSync;

        // Sonarr and Radarr hold a library Nodearr reads. Prowlarr holds none (ADR 0007).
        public bool HoldsLibrary => Implementation != nameof(Prowlarr.Prowlarr);

        // Radarr and Whisparr v3 hold movies, each with one file. Sonarr holds series.
        public bool HoldsMovies => Implementation is nameof(Radarr.Radarr) or nameof(Whisparr.WhisparrV3);
    }
}
