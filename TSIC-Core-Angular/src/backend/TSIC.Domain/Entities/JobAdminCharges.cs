using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobAdminCharges
{
    public int Id { get; set; }

    public int ChargeTypeId { get; set; }

    public decimal ChargeAmount { get; set; }

    public string? Comment { get; set; }

    public Guid JobId { get; set; }

    public int Year { get; set; }

    public int Month { get; set; }

    public DateTime CreateDate { get; set; }

    public virtual JobAdminChargeTypes ChargeType { get; set; } = null!;

    public virtual Jobs Job { get; set; } = null!;
}
