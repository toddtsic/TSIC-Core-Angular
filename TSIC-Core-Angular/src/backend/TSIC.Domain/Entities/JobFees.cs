using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobFees
{
    public Guid JobFeeId { get; set; }

    public Guid JobId { get; set; }

    public string RoleId { get; set; } = null!;

    public Guid? AgegroupId { get; set; }

    public Guid? TeamId { get; set; }

    public decimal? Deposit { get; set; }

    public decimal? BalanceDue { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual Agegroups? Agegroup { get; set; }

    public virtual ICollection<FeeModifiers> FeeModifiers { get; set; } = new List<FeeModifiers>();

    public virtual Jobs Job { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual Teams? Team { get; set; }
}
