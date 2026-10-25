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
        List<CatalogEpisode> ForSeries(int applicationId, IEnumerable<int> arrSeriesIds);
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

        public List<CatalogEpisode> ForSeries(int applicationId, IEnumerable<int> arrSeriesIds)
        {
            var ids = arrSeriesIds.Distinct().ToList();

            return ids.Count == 0 ? new List<CatalogEpisode>() : Query(x => x.ApplicationId == applicationId && ids.Contains(x.ArrSeriesId));
        }
    }

    // How many files a title has, counted by the database: a page of titles must not load every file.
    public class TitleFileCount
    {
        public int ApplicationId { get; set; }
        public int ArrId { get; set; }
        public int Files { get; set; }
    }

    public interface ICatalogEpisodeFileRepository : ICatalogRepository<CatalogEpisodeFile>
    {
        // Files the Arr app still reports, per series.
        List<TitleFileCount> FilesPerSeries();
        List<CatalogEpisodeFile> ForSeries(int applicationId, IEnumerable<int> arrSeriesIds);
    }

    public class CatalogEpisodeFileRepository : CatalogRepository<CatalogEpisodeFile>, ICatalogEpisodeFileRepository
    {
        public CatalogEpisodeFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<TitleFileCount> FilesPerSeries()
        {
            using var connection = _database.OpenConnection();

            return connection.Query<TitleFileCount>($"SELECT \"ApplicationId\", \"ArrSeriesId\" AS \"ArrId\", COUNT(*) AS \"Files\" FROM \"{_table}\" WHERE \"MissingSince\" IS NULL GROUP BY \"ApplicationId\", \"ArrSeriesId\"").ToList();
        }

        public List<CatalogEpisodeFile> ForSeries(int applicationId, IEnumerable<int> arrSeriesIds)
        {
            var ids = arrSeriesIds.Distinct().ToList();

            return ids.Count == 0 ? new List<CatalogEpisodeFile>() : Query(x => x.ApplicationId == applicationId && ids.Contains(x.ArrSeriesId));
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
        // Files the Arr app still reports, per movie.
        List<TitleFileCount> FilesPerMovie();
        PagingSpec<CatalogMovieFile> GetPagedByKind(PagingSpec<CatalogMovieFile> pagingSpec, string kind);
    }

    public class CatalogMovieFileRepository : CatalogRepository<CatalogMovieFile>, ICatalogMovieFileRepository
    {
        public CatalogMovieFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<TitleFileCount> FilesPerMovie()
        {
            using var connection = _database.OpenConnection();

            return connection.Query<TitleFileCount>($"SELECT \"ApplicationId\", \"ArrMovieId\" AS \"ArrId\", COUNT(*) AS \"Files\" FROM \"{_table}\" WHERE \"MissingSince\" IS NULL GROUP BY \"ApplicationId\", \"ArrMovieId\"").ToList();
        }

        public PagingSpec<CatalogMovieFile> GetPagedByKind(PagingSpec<CatalogMovieFile> pagingSpec, string kind)
        {
            if (kind is not ("movies" or "scenes"))
            {
                return GetPaged(pagingSpec);
            }

            SqlBuilder BuildQuery()
            {
                var builder = Builder().InnerJoin<CatalogMovieFile, CatalogMovie>((file, movie) => file.ApplicationId == movie.ApplicationId && file.ArrMovieId == movie.ArrId);

                if (kind == "scenes")
                {
                    builder.Where("\"CatalogMovies\".\"ItemType\" = @itemType", new { itemType = "scene" });
                }
                else
                {
                    builder.Where("(\"CatalogMovies\".\"ItemType\" IS NULL OR \"CatalogMovies\".\"ItemType\" <> @itemType)", new { itemType = "scene" });
                }

                return builder;
            }

            pagingSpec.Records = GetPagedRecords(BuildQuery(), pagingSpec, Query);
            pagingSpec.TotalRecords = GetPagedRecordCount(BuildQuery().SelectCount(), pagingSpec);

            return pagingSpec;
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
        List<TitleFileCount> FilesPerAlbum();
    }

    public class CatalogTrackFileRepository : CatalogRepository<CatalogTrackFile>, ICatalogTrackFileRepository
    {
        public CatalogTrackFileRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<TitleFileCount> FilesPerAlbum()
        {
            using var connection = _database.OpenConnection();

            return connection.Query<TitleFileCount>($"SELECT \"ApplicationId\", \"ArrAlbumId\" AS \"ArrId\", COUNT(*) AS \"Files\" FROM \"{_table}\" WHERE \"MissingSince\" IS NULL GROUP BY \"ApplicationId\", \"ArrAlbumId\"").ToList();
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
