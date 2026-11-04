using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Catalog;
using NzbDrone.Core.Nodes;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.Statistics
{
    public interface IStatisticsTitles
    {
        // The Arr app and title of a file a friend asked for, by the name offers give it. Null
        // when the catalog has no such file.
        (int ApplicationId, string Title)? TitleOf(string offerFileId);

        // How many titles each Arr app shares now, with at least one trusted friend.
        Dictionary<int, int> SharedTitles();
    }

    // A title is what the Arr app keeps as one: a series, a movie, or an album, which Lidarr grabs
    // and imports whole. Keys look like series-{application}-{id}.
    public class StatisticsTitles : IStatisticsTitles
    {
        private readonly ICatalogEpisodeFileRepository _episodeFiles;
        private readonly ICatalogMovieFileRepository _movieFiles;
        private readonly ICatalogTrackFileRepository _trackFiles;
        private readonly IPeerRepository _peers;
        private readonly ISharingService _sharing;

        public StatisticsTitles(ICatalogEpisodeFileRepository episodeFiles,
                                ICatalogMovieFileRepository movieFiles,
                                ICatalogTrackFileRepository trackFiles,
                                IPeerRepository peers,
                                ISharingService sharing)
        {
            _episodeFiles = episodeFiles;
            _movieFiles = movieFiles;
            _trackFiles = trackFiles;
            _peers = peers;
            _sharing = sharing;
        }

        public (int ApplicationId, string Title)? TitleOf(string offerFileId)
        {
            if (!TryParse(offerFileId, out var kind, out var applicationId, out var arrId))
            {
                return null;
            }

            var title = kind switch
            {
                "e" => _episodeFiles.ForArrIds(applicationId, new[] { arrId }).Select(f => Series(applicationId, f.ArrSeriesId)).FirstOrDefault(),
                "m" => _movieFiles.ForArrIds(applicationId, new[] { arrId }).Select(f => Movie(applicationId, f.ArrMovieId)).FirstOrDefault(),
                "t" => _trackFiles.ForArrIds(applicationId, new[] { arrId }).Select(f => Album(applicationId, f.ArrAlbumId)).FirstOrDefault(),
                _ => null
            };

            return title == null ? null : (applicationId, title);
        }

        public Dictionary<int, int> SharedTitles()
        {
            var offered = _peers.All()
                .Where(p => p.State == PeerState.Trusted)
                .SelectMany(p => _sharing.BuildOffer(p, true).Items.Select(i => i.Id))
                .ToHashSet();

            var titles = new HashSet<(int ApplicationId, string Title)>();
            var wanted = offered
                .Select(id => TryParse(id, out var kind, out var applicationId, out var arrId) ? (kind, applicationId, arrId) : default)
                .Where(w => w.kind != null)
                .GroupBy(w => (w.kind, w.applicationId));

            foreach (var group in wanted)
            {
                var (kind, applicationId) = group.Key;
                var ids = group.Select(w => w.arrId).ToList();
                var keys = kind switch
                {
                    "e" => _episodeFiles.ForArrIds(applicationId, ids).Select(f => Series(applicationId, f.ArrSeriesId)),
                    "m" => _movieFiles.ForArrIds(applicationId, ids).Select(f => Movie(applicationId, f.ArrMovieId)),
                    "a" => ids.Select(id => Album(applicationId, id)),
                    _ => Enumerable.Empty<string>()
                };

                foreach (var key in keys)
                {
                    titles.Add((applicationId, key));
                }
            }

            return titles.GroupBy(t => t.ApplicationId).ToDictionary(g => g.Key, g => g.Count());
        }

        private static string Series(int applicationId, int arrId) => $"series-{applicationId}-{arrId}";

        private static string Movie(int applicationId, int arrId) => $"movie-{applicationId}-{arrId}";

        private static string Album(int applicationId, int arrId) => $"album-{applicationId}-{arrId}";

        // e-, m- and t- name single files, an episode's, a movie's or a track's. a- names a whole
        // album in an offer.
        private static bool TryParse(string offerFileId, out string kind, out int applicationId, out int arrId)
        {
            var parts = (offerFileId ?? string.Empty).Split('-');

            kind = parts[0];
            applicationId = 0;
            arrId = 0;

            return parts.Length == 3 && kind is "e" or "m" or "t" or "a" && int.TryParse(parts[1], out applicationId) && int.TryParse(parts[2], out arrId);
        }
    }
}
