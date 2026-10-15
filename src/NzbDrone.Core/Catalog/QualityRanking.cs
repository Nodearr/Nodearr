using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;

namespace NzbDrone.Core.Catalog
{
    // How a quality profile in Sonarr or Radarr orders its qualities, kept so that "better than
    // yours" means what it means to that app. Ranks go from 0, the least wanted, upwards. Qualities
    // in one group share a rank, as the Arr apps treat them as equal.
    public class QualityRanking
    {
        public bool UpgradeAllowed { get; set; }

        // The rank at which the Arr app stops looking for something better. -1 when not known.
        public int CutoffRank { get; set; } = -1;

        public List<RankedQuality> Qualities { get; set; } = new();

        public RankedQuality Find(string qualityName)
        {
            if (qualityName.IsNullOrWhiteSpace())
            {
                return null;
            }

            return Qualities.FirstOrDefault(q => string.Equals(q.Name, qualityName, StringComparison.OrdinalIgnoreCase));
        }

        // Whether the Arr app would take a file of this quality in place of one of the current
        // quality: the new one is allowed, ranks higher, upgrades are on and the cutoff is not met.
        // Custom formats are left to the Arr app, which judges them when it searches.
        public bool IsUpgrade(string current, string candidate)
        {
            var wanted = Find(candidate);

            if (wanted == null || !wanted.Allowed || !UpgradeAllowed)
            {
                return false;
            }

            var held = Find(current);

            if (held == null)
            {
                // A file the profile does not rank is one the Arr app would replace with anything allowed.
                return true;
            }

            if (CutoffRank >= 0 && held.Rank >= CutoffRank)
            {
                return false;
            }

            return wanted.Rank > held.Rank;
        }

        public string ToStored()
        {
            return this.ToJson();
        }

        public static QualityRanking FromStored(string stored)
        {
            if (stored.IsNullOrWhiteSpace())
            {
                return null;
            }

            try
            {
                return Json.Deserialize<QualityRanking>(stored);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    public class RankedQuality
    {
        public string Name { get; set; }
        public int Rank { get; set; }
        public bool Allowed { get; set; }
    }
}
