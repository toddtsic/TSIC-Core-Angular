using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Constants;
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
    private readonly IFeeRepository _feeRepo;

    public TeamPlacementService(
        IJobRepository jobRepo,
        IAgeGroupRepository agegroupRepo,
        ITeamRepository teamRepo,
        IDivisionRepository divisionRepo,
        IFeeRepository feeRepo)
    {
        _jobRepo = jobRepo;
        _agegroupRepo = agegroupRepo;
        _teamRepo = teamRepo;
        _divisionRepo = divisionRepo;
        _feeRepo = feeRepo;
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
            // Not full → place the team in the agegroup's "Unassigned" holding
            // division (legacy parity: the monolith looked up the division named
            // "Unassigned" and registered the team there). Find-or-create so an
            // agegroup that predates the auto-create invariant still resolves to a
            // real division instead of leaving DivId null and orphaning the team
            // from the LADT tree and scheduling. The admin stub-team path bypasses
            // all of this via skipCapacityCheck and supplies its own division.
            var unassignedDiv = await FindOrCreateDivisionAsync(
                agegroup.AgegroupId, DivisionConstants.Unassigned, userId, cancellationToken);

            return new TeamPlacementResult
            {
                AgegroupId = agegroup.AgegroupId,
                DivisionId = unassignedDiv.DivId,
                LeagueId = agegroup.LeagueId,
                IsWaitlisted = false
            };
        }

        // Agegroup is full — auto-create waitlist mirror.
        // Team registration always supports waitlists (driven by MaxTeams per agegroup).
        // Waitlists are mandatory job-wide, so there is no opt-in to check here.

        // Find-or-create WAITLIST mirror agegroup
        var waitlistAgName = $"WAITLIST - {agegroup.AgegroupName}";
        var waitlistAg = await FindOrCreateWaitlistAgegroupAsync(
            agegroup, waitlistAgName, userId, cancellationToken);

        // Find-or-create WAITLIST mirror division
        var waitlistDivName = $"WAITLIST - {divisionName ?? agegroup.AgegroupName}";
        var waitlistDiv = await FindOrCreateDivisionAsync(
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
            Modified = DateTime.Now
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
            var rosterCount = await _teamRepo.GetAssignedPlayerCountAsync(sourceTeamId, cancellationToken);
            if (rosterCount >= team.MaxCount)
            {
                // Roster is full — mint (or reuse) the waitlist mirror. Null means the
                // job does not use waitlists → hard-stop, same as the legacy behavior.
                var waitlistTeam = await MintWaitlistMirrorAsync(jobId, sourceTeamId, userId, cancellationToken)
                    ?? throw new InvalidOperationException("Team roster is full");

                return new RosterPlacementResult
                {
                    TeamId = waitlistTeam.TeamId,
                    IsWaitlisted = true,
                    WaitlistTeamName = waitlistTeam.TeamName
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

    /// <inheritdoc />
    public async Task EnsureWaitlistMirrorAsync(
        Guid jobId,
        Guid realTeamId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // Fire-and-forget mint — callers only need the side effect (the mirror exists).
        await MintWaitlistMirrorAsync(jobId, realTeamId, userId, cancellationToken);
    }

    /// <summary>
    /// Find-or-create the full WAITLIST mirror (agegroup + division + team + its $0 fee
    /// stamp) for a real team. Waitlists are mandatory job-wide, so this always mints
    /// (the GetUsesWaitlistsAsync gate is retained but now constant-true). Performs no
    /// capacity check; idempotent. Shared by
    /// the live overflow path (<see cref="ResolveRosterPlacementAsync"/>) and the proactive
    /// mint-on-fill hook (<see cref="EnsureWaitlistMirrorAsync"/>).
    /// </summary>
    private async Task<Domain.Entities.Teams?> MintWaitlistMirrorAsync(
        Guid jobId, Guid realTeamId, string? userId,
        CancellationToken cancellationToken)
    {
        var usesWaitlists = await _jobRepo.GetUsesWaitlistsAsync(jobId, cancellationToken);
        if (!usesWaitlists)
            return null;

        var team = await _teamRepo.GetTeamFromTeamId(realTeamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {realTeamId} not found.");

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
        var waitlistDiv = await FindOrCreateDivisionAsync(
            waitlistAg.AgegroupId, waitlistDivName, userId, cancellationToken);

        // Find-or-create WAITLIST team mirror (also stamps its $0 fee row)
        var waitlistTeamName = $"WAITLIST - {team.TeamName}";
        return await FindOrCreateWaitlistTeamAsync(
            team, waitlistAg, waitlistDiv, waitlistTeamName, jobId, userId, cancellationToken);
    }

    // ── Find-or-create helpers ──

    /// <summary>
    /// Find a division by name within an agegroup, or create it if absent.
    /// Serves both the "Unassigned" holding division (normal placement) and the
    /// "WAITLIST - ..." mirror divisions (overflow placement).
    /// </summary>
    private async Task<Divisions> FindOrCreateDivisionAsync(
        Guid agegroupId, string divName, string? userId,
        CancellationToken cancellationToken)
    {
        var divisions = await _divisionRepo.GetByAgegroupIdAsync(agegroupId, cancellationToken);
        var existing = divisions.Find(d =>
            string.Equals(d.DivName, divName, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
            return existing;

        var division = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = agegroupId,
            DivName = divName,
            LebUserId = userId,
            Modified = DateTime.Now
        };
        _divisionRepo.Add(division);
        await _divisionRepo.SaveChangesAsync(cancellationToken);

        return division;
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
            // Idempotent: an older mirror (minted before the $0-stamp invariant)
            // may lack its fee row — ensure it before handing the team back.
            await EnsureWaitlistTeamFeeAsync(
                jobId, waitlistAg.AgegroupId, existing.TeamId, userId, cancellationToken);
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
            Createdate = DateTime.Now,
            Modified = DateTime.Now
        };
        _teamRepo.Add(waitlistTeam);
        await _teamRepo.SaveChangesAsync(cancellationToken);

        await EnsureWaitlistTeamFeeAsync(
            jobId, waitlistAg.AgegroupId, waitlistTeam.TeamId, userId, cancellationToken);

        return waitlistTeam;
    }

    /// <summary>
    /// Stamps an explicit $0 Player fee row on a waitlist mirror team (idempotent).
    /// The mirror inherits no <c>fees.JobFees</c> row, so without this the cascade
    /// would resolve a player overflowing onto it up to the league tier (charged) or
    /// fail loud with "Fee not set". A team-scoped (Deposit=0, BalanceDue=0) row makes
    /// the resolver return (0, 0, FeeConfigured=true) — genuinely free, but configured.
    /// </summary>
    private async Task EnsureWaitlistTeamFeeAsync(
        Guid jobId, Guid waitlistAgegroupId, Guid waitlistTeamId, string? userId,
        CancellationToken cancellationToken)
    {
        var existing = await _feeRepo.GetTrackedByScopeAsync(
            jobId, RoleConstants.Player, waitlistAgegroupId, waitlistTeamId, null, cancellationToken);
        if (existing != null)
            return;

        _feeRepo.Add(new JobFees
        {
            JobFeeId = Guid.NewGuid(),
            JobId = jobId,
            RoleId = RoleConstants.Player,
            AgegroupId = waitlistAgegroupId,
            TeamId = waitlistTeamId,
            LeagueId = null,
            Deposit = 0m,
            BalanceDue = 0m,
            Modified = DateTime.Now,
            LebUserId = userId
        });
        await _feeRepo.SaveChangesAsync(cancellationToken);
    }
}
