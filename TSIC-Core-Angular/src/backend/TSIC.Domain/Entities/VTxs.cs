using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class VTxs
{
    public DateTime? SettlementTs { get; set; }

    public int? SettlementMonth { get; set; }

    public int? SettlementYear { get; set; }

    public string? CustomerName { get; set; }

    public Guid? CustomerId { get; set; }

    public string? JobName { get; set; }

    public Guid? JobId { get; set; }

    public string? PaymentMethod { get; set; }

    public string? Registrant { get; set; }

    public string? RegistrantFirstName { get; set; }

    public string? RegistrantLastName { get; set; }

    public int BOldSysTx { get; set; }

    public string? TransactionStatus { get; set; }

    public decimal? SettlementAmount { get; set; }

    public string? SettlementDateTime { get; set; }

    public string? AuthorizationAmount { get; set; }

    public string TransactionId { get; set; } = null!;

    public string? ReferenceTransactionId { get; set; }

    public string? TransactionType { get; set; }

    public string? InvoiceNumber { get; set; }
}
