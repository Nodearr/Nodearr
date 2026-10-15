using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Tv;
using NzbDrone.Core.Tv.Events;

namespace NzbDrone.Core.MediaCover
{
    public interface IMapCoversToLocal
    {
        void ConvertToLocalUrls(int seriesId, IEnumerable<MediaCover> covers);
        string GetCoverPath(int seriesId, MediaCoverTypes coverType, int? height = null);
    }

    public class MediaCoverService :
        IHandleAsync<SeriesUpdatedEvent>,
        IHandleAsync<SeriesDeletedEvent>,
        IMapCoversToLocal
    {
        private readonly IMediaCoverProxy _mediaCoverProxy;
        private readonly IHttpClient _httpClient;
        private readonly IDiskProvider _diskProvider;
        private readonly ICoverExistsSpecification _coverExistsSpecification;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        private readonly string _coverRootFolder;

        public MediaCoverService(IMediaCoverProxy mediaCoverProxy,
                                 IHttpClient httpClient,
                                 IDiskProvider diskProvider,
                                 IAppFolderInfo appFolderInfo,
                                 ICoverExistsSpecification coverExistsSpecification,
                                 IConfigFileProvider configFileProvider,
                                 IEventAggregator eventAggregator,
                                 Logger logger)
        {
            _mediaCoverProxy = mediaCoverProxy;
            _httpClient = httpClient;
            _diskProvider = diskProvider;
            _coverExistsSpecification = coverExistsSpecification;
            _configFileProvider = configFileProvider;
            _eventAggregator = eventAggregator;
            _logger = logger;

            _coverRootFolder = appFolderInfo.GetMediaCoverPath();
        }

        public string GetCoverPath(int seriesId, MediaCoverTypes coverType, int? height = null)
        {
            var heightSuffix = height.HasValue ? "-" + height.ToString() : "";

            return Path.Combine(GetSeriesCoverPath(seriesId), coverType.ToString().ToLower() + heightSuffix + GetExtension(coverType));
        }

        public void ConvertToLocalUrls(int seriesId, IEnumerable<MediaCover> covers)
        {
            if (seriesId == 0)
            {
                // Series isn't in Nodearr yet, map via a proxy to circumvent referrer issues
                foreach (var mediaCover in covers)
                {
                    mediaCover.Url = _mediaCoverProxy.RegisterUrl(mediaCover.RemoteUrl);
                }
            }
            else
            {
                foreach (var mediaCover in covers)
                {
                    if (mediaCover.CoverType == MediaCoverTypes.Unknown)
                    {
                        continue;
                    }

                    var filePath = GetCoverPath(seriesId, mediaCover.CoverType);

                    mediaCover.Url = _configFileProvider.UrlBase + @"/MediaCover/" + seriesId + "/" + mediaCover.CoverType.ToString().ToLower() + GetExtension(mediaCover.CoverType);

                    if (_diskProvider.FileExists(filePath))
                    {
                        var lastWrite = _diskProvider.FileGetLastWrite(filePath);
                        mediaCover.Url += "?lastWrite=" + lastWrite.Ticks;
                    }
                }
            }
        }

        private string GetSeriesCoverPath(int seriesId)
        {
            return Path.Combine(_coverRootFolder, seriesId.ToString());
        }

        private bool EnsureCovers(Series series)
        {
            var updated = false;

            foreach (var cover in series.Images)
            {
                if (cover.CoverType == MediaCoverTypes.Unknown)
                {
                    continue;
                }

                var fileName = GetCoverPath(series.Id, cover.CoverType);

                try
                {
                    if (!_coverExistsSpecification.AlreadyExists(cover.RemoteUrl, fileName))
                    {
                        DownloadCover(series, cover);
                        updated = true;
                    }
                }
                catch (HttpException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", series, e.Message);
                }
                catch (WebException e)
                {
                    _logger.Warn("Couldn't download media cover for {0}. {1}", series, e.Message);
                }
                catch (Exception e)
                {
                    _logger.Error(e, "Couldn't download media cover for {0}", series);
                }
            }

            return updated;
        }

        private void DownloadCover(Series series, MediaCover cover)
        {
            var fileName = GetCoverPath(series.Id, cover.CoverType);

            _logger.Info("Downloading {0} for {1} {2}", cover.CoverType, series, cover.RemoteUrl);
            _httpClient.DownloadFile(cover.RemoteUrl, fileName);
        }

        private string GetExtension(MediaCoverTypes coverType)
        {
            switch (coverType)
            {
                default:
                    return ".jpg";

                case MediaCoverTypes.Clearlogo:
                    return ".png";
            }
        }

        public void HandleAsync(SeriesUpdatedEvent message)
        {
            var updated = EnsureCovers(message.Series);

            _eventAggregator.PublishEvent(new MediaCoversUpdatedEvent(message.Series, updated));
        }

        public void HandleAsync(SeriesDeletedEvent message)
        {
            foreach (var series in message.Series)
            {
                var path = GetSeriesCoverPath(series.Id);
                if (_diskProvider.FolderExists(path))
                {
                    _diskProvider.DeleteFolder(path, true);
                }
            }
        }
    }
}
