namespace NzbDrone.Core.Applications.Radarr
{
    public class RadarrSettings : ArrApplicationSettings
    {
        public RadarrSettings()
        {
            BaseUrl = "http://localhost:7878";
            DownloadCategory = "movies";
            SyncCategories = new[] { 2030, 2040, 2045 };
        }
    }
}
