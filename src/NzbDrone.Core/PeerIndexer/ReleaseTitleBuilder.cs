using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.PeerCatalog;
using NzbDrone.Core.Sharing;

namespace NzbDrone.Core.PeerIndexer
{
    // The release title an Arr app is shown for a peer's file. Sonarr and Radarr decide quality,
    // custom formats and which episode a result is by parsing this title, so it has to say what the
    // peer's own Arr app knows about the file.
    public static class ReleaseTitleBuilder
    {
        private const int MaxLength = 240;

        private static readonly Regex QualityNameRegex = new(@"^(?<source>WEBDL|WEBRip|HDTV|Bluray|Remux)-(?<resolution>\d{3,4})p(?<remux> Remux)?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Dictionary<string, string> SourceTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            { "WEBDL", "WEB-DL" },
            { "WEBRip", "WEBRip" },
            { "HDTV", "HDTV" },
            { "Bluray", "BluRay" },
            { "Remux", "BluRay.Remux" }
        };

        // Arr quality names that are not a source and a resolution. The rest are passed through as
        // they are, which the Arr parsers read back.
        private static readonly Dictionary<string, string> NamedQualities = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Unknown", string.Empty },
            { "SDTV", "SDTV" },
            { "DVD", "DVD" },
            { "DVD-R", "DVD-R" },
            { "Raw-HD", "Raw-HD" },
            { "BR-DISK", "BR-DISK" }
        };

        public static string For(PeerCatalogItem item)
        {
            return For(item, null);
        }

        // Sonarr asks for an episode either by its number or, for a series it calls daily, by the
        // day it aired, and it turns down a title written the other way. So the title is written
        // the way it was asked for: with the season and episode when airDate is null, and with
        // that day (yyyy-MM-dd) when it is not.
        public static string For(PeerCatalogItem item, string airDate)
        {
            var sceneName = (item.SceneName ?? string.Empty).Trim();

            return IsUsable(sceneName, item, airDate) ? sceneName : Build(item, airDate);
        }

        // The name the release had when the peer's Arr app grabbed it is the best title there is,
        // as long as an Arr app can still make sense of it.
        private static bool IsUsable(string sceneName, PeerCatalogItem item, string airDate)
        {
            if (sceneName.Length == 0 || sceneName.Length > MaxLength || sceneName.Any(c => char.IsControl(c) || c == '/' || c == '\\'))
            {
                return false;
            }

            if (item.MediaKind == MediaKind.Movie)
            {
                // Radarr reads a movie title up to its year.
                return item.Year == 0 || sceneName.Contains(item.Year.ToString(CultureInfo.InvariantCulture));
            }

            try
            {
                var parsed = Parser.Parser.ParseTitle(sceneName);

                if (parsed == null)
                {
                    return false;
                }

                // A release named by day is no use to a search by number, and the other way round.
                return airDate == null ? !parsed.IsDaily : parsed.IsDaily && parsed.AirDate == airDate;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string Build(PeerCatalogItem item)
        {
            return Build(item, null);
        }

        public static string Build(PeerCatalogItem item, string airDate)
        {
            var parts = new List<string> { CleanTitle(item.Title) };

            if (item.MediaKind == MediaKind.Movie)
            {
                if (item.Year > 0)
                {
                    parts.Add(item.Year.ToString(CultureInfo.InvariantCulture));
                }

                parts.AddRange(Tokens(item.Edition));
            }
            else if (airDate != null)
            {
                parts.Add(airDate.Replace('-', '.'));
            }
            else
            {
                // Sonarr reads the numbers in an indexer's title as the scene's, for a series that has them.
                var episodes = item.GetTitleEpisodeNumbers();

                parts.Add($"S{item.TitleSeasonNumber:00}" + string.Concat(episodes.Select(e => $"E{e:00}")));
            }

            parts.AddRange(LanguageTokens(item.Languages));
            parts.Add(QualityTokens(item.Quality));
            parts.AddRange(Tokens(item.VideoDynamicRange).Where(t => !t.Equals("SDR", StringComparison.OrdinalIgnoreCase)));
            parts.AddRange(Tokens(item.AudioCodec));

            if (item.AudioChannels > 0 && item.AudioCodec.IsNotNullOrWhiteSpace())
            {
                parts.Add(item.AudioChannels.ToString("0.0", CultureInfo.InvariantCulture));
            }

            parts.AddRange(Tokens(item.VideoCodec));

            var title = string.Join(".", parts.Where(p => p.IsNotNullOrWhiteSpace()));
            var group = new string((item.ReleaseGroup ?? string.Empty).Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());

            return group.Length > 0 ? $"{title}-{group}" : title;
        }

        // What an Arr app calls a quality, written the way release names write it.
        public static string QualityTokens(string qualityName)
        {
            qualityName = (qualityName ?? string.Empty).Trim();

            if (NamedQualities.TryGetValue(qualityName, out var named))
            {
                return named;
            }

            var match = QualityNameRegex.Match(qualityName);

            if (!match.Success)
            {
                return string.Join(".", Tokens(qualityName));
            }

            var source = SourceTokens[match.Groups["source"].Value];

            return $"{match.Groups["resolution"].Value}p.{source}" + (match.Groups["remux"].Success ? ".Remux" : string.Empty);
        }

        // English is what the Arr apps assume when a title names no language.
        private static IEnumerable<string> LanguageTokens(string languages)
        {
            return (languages ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.Equals("English", StringComparison.OrdinalIgnoreCase) &&
                            !l.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
                            !l.Equals("Original", StringComparison.OrdinalIgnoreCase))
                .SelectMany(Tokens);
        }

        private static string CleanTitle(string title)
        {
            var words = Tokens((title ?? string.Empty).Replace("&", " and "), keepDashes: false).ToList();

            return words.Count > 0 ? string.Join(".", words) : "Unknown";
        }

        // Splits free text into the dot-separated words of a release name.
        private static IEnumerable<string> Tokens(string value)
        {
            return Tokens(value, keepDashes: true);
        }

        private static IEnumerable<string> Tokens(string value, bool keepDashes)
        {
            var cleaned = new string((value ?? string.Empty).Replace("'", string.Empty).Replace("’", string.Empty).Select(c => char.IsLetterOrDigit(c) || (keepDashes && (c == '-' || c == '+')) ? c : ' ').ToArray());

            return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim('-')).Where(t => t.Length > 0);
        }
    }
}
