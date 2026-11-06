using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace NzbDrone.Core.Update
{
    // What https://updates.nodearr.com/v1/<branch>.json says about the newest release, once its
    // signature has been checked (ADR 0024). build/Publish-UpdateManifest.ps1 writes it.
    //
    //   {
    //     "format": 1,
    //     "branch": "main",
    //     "version": "0.10.0",
    //     "released": "2026-11-10T12:00:00Z",
    //     "changelog": "https://nodearr.com/changelog/#0100",
    //     "summary": "One sentence on what the release is for.",
    //     "changes": [ { "heading": "Added", "items": [ "..." ] } ],
    //     "image": "ghcr.io/nodearr/nodearr:0.10.0",
    //     "rollout": 100,
    //     "packages": {
    //       "win-x64": { "file": "Nodearr.0.10.0.win-x64.zip", "url": "https://github.com/...", "size": 1, "sha256": "..." }
    //     }
    //   }
    public class UpdateManifest
    {
        public const int Format = 1;

        private static readonly Regex Sha256Pattern = new Regex("^[0-9a-f]{64}$", RegexOptions.Compiled);

        public string Branch { get; set; }
        public SemanticVersion Version { get; set; }
        public DateTime Released { get; set; }
        public string Changelog { get; set; }
        public string Summary { get; set; }
        public List<UpdateManifestChanges> Changes { get; set; } = new List<UpdateManifestChanges>();
        public string Image { get; set; }

        // The share of nodes, 1 to 100, that take this release.
        public int Rollout { get; set; } = 100;

        // By .NET runtime identifier: win-x64, linux-musl-arm64 and so on.
        public Dictionary<string, UpdateManifestPackage> Packages { get; set; } = new Dictionary<string, UpdateManifestPackage>(StringComparer.OrdinalIgnoreCase);

        // Throws FormatException when the manifest is not one this version of Nodearr understands.
        public static UpdateManifest Parse(byte[] json)
        {
            try
            {
                using var document = JsonDocument.Parse(json);
                return Read(document.RootElement);
            }
            catch (JsonException e)
            {
                throw new FormatException("The update manifest is not JSON", e);
            }
            catch (InvalidOperationException e)
            {
                throw new FormatException("The update manifest has a value of the wrong kind", e);
            }
        }

        private static UpdateManifest Read(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The update manifest is not an object");
            }

            if (!root.TryGetProperty("format", out var format) || format.GetInt32() != Format)
            {
                throw new FormatException($"The update manifest is not format {Format}");
            }

            var manifest = new UpdateManifest
            {
                Branch = RequiredText(root, "branch"),
                Version = SemanticVersion.Parse(RequiredText(root, "version")),
                Released = DateTime.Parse(RequiredText(root, "released"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal),
                Changelog = OptionalHttpsUrl(root, "changelog"),
                Summary = OptionalText(root, "summary"),
                Image = OptionalText(root, "image")
            };

            if (root.TryGetProperty("rollout", out var rollout))
            {
                manifest.Rollout = rollout.GetInt32();

                if (manifest.Rollout < 1 || manifest.Rollout > 100)
                {
                    throw new FormatException("The update manifest's rollout is not from 1 to 100");
                }
            }

            if (root.TryGetProperty("changes", out var changes))
            {
                foreach (var section in changes.EnumerateArray())
                {
                    manifest.Changes.Add(new UpdateManifestChanges
                    {
                        Heading = RequiredText(section, "heading"),
                        Items = section.GetProperty("items").EnumerateArray().Select(i => i.GetString()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList()
                    });
                }
            }

            if (!root.TryGetProperty("packages", out var packages) || packages.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("The update manifest lists no packages");
            }

            foreach (var entry in packages.EnumerateObject())
            {
                var package = new UpdateManifestPackage
                {
                    File = RequiredText(entry.Value, "file"),
                    Url = OptionalHttpsUrl(entry.Value, "url") ?? throw new FormatException($"The {entry.Name} package has no address"),
                    Size = entry.Value.GetProperty("size").GetInt64(),
                    Sha256 = RequiredText(entry.Value, "sha256").ToLowerInvariant()
                };

                if (package.Size <= 0 || !Sha256Pattern.IsMatch(package.Sha256) || package.File.IndexOfAny(new[] { '/', '\\' }) >= 0)
                {
                    throw new FormatException($"The {entry.Name} package is not described properly");
                }

                manifest.Packages[entry.Name] = package;
            }

            return manifest;
        }

        private static string RequiredText(JsonElement element, string name)
        {
            var text = OptionalText(element, name);

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new FormatException($"The update manifest has no {name}");
            }

            return text;
        }

        private static string OptionalText(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;
        }

        private static string OptionalHttpsUrl(JsonElement element, string name)
        {
            var text = OptionalText(element, name);

            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            // http only on this machine, for a manifest and packages served locally to test an update.
            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) ||
                !(uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            {
                throw new FormatException($"The update manifest's {name} is not an https address");
            }

            return text;
        }
    }

    public class UpdateManifestChanges
    {
        public string Heading { get; set; }
        public List<string> Items { get; set; } = new List<string>();
    }

    public class UpdateManifestPackage
    {
        public string File { get; set; }
        public string Url { get; set; }
        public long Size { get; set; }
        public string Sha256 { get; set; }
    }
}
