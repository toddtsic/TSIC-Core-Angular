using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class StandingsSortProfileRules
{
    public int StandingsSortProfileRuleId { get; set; }

    public int StandingsSortProfileId { get; set; }

    public int StandingsSortRuleId { get; set; }

    public int SortOrder { get; set; }

    public virtual StandingsSortProfiles StandingsSortProfile { get; set; } = null!;

    public virtual StandingsSortRules StandingsSortRule { get; set; } = null!;
}
