using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class JobInvoiceNumber
{
    public int CustomerAi { get; set; }

    public int InvoiceNumberId { get; set; }

    public string? InvoiceNumber { get; set; }

    public int JobAi { get; set; }
}
