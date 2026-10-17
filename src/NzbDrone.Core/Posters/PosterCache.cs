using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Posters
{
    // Posters kept on disk, each under a key, and the knowledge that a title has none. A poster is
    // kept until it is older than the caller allows; "none" until the time it was given.
    public class PosterCache
    {
        private static readonly Regex Plain = new("^[a-z0-9-]{1,80}$", RegexOptions.Compiled);

        private readonly string _folder;

        public PosterCache(string folder)
        {
            _folder = folder;
        }

        // Found: the poster, or null when the title is known to have none for now. Not found:
        // nothing is known, or what was known is too old.
        public bool TryGet(string key, TimeSpan maxAge, out byte[] poster)
        {
            poster = null;

            var none = PathOf(key, ".none");

            if (File.Exists(none) && DateTime.TryParse(File.ReadAllText(none), null, System.Globalization.DateTimeStyles.RoundtripKind, out var until) && until > DateTime.UtcNow)
            {
                return true;
            }

            var image = PathOf(key, ".img");

            if (File.Exists(image) && DateTime.UtcNow - File.GetLastWriteTimeUtc(image) < maxAge)
            {
                poster = File.ReadAllBytes(image);

                return true;
            }

            return false;
        }

        public void Put(string key, byte[] poster)
        {
            Directory.CreateDirectory(_folder);
            File.Delete(PathOf(key, ".none"));

            // Written whole, then moved, so a reader never sees half a poster.
            var image = PathOf(key, ".img");
            var temp = image + ".tmp";

            File.WriteAllBytes(temp, poster);
            File.Move(temp, image, true);
        }

        public void PutNone(string key, TimeSpan forHowLong)
        {
            Directory.CreateDirectory(_folder);
            File.WriteAllText(PathOf(key, ".none"), (DateTime.UtcNow + forHowLong).ToString("o"));
        }

        // A key is a file name: plain ones as they are, anything else by its hash.
        private string PathOf(string key, string extension)
        {
            var name = key.ToLowerInvariant();

            if (!Plain.IsMatch(name))
            {
                name = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant()[..32];
            }

            return Path.Combine(_folder, name + extension);
        }
    }
}
