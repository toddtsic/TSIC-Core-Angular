using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class FeeModifiers
{
    public Guid FeeModifierId { get; set; }

    public Guid JobFeeId { get; set; }

    public string ModifierType { get; set; } = null!;

    public decimal Amount { get; set; }

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual JobFees JobFee { get; set; } = null!;

    public virtual AspNetUsers? LebUser { get; set; }
}
