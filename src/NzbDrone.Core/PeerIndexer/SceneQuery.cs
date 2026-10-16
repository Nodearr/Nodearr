using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.PeerCatalog;

namespace NzbDrone.Core.PeerIndexer
{
    // Whisparr has no ID to search for a scene by. It searches by text, several ways for each
    // scene, depending on how it is set up: the title; the title and the release date; the studio
    // and the title; the studio and the date; the studio and the scene's code. The studio can be
    // its name, an alias, or its name run together (Couch Casting-X, CouchCastingX). The date can
    // be yy.MM.dd or dd.MM.yyyy. The title has a leading "The" taken off, "&" made "and", and
    // apostrophes and accents taken out. See Whisparr's ReleaseSearchService (ADR 0015, amendments).
    //
    // A scene matches when a date in the query is its release date, and every other word in the
    // query is in its studio, title, code or release title.
    public class SceneQuery
    {
        private SceneQuery(string releaseDate, List<string> words)
        {
            ReleaseDate = releaseDate;
            Words = words;
        }

        // yyyy-MM-dd, or null when the query names no day.
        public string ReleaseDate { get; }

        // The rest of the query, lower case, without accents.
        public List<string> Words { get; }

        public bool IsEmpty => ReleaseDate == null && Words.Count == 0;

        public static SceneQuery Parse(string text)
        {
            var words = Split(text);

            for (var i = 0; i < words.Count; i++)
            {
                // Three numbers that make a day, either way round, or yyyy MM dd.
                if (i + 2 < words.Count && TryDate(words[i], words[i + 1], words[i + 2], out var day))
                {
                    words.RemoveRange(i, 3);

                    return new SceneQuery(day, words);
                }

                // yyMMdd, run together.
                if (words[i].Length == 6 && words[i].All(char.IsDigit) && TryDate(words[i][..2], words[i][2..4], words[i][4..], out day))
                {
                    words.RemoveAt(i);

                    return new SceneQuery(day, words);
                }
            }

            return new SceneQuery(null, words);
        }

        public bool Matches(PeerCatalogItem item)
        {
            if (!item.IsAdult || IsEmpty)
            {
                return false;
            }

            if (ReleaseDate != null && item.ReleaseDate != ReleaseDate)
            {
                return false;
            }

            var haystack = Haystack(item);

            return Words.All(w => haystack.Any(h => h.Contains(w, StringComparison.Ordinal)));
        }

        private static List<string> Haystack(PeerCatalogItem item)
        {
            var words = new List<string>();

            foreach (var text in new[] { item.Studio, item.Title, item.Code, item.ReleaseTitle, item.SceneName })
            {
                words.AddRange(Split(text));

                // As Whisparr writes it: apostrophes and the like taken out, not split on.
                words.AddRange(Split((text ?? string.Empty).Replace("'", string.Empty).Replace("’", string.Empty).Replace("`", string.Empty)));
            }

            // The studio run together, as Whisparr's "clean" studio search writes it.
            var studio = string.Concat(Split(item.Studio));

            if (studio.Length > 0)
            {
                words.Add(studio);
            }

            if (item.Year > 0)
            {
                words.Add(item.Year.ToString(CultureInfo.InvariantCulture));
            }

            return words.Distinct().ToList();
        }

        private static List<string> Split(string text)
        {
            var cleaned = (text ?? string.Empty).Replace("&", " and ").RemoveDiacritics().ToLowerInvariant();

            return new string(cleaned.Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray())
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .ToList();
        }

        private static bool TryDate(string a, string b, string c, out string day)
        {
            day = null;

            if (!(a.All(char.IsDigit) && b.All(char.IsDigit) && c.All(char.IsDigit)))
            {
                return false;
            }

            string text;

            if (a.Length == 2 && b.Length == 2 && c.Length == 2)
            {
                // yy.MM.dd. A two-digit year is this century's.
                text = $"20{a}-{b}-{c}";
            }
            else if (a.Length == 2 && b.Length == 2 && c.Length == 4)
            {
                // dd.MM.yyyy
                text = $"{c}-{b}-{a}";
            }
            else if (a.Length == 4 && b.Length == 2 && c.Length == 2)
            {
                // yyyy.MM.dd
                text = $"{a}-{b}-{c}";
            }
            else
            {
                return false;
            }

            if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            {
                return false;
            }

            day = text;

            return true;
        }
    }
}
