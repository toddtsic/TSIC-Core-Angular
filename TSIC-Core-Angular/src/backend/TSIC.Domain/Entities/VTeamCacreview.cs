using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class VTeamCacreview
{
    public string? AgegroupName { get; set; }

    public string? TeamName { get; set; }

    public DateTime? Effectiveasofdate { get; set; }

    public DateTime? Expireondate { get; set; }

    public DateTime? Startdate { get; set; }

    public DateTime? Enddate { get; set; }

    public string? Gender { get; set; }

    public decimal? PerRegistrantFee { get; set; }

    public string? KeywordPairs { get; set; }

    public string? TeamComments { get; set; }
}
