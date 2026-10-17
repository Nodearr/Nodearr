using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Newtonsoft.Json.Linq;
using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Applications
{
    public class LibraryAddException : NzbDroneException
    {
        public LibraryAddException(string message)
            : base(message)
        {
        }
    }

    public class SeasonEpisode
    {
        public int SeasonNumber { get; set; }
        public int EpisodeNumber { get; set; }
    }

    // What a friend sent, as this node's Arr app is asked to add it and look for it.
    public class LibraryAddRequest
    {
        public int TvdbId { get; set; }
        public int TmdbId { get; set; }
        public string ImdbId { get; set; }
        public string ForeignArtistId { get; set; }

        // The Arr app's own ID for it, where the local catalog says the app has it already.
        public int? ExistingArrId { get; set; }

        // The whole title, and so what arrives in it later too. Otherwise these parts of it.
        public bool Whole { get; set; }
        public List<int> Seasons { get; set; } = new();
        public List<SeasonEpisode> Episodes { get; set; } = new();
        public List<string> Albums { get; set; } = new();

        // How to add it, when it is not there yet.
        public string RootFolderPath { get; set; }
        public int QualityProfileId { get; set; }
        public int? MetadataProfileId { get; set; }
        public string SeriesType { get; set; }
        public bool SeasonFolder { get; set; } = true;
    }

    public class LibraryRootFolder
    {
        public string Path { get; set; }
        public long? FreeSpace { get; set; }
    }

    public class LibraryProfile
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    // What the Arr app offers to add something with.
    public class LibraryAddChoices
    {
        public List<LibraryRootFolder> RootFolders { get; set; } = new();
        public List<LibraryProfile> QualityProfiles { get; set; } = new();

        // Lidarr only.
        public List<LibraryProfile> MetadataProfiles { get; set; } = new();
    }

    // An Arr app Nodearr can add a title to, monitor what was sent of it, and search for it.
    public interface ILibraryAdder
    {
        LibraryAddChoices GetAddChoices();

        // Says what was done, such as "Added to Sonarr, monitoring Season 1, and searching".
        string AddAndSearch(LibraryAddRequest request);
    }

    // What every Arr app's add path has in common.
    public static class LibraryAdd
    {
        // How long an Arr app may take to read a title it has just added, before it can be
        // monitored by part and searched. Sonarr and Lidarr read it from their metadata servers.
        public static TimeSpan RefreshWait { get; set; } = TimeSpan.FromSeconds(120);
        public static TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(3);

        public static LibraryAddChoices Choices(IArrV3Proxy proxy, IArrConnection settings, bool metadataProfiles)
        {
            var choices = new LibraryAddChoices
            {
                RootFolders = proxy.GetArray(settings, "/rootfolder")
                    .Select(f => new LibraryRootFolder { Path = f.Value<string>("path"), FreeSpace = f.Value<long?>("freeSpace") })
                    .Where(f => !string.IsNullOrWhiteSpace(f.Path))
                    .ToList(),
                QualityProfiles = Profiles(proxy.GetArray(settings, "/qualityprofile"))
            };

            if (metadataProfiles)
            {
                choices.MetadataProfiles = Profiles(proxy.GetArray(settings, "/metadataprofile"));
            }

            return choices;
        }

        // A root folder and a profile the Arr app has, so a request cannot point it anywhere else.
        public static void CheckOptions(LibraryAddChoices choices, LibraryAddRequest request, string appName)
        {
            if (!choices.RootFolders.Any(f => string.Equals(f.Path, request.RootFolderPath, StringComparison.Ordinal)))
            {
                throw new LibraryAddException($"{appName} has no root folder {request.RootFolderPath}");
            }

            if (choices.QualityProfiles.All(p => p.Id != request.QualityProfileId))
            {
                throw new LibraryAddException($"{appName} has no such quality profile");
            }
        }

        // Asks until the answer will do, or the wait is up. The last answer either way.
        public static T WaitFor<T>(Func<T> ask, Func<T, bool> ready)
        {
            var until = DateTime.UtcNow + RefreshWait;
            var answer = ask();

            while (!ready(answer) && DateTime.UtcNow < until)
            {
                Thread.Sleep(PollInterval);
                answer = ask();
            }

            return answer;
        }

        // Whether the Arr app is still running a command of this name, such as the refresh it
        // starts on a title it has just added. It sets what is monitored as that ends.
        public static bool IsRunning(IArrV3Proxy proxy, IArrConnection settings, string commandName)
        {
            return proxy.GetArray(settings, "/command")
                .Any(c => c.Value<string>("name") == commandName && c.Value<string>("status") is "queued" or "started");
        }

        public static string Join(IReadOnlyList<string> items)
        {
            return items.Count switch
            {
                0 => string.Empty,
                1 => items[0],
                _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1]
            };
        }

        private static List<LibraryProfile> Profiles(JArray profiles)
        {
            return profiles.Select(p => new LibraryProfile { Id = p.Value<int>("id"), Name = p.Value<string>("name") }).ToList();
        }
    }
}
