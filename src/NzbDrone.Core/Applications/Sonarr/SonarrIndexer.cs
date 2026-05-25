using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace NzbDrone.Core.Applications.Sonarr
{
    public class SonarrIndexer
    {
        public int Id { get; set; }
        public bool EnableRss { get; set; }
        public bool EnableAutomaticSearch { get; set; }
        public bool EnableInteractiveSearch { get; set; }
        public int Priority { get; set; }
        public string Name { get; set; }
        public string ImplementationName { get; set; }
        public string Implementation { get; set; }
        public string ConfigContract { get; set; }
        public string InfoLink { get; set; }
        public int? DownloadClientId { get; set; }
        public int? SeasonSearchMaximumSingleEpisodeAge { get; set; }
        public HashSet<int> Tags { get; set; }
        public List<SonarrField> Fields { get; set; }

        public bool Equals(SonarrIndexer other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            var apiKey = GetStringField("apiKey");
            var otherApiKey = other.GetStringField("apiKey");
            var apiKeyCompare = apiKey == otherApiKey || otherApiKey == "********";

            return other.EnableRss == EnableRss &&
                   other.EnableAutomaticSearch == EnableAutomaticSearch &&
                   other.EnableInteractiveSearch == EnableInteractiveSearch &&
                   other.Name == Name &&
                   other.Implementation == Implementation &&
                   other.Priority == Priority &&
                   other.Id == Id &&
                   apiKeyCompare &&
                   GetStringField("apiPath") == other.GetStringField("apiPath") &&
                   GetStringField("baseUrl") == other.GetStringField("baseUrl") &&
                   JToken.DeepEquals(GetArrayField("categories"), other.GetArrayField("categories")) &&
                   JToken.DeepEquals(GetArrayField("animeCategories"), other.GetArrayField("animeCategories")) &&
                   GetBoolField("animeStandardFormatSearch") == other.GetBoolField("animeStandardFormatSearch") &&
                   GetIntField("minimumSeeders") == other.GetIntField("minimumSeeders") &&
                   GetDoubleField("seedCriteria.seedRatio") == other.GetDoubleField("seedCriteria.seedRatio") &&
                   GetIntField("seedCriteria.seedTime") == other.GetIntField("seedCriteria.seedTime") &&
                   GetIntField("seedCriteria.seasonPackSeedTime") == other.GetIntField("seedCriteria.seasonPackSeedTime") &&
                   GetBoolField("rejectBlocklistedTorrentHashesWhileGrabbing") == other.GetBoolField("rejectBlocklistedTorrentHashesWhileGrabbing");
        }

        private object GetFieldValue(string fieldName)
        {
            return Fields?.FirstOrDefault(x => x.Name == fieldName)?.Value;
        }

        private string GetStringField(string fieldName)
        {
            return GetFieldValue(fieldName)?.ToString();
        }

        private bool? GetBoolField(string fieldName)
        {
            var value = GetFieldValue(fieldName);
            return value == null ? null : Convert.ToBoolean(value);
        }

        private int? GetIntField(string fieldName)
        {
            var value = GetFieldValue(fieldName);
            return value == null ? null : Convert.ToInt32(value);
        }

        private double? GetDoubleField(string fieldName)
        {
            var value = GetFieldValue(fieldName);
            return value == null ? null : Convert.ToDouble(value);
        }

        private JArray GetArrayField(string fieldName)
        {
            var value = GetFieldValue(fieldName);

            if (value == null)
            {
                return new JArray();
            }

            if (value is JArray array)
            {
                return array;
            }

            if (value is JToken token)
            {
                return token.Type == JTokenType.Array ? (JArray)token : JArray.FromObject(token);
            }

            return JArray.FromObject(value);
        }
    }
}
