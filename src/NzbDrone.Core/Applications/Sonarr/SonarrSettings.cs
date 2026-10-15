namespace NzbDrone.Core.Applications.Sonarr
{
    public class SonarrSettings : ArrApplicationSettings
    {
        public SonarrSettings()
        {
            BaseUrl = "http://localhost:8989";
            DownloadCategory = "tv";
            SyncCategories = new[] { 5030, 5040, 5045 };
        }
    }
}
