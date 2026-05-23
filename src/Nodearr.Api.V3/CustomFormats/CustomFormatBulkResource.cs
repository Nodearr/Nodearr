using System.Collections.Generic;

namespace Nodearr.Api.V3.CustomFormats
{
    public class CustomFormatBulkResource
    {
        public HashSet<int> Ids { get; set; } = new();
        public bool? IncludeCustomFormatWhenRenaming { get; set; }
    }
}
