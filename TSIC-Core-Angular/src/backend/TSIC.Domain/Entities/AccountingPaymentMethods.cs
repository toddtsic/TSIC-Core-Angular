using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class AccountingPaymentMethods
{
    public string? LebUserId { get; set; }

    public DateTime? Modified { get; set; }

    public string? PaymentMethod { get; set; }

    public Guid PaymentMethodId { get; set; }

    public int Ai { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual ICollection<RegistrationAccounting> RegistrationAccounting { get; set; } = new List<RegistrationAccounting>();

    public virtual ICollection<StoreCartBatchAccounting> StoreCartBatchAccounting { get; set; } = new List<StoreCartBatchAccounting>();
}
