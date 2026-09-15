using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobLeagues
{
    public Guid JobLeagueId { get; set; }

    public bool BIsPrimary { get; set; }

    public Guid JobId { get; set; }

    public Guid LeagueId { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public DateTime? LateFeeEnd { get; set; }

    public decimal? LateFee { get; set; }

    public DateTime? LateFeeStart { get; set; }

    public decimal? DiscountFee { get; set; }

    public DateTime? DiscountFeeStart { get; set; }

    public DateTime? DiscountFeeEnd { get; set; }

    public decimal? BaseFee { get; set; }

    public virtual Jobs Job { get; set; } = null!;

    public virtual Leagues League { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
