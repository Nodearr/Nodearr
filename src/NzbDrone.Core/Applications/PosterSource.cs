using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.Applications
{
    // What a poster is of: a series or a Whisparr v2 scene, a movie or a Whisparr v3 film or scene,
    // or an album.
    public enum PosterSubject
    {
        Series,
        Movie,
        Album
    }

    // An Arr app that can hand over the poster it keeps for a title. The Arr apps download each
    // title's images when they add it, and keep resized copies on disk.
    public interface IPosterSource
    {
        // The image's bytes, or null when the app has none for it.
        byte[] GetPoster(PosterSubject subject, int arrId);
    }

    public static class PosterImages
    {
        // Larger is refused: no poster the Arr apps keep comes near it.
        public const int MaxBytes = 2 * 1024 * 1024;

        // The Arr apps' 500-pixel-wide copy, where they keep one, from their media cover API.
        public static byte[] FromMediaCover(IArrV3Proxy proxy, IArrConnection settings, string path)
        {
            return Checked(proxy.GetBytes(settings, $"/mediacover/{path}"));
        }

        // From the image paths the app gives a title, for an app whose media cover API does not
        // reach every image it keeps, as Whisparr v3's scenes. Its resized copy first.
        public static byte[] FromImages(IArrV3Proxy proxy, IArrConnection settings, JToken images, params string[] coverTypes)
        {
            var url = coverTypes
                .Select(t => images?.FirstOrDefault(i => string.Equals(i.Value<string>("coverType"), t, StringComparison.OrdinalIgnoreCase))?.Value<string>("url"))
                .FirstOrDefault(u => u != null && u.StartsWith("/MediaCover/", StringComparison.OrdinalIgnoreCase));

            if (url == null)
            {
                return null;
            }

            var path = url.Split('?')[0];
            var dot = path.LastIndexOf('.');
            var resized = dot > 0 ? $"{path[..dot]}-500{path[dot..]}" : path;

            return Checked(proxy.GetBytes(settings, resized, false)) ?? Checked(proxy.GetBytes(settings, path, false));
        }

        // A JPEG or a PNG, and not too large. Anything else is no poster.
        public static byte[] Checked(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length > MaxBytes)
            {
                return null;
            }

            var jpeg = bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF;
            var png = bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47;

            return jpeg || png ? bytes : null;
        }

        public static string ContentType(byte[] bytes)
        {
            return bytes is { Length: > 0 } && bytes[0] == 0x89 ? "image/png" : "image/jpeg";
        }
    }
}
