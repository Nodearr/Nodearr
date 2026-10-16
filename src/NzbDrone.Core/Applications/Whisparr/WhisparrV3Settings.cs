namespace NzbDrone.Core.Applications.Whisparr
{
    public class WhisparrV3Settings : ArrApplicationSettings
    {
        public WhisparrV3Settings()
        {
            BaseUrl = "http://localhost:6969";
            DownloadCategory = "whisparr";

            // The adult family: the whole of it, and the subcategories Nodearr files its results under.
            SyncCategories = new[] { 6000, 6040, 6045, 6080, 6090 };
        }
    }
}
