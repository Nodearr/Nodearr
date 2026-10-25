using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Catalog
{
    // How a quality profile in Sonarr or Radarr orders its qualities, kept so that "better than
    // yours" means what it means to that app. Ranks go from 0, the least wanted, upwards. Qualities
    // in one group share a rank, as the Arr apps treat them as equal.
    public class QualityRanking
    {
        public bool UpgradeAllowed { get; set; }

        // The Arr app's Propers and Repacks setting is "Prefer and upgrade", its default, so a later
        // revision of the quality a file has replaces it. A ranking kept before this was read has it.
        public bool UpgradeToPropers { get; set; } = true;

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
            return IsUpgrade(current, new Revision(), candidate, new Revision());
        }

        // Sonarr's rule, which Radarr, Lidarr and Whisparr share: the same quality at a later
        // revision is taken even past the cutoff and with upgrades turned off, unless the app is
        // told not to upgrade to propers. Anything else as above.
        public bool IsUpgrade(string current, Revision currentRevision, string candidate, Revision candidateRevision)
        {
            var wanted = Find(candidate);

            if (wanted == null || !wanted.Allowed)
            {
                return false;
            }

            if (UpgradeToPropers && string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase) && candidateRevision.CompareTo(currentRevision) > 0)
            {
                return true;
            }

            if (!UpgradeAllowed)
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
