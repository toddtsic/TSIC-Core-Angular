using System;
using System.Collections.Generic;

namespace TSIC.Infrastructure.Data.SqlDbContext;

public partial class NuveiFunding
{
    public string FundingEvent { get; set; } = null!;

    public string? FundingType { get; set; }

    public string RefNumber { get; set; } = null!;

    public decimal FundingAmount { get; set; }

    public DateTime FundingDate { get; set; }

    public int Ai { get; set; }
}
