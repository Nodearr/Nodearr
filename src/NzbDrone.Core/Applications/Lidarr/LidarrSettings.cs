namespace NzbDrone.Core.Applications.Lidarr
{
    public class LidarrSettings : ArrApplicationSettings
    {
        public LidarrSettings()
        {
            BaseUrl = "http://localhost:8686";
            DownloadCategory = "music";
            SyncCategories = new[] { 3000, 3010, 3040 };
        }

        protected override string ApiRoute => "/api/v1";
    }
}
