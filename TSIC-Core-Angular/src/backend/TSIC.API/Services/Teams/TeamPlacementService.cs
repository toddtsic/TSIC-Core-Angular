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

    public async Task<RosterPlacementResult> ResolveRosterPlacementAsync(
        Guid jobId,
        Guid sourceTeamId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        var team = await _teamRepo.GetTeamFromTeamId(sourceTeamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {sourceTeamId} not found.");

        // Check roster capacity (MaxCount=0 means unlimited)
        if (team.MaxCount > 0)
        {
            var rosterCount = await _teamRepo.GetPlayerCountAsync(sourceTeamId, cancellationToken);
            if (rosterCount >= team.MaxCount)
            {
                // Roster is full — check if job uses waitlists
                var usesWaitlists = await _jobRepo.GetUsesWaitlistsAsync(jobId, cancellationToken);
                if (!usesWaitlists)
                {
                    throw new InvalidOperationException("Team roster is full");
                }

                // Load parent agegroup + division for mirror creation
                var agegroup = await _agegroupRepo.GetByIdAsync(team.AgegroupId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Agegroup {team.AgegroupId} not found.");

                string? divName = null;
                if (team.DivId.HasValue)
                {
                    var div = await _divisionRepo.GetByIdReadOnlyAsync(team.DivId.Value, cancellationToken);
                    divName = div?.DivName;
                }

                // Reuse same find-or-create for agegroup + division (idempotent with team placement path)
                var waitlistAgName = $"WAITLIST - {agegroup.AgegroupName}";
                var waitlistAg = await FindOrCreateWaitlistAgegroupAsync(
                    agegroup, waitlistAgName, userId, cancellationToken);

                var waitlistDivName = $"WAITLIST - {divName ?? agegroup.AgegroupName}";
                var waitlistDiv = await FindOrCreateWaitlistDivisionAsync(
                    waitlistAg.AgegroupId, waitlistDivName, userId, cancellationToken);

                // Find-or-create WAITLIST team mirror
                var waitlistTeamName = $"WAITLIST - {team.TeamName}";
                var waitlistTeam = await FindOrCreateWaitlistTeamAsync(
                    team, waitlistAg, waitlistDiv, waitlistTeamName, jobId, userId, cancellationToken);

                return new RosterPlacementResult
                {
                    TeamId = waitlistTeam.TeamId,
                    IsWaitlisted = true,
                    WaitlistTeamName = waitlistTeamName
                };
            }
        }

        // Roster has capacity (or unlimited)
        return new RosterPlacementResult
        {
            TeamId = sourceTeamId,
            IsWaitlisted = false
        };
    }

    // ── Find-or-create helpers ──

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

    private async Task<Domain.Entities.Teams> FindOrCreateWaitlistTeamAsync(
        Domain.Entities.Teams sourceTeam, Agegroups waitlistAg, Divisions waitlistDiv,
        string waitlistTeamName, Guid jobId, string? userId,
        CancellationToken cancellationToken)
    {
        // Search untracked teams in waitlist agegroup for existing mirror
        var existingTeams = await _teamRepo.GetByAgegroupIdAsync(waitlistAg.AgegroupId, cancellationToken);
        var existing = existingTeams.Find(t =>
            string.Equals(t.TeamName, waitlistTeamName, StringComparison.OrdinalIgnoreCase)
            && t.DivId == waitlistDiv.DivId);

        if (existing != null)
        {
            return await _teamRepo.GetTeamFromTeamId(existing.TeamId, cancellationToken) ?? existing;
        }

        var nextRank = await _teamRepo.GetNextDivRankAsync(waitlistDiv.DivId, cancellationToken);

        var waitlistTeam = new Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = waitlistAg.LeagueId,
            AgegroupId = waitlistAg.AgegroupId,
            DivId = waitlistDiv.DivId,
            TeamName = waitlistTeamName,
            MaxCount = 100000,
            BHideRoster = true,
            BAllowSelfRostering = true,
            Active = true,
            DivRank = nextRank,
            Gender = sourceTeam.Gender,
            KeywordPairs = sourceTeam.KeywordPairs,
            Season = sourceTeam.Season,
            Year = sourceTeam.Year,
            Startdate = sourceTeam.Startdate,
            Enddate = sourceTeam.Enddate,
            LebUserId = userId,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };
        _teamRepo.Add(waitlistTeam);
        await _teamRepo.SaveChangesAsync(cancellationToken);

        return waitlistTeam;
    }
}
