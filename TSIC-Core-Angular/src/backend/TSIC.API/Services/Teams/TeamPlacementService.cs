using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Teams;

/// <summary>
/// Centralized team placement with automatic waitlist overflow.
/// When an agegroup is full and the job uses waitlists, finds-or-creates
/// a WAITLIST mirror agegroup + division and returns those IDs.
/// </summary>
public class TeamPlacementService : ITeamPlacementService
{
    private readonly IJobRepository _jobRepo;
    private readonly IAgeGroupRepository _agegroupRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IDivisionRepository _divisionRepo;

    public TeamPlacementService(
        IJobRepository jobRepo,
        IAgeGroupRepository agegroupRepo,
        ITeamRepository teamRepo,
        IDivisionRepository divisionRepo)
    {
        _jobRepo = jobRepo;
        _agegroupRepo = agegroupRepo;
        _teamRepo = teamRepo;
        _divisionRepo = divisionRepo;
    }

    public async Task<TeamPlacementResult> ResolvePlacementAsync(
        Guid jobId,
        Guid targetAgegroupId,
        string teamName,
        string? divisionName = null,
        string? userId = null,
        bool skipCapacityCheck = false,
        CancellationToken cancellationToken = default)
    {
        var agegroup = await _agegroupRepo.GetByIdAsync(targetAgegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {targetAgegroupId} not found.");

        // Admin bypass — place directly, no capacity check
        if (skipCapacityCheck)
        {
            return new TeamPlacementResult
            {
                AgegroupId = agegroup.AgegroupId,
                LeagueId = agegroup.LeagueId,
                IsWaitlisted = false
            };
        }

        // Check capacity
        var registeredCount = await _teamRepo.GetRegisteredCountForAgegroupAsync(
            jobId, targetAgegroupId, cancellationToken);

        if (registeredCount < agegroup.MaxTeams)
        {
            return new TeamPlacementResult
            {
                AgegroupId = agegroup.AgegroupId,
                LeagueId = agegroup.LeagueId,
                IsWaitlisted = false
            };
        }

        // Agegroup is full — check if job uses waitlists
        var usesWaitlists = await _jobRepo.GetUsesWaitlistsAsync(jobId, cancellationToken);
        if (!usesWaitlists)
        {
            throw new InvalidOperationException("This age group is full");
        }

        // Find-or-create WAITLIST mirror agegroup
        var waitlistAgName = $"WAITLIST - {agegroup.AgegroupName}";
        var waitlistAg = await FindOrCreateWaitlistAgegroupAsync(
            agegroup, waitlistAgName, userId, cancellationToken);

        // Find-or-create WAITLIST mirror division
        var waitlistDivName = $"WAITLIST - {divisionName ?? agegroup.AgegroupName}";
        var waitlistDiv = await FindOrCreateWaitlistDivisionAsync(
            waitlistAg.AgegroupId, waitlistDivName, userId, cancellationToken);

        return new TeamPlacementResult
        {
            AgegroupId = waitlistAg.AgegroupId,
            DivisionId = waitlistDiv.DivId,
            LeagueId = agegroup.LeagueId,
            IsWaitlisted = true,
            WaitlistAgegroupName = waitlistAgName
        };
    }

    private async Task<Agegroups> FindOrCreateWaitlistAgegroupAsync(
        Agegroups sourceAg, string waitlistName, string? userId,
        CancellationToken cancellationToken)
    {
        // Search siblings (untracked) for existing mirror
        var siblings = await _agegroupRepo.GetByLeagueIdAsync(sourceAg.LeagueId, cancellationToken);
        var existing = siblings.Find(s =>
            string.Equals(s.AgegroupName, waitlistName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            // Return tracked entity for potential downstream use
            return await _agegroupRepo.GetByIdAsync(existing.AgegroupId, cancellationToken) ?? existing;
        }

        var waitlistAg = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = sourceAg.LeagueId,
            AgegroupName = waitlistName,
            Color = "#808080",
            Season = sourceAg.Season,
            SortAge = sourceAg.SortAge,
            Gender = sourceAg.Gender,
            MaxTeams = 1000,
            MaxTeamsPerClub = 100,
            BAllowSelfRostering = true,
            BChampionsByDivision = false,
            BHideStandings = true,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _agegroupRepo.Add(waitlistAg);
        await _agegroupRepo.SaveChangesAsync(cancellationToken);

        return waitlistAg;
    }

    private async Task<Divisions> FindOrCreateWaitlistDivisionAsync(
        Guid waitlistAgegroupId, string waitlistDivName, string? userId,
        CancellationToken cancellationToken)
    {
        var divisions = await _divisionRepo.GetByAgegroupIdAsync(waitlistAgegroupId, cancellationToken);
        var existing = divisions.Find(d =>
            string.Equals(d.DivName, waitlistDivName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return existing;

        var waitlistDiv = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = waitlistAgegroupId,
            DivName = waitlistDivName,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _divisionRepo.Add(waitlistDiv);
        await _divisionRepo.SaveChangesAsync(cancellationToken);

        return waitlistDiv;
    }
}
