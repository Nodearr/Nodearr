using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Catalog
{
    public interface ICatalogRepository<TModel> : IBasicRepository<TModel>
        where TModel : CatalogModel, new()
    {
        List<TModel> ForApplication(int applicationId);
        List<TModel> ForArrIds(int applicationId, IEnumerable<int> arrIds);
        void DeleteForApplication(int applicationId);
        int Count(int applicationId, bool missingOnly);
    }

    public abstract class CatalogRepository<TModel> : BasicRepository<TModel>, ICatalogRepository<TModel>
        where TModel : CatalogModel, new()
    {
        protected CatalogRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<TModel> ForApplication(int applicationId)
        {
            return Query(x => x.ApplicationId == applicationId);
        }

        public List<TModel> ForArrIds(int applicationId, IEnumerable<int> arrIds)
        {
            var ids = arrIds.Distinct().ToList();

            return ids.Count == 0 ? new List<TModel>() : Query(x => x.ApplicationId == applicationId && ids.Contains(x.ArrId));
        }

        public void DeleteForApplication(int applicationId)
        {
            Delete(x => x.ApplicationId == applicationId);
        }

        public int Count(int applicationId, bool missingOnly)
        {
            var missing = missingOnly ? " AND \"MissingSince\" IS NOT NULL" : string.Empty;

            using var connection = _database.OpenConnection();

            return connection.ExecuteScalar<int>($"SELECT COUNT(*) FROM \"{_table}\" WHERE \"ApplicationId\" = @applicationId{missing}", new { applicationId });
        }
    }

    public interface ICatalogQualityProfileRepository : ICatalogRepository<CatalogQualityProfile>
    {
    }

    public class CatalogQualityProfileRepository : CatalogRepository<CatalogQualityProfile>, ICatalogQualityProfileRepository
    {
        public CatalogQualityProfileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogSeriesRepository : ICatalogRepository<CatalogSeries>
    {
    }

    public class CatalogSeriesRepository : CatalogRepository<CatalogSeries>, ICatalogSeriesRepository
    {
        public CatalogSeriesRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogEpisodeRepository : ICatalogRepository<CatalogEpisode>
    {
        List<CatalogEpisode> ForFiles(int applicationId, IEnumerable<int> arrEpisodeFileIds);
        List<CatalogEpisode> ForSeries(int applicationId, int arrSeriesId);
    }

    public class CatalogEpisodeRepository : CatalogRepository<CatalogEpisode>, ICatalogEpisodeRepository
    {
        public CatalogEpisodeRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<CatalogEpisode> ForFiles(int applicationId, IEnumerable<int> arrEpisodeFileIds)
        {
            var ids = arrEpisodeFileIds.Distinct().ToList();

            return ids.Count == 0 ? new List<CatalogEpisode>() : Query(x => x.ApplicationId == applicationId && ids.Contains(x.ArrEpisodeFileId));
        }

        public List<CatalogEpisode> ForSeries(int applicationId, int arrSeriesId)
        {
            return Query(x => x.ApplicationId == applicationId && x.ArrSeriesId == arrSeriesId);
        }
    }

    public interface ICatalogEpisodeFileRepository : ICatalogRepository<CatalogEpisodeFile>
    {
    }

    public class CatalogEpisodeFileRepository : CatalogRepository<CatalogEpisodeFile>, ICatalogEpisodeFileRepository
    {
        public CatalogEpisodeFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogMovieRepository : ICatalogRepository<CatalogMovie>
    {
    }

    public class CatalogMovieRepository : CatalogRepository<CatalogMovie>, ICatalogMovieRepository
    {
        public CatalogMovieRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogMovieFileRepository : ICatalogRepository<CatalogMovieFile>
    {
    }

    public class CatalogMovieFileRepository : CatalogRepository<CatalogMovieFile>, ICatalogMovieFileRepository
    {
        public CatalogMovieFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogArtistRepository : ICatalogRepository<CatalogArtist>
    {
    }

    public class CatalogArtistRepository : CatalogRepository<CatalogArtist>, ICatalogArtistRepository
    {
        public CatalogArtistRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogAlbumRepository : ICatalogRepository<CatalogAlbum>
    {
    }

    public class CatalogAlbumRepository : CatalogRepository<CatalogAlbum>, ICatalogAlbumRepository
    {
        public CatalogAlbumRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogTrackFileRepository : ICatalogRepository<CatalogTrackFile>
    {
    }

    public class CatalogTrackFileRepository : CatalogRepository<CatalogTrackFile>, ICatalogTrackFileRepository
    {
        public CatalogTrackFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }
    }

    public interface ICatalogSyncStatusRepository : IBasicRepository<CatalogSyncStatus>
    {
        CatalogSyncStatus ForApplication(int applicationId);
    }

    public class CatalogSyncStatusRepository : BasicRepository<CatalogSyncStatus>, ICatalogSyncStatusRepository
    {
        public CatalogSyncStatusRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public CatalogSyncStatus ForApplication(int applicationId)
        {
            return Query(x => x.ApplicationId == applicationId).FirstOrDefault();
        }
    }
}
