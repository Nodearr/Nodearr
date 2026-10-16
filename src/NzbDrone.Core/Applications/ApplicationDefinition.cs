using NzbDrone.Core.ThingiProvider;

namespace NzbDrone.Core.Applications
{
    public class ApplicationDefinition : ProviderDefinition
    {
        public ApplicationSyncLevel SyncLevel { get; set; }

        public override bool Enable => SyncLevel is ApplicationSyncLevel.AddOnly or ApplicationSyncLevel.FullSync;

        // Sonarr, Radarr, Whisparr and Lidarr hold a library Nodearr reads. Prowlarr holds none (ADR 0007).
        public bool HoldsLibrary => Implementation != nameof(Prowlarr.Prowlarr);

        // Radarr and Whisparr v3 hold movies, each with one file, and Whisparr v2's scenes are kept
        // as movies too. Sonarr holds series.
        public bool HoldsMovies => Implementation is nameof(Radarr.Radarr) or nameof(Whisparr.WhisparrV3) or nameof(Whisparr.WhisparrV2);

        // Lidarr holds artists, their albums, and the albums' tracks.
        public bool HoldsMusic => Implementation == nameof(Lidarr.Lidarr);

        // Whisparr v2 tells Nodearr which site changed, not which scene, so it is read again whole.
        public bool RefreshesWhole => Implementation == nameof(Whisparr.WhisparrV2);
    }
}
