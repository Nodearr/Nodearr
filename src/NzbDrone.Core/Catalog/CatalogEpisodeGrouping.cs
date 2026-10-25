using System;
using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.Catalog
{
    // A single row in the series tree. One file can account for several episodes; an episode
    // without a file still gets a row so the table can show it as missing.
    public class CatalogEpisodeFileGroup
    {
        public int SeasonNumber { get; set; }
        public CatalogEpisodeFile File { get; set; }
        public List<CatalogEpisode> Episodes { get; set; } = new();

        public bool IsMissing => File == null || File.MissingSince.HasValue || Episodes.Any(e => e.MissingSince.HasValue);
    }

    public class CatalogSeasonGroup
    {
        public int SeasonNumber { get; set; }
        public List<CatalogEpisodeFileGroup> Files { get; set; } = new();
        public int EpisodeCount => Files.Sum(f => f.Episodes.Count);
        public int MissingCount => Files.Where(f => f.IsMissing).Sum(f => f.Episodes.Count);
        public long Size => Files.Where(f => f.File != null).Sum(f => f.File.Size);
    }

    public static class CatalogEpisodeGrouping
    {
        public static List<CatalogSeasonGroup> Build(string seriesType,
                                                     IEnumerable<CatalogEpisode> episodes,
                                                     IEnumerable<CatalogEpisodeFile> files)
        {
            var episodesByFile = episodes
                .Where(e => e.ArrEpisodeFileId != 0)
                .GroupBy(e => e.ArrEpisodeFileId)
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.SeasonNumber).ThenBy(e => e.EpisodeNumber).ToList());
            var rows = new List<CatalogEpisodeFileGroup>();
            var usedEpisodes = new HashSet<int>();

            foreach (var file in files)
            {
                var contained = episodesByFile.GetValueOrDefault(file.ArrId) ?? new List<CatalogEpisode>();
                foreach (var episode in contained)
                {
                    usedEpisodes.Add(episode.Id);
                }

                rows.Add(new CatalogEpisodeFileGroup
                {
                    SeasonNumber = file.SeasonNumber,
                    File = file,
                    Episodes = contained
                });
            }

            foreach (var episode in episodes.Where(e => !usedEpisodes.Contains(e.Id)))
            {
                rows.Add(new CatalogEpisodeFileGroup
                {
                    SeasonNumber = episode.SeasonNumber,
                    Episodes = new List<CatalogEpisode> { episode }
                });
            }

            return rows
                .GroupBy(row => row.SeasonNumber)
                .Select(group => new CatalogSeasonGroup
                {
                    SeasonNumber = group.Key,
                    Files = group.OrderBy(row => seriesType == "daily" ? RowSortDate(row) : DateTime.MinValue)
                                 .ThenBy(row => RowSortEpisode(row, seriesType))
                                 .ThenBy(row => row.File?.Path, StringComparer.OrdinalIgnoreCase)
                                 .ToList()
                })

                // Sonarr's season 0 specials belong after the numbered seasons.
                .OrderBy(group => group.SeasonNumber == 0 ? int.MaxValue : group.SeasonNumber)
                .ToList();
        }

        public static string EpisodeLabel(CatalogEpisode episode, string seriesType)
        {
            if (seriesType == "anime" && episode.AbsoluteEpisodeNumber.HasValue)
            {
                return $"#{episode.AbsoluteEpisodeNumber.Value:000}";
            }

            return $"S{episode.SeasonNumber:00}E{episode.EpisodeNumber:00}";
        }

        public static DateTime? AirDate(CatalogEpisodeFileGroup row)
        {
            return row.Episodes.Select(e => e.AirDateUtc).Where(d => d.HasValue).OrderBy(d => d).FirstOrDefault();
        }

        private static DateTime RowSortDate(CatalogEpisodeFileGroup row)
        {
            return AirDate(row) ?? DateTime.MaxValue;
        }

        private static int RowSortEpisode(CatalogEpisodeFileGroup row, string seriesType)
        {
            return row.Episodes.Select(e => seriesType == "anime" ? e.AbsoluteEpisodeNumber ?? e.EpisodeNumber : e.EpisodeNumber).DefaultIfEmpty(int.MaxValue).Min();
        }
    }
}
