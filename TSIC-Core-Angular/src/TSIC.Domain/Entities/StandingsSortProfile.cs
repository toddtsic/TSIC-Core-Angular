using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StandingsSortProfile
{
    public int StandingsSortProfileId { get; set; }

    public string StandingsSortProfileName { get; set; } = null!;

    public virtual ICollection<League> Leagues { get; set; } = new List<League>();

    public virtual ICollection<StandingsSortProfileRule> StandingsSortProfileRules { get; set; } = new List<StandingsSortProfileRule>();
}
