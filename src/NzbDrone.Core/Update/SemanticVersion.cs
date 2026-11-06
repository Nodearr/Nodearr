using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Update
{
    // A release version as tagged, 0.10.0 or 0.10.0-beta.1, compared as semver.org says: by the three
    // numbers, then a version without a pre-release tag after one with, then the tag's parts in turn,
    // numbers by value and words by text. Build metadata after a + is ignored.
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        private static readonly Regex Pattern = new Regex(@"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$", RegexOptions.Compiled);

        private SemanticVersion(int major, int minor, int patch, string preRelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            PreRelease = preRelease ?? string.Empty;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string PreRelease { get; }

        public bool IsPreRelease => PreRelease.Length > 0;

        // The four-number version .NET and the update history use, without the pre-release tag.
        public Version ToVersion() => new Version(Major, Minor, Patch, 0);

        public static bool TryParse(string text, out SemanticVersion version)
        {
            version = null;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var match = Pattern.Match(text.Trim());

            if (!match.Success ||
                !int.TryParse(match.Groups[1].Value, out var major) ||
                !int.TryParse(match.Groups[2].Value, out var minor) ||
                !int.TryParse(match.Groups[3].Value, out var patch))
            {
                return false;
            }

            var preRelease = match.Groups[4].Success ? match.Groups[4].Value : null;

            if (preRelease != null && preRelease.Split('.').Any(p => p.Length == 0))
            {
                return false;
            }

            version = new SemanticVersion(major, minor, patch, preRelease);
            return true;
        }

        public static SemanticVersion Parse(string text)
        {
            if (!TryParse(text, out var version))
            {
                throw new FormatException($"'{text}' is not a version such as 0.10.0 or 0.10.0-beta.1");
            }

            return version;
        }

        public int CompareTo(SemanticVersion other)
        {
            if (other is null)
            {
                return 1;
            }

            var result = Major.CompareTo(other.Major);

            if (result == 0)
            {
                result = Minor.CompareTo(other.Minor);
            }

            if (result == 0)
            {
                result = Patch.CompareTo(other.Patch);
            }

            if (result != 0)
            {
                return result;
            }

            if (!IsPreRelease || !other.IsPreRelease)
            {
                return other.IsPreRelease.CompareTo(IsPreRelease);
            }

            var mine = PreRelease.Split('.');
            var theirs = other.PreRelease.Split('.');

            for (var i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
            {
                var mineIsNumber = int.TryParse(mine[i], out var mineNumber) && mine[i].All(char.IsAsciiDigit);
                var theirsIsNumber = int.TryParse(theirs[i], out var theirsNumber) && theirs[i].All(char.IsAsciiDigit);

                if (mineIsNumber && theirsIsNumber)
                {
                    result = mineNumber.CompareTo(theirsNumber);
                }
                else if (mineIsNumber != theirsIsNumber)
                {
                    result = mineIsNumber ? -1 : 1;
                }
                else
                {
                    result = string.CompareOrdinal(mine[i], theirs[i]);
                }

                if (result != 0)
                {
                    return Math.Sign(result);
                }
            }

            return mine.Length.CompareTo(theirs.Length);
        }

        public bool Equals(SemanticVersion other) => CompareTo(other) == 0;

        public override bool Equals(object obj) => obj is SemanticVersion other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, PreRelease);

        public override string ToString() => IsPreRelease ? $"{Major}.{Minor}.{Patch}-{PreRelease}" : $"{Major}.{Minor}.{Patch}";

        public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;

        public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;

        public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;

        public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
    }
}
