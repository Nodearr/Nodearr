namespace NzbDrone.Core.Applications.Whisparr
{
    public class WhisparrV2Settings : ArrApplicationSettings
    {
        public WhisparrV2Settings()
        {
            BaseUrl = "http://localhost:6969";
            DownloadCategory = "whisparr";

            // Adult content is shared with a friend only when chosen for them.
            ShareWithNewFriends = false;
            SyncCategories = new[] { 6000, 6040, 6045, 6080, 6090 };
        }
    }
}
