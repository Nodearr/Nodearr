using System;
using System.Collections.Generic;
using System.Net;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.Tv;

namespace NzbDrone.Core.MetadataSource
{
    // Nodearr keeps no series of its own: Sonarr does, and Nodearr reads them from it. Sonarr's
    // metadata client, SkyHookProxy, came with the port and asked a host renamed for Nodearr that
    // nothing serves. The inherited endpoints that look up or add a series still exist, so they are
    // told this instead of making a request that cannot succeed.
    public class NoSeriesMetadata : IProvideSeriesInfo, ISearchForNewSeries
    {
        private const string Message = "Nodearr does not look up series. Add series in Sonarr, and Nodearr reads them from it.";

        public Tuple<Series, List<Episode>> GetSeriesInfo(int tvdbSeriesId) => throw NotHere();

        public List<Series> SearchForNewSeries(string title) => throw NotHere();

        public List<Series> SearchForNewSeriesByImdbId(string imdbId) => throw NotHere();

        public List<Series> SearchForNewSeriesByAniListId(int aniListId) => throw NotHere();

        public List<Series> SearchForNewSeriesByTmdbId(int tmdbId) => throw NotHere();

        public List<Series> SearchForNewSeriesByMyAnimeListId(int malId) => throw NotHere();

        private static NzbDroneClientException NotHere() => new(HttpStatusCode.NotImplemented, Message);
    }
}
