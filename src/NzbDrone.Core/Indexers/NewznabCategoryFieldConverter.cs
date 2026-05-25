using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Indexers.Newznab;

namespace NzbDrone.Core.Indexers
{
    public class NewznabCategoryFieldConverter : ISelectOptionsConverter
    {
        public List<SelectOption> GetSelectOptions()
        {
            return NewznabCategoryFieldOptionsConverter.GetFieldSelectOptions(null)
                .Select((option, index) => new SelectOption
                {
                    Value = option.Value,
                    Name = option.Name,
                    Hint = option.Hint,
                    Order = index
                })
                .ToList();
        }
    }
}
