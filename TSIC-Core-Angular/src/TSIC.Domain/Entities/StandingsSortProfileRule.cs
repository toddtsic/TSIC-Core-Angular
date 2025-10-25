using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StandingsSortProfileRule
{
    public int StandingsSortProfileRuleId { get; set; }

    public int StandingsSortProfileId { get; set; }

    public int StandingsSortRuleId { get; set; }

    public int SortOrder { get; set; }

    public virtual StandingsSortProfile StandingsSortProfile { get; set; } = null!;

    public virtual StandingsSortRule StandingsSortRule { get; set; } = null!;
}
