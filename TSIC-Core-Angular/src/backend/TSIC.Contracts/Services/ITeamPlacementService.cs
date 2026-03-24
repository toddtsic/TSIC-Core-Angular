using TSIC.Contracts.Dtos;

namespace TSIC.Contracts.Services;

/// <summary>
/// Centralized service for resolving team placement into agegroups with automatic waitlist overflow.
/// All entry points that create teams (wizard, LADT admin) must call this service.
/// </summary>
public interface ITeamPlacementService
{
    /// <summary>
    /// Resolve the actual agegroup + division for a team placement.
    /// If the target agegroup is full and the job uses waitlists, finds-or-creates
    /// the WAITLIST mirror agegroup/division and returns those IDs instead.
    /// If full and no waitlists, throws InvalidOperationException.
    /// If skipCapacityCheck is true, skips capacity check entirely.
    /// </summary>
    Task<TeamPlacementResult> ResolvePlacementAsync(
        Guid jobId,
        Guid targetAgegroupId,
        string teamName,
        string? divisionName = null,
        string? userId = null,
        bool skipCapacityCheck = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve where a player should be rostered when a team is full.
    /// If BUseWaitlists=true, finds-or-creates WAITLIST agegroup + division + team mirror.
    /// Uses the same WAITLIST agegroup/division as ResolvePlacementAsync (idempotent).
    /// </summary>
    Task<RosterPlacementResult> ResolveRosterPlacementAsync(
        Guid jobId,
        Guid sourceTeamId,
        string? userId = null,
        CancellationToken cancellationToken = default);
}
