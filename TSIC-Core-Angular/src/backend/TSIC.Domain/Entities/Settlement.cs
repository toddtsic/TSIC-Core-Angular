using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class Settlement
{
    public Guid SettlementId { get; set; }

    public int RegistrationAccountingId { get; set; }

    public string AdnTransactionId { get; set; } = null!;

    public string Status { get; set; } = null!;

    public DateTime SubmittedAt { get; set; }

    public DateTime NextCheckAt { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public DateTime? SettledAt { get; set; }

    public string? ReturnReasonCode { get; set; }

    public string? ReturnReasonText { get; set; }

    public string? AccountLast4 { get; set; }

    public string? AccountType { get; set; }

    public string? NameOnAccount { get; set; }

    public DateTime Modified { get; set; }

    public string? LebUserId { get; set; }

    public virtual RegistrationAccounting RegistrationAccounting { get; set; } = null!;
}
