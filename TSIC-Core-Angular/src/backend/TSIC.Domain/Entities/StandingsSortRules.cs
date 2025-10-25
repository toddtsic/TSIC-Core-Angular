using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StandingsSortRules
{
    public int StandingsSortRuleId { get; set; }

    public string StandingsSortRuleName { get; set; } = null!;

    public string? StandingsSortRuleDescription { get; set; }

    public int? StandingsSortRuleConstraint { get; set; }

    public virtual ICollection<StandingsSortProfileRules> StandingsSortProfileRules { get; set; } = new List<StandingsSortProfileRules>();
}
