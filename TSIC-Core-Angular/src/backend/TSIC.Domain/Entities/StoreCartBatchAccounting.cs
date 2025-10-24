using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class StoreCartBatchAccounting
{
    public int StoreCartBatchAccountingId { get; set; }

    public int StoreCartBatchId { get; set; }

    public Guid PaymentMethodId { get; set; }

    public decimal Paid { get; set; }

    public DateTime CreateDate { get; set; }

    public string? Cclast4 { get; set; }

    public string? CcexpDate { get; set; }

    public string? AdnInvoiceNo { get; set; }

    public string? AdnTransactionId { get; set; }

    public string? Comment { get; set; }

    public int? DiscountCodeAi { get; set; }

    public DateTime Modified { get; set; }

    public string LebUserId { get; set; } = null!;

    public virtual JobDiscountCodes? DiscountCodeAiNavigation { get; set; }

    public virtual AspNetUsers LebUser { get; set; } = null!;

    public virtual AccountingPaymentMethods PaymentMethod { get; set; } = null!;

    public virtual StoreCartBatches StoreCartBatch { get; set; } = null!;
}
