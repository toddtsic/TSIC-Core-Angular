using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class RegistrationAccounting
{
    public Guid? RegistrationId { get; set; }

    public int AId { get; set; }

    public bool? Active { get; set; }

    public string? AdnCc4 { get; set; }

    public string? AdnCcexpDate { get; set; }

    public string? AdnInvoiceNo { get; set; }

    public string? AdnTransactionId { get; set; }

    public string? CheckNo { get; set; }

    public string? Comment { get; set; }

    public DateTime? Createdate { get; set; }

    public decimal? Dueamt { get; set; }

    public string? LebUserId { get; set; }

    public DateTime Modified { get; set; }

    public decimal? Payamt { get; set; }

    public Guid PaymentMethodId { get; set; }

    public string? Paymeth { get; set; }

    public string? PromoCode { get; set; }

    public Guid? TeamId { get; set; }

    public int? DiscountCodeAi { get; set; }

    public virtual JobDiscountCodes? DiscountCodeAiNavigation { get; set; }

    public virtual AspNetUsers? LebUser { get; set; }

    public virtual AccountingPaymentMethods PaymentMethod { get; set; } = null!;

    public virtual Registrations? Registration { get; set; }

    public virtual Teams? Team { get; set; }
}
