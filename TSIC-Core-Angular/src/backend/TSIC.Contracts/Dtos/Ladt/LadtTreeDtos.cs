namespace TSIC.Contracts.Dtos.Ladt;

/// <summary>
/// Root response for the LADT tree hierarchy endpoint.
/// Contains the full 4-level tree with aggregate counts.
/// </summary>
public record LadtTreeRootDto
{
    public required List<LadtTreeNodeDto> Leagues { get; init; }
    public required int TotalTeams { get; init; }
    public required int TotalPlayers { get; init; }
    public required List<Guid> ScheduledTeamIds { get; init; }

    /// <summary>
    /// Job-level full-payment phase baselines — the fallback the fee resolver uses when
    /// no per-scope JobFees override exists (ResolvedFee.ResolveFullPaymentPhase). The LADT
    /// grid uses these so the Payment Phase column reflects the true resolved phase.
    /// Players flag drives the Player role; Teams flag drives the ClubRep role.
    /// </summary>
    public required bool BPlayersFullPaymentRequired { get; init; }
    public required bool BTeamsFullPaymentRequired { get; init; }

    /// <summary>
    /// The job's own sport (Jobs.SportId), or NULL when the job has never had one set.
    /// Seeds the sport dropdown on the empty-state Create League form so the first league
    /// inherits the job's sport rather than asking the operator to re-pick it; null opens
    /// the dropdown unselected. Carried on the tree because the tree is already loaded when
    /// that form renders.
    ///
    /// The storage column is a non-nullable Guid that reads Guid.Empty when unset — an
    /// absence spelled as a value. That translation happens once, here at the boundary, so
    /// no consumer has to recognise a magic GUID.
    /// </summary>
    public required Guid? JobSportId { get; init; }
}

/// <summary>
/// A single node in the LADT hierarchy tree.
/// Level: 0=League, 1=Agegroup, 2=Division, 3=Team
/// </summary>
public record LadtTreeNodeDto
{
    public required Guid Id { get; init; }
    public Guid? ParentId { get; init; }
    public required string Name { get; init; }
    public required int Level { get; init; }
    public required bool IsLeaf { get; init; }
    public required int TeamCount { get; init; }
    public required int PlayerCount { get; init; }
    public required bool Expanded { get; init; }
    public required bool Active { get; init; }
    public string? ClubName { get; init; }
    public string? Color { get; init; }
    public List<LadtTreeNodeDto>? Children { get; init; }
}

/// <summary>
/// Optional request body for stub creation endpoints.
/// When null or when Name is null/empty, the backend uses its default naming logic.
/// </summary>
public record CreateStubRequest
{
    public string? Name { get; init; }
}
