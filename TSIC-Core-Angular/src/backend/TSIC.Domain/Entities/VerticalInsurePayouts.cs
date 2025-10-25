using System;
using System.Collections.Generic;

namespace TSIC.Domain.Entities;

public partial class VerticalInsurePayouts
{
    public double? Year { get; set; }

    public double? Month { get; set; }

    public string? PolicyNumber { get; set; }

    public string? PurchaseDateString { get; set; }

    public DateTime? PolicyEffectiveDate { get; set; }

    public decimal? NetWrittenPremium { get; set; }

    public decimal? Payout { get; set; }

    public DateTime? PurchaseDate { get; set; }

    public Guid Id { get; set; }
}
