using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class NuveiBatches
{
    public DateTime BatchCloseDate { get; set; }

    public int BatchId { get; set; }

    public decimal BatchNet { get; set; }

    public decimal SaleAmt { get; set; }

    public decimal? ReturnAmt { get; set; }

    public int Ai { get; set; }
}
