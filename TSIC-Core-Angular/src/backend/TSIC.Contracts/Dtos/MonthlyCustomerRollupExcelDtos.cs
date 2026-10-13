namespace TSIC.Contracts.Dtos;

/// <summary>
/// One month of settled card and check revenue for a customer, or for the fixed group of
/// customers it belongs to — the EF replacement for the data behind the
/// <c>adn.monthlycustomerrollups</c> proc.
/// </summary>
public record MonthlyCustomerRollupDto
{
    /// <summary>True when the customer belongs to a group, so the report spans several customers.</summary>
    public bool SpansCustomerGroup { get; init; }

    /// <summary>True for the STEPS group, whose report omits the per-customer summary rows.</summary>
    public bool StepsGroup { get; init; }

    /// <summary>Jobs with settled card activity and a statistics row for the month, in job-name order.</summary>
    public required List<MonthlyCustomerRollupJobDto> Jobs { get; init; }
}

/// <summary>One job's month: the raw sums the report's fees and net deposit are worked out from.</summary>
public record MonthlyCustomerRollupJobDto
{
    public string? CustomerName { get; init; }
    public Guid JobId { get; init; }
    public string? JobName { get; init; }

    /// <summary>Settled card payments. Null only when no settled amount is known at all.</summary>
    public decimal? CardRevenue { get; init; }

    /// <summary>Settled card credits (negative amounts).</summary>
    public decimal? CardRefunds { get; init; }

    /// <summary>Payments and credits added together without their signs: the base for the card fee.</summary>
    public decimal? SettledAbsolute { get; init; }

    public decimal? ProcessingFeePercent { get; init; }
    public int? NewPlayers { get; init; }
    public int? NewTeams { get; init; }
    public decimal? PerPlayerCharge { get; init; }
    public decimal? PerTeamCharge { get; init; }

    /// <summary>Check payments recorded in the month; null when there were none.</summary>
    public decimal? CheckRevenue { get; init; }
}
