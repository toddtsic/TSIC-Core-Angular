using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AccountingApplyToSummaries
{
    public Guid ApplyToId { get; set; }

    public decimal SumAmtPaid { get; set; }

    public decimal SumFees { get; set; }

    public decimal SumOwed { get; set; }

    public decimal PayamtBase { get; set; }

    public decimal PayamtDc { get; set; }

    public decimal PayamtDon { get; set; }

    public decimal PayamtLf { get; set; }

    public string? MaxAdntransactionId { get; set; }

    public virtual Registrations ApplyTo { get; set; } = null!;
}
