using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Parser;
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

            // Whisparr reads a scene's own release name, studio and date first, better than anything
            // built here.
            // Lidarr reads a release's own name for its artist and album, and so does Whisparr for
            // a scene's studio and date, better than anything built here.
            if (item.IsScene || item.MediaKind == MediaKind.Music)
            {
                return true;
            }

            if (item.MediaKind == MediaKind.Movie)
            {
                // Radarr reads a movie title up to its year.
                return (item.Year == 0 || sceneName.Contains(item.Year.ToString(CultureInfo.InvariantCulture))) && SaysRevisionOf(sceneName, item);
            }

            try
            {
                var parsed = Parser.Parser.ParseTitle(sceneName);

                if (parsed == null)
                {
                    return false;
                }

                // A release named by day is no use to a search by number, and the other way round.
                return (airDate == null ? !parsed.IsDaily : parsed.IsDaily && parsed.AirDate == airDate) && SaysRevisionOf(sceneName, item);
            }
            catch (Exception)
            {
                return false;
            }
        }

        // Whether a release name says the revision the peer's Arr app recorded for the file. One that
        // says otherwise would have it taken for something it is not, and a title is built instead.
        private static bool SaysRevisionOf(string sceneName, PeerCatalogItem item)
        {
            var said = QualityParser.ParseQuality(sceneName).Revision;

            return said.Version == item.RevisionVersion && said.Real == item.RevisionReal && said.IsRepack == item.RevisionIsRepack;
        }

        public static string Build(PeerCatalogItem item)
        {
            return Build(item, null);
        }

        public static string Build(PeerCatalogItem item, string airDate)
        {
            if (item.MediaKind == MediaKind.Music)
            {
                return Album(item);
            }

            var parts = new List<string>();

            if (item.IsScene)
            {
                // As scenes are released: the studio run together, the day as yy.MM.dd, the title.
                parts.Add(RunTogether(item.Studio));

                if (DateTime.TryParseExact(item.ReleaseDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var released))
                {
                    parts.Add(released.ToString("yy.MM.dd", CultureInfo.InvariantCulture));
                }

                parts.Add(CleanTitle(item.Title));
                parts.Add("XXX");
            }
            else if (item.MediaKind == MediaKind.Movie)
            {
                parts.Add(CleanTitle(item.Title));

                if (item.Year > 0)
                {
                    parts.Add(item.Year.ToString(CultureInfo.InvariantCulture));
                }

                parts.AddRange(Tokens(item.Edition));

                if (item.IsAdult)
                {
                    parts.Add("XXX");
                }
            }
            else if (airDate != null)
            {
                parts.Add(CleanTitle(item.Title));
                parts.Add(airDate.Replace('-', '.'));
            }
            else
            {
                parts.Add(CleanTitle(item.Title));

                // Sonarr reads the numbers in an indexer's title as the scene's, for a series that has them.
                var episodes = item.GetTitleEpisodeNumbers();

                parts.Add($"S{item.TitleSeasonNumber:00}" + string.Concat(episodes.Select(e => $"E{e:00}")));
            }

            parts.AddRange(RevisionTokens(item.RevisionVersion, item.RevisionReal, item.RevisionIsRepack));
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

        // As Lidarr names an album's folder, which its parser reads back: Artist - Album (Year)
        // [Quality]. Lidarr's MP3-320 is written MP3 320, the way release names write it.
        private static string Album(PeerCatalogItem item)
        {
            var title = $"{Words(item.Artist)} - {Words(item.Title)}";

            if (item.Year > 0)
            {
                title += $" ({item.Year.ToString(CultureInfo.InvariantCulture)})";
            }

            var quality = Words((item.Quality ?? string.Empty).Replace('-', ' '));

            if (quality != "Unknown")
            {
                title += $" [{quality}]";
            }

            return title.Length > MaxLength ? title[..MaxLength].TrimEnd() : title;
        }

        // Free text kept as words, without anything a title or a folder name cannot hold.
        private static string Words(string value)
        {
            var words = (value ?? string.Empty)
                .Select(c => char.IsControl(c) || c is '/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|' or '[' or ']' ? ' ' : c)
                .ToArray();

            var text = string.Join(" ", new string(words).Split(' ', StringSplitOptions.RemoveEmptyEntries));

            return text.Length > 0 ? text : "Unknown";
        }

        // What an Arr app calls a quality, written the way release names write it.
        // A revision the way the Arr apps read one from a release name (Sonarr's QualityParser, which
        // the others share): PROPER is version 2, REPACK a version 2 repack, REPACK2 a version 3 one,
        // and so on, and each REAL counts. A later version that is not a repack can only be written
        // PROPER: REPACK2 would make it a repack, which an Arr app turns down from another group.
        public static IEnumerable<string> RevisionTokens(int version, int real, bool isRepack)
        {
            for (var i = 0; i < Math.Min(real, 3); i++)
            {
                yield return "REAL";
            }

            if (version <= 1)
            {
                yield break;
            }

            if (isRepack)
            {
                yield return version == 2 ? "REPACK" : $"REPACK{Math.Min(version - 1, 9)}";
            }
            else
            {
                yield return "PROPER";
            }
        }

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

        // Couch Casting-X as CouchCastingX, the way studios are written in release names.
        private static string RunTogether(string studio)
        {
            return string.Concat(Tokens(studio, keepDashes: false).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
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
