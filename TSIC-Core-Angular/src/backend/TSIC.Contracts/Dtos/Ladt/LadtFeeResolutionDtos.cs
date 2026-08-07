namespace TSIC.Contracts.Dtos.Ladt;

/// <summary>
/// The canonical fee-resolution map for the LADT editor grids: one entry per league,
/// agegroup, and team node, each resolved per role (Player, Club Rep) with the effective
/// amounts, payment phase, modifier winners, the SOURCE tier of each value, and a
/// downward summary of what more-specific scopes override.
///
/// Display-only. The charging path (<c>FeeRepository.GetResolvedFeeAsync</c>) is the
/// authority; <c>LadtFeeResolutionMapBuilder</c> re-implements the same cascade and the
/// equivalence suite (<c>LadtFeeResolutionMapTests</c>) pins the two together. Never
/// stamp or charge from this DTO.
/// </summary>
public record LadtFeeResolutionMapDto
{
    public IReadOnlyList<LadtFeeNodeResolutionDto> Nodes { get; init; } = [];
}

/// <summary>
/// One entry per league (Level 0), agegroup (Level 1 — system buckets included so their
/// grid rows join), and team (Level 3 — inactive included). Divisions carry no fee scope.
/// </summary>
public record LadtFeeNodeResolutionDto
{
    /// <summary>leagueId / agegroupId / teamId — the grid row PK the FE joins on.</summary>
    public required Guid NodeId { get; init; }

    /// <summary>0 = league, 1 = agegroup, 3 = team (mirrors the LADT tree levels).</summary>
    public required int Level { get; init; }

    public required LadtFeeRoleResolutionDto Player { get; init; }

    public required LadtFeeRoleResolutionDto ClubRep { get; init; }
}

/// <summary>
/// Fee resolution for one role at one node. Source tiers are the strings
/// "league" | "agegroup" | "team" — the vocabulary the grid tooltips already key on.
/// </summary>
public record LadtFeeRoleResolutionDto
{
    public required string RoleId { get; init; }

    /// <summary>True when ANY base-fee row exists in this node's cascade chain.
    /// Distinguishes genuinely unconfigured (false) from a configured $0 fee (true).</summary>
    public required bool FeeConfigured { get; init; }

    public decimal? Deposit { get; init; }

    /// <summary>Tier that supplied Deposit; null when no tier sets it.</summary>
    public string? DepositSource { get; init; }

    public decimal? BalanceDue { get; init; }

    /// <summary>Tier that supplied BalanceDue; null when no tier sets it.</summary>
    public string? BalanceDueSource { get; init; }

    /// <summary>Effective payment phase: most-specific BFullPaymentRequired stamp ?? false.
    /// The legacy Jobs.b*FullPaymentRequired columns are NEVER consulted
    /// (see ResolvedFee.ResolveFullPaymentPhase).</summary>
    public required bool FullPayment { get; init; }

    /// <summary>Tier whose stamp decided FullPayment; null = no stamp anywhere in the
    /// chain (silence = deposit phase — the FE renders this as the job baseline).</summary>
    public string? PhaseSource { get; init; }

    /// <summary>True when Deposit AND BalanceDue resolve &gt; 0 at this node or at any
    /// non-bucket descendant scope — the verified "is a Deposit/PIF pill meaningful
    /// here" verdict (otherwise the phase displays as Single).</summary>
    public required bool TwoPhase { get; init; }

    public LadtFeeModifierResolutionDto? EarlyBird { get; init; }

    public LadtFeeModifierResolutionDto? LateFee { get; init; }

    /// <summary>Downward summary. Null on team nodes (leaf); never null at levels 0/1.</summary>
    public LadtFeeBelowSummaryDto? Below { get; init; }
}

/// <summary>
/// One modifier type resolved at a node. Carries BOTH the configured winner (date
/// windows ignored — what the grid displays today) and the active-now winner (the
/// charging path's <c>EvaluateModifiersAsync(now)</c> number). The two differ when a
/// more-specific scope's window has expired while a broader scope's is active.
/// </summary>
public record LadtFeeModifierResolutionDto
{
    /// <summary>Configured winner: most-specific tier carrying the type, windows ignored;
    /// sum of that tier's modifiers of the type.</summary>
    public required decimal Amount { get; init; }

    public required string Source { get; init; }

    /// <summary>True when any window at the configured winning tier contains now.</summary>
    public required bool Active { get; init; }

    /// <summary>Active-now winner amount (most-specific tier with an active window);
    /// null when no window of this type is active anywhere in the chain.</summary>
    public decimal? ActiveAmount { get; init; }

    public string? ActiveSource { get; init; }
}

/// <summary>
/// What more-specific scopes under a node set locally, per field family. Candidate
/// scopes exclude system-bucket agegroups (WAITLIST/Dropped/Registration) and their
/// teams; league nodes span BOTH tiers below (agegroups and teams).
/// </summary>
public record LadtFeeBelowSummaryDto
{
    public required LadtFeeBelowAmountsDto Amounts { get; init; }

    public required LadtFeeBelowPhaseDto Phase { get; init; }

    public required LadtFeeBelowModifierDto EarlyBird { get; init; }

    public required LadtFeeBelowModifierDto LateFee { get; init; }
}

public record LadtFeeBelowAmountsDto
{
    /// <summary>Descendant scopes whose OWN row sets a local Deposit or BalanceDue.</summary>
    public required int OverrideCount { get; init; }

    /// <summary>True when every overriding scope's RESOLVED (deposit, balanceDue) pair
    /// equals this node's; vacuously true when OverrideCount is 0.</summary>
    public required bool Agrees { get; init; }

    /// <summary>Distinct resolved pairs at the overriding scopes (for a "varies" hover).</summary>
    public IReadOnlyList<LadtFeeAmountPairDto> DistinctValues { get; init; } = [];
}

public record LadtFeeAmountPairDto
{
    public decimal? Deposit { get; init; }

    public decimal? BalanceDue { get; init; }
}

public record LadtFeeBelowPhaseDto
{
    /// <summary>Descendant scopes with their own BFullPaymentRequired stamp.</summary>
    public required int OverrideCount { get; init; }

    /// <summary>True when every stamped scope's effective phase equals this node's.</summary>
    public required bool Agrees { get; init; }

    public IReadOnlyList<bool> DistinctValues { get; init; } = [];
}

public record LadtFeeBelowModifierDto
{
    /// <summary>Descendant scopes whose own row carries the modifier type.</summary>
    public required int OverrideCount { get; init; }

    /// <summary>True when every overriding scope's summed amount equals this node's
    /// resolved amount for the type.</summary>
    public required bool Agrees { get; init; }

    /// <summary>Distinct per-scope summed amounts (windows ignored, matching Amount).</summary>
    public IReadOnlyList<decimal> DistinctValues { get; init; } = [];
}
