using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class StandingsSortProfiles
{
    public int StandingsSortProfileId { get; set; }

    public string StandingsSortProfileName { get; set; } = null!;

    public virtual ICollection<Leagues> Leagues { get; set; } = new List<Leagues>();

    public virtual ICollection<StandingsSortProfileRules> StandingsSortProfileRules { get; set; } = new List<StandingsSortProfileRules>();
}
