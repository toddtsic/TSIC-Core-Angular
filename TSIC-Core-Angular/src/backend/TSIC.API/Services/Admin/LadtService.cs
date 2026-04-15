using TSIC.Contracts.Dtos.Ladt;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;
using TSIC.API.Services.Players;

// Note: Use TSIC.Domain.Entities.Teams (fully-qualified) to resolve
// collision with TSIC.API.Services.Teams namespace.

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for the LADT (League/Age-Group/Division/Team) admin hierarchy.
/// Handles all 4 entity levels with shared validation and authorization logic.
/// </summary>
public sealed class LadtService : ILadtService
{
    private const string UnassignedDivisionName = "Unassigned";

    private readonly ILeagueRepository _leagueRepo;
    private readonly IAgeGroupRepository _agegroupRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly ITeamRepository _teamRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IRegistrationAccountingRepository _regAcctRepo;
    private readonly IJobRepository _jobRepo;
    private readonly IFeeResolutionService _feeService;
    private readonly IClubTeamRepository _clubTeamRepo;
    private readonly IClubRepository _clubRepo;
    private readonly IScheduleRepository _scheduleRepo;
    private readonly ITeamPlacementService _placement;
    private readonly IFeeRepository _feeRepo;

    public LadtService(
        ILeagueRepository leagueRepo,
        IAgeGroupRepository agegroupRepo,
        IDivisionRepository divisionRepo,
        ITeamRepository teamRepo,
        IRegistrationRepository registrationRepo,
        IRegistrationAccountingRepository regAcctRepo,
        IJobRepository jobRepo,
        IFeeResolutionService feeService,
        IClubTeamRepository clubTeamRepo,
        IClubRepository clubRepo,
        IScheduleRepository scheduleRepo,
        ITeamPlacementService placement,
        IFeeRepository feeRepo)
    {
        _leagueRepo = leagueRepo;
        _agegroupRepo = agegroupRepo;
        _divisionRepo = divisionRepo;
        _teamRepo = teamRepo;
        _registrationRepo = registrationRepo;
        _regAcctRepo = regAcctRepo;
        _jobRepo = jobRepo;
        _feeService = feeService;
        _clubTeamRepo = clubTeamRepo;
        _clubRepo = clubRepo;
        _scheduleRepo = scheduleRepo;
        _placement = placement;
        _feeRepo = feeRepo;
    }

    // ═══════════════════════════════════════════
    // Tree
    // ═══════════════════════════════════════════

    public async Task<LadtTreeRootDto> GetLadtTreeAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var leagues = await _leagueRepo.GetLeaguesByJobIdAsync(jobId, cancellationToken);
        var playerCounts = await _teamRepo.GetPlayerCountsByTeamAsync(jobId, cancellationToken);
        var clubNames = await _teamRepo.GetClubNamesByJobAsync(jobId, cancellationToken);
        var scheduledTeamIds = await _teamRepo.GetScheduledTeamIdsAsync(jobId, cancellationToken);

        var totalTeams = 0;
        var totalPlayers = 0;

        var leagueNodes = new List<LadtTreeNodeDto>();

        foreach (var league in leagues)
        {
            var agegroups = await _agegroupRepo.GetByLeagueIdAsync(league.LeagueId, cancellationToken);
            var agegroupNodes = new List<LadtTreeNodeDto>();

            foreach (var ag in agegroups)
            {
                // The "Dropped Teams" agegroup is the history bucket for soft-dropped
                // (inactive) teams. Its rollups should count those teams so the node
                // shows a real number; jobwide totals stay active-only.
                var isDroppedAg = string.Equals(ag.AgegroupName, "Dropped Teams", StringComparison.OrdinalIgnoreCase);

                var divisions = await _divisionRepo.GetByAgegroupIdAsync(ag.AgegroupId, cancellationToken);
                var divisionNodes = new List<LadtTreeNodeDto>();

                foreach (var div in divisions)
                {
                    var teams = await _teamRepo.GetByDivisionIdAsync(div.DivId, cancellationToken);
                    var teamNodes = new List<LadtTreeNodeDto>();

                    foreach (var team in teams)
                    {
                        var pc = playerCounts.GetValueOrDefault(team.TeamId, 0);
                        var isActive = team.Active == true;
                        if (isActive)
                        {
                            totalPlayers += pc;
                            totalTeams++;
                        }

                        teamNodes.Add(new LadtTreeNodeDto
                        {
                            Id = team.TeamId,
                            ParentId = div.DivId,
                            Name = team.TeamName ?? "(unnamed)",
                            Level = 3,
                            IsLeaf = true,
                            TeamCount = 0,
                            PlayerCount = pc,
                            Expanded = false,
                            Active = team.Active == true,
                            ClubName = clubNames.GetValueOrDefault(team.TeamId),
                            Children = null
                        });
                    }

                    var rollupTeamNodes = isDroppedAg ? teamNodes : teamNodes.Where(t => t.Active).ToList();
                    var divTeamCount = rollupTeamNodes.Count;
                    var divPlayerCount = rollupTeamNodes.Sum(t => t.PlayerCount);

                    divisionNodes.Add(new LadtTreeNodeDto
                    {
                        Id = div.DivId,
                        ParentId = ag.AgegroupId,
                        Name = div.DivName ?? "(unnamed)",
                        Level = 2,
                        IsLeaf = teamNodes.Count == 0,
                        TeamCount = divTeamCount,
                        PlayerCount = divPlayerCount,
                        Expanded = false,
                        Active = true,
                        Children = teamNodes
                    });
                }

                var agTeamCount = divisionNodes.Sum(d => d.TeamCount);
                var agPlayerCount = divisionNodes.Sum(d => d.PlayerCount);

                agegroupNodes.Add(new LadtTreeNodeDto
                {
                    Id = ag.AgegroupId,
                    ParentId = league.LeagueId,
                    Name = ag.AgegroupName ?? "(unnamed)",
                    Level = 1,
                    IsLeaf = divisionNodes.Count == 0,
                    TeamCount = agTeamCount,
                    PlayerCount = agPlayerCount,
                    Expanded = false,
                    Active = true,
                    Color = ag.Color,
                    Children = divisionNodes
                });
            }

            var leagueTeamCount = agegroupNodes.Sum(a => a.TeamCount);
            var leaguePlayerCount = agegroupNodes.Sum(a => a.PlayerCount);

            leagueNodes.Add(new LadtTreeNodeDto
            {
                Id = league.LeagueId,
                ParentId = null,
                Name = league.LeagueName ?? "(unnamed)",
                Level = 0,
                IsLeaf = agegroupNodes.Count == 0,
                TeamCount = leagueTeamCount,
                PlayerCount = leaguePlayerCount,
                Expanded = true,
                Active = true,
                Children = agegroupNodes
            });
        }

        return new LadtTreeRootDto
        {
            Leagues = leagueNodes,
            TotalTeams = totalTeams,
            TotalPlayers = totalPlayers,
            ScheduledTeamIds = scheduledTeamIds.ToList()
        };
    }

    // ═══════════════════════════════════════════
    // Lookups
    // ═══════════════════════════════════════════

    // Whitelist of team sports TSIC actually supports. The Sports table includes
    // a long legacy list (camping, caving, kayaking, etc.) that leaks into the
    // league-edit dropdown; filter it here rather than mutate the table so any
    // historical references remain resolvable.
    private static readonly HashSet<string> AllowedSportNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "lacrosse", "soccer", "football", "hockey", "field hockey",
        "basketball", "baseball", "softball", "volleyball",
        "wrestling", "rugby", "cheerleading"
    };

    public async Task<List<SportOptionDto>> GetSportsAsync(CancellationToken cancellationToken = default)
    {
        var sports = await _leagueRepo.GetAllSportsAsync(cancellationToken);
        return sports
            .Where(s => s.SportName != null && AllowedSportNames.Contains(s.SportName))
            .Select(s => new SportOptionDto
            {
                SportId = s.SportId,
                SportName = ToTitleCase(s.SportName!)
            })
            .OrderBy(s => s.SportName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ToTitleCase(string name)
    {
        // "lacrosse" → "Lacrosse", "field hockey" → "Field Hockey"
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant()));
    }

    // ═══════════════════════════════════════════
    // League CRUD
    // ═══════════════════════════════════════════

    public async Task<LeagueDetailDto> GetLeagueDetailAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
        return await _leagueRepo.GetByIdWithSportAsync(leagueId, cancellationToken)
            ?? throw new KeyNotFoundException($"League {leagueId} not found.");
    }

    public async Task<LeagueDetailDto> UpdateLeagueAsync(Guid leagueId, UpdateLeagueRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
        var league = await _leagueRepo.GetByIdAsync(leagueId, cancellationToken)
            ?? throw new KeyNotFoundException($"League {leagueId} not found.");

        league.LeagueName = request.LeagueName;
        league.SportId = request.SportId;
        league.BHideContacts = request.BHideContacts;
        league.BHideStandings = request.BHideStandings;
        league.RescheduleEmailsToAddon = request.RescheduleEmailsToAddon;
        league.LebUserId = userId;
        league.Modified = DateTime.UtcNow;

        await _leagueRepo.SaveChangesAsync(cancellationToken);

        return await _leagueRepo.GetByIdWithSportAsync(leagueId, cancellationToken)
            ?? throw new InvalidOperationException("Failed to retrieve updated league.");
    }

    // ═══════════════════════════════════════════
    // Agegroup CRUD
    // ═══════════════════════════════════════════

    public async Task<AgegroupDetailDto> GetAgegroupDetailAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);
        var ag = await _agegroupRepo.GetByIdAsync(agegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {agegroupId} not found.");
        return MapAgegroup(ag);
    }

    public async Task<AgegroupDetailDto> CreateAgegroupAsync(CreateAgegroupRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(request.LeagueId, jobId, cancellationToken);
        var jobSY = await _jobRepo.GetJobSeasonYearAsync(jobId, cancellationToken);

        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = request.LeagueId,
            AgegroupName = request.AgegroupName,
            Season = jobSY?.Season,
            Color = request.Color,
            Gender = request.Gender,
            DobMin = request.DobMin,
            DobMax = request.DobMax,
            GradYearMin = request.GradYearMin,
            GradYearMax = request.GradYearMax,
            SchoolGradeMin = request.SchoolGradeMin,
            SchoolGradeMax = request.SchoolGradeMax,
            MaxTeams = request.MaxTeams,
            MaxTeamsPerClub = request.MaxTeamsPerClub,
            BAllowSelfRostering = request.BAllowSelfRostering,
            BChampionsByDivision = request.BChampionsByDivision,
            BAllowApiRosterAccess = request.BAllowApiRosterAccess,
            BHideStandings = request.BHideStandings,
            SortAge = request.SortAge,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _agegroupRepo.Add(ag);

        // Auto-create stub division
        var stubDiv = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = ag.AgegroupId,
            DivName = UnassignedDivisionName,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _divisionRepo.Add(stubDiv);

        await _agegroupRepo.SaveChangesAsync(cancellationToken);

        return MapAgegroup(ag);
    }

    public async Task<AgegroupDetailDto> UpdateAgegroupAsync(Guid agegroupId, UpdateAgegroupRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);
        var ag = await _agegroupRepo.GetByIdAsync(agegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {agegroupId} not found.");

        // Detect name change for waitlist cascade (skip if this IS a waitlist agegroup)
        var oldName = ag.AgegroupName;
        var nameChanged = oldName != null && request.AgegroupName != null
            && !string.Equals(oldName, request.AgegroupName, StringComparison.Ordinal)
            && !oldName.Contains("WAITLIST", StringComparison.OrdinalIgnoreCase);

        ag.AgegroupName = request.AgegroupName;
        // Season is immutable (set from Job on creation) — never overwrite
        ag.Color = request.Color;
        ag.Gender = request.Gender;
        ag.DobMin = request.DobMin;
        ag.DobMax = request.DobMax;
        ag.GradYearMin = request.GradYearMin;
        ag.GradYearMax = request.GradYearMax;
        ag.SchoolGradeMin = request.SchoolGradeMin;
        ag.SchoolGradeMax = request.SchoolGradeMax;
        ag.MaxTeams = request.MaxTeams;
        ag.MaxTeamsPerClub = request.MaxTeamsPerClub;
        ag.BAllowSelfRostering = request.BAllowSelfRostering;
        ag.BChampionsByDivision = request.BChampionsByDivision;
        ag.BAllowApiRosterAccess = request.BAllowApiRosterAccess;
        ag.BHideStandings = request.BHideStandings;
        ag.SortAge = request.SortAge;
        ag.LebUserId = userId;
        ag.Modified = DateTime.UtcNow;

        // Cascade rename to "WAITLIST - {oldName}" sibling if it exists
        if (nameChanged)
        {
            var siblings = await _agegroupRepo.GetByLeagueIdAsync(ag.LeagueId, cancellationToken);
            var waitlistMatch = siblings.Find(s =>
                string.Equals(s.AgegroupName, $"WAITLIST - {oldName}", StringComparison.OrdinalIgnoreCase));
            if (waitlistMatch != null)
            {
                // Re-fetch tracked so SaveChanges persists the rename
                var waitlistMirror = await _agegroupRepo.GetByIdAsync(waitlistMatch.AgegroupId, cancellationToken);
                if (waitlistMirror != null)
                {
                    waitlistMirror.AgegroupName = $"WAITLIST - {request.AgegroupName}";
                    waitlistMirror.LebUserId = userId;
                    waitlistMirror.Modified = DateTime.UtcNow;
                }
            }
        }

        await _agegroupRepo.SaveChangesAsync(cancellationToken);

        // Sync denormalized agegroup name in Schedule records
        await _scheduleRepo.SynchronizeScheduleAgegroupNameAsync(agegroupId, jobId, request.AgegroupName ?? string.Empty, cancellationToken);

        return MapAgegroup(ag);
    }

    public async Task UpdateAgegroupColorAsync(Guid agegroupId, string? color, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);
        var ag = await _agegroupRepo.GetByIdAsync(agegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {agegroupId} not found.");

        ag.Color = color;
        ag.LebUserId = userId;
        ag.Modified = DateTime.UtcNow;

        await _agegroupRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAgegroupAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);

        if (await _agegroupRepo.HasTeamsAsync(agegroupId, cancellationToken))
            throw new InvalidOperationException("Cannot delete agegroup that contains teams. Remove all teams first.");

        var ag = await _agegroupRepo.GetByIdAsync(agegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {agegroupId} not found.");

        // Delete child divisions first
        var divisions = await _divisionRepo.GetByAgegroupIdAsync(agegroupId, cancellationToken);
        foreach (var div in divisions)
        {
            var tracked = await _divisionRepo.GetByIdAsync(div.DivId, cancellationToken);
            if (tracked != null) _divisionRepo.Remove(tracked);
        }

        // Batch-delete any JobFees rows still referencing this agegroup (FK_JobFees_Agegroups).
        await _feeRepo.DeleteByAgegroupIdAsync(agegroupId, cancellationToken);

        _agegroupRepo.Remove(ag);
        await _agegroupRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task<AgegroupDetailDto> CloneAgegroupAsync(Guid agegroupId, CloneAgegroupRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);
        var source = await _agegroupRepo.GetByIdAsync(agegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {agegroupId} not found.");

        // Build the new agegroup. Always-copied: league linkage, name (from request),
        // season, audit. Gated by flags: eligibility, roster settings, visual, fees.
        var clone = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = source.LeagueId,
            AgegroupName = request.AgegroupName,
            Season = source.Season,
            SortAge = source.SortAge,
            MaxTeams = source.MaxTeams,
            MaxTeamsPerClub = source.MaxTeamsPerClub,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        if (request.CopyEligibility)
        {
            clone.Gender = source.Gender;
            clone.DobMin = source.DobMin;
            clone.DobMax = source.DobMax;
            clone.GradYearMin = source.GradYearMin;
            clone.GradYearMax = source.GradYearMax;
            clone.SchoolGradeMin = source.SchoolGradeMin;
            clone.SchoolGradeMax = source.SchoolGradeMax;
        }

        if (request.CopyRosterSettings)
        {
            clone.BAllowSelfRostering = source.BAllowSelfRostering;
            clone.BChampionsByDivision = source.BChampionsByDivision;
            clone.BHideStandings = source.BHideStandings;
            clone.BAllowApiRosterAccess = source.BAllowApiRosterAccess;
        }

        if (request.CopyVisualIdentity)
        {
            clone.Color = source.Color;
        }

        if (request.CopyFees)
        {
            // Entity-level fee windows + override live alongside JobFees rows.
            clone.LateFee = source.LateFee;
            clone.LateFeeStart = source.LateFeeStart;
            clone.LateFeeEnd = source.LateFeeEnd;
            clone.DiscountFee = source.DiscountFee;
            clone.DiscountFeeStart = source.DiscountFeeStart;
            clone.DiscountFeeEnd = source.DiscountFeeEnd;
            clone.PlayerFeeOverride = source.PlayerFeeOverride;
        }

        _agegroupRepo.Add(clone);

        // Agegroup-scoped JobFees (+ modifiers): fresh ids, repoint to new AgegroupId.
        if (request.CopyFees)
        {
            var sourceFees = await _feeRepo.GetByAgegroupScopeAsync(source.AgegroupId, cancellationToken);
            foreach (var sf in sourceFees)
            {
                var newFeeId = Guid.NewGuid();
                _feeRepo.Add(new JobFees
                {
                    JobFeeId = newFeeId,
                    JobId = sf.JobId,
                    RoleId = sf.RoleId,
                    AgegroupId = clone.AgegroupId,
                    TeamId = null,
                    Deposit = sf.Deposit,
                    BalanceDue = sf.BalanceDue,
                    Modified = DateTime.UtcNow,
                    LebUserId = userId
                });
                foreach (var mod in sf.FeeModifiers)
                {
                    _feeRepo.AddModifier(new FeeModifiers
                    {
                        FeeModifierId = Guid.NewGuid(),
                        JobFeeId = newFeeId,
                        ModifierType = mod.ModifierType,
                        Amount = mod.Amount,
                        StartDate = mod.StartDate,
                        EndDate = mod.EndDate,
                        Modified = DateTime.UtcNow,
                        LebUserId = userId
                    });
                }
            }
        }

        // Divisions (shells only). Teams are never cloned with an agegroup — users
        // populate teams manually or via per-team clone.
        var hasUnassigned = false;
        if (request.CopyDivisions)
        {
            var sourceDivisions = await _divisionRepo.GetByAgegroupIdAsync(source.AgegroupId, cancellationToken);
            foreach (var sd in sourceDivisions)
            {
                _divisionRepo.Add(new Divisions
                {
                    DivId = Guid.NewGuid(),
                    AgegroupId = clone.AgegroupId,
                    DivName = sd.DivName,
                    MaxRoundNumberToShow = sd.MaxRoundNumberToShow,
                    LebUserId = userId,
                    Modified = DateTime.UtcNow
                });
                if (string.Equals(sd.DivName, UnassignedDivisionName, StringComparison.OrdinalIgnoreCase))
                    hasUnassigned = true;
            }
        }

        // Every agegroup must have an Unassigned division — matches CreateAgegroupAsync.
        // Seed one if CopyDivisions is off, or if the source lacked one.
        if (!hasUnassigned)
        {
            _divisionRepo.Add(new Divisions
            {
                DivId = Guid.NewGuid(),
                AgegroupId = clone.AgegroupId,
                DivName = UnassignedDivisionName,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            });
        }

        await _agegroupRepo.SaveChangesAsync(cancellationToken);

        return MapAgegroup(clone);
    }

    public async Task<Guid> AddStubAgegroupAsync(Guid leagueId, Guid jobId, string userId, string? name = null, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
        var jobSY = await _jobRepo.GetJobSeasonYearAsync(jobId, cancellationToken);

        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = string.IsNullOrWhiteSpace(name) ? "New Age Group" : name.Trim(),
            Season = jobSY?.Season,
            MaxTeams = 0,
            MaxTeamsPerClub = 0,
            SortAge = 0,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _agegroupRepo.Add(ag);

        // Auto-create stub division
        var stubDiv = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = ag.AgegroupId,
            DivName = UnassignedDivisionName,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _divisionRepo.Add(stubDiv);

        await _agegroupRepo.SaveChangesAsync(cancellationToken);
        return ag.AgegroupId;
    }

    // ═══════════════════════════════════════════
    // Division CRUD
    // ═══════════════════════════════════════════

    public async Task<DivisionDetailDto> GetDivisionDetailAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(divId, jobId, cancellationToken);
        var div = await _divisionRepo.GetByIdReadOnlyAsync(divId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");
        return MapDivision(div);
    }

    public async Task<DivisionDetailDto> CreateDivisionAsync(CreateDivisionRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(request.AgegroupId, jobId, cancellationToken);

        // Check for duplicate division name within the same age group
        var existing = await _divisionRepo.GetByAgegroupIdAsync(request.AgegroupId, cancellationToken);
        if (existing.Any(d => string.Equals(d.DivName, request.DivName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException(
                $"A division named '{request.DivName}' already exists in this age group.");

        var div = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = request.AgegroupId,
            DivName = request.DivName,
            MaxRoundNumberToShow = request.MaxRoundNumberToShow,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _divisionRepo.Add(div);
        await _divisionRepo.SaveChangesAsync(cancellationToken);
        return MapDivision(div);
    }

    public async Task<DivisionDetailDto> UpdateDivisionAsync(Guid divId, UpdateDivisionRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(divId, jobId, cancellationToken);
        var div = await _divisionRepo.GetByIdAsync(divId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");

        // Block renaming the "Unassigned" division
        if (string.Equals(div.DivName, UnassignedDivisionName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.DivName, UnassignedDivisionName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot rename the 'Unassigned' division.");

        // Detect name change for waitlist cascade
        var oldDivName = div.DivName;
        var divNameChanged = oldDivName != null && request.DivName != null
            && !string.Equals(oldDivName, request.DivName, StringComparison.Ordinal)
            && !oldDivName.Contains("WAITLIST", StringComparison.OrdinalIgnoreCase);

        // Check for duplicate division name within the same age group
        if (!string.Equals(div.DivName, request.DivName, StringComparison.OrdinalIgnoreCase))
        {
            var siblings = await _divisionRepo.GetByAgegroupIdAsync(div.AgegroupId, cancellationToken);
            if (siblings.Any(d => d.DivId != divId
                && string.Equals(d.DivName, request.DivName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException(
                    $"A division named '{request.DivName}' already exists in this age group.");
        }

        div.DivName = request.DivName;
        div.MaxRoundNumberToShow = request.MaxRoundNumberToShow;
        div.LebUserId = userId;
        div.Modified = DateTime.UtcNow;

        // Cascade rename to WAITLIST division mirror if it exists
        if (divNameChanged)
        {
            var ag = await _agegroupRepo.GetByIdAsync(div.AgegroupId, cancellationToken);
            if (ag != null)
            {
                var agSiblings = await _agegroupRepo.GetByLeagueIdAsync(ag.LeagueId, cancellationToken);
                var waitlistAg = agSiblings.Find(s =>
                    string.Equals(s.AgegroupName, $"WAITLIST - {ag.AgegroupName}", StringComparison.OrdinalIgnoreCase));
                if (waitlistAg != null)
                {
                    var waitlistDivs = await _divisionRepo.GetByAgegroupIdAsync(waitlistAg.AgegroupId, cancellationToken);
                    var waitlistDiv = waitlistDivs.Find(d =>
                        string.Equals(d.DivName, $"WAITLIST - {oldDivName}", StringComparison.OrdinalIgnoreCase));
                    if (waitlistDiv != null)
                    {
                        var trackedDiv = await _divisionRepo.GetByIdAsync(waitlistDiv.DivId, cancellationToken);
                        if (trackedDiv != null)
                        {
                            trackedDiv.DivName = $"WAITLIST - {request.DivName}";
                            trackedDiv.LebUserId = userId;
                            trackedDiv.Modified = DateTime.UtcNow;
                        }
                    }
                }
            }
        }

        await _divisionRepo.SaveChangesAsync(cancellationToken);

        // Sync denormalized division name in Schedule records (DivName + Div2Name)
        await _scheduleRepo.SynchronizeScheduleDivisionNameAsync(divId, jobId, request.DivName ?? string.Empty, cancellationToken);

        return MapDivision(div);
    }

    public async Task DeleteDivisionAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(divId, jobId, cancellationToken);

        var div = await _divisionRepo.GetByIdAsync(divId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");

        if (string.Equals(div.DivName, UnassignedDivisionName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cannot delete the 'Unassigned' division.");

        if (await _divisionRepo.HasTeamsAsync(divId, cancellationToken))
            throw new InvalidOperationException("Cannot delete division that contains teams. Remove all teams first.");

        _divisionRepo.Remove(div);
        await _divisionRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddStubDivisionAsync(Guid agegroupId, Guid jobId, string userId, string? name = null, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);

        string divName;
        if (!string.IsNullOrWhiteSpace(name))
        {
            divName = name.Trim();
        }
        else
        {
            // Find next available "Pool X" name that doesn't collide with existing divisions
            var existing = await _divisionRepo.GetByAgegroupIdAsync(agegroupId, cancellationToken);
            var existingNames = existing.Select(d => d.DivName?.ToUpperInvariant()).ToHashSet();
            var letter = 'A';
            do { divName = $"Pool {letter}"; letter++; } while (existingNames.Contains(divName.ToUpperInvariant()));
        }

        var div = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = agegroupId,
            DivName = divName,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };

        _divisionRepo.Add(div);
        await _divisionRepo.SaveChangesAsync(cancellationToken);
        return div.DivId;
    }

    // ═══════════════════════════════════════════
    // Team CRUD
    // ═══════════════════════════════════════════

    public async Task<TeamDetailDto> GetTeamDetailAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateTeamOwnershipAsync(teamId, jobId, cancellationToken);
        var team = await _teamRepo.GetByIdReadOnlyAsync(teamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");
        var playerCount = await _teamRepo.GetPlayerCountAsync(teamId, cancellationToken);
        var clubName = await _teamRepo.GetClubNameForTeamAsync(teamId, cancellationToken);
        return MapTeam(team, playerCount, clubName);
    }

    public async Task<TeamDetailDto> CreateTeamAsync(CreateTeamRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(request.DivId, jobId, cancellationToken);

        // Get parent agegroup/league from division
        var div = await _divisionRepo.GetByIdReadOnlyAsync(request.DivId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {request.DivId} not found.");
        var ag = await _agegroupRepo.GetByIdAsync(div.AgegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {div.AgegroupId} not found.");

        var nextRank = await _teamRepo.GetNextDivRankAsync(request.DivId, cancellationToken);
        var jobSY = await _jobRepo.GetJobSeasonYearAsync(jobId, cancellationToken);

        var team = new TSIC.Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = ag.LeagueId,
            AgegroupId = ag.AgegroupId,
            DivId = request.DivId,
            TeamName = request.TeamName,
            Active = request.Active ?? true,
            DivRank = nextRank,
            DivisionRequested = request.DivisionRequested,
            Color = request.Color,
            MaxCount = request.MaxCount,
            BAllowSelfRostering = request.BAllowSelfRostering,
            BHideRoster = request.BHideRoster,
            Startdate = request.Startdate,
            Enddate = request.Enddate,
            Effectiveasofdate = request.Effectiveasofdate,
            Expireondate = request.Expireondate,
            DobMin = request.DobMin,
            DobMax = request.DobMax,
            GradYearMin = request.GradYearMin,
            GradYearMax = request.GradYearMax,
            SchoolGradeMin = request.SchoolGradeMin,
            SchoolGradeMax = request.SchoolGradeMax,
            Gender = request.Gender,
            Season = jobSY?.Season,
            Year = jobSY?.Year,
            Dow = request.Dow,
            Dow2 = request.Dow2,
            FieldId1 = request.FieldId1,
            FieldId2 = request.FieldId2,
            FieldId3 = request.FieldId3,
            LevelOfPlay = request.LevelOfPlay,
            Requests = request.Requests,
            KeywordPairs = request.KeywordPairs,
            TeamComments = request.TeamComments,
            LebUserId = userId,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };

        _teamRepo.Add(team);
        await _teamRepo.SaveChangesAsync(cancellationToken);
        return MapTeam(team, 0);
    }

    public async Task<TeamDetailDto> UpdateTeamAsync(Guid teamId, UpdateTeamRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateTeamOwnershipAsync(teamId, jobId, cancellationToken);
        var team = await _teamRepo.GetTeamFromTeamId(teamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");

        // Detect team name change for waitlist cascade
        var oldTeamName = team.TeamName;
        var teamNameChanged = request.TeamName != null && oldTeamName != null
            && !string.Equals(oldTeamName, request.TeamName, StringComparison.Ordinal)
            && !oldTeamName.Contains("WAITLIST", StringComparison.OrdinalIgnoreCase);

        if (request.TeamName != null) team.TeamName = request.TeamName;
        if (request.Active.HasValue) team.Active = request.Active;
        if (request.DivisionRequested != null) team.DivisionRequested = request.DivisionRequested;
        if (request.LastLeagueRecord != null) team.LastLeagueRecord = request.LastLeagueRecord;
        if (request.Color != null) team.Color = request.Color;
        if (request.MaxCount.HasValue) team.MaxCount = request.MaxCount.Value;
        if (request.BAllowSelfRostering.HasValue) team.BAllowSelfRostering = request.BAllowSelfRostering;
        if (request.BHideRoster.HasValue) team.BHideRoster = request.BHideRoster.Value;
        team.Startdate = request.Startdate;
        team.Enddate = request.Enddate;
        team.Effectiveasofdate = request.Effectiveasofdate;
        team.Expireondate = request.Expireondate;
        team.DobMin = request.DobMin;
        team.DobMax = request.DobMax;
        team.GradYearMin = request.GradYearMin;
        team.GradYearMax = request.GradYearMax;
        team.SchoolGradeMin = request.SchoolGradeMin;
        team.SchoolGradeMax = request.SchoolGradeMax;
        if (request.Gender != null) team.Gender = request.Gender;
        // Season/Year are immutable (set from Job on creation) — never overwrite
        team.Dow = request.Dow;
        team.Dow2 = request.Dow2;
        team.FieldId1 = request.FieldId1;
        team.FieldId2 = request.FieldId2;
        team.FieldId3 = request.FieldId3;
        if (request.LevelOfPlay != null) team.LevelOfPlay = request.LevelOfPlay;
        team.Requests = request.Requests;
        team.KeywordPairs = request.KeywordPairs;
        team.TeamComments = request.TeamComments;
        team.LebUserId = userId;
        team.Modified = DateTime.UtcNow;

        // Cascade rename to WAITLIST team mirror if it exists
        if (teamNameChanged)
        {
            var ag = await _agegroupRepo.GetByIdAsync(team.AgegroupId, cancellationToken);
            if (ag != null)
            {
                var agSiblings = await _agegroupRepo.GetByLeagueIdAsync(ag.LeagueId, cancellationToken);
                var waitlistAg = agSiblings.Find(s =>
                    string.Equals(s.AgegroupName, $"WAITLIST - {ag.AgegroupName}", StringComparison.OrdinalIgnoreCase));
                if (waitlistAg != null)
                {
                    var waitlistTeams = await _teamRepo.GetByAgegroupIdAsync(waitlistAg.AgegroupId, cancellationToken);
                    var waitlistTeam = waitlistTeams.Find(t =>
                        string.Equals(t.TeamName, $"WAITLIST - {oldTeamName}", StringComparison.OrdinalIgnoreCase));
                    if (waitlistTeam != null)
                    {
                        var trackedTeam = await _teamRepo.GetTeamFromTeamId(waitlistTeam.TeamId, cancellationToken);
                        if (trackedTeam != null)
                        {
                            trackedTeam.TeamName = $"WAITLIST - {request.TeamName}";
                            trackedTeam.LebUserId = userId;
                            trackedTeam.Modified = DateTime.UtcNow;
                        }
                    }
                }
            }
        }

        await _teamRepo.SaveChangesAsync(cancellationToken);

        // Sync denormalized team names in Schedule if team name changed
        if (request.TeamName != null)
            await _scheduleRepo.SynchronizeScheduleNamesForTeamAsync(teamId, jobId, cancellationToken);

        var playerCount = await _teamRepo.GetPlayerCountAsync(teamId, cancellationToken);
        var clubName = await _teamRepo.GetClubNameForTeamAsync(teamId, cancellationToken);
        return MapTeam(team, playerCount, clubName);
    }

    public async Task<DeleteTeamResultDto> DeleteTeamAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateTeamOwnershipAsync(teamId, jobId, cancellationToken);

        if (await _teamRepo.HasRosteredPlayersAsync(teamId, cancellationToken))
        {
            // Soft delete: set Active = false (players are still assigned)
            var team = await _teamRepo.GetTeamFromTeamId(teamId, cancellationToken)
                ?? throw new KeyNotFoundException($"Team {teamId} not found.");
            var divId = team.DivId;
            team.Active = false;
            team.Modified = DateTime.UtcNow;
            await _teamRepo.SaveChangesAsync(cancellationToken);

            // Renumber remaining active teams to maintain contiguous 1..N ranking
            if (divId.HasValue)
                await _teamRepo.RenumberDivRanksAsync(divId.Value, cancellationToken);

            return new DeleteTeamResultDto
            {
                WasDeactivated = true,
                Message = "Team was deactivated because it has rostered players. The team still exists but is no longer active."
            };
        }

        var teamToDelete = await _teamRepo.GetTeamFromTeamId(teamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");
        var deletedDivId = teamToDelete.DivId;
        _teamRepo.Remove(teamToDelete);
        await _teamRepo.SaveChangesAsync(cancellationToken);

        // Renumber remaining active teams to maintain contiguous 1..N ranking
        if (deletedDivId.HasValue)
            await _teamRepo.RenumberDivRanksAsync(deletedDivId.Value, cancellationToken);

        return new DeleteTeamResultDto
        {
            WasDeactivated = false,
            Message = "Team was permanently deleted."
        };
    }

    public async Task<DropTeamResultDto> DropTeamAsync(Guid teamId, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateTeamOwnershipAsync(teamId, jobId, cancellationToken);

        var team = await _teamRepo.GetTeamFromTeamId(teamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");

        // Check all three conditions for hard delete eligibility
        var isScheduled = await _teamRepo.IsTeamScheduledAsync(teamId, jobId, cancellationToken);
        var playerCount = await _teamRepo.GetPlayerCountAsync(teamId, cancellationToken);
        var hasPayments = await _regAcctRepo.HasPaymentsForTeamAsync(teamId, cancellationToken);

        // Capture source division for renumbering after removal
        var sourceDivId = team.DivId;

        // Hard delete: no players, no payments, no schedule — clean team with no footprint
        if (!isScheduled && playerCount == 0 && !hasPayments)
        {
            var clubRepRegId = team.ClubrepRegistrationid;
            _teamRepo.Remove(team);
            await _teamRepo.SaveChangesAsync(cancellationToken);

            // Renumber remaining active teams to maintain contiguous 1..N ranking
            if (sourceDivId.HasValue)
                await _teamRepo.RenumberDivRanksAsync(sourceDivId.Value, cancellationToken);

            // Recalculate club rep financials since team fees were baked in
            if (clubRepRegId.HasValue)
                await _registrationRepo.SynchronizeClubRepFinancialsAsync(clubRepRegId.Value, userId, cancellationToken);

            return new DropTeamResultDto
            {
                WasDropped = false,
                WasDeleted = true,
                Message = "Team permanently deleted (no players, payments, or schedule history).",
                PlayersAffected = 0
            };
        }

        // Soft drop: team has history — block if scheduled, otherwise move to Dropped Teams
        if (isScheduled)
            throw new InvalidOperationException("Cannot drop a team that is assigned to a schedule.");

        // Find or create "Dropped Teams" agegroup + division under the same league
        var droppedAgId = await FindOrCreateDroppedTeamsAgegroupAsync(team.LeagueId, userId, cancellationToken);
        var droppedDivId = await FindOrCreateDroppedTeamsDivisionAsync(droppedAgId, userId, cancellationToken);

        // Zero out all player fees for this team
        var playersAffected = await _registrationRepo.ZeroFeesForTeamAsync(teamId, jobId, cancellationToken);

        // Move team to Dropped Teams and deactivate
        team.AgegroupId = droppedAgId;
        team.DivId = droppedDivId;
        team.Active = false;
        team.LebUserId = userId;
        team.Modified = DateTime.UtcNow;

        await _teamRepo.SaveChangesAsync(cancellationToken);

        // Renumber source division to maintain contiguous 1..N ranking
        if (sourceDivId.HasValue)
            await _teamRepo.RenumberDivRanksAsync(sourceDivId.Value, cancellationToken);

        // Recalculate club rep financials after fee zeroing
        if (team.ClubrepRegistrationid.HasValue)
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(team.ClubrepRegistrationid.Value, userId, cancellationToken);

        return new DropTeamResultDto
        {
            WasDropped = true,
            WasDeleted = false,
            Message = $"Team moved to Dropped Teams and deactivated. {playersAffected} player fee(s) zeroed.",
            PlayersAffected = playersAffected
        };
    }

    private async Task<Guid> FindOrCreateDroppedTeamsAgegroupAsync(Guid leagueId, string userId, CancellationToken cancellationToken)
    {
        const string DroppedTeamsName = "Dropped Teams";
        var agegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, cancellationToken);
        var existing = agegroups.Find(a =>
            string.Equals(a.AgegroupName, DroppedTeamsName, StringComparison.OrdinalIgnoreCase));

        if (existing != null) return existing.AgegroupId;

        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = DroppedTeamsName,
            MaxTeams = 999,
            MaxTeamsPerClub = 999,
            SortAge = 254,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _agegroupRepo.Add(ag);
        await _agegroupRepo.SaveChangesAsync(cancellationToken);
        return ag.AgegroupId;
    }

    private async Task<Guid> FindOrCreateDroppedTeamsDivisionAsync(Guid droppedAgId, string userId, CancellationToken cancellationToken)
    {
        const string DroppedTeamsName = "Dropped Teams";
        var divisions = await _divisionRepo.GetByAgegroupIdAsync(droppedAgId, cancellationToken);
        var existing = divisions.Find(d =>
            string.Equals(d.DivName, DroppedTeamsName, StringComparison.OrdinalIgnoreCase));

        if (existing != null) return existing.DivId;

        var div = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = droppedAgId,
            DivName = DroppedTeamsName,
            LebUserId = userId,
            Modified = DateTime.UtcNow
        };
        _divisionRepo.Add(div);
        await _divisionRepo.SaveChangesAsync(cancellationToken);
        return div.DivId;
    }

    public async Task<TeamDetailDto> CloneTeamAsync(Guid teamId, CloneTeamRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateTeamOwnershipAsync(teamId, jobId, cancellationToken);
        var source = await _teamRepo.GetByIdReadOnlyAsync(teamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");

        // Server-side enforcement of UI-coupled rules:
        //   Club linkage requires a source clubrep and forces fee copy, so that the
        //   cloned team lands with the fees the clubrep is financially responsible for.
        if (request.AddToClubLibrary && !source.ClubrepRegistrationid.HasValue)
            throw new InvalidOperationException("Cannot copy club linkage: source team has no club rep.");
        if (request.AddToClubLibrary && !request.CopyFees)
            throw new InvalidOperationException("Copy fees must be enabled when copying club linkage.");

        var nextRank = source.DivId.HasValue
            ? await _teamRepo.GetNextDivRankAsync(source.DivId.Value, cancellationToken)
            : 1;

        // Clone always lands in source's exact division. Cross-AG/div moves are handled
        // by the pool swapper, not by clone.
        var clone = new TSIC.Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = source.JobId,
            LeagueId = source.LeagueId,
            AgegroupId = source.AgegroupId,
            DivId = source.DivId,
            TeamName = request.TeamName,
            Active = true,
            DivRank = nextRank,
            Season = source.Season,
            Year = source.Year,
            LebUserId = userId,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };

        if (request.CopyEligibility)
        {
            clone.DobMin = source.DobMin;
            clone.DobMax = source.DobMax;
            clone.GradYearMin = source.GradYearMin;
            clone.GradYearMax = source.GradYearMax;
            clone.SchoolGradeMin = source.SchoolGradeMin;
            clone.SchoolGradeMax = source.SchoolGradeMax;
            clone.Gender = source.Gender;
        }

        if (request.CopyRosterSettings)
        {
            clone.MaxCount = source.MaxCount;
            clone.BAllowSelfRostering = source.BAllowSelfRostering;
            clone.BHideRoster = source.BHideRoster;
        }

        if (request.CopyDates)
        {
            clone.Startdate = source.Startdate;
            clone.Enddate = source.Enddate;
            clone.Effectiveasofdate = source.Effectiveasofdate;
            clone.Expireondate = source.Expireondate;
        }

        if (request.CopyVisualIdentity)
        {
            clone.Color = source.Color;
            clone.LevelOfPlay = source.LevelOfPlay;
        }

        // Link clone to source team's club rep (defensive re-check guaranteed above)
        if (request.AddToClubLibrary)
        {
            clone.ClubrepRegistrationid = source.ClubrepRegistrationid;
            clone.ClubrepId = source.ClubrepId;

            if (source.ClubTeamId.HasValue)
            {
                var sourceClubTeam = await _clubTeamRepo.GetByIdAsync(source.ClubTeamId.Value, cancellationToken);
                if (sourceClubTeam != null)
                {
                    var newClubTeam = new ClubTeams
                    {
                        ClubId = sourceClubTeam.ClubId,
                        ClubTeamName = request.TeamName,
                        ClubTeamGradYear = sourceClubTeam.ClubTeamGradYear,
                        ClubTeamLevelOfPlay = sourceClubTeam.ClubTeamLevelOfPlay,
                        LebUserId = userId,
                        Modified = DateTime.UtcNow
                    };
                    _clubTeamRepo.Add(newClubTeam);
                    await _clubTeamRepo.SaveChangesAsync(cancellationToken);
                    clone.ClubTeamId = newClubTeam.ClubTeamId;
                }
            }
        }

        _teamRepo.Add(clone);

        // Stage cloned fees (team-scoped JobFees + their FeeModifiers). Each row gets
        // a fresh Id and points at the new clone.TeamId.
        if (request.CopyFees)
        {
            var sourceFees = await _feeRepo.GetByTeamIdAsync(source.TeamId, cancellationToken);
            foreach (var sf in sourceFees)
            {
                var newFeeId = Guid.NewGuid();
                _feeRepo.Add(new JobFees
                {
                    JobFeeId = newFeeId,
                    JobId = sf.JobId,
                    RoleId = sf.RoleId,
                    AgegroupId = sf.AgegroupId,
                    TeamId = clone.TeamId,
                    Deposit = sf.Deposit,
                    BalanceDue = sf.BalanceDue,
                    Modified = DateTime.UtcNow,
                    LebUserId = userId
                });
                foreach (var mod in sf.FeeModifiers)
                {
                    _feeRepo.AddModifier(new FeeModifiers
                    {
                        FeeModifierId = Guid.NewGuid(),
                        JobFeeId = newFeeId,
                        ModifierType = mod.ModifierType,
                        Amount = mod.Amount,
                        StartDate = mod.StartDate,
                        EndDate = mod.EndDate,
                        Modified = DateTime.UtcNow,
                        LebUserId = userId
                    });
                }
            }
        }

        await _teamRepo.SaveChangesAsync(cancellationToken);

        // Resolver runs AFTER fees are persisted so the club rep's financial rollup
        // reflects the new team's fees.
        if (clone.ClubrepRegistrationid.HasValue)
            await _registrationRepo.SynchronizeClubRepFinancialsAsync(clone.ClubrepRegistrationid.Value, userId, cancellationToken);

        return MapTeam(clone, 0);
    }

    public async Task<Guid> AddStubTeamAsync(Guid divId, Guid jobId, string userId, string? name = null, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(divId, jobId, cancellationToken);

        var div = await _divisionRepo.GetByIdReadOnlyAsync(divId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");
        var ag = await _agegroupRepo.GetByIdAsync(div.AgegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {div.AgegroupId} not found.");

        // Resolve placement (admin bypass — places directly into requested division)
        var teamName = string.IsNullOrWhiteSpace(name) ? "New Team" : name.Trim();
        var placement = await _placement.ResolvePlacementAsync(
            jobId, ag.AgegroupId, teamName,
            divisionName: div.DivName, userId: userId,
            skipCapacityCheck: true, cancellationToken: cancellationToken);

        var nextRank = await _teamRepo.GetNextDivRankAsync(divId, cancellationToken);
        var jobSY = await _jobRepo.GetJobSeasonYearAsync(jobId, cancellationToken);

        var team = new TSIC.Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = placement.LeagueId,
            AgegroupId = placement.AgegroupId,
            DivId = placement.DivisionId ?? divId,
            TeamName = teamName,
            Active = true,
            DivRank = nextRank,
            Season = jobSY?.Season,
            Year = jobSY?.Year,
            MaxCount = 0,
            BHideRoster = false,
            LebUserId = userId,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };

        _teamRepo.Add(team);
        await _teamRepo.SaveChangesAsync(cancellationToken);
        return team.TeamId;
    }

    public async Task<List<ClubRegistrationDto>> GetClubRegistrationsForJobAsync(Guid jobId, CancellationToken ct = default)
    {
        var clubs = await _registrationRepo.GetClubRegistrationsForJobAsync(jobId, ct);
        return clubs.Select(c => new ClubRegistrationDto
        {
            RegistrationId = c.RegistrationId,
            ClubName = c.ClubName
        }).OrderBy(c => c.ClubName).ToList();
    }

    public async Task<MoveTeamToClubResultDto> MoveTeamToClubAsync(
        Guid teamId, MoveTeamToClubRequest request, Guid jobId, string userId, CancellationToken ct = default)
    {
        // 1. Validate team belongs to this job
        await ValidateTeamOwnershipAsync(teamId, jobId, ct);

        // 2. Fetch the team (tracked for updates)
        var team = await _teamRepo.GetTeamFromTeamId(teamId, ct)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");

        // 3. Guard: team must have club rep context
        if (!team.ClubrepRegistrationid.HasValue)
            throw new InvalidOperationException("Team has no club rep context — cannot change club.");

        var sourceRegistrationId = team.ClubrepRegistrationid.Value;

        // 4. Guard: can't move to same club
        if (sourceRegistrationId == request.TargetRegistrationId)
            throw new InvalidOperationException("Team is already assigned to this club.");

        // 5. Fetch target registration and validate
        var targetReg = await _registrationRepo.GetByIdAsync(request.TargetRegistrationId, ct)
            ?? throw new KeyNotFoundException("Target club registration not found.");
        if (targetReg.JobId != jobId)
            throw new InvalidOperationException("Target registration does not belong to this event.");

        // 6. Determine scope: single team or all teams from this club
        List<TSIC.Domain.Entities.Teams> teamsToMove;
        if (request.MoveAllFromClub)
        {
            teamsToMove = await _teamRepo.GetTeamsByClubRepRegistrationAsync(jobId, sourceRegistrationId, ct);
        }
        else
        {
            teamsToMove = [team];
        }

        // 7. Resolve target club for ClubTeamId reassignment (if any team has one)
        TSIC.Domain.Entities.Clubs? targetClub = null;
        if (teamsToMove.Exists(t => t.ClubTeamId.HasValue) && !string.IsNullOrEmpty(targetReg.ClubName))
        {
            targetClub = await _clubRepo.GetByNameAsync(targetReg.ClubName, ct);
        }

        // 8. Update each team
        foreach (var t in teamsToMove)
        {
            t.ClubrepRegistrationid = request.TargetRegistrationId;
            t.ClubrepId = targetReg.UserId;

            // Reassign ClubTeamId if present
            if (t.ClubTeamId.HasValue && targetClub is not null)
            {
                var sourceClubTeam = await _clubTeamRepo.GetByIdAsync(t.ClubTeamId.Value, ct);
                if (sourceClubTeam != null)
                {
                    // Find or create matching ClubTeam under target club
                    var targetClubTeams = await _clubTeamRepo.GetByClubIdAsync(targetClub.ClubId, ct);
                    var match = targetClubTeams.Find(ct2 =>
                        ct2.ClubTeamName == sourceClubTeam.ClubTeamName
                        && ct2.ClubTeamGradYear == sourceClubTeam.ClubTeamGradYear);

                    if (match != null)
                    {
                        t.ClubTeamId = match.ClubTeamId;
                    }
                    else
                    {
                        var newCt = new TSIC.Domain.Entities.ClubTeams
                        {
                            ClubId = targetClub.ClubId,
                            ClubTeamName = sourceClubTeam.ClubTeamName,
                            ClubTeamGradYear = sourceClubTeam.ClubTeamGradYear,
                            ClubTeamLevelOfPlay = sourceClubTeam.ClubTeamLevelOfPlay,
                            LebUserId = userId,
                            Modified = DateTime.UtcNow
                        };
                        _clubTeamRepo.Add(newCt);
                        await _clubTeamRepo.SaveChangesAsync(ct);
                        t.ClubTeamId = newCt.ClubTeamId;
                    }
                }
            }

            t.LebUserId = userId;
            t.Modified = DateTime.UtcNow;
        }

        // 9. Save all team changes
        await _teamRepo.SaveChangesAsync(ct);

        // 10. Recalculate club rep financials for BOTH clubs
        await _registrationRepo.SynchronizeClubRepFinancialsAsync(sourceRegistrationId, userId, ct);
        await _registrationRepo.SynchronizeClubRepFinancialsAsync(request.TargetRegistrationId, userId, ct);

        // 11. Sync denormalized schedule team names
        foreach (var t in teamsToMove)
        {
            await _scheduleRepo.SynchronizeScheduleNamesForTeamAsync(t.TeamId, jobId, ct);
        }

        return new MoveTeamToClubResultDto
        {
            TeamsAffected = teamsToMove.Count,
            Message = teamsToMove.Count == 1
                ? $"Team moved to {targetReg.ClubName}."
                : $"{teamsToMove.Count} teams moved to {targetReg.ClubName}."
        };
    }

    // ═══════════════════════════════════════════
    // Batch Operations
    // ═══════════════════════════════════════════

    public async Task<int> UpdatePlayerFeesToAgegroupFeesAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);

        _ = await _agegroupRepo.GetByIdAsync(agegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {agegroupId} not found.");

        var teams = await _teamRepo.GetByAgegroupIdAsync(agegroupId, cancellationToken);
        if (teams.Count == 0) return 0;

        var teamIds = teams.Select(t => t.TeamId).ToList();
        var registrations = await _registrationRepo.GetActivePlayerRegistrationsByTeamIdsAsync(jobId, teamIds, cancellationToken);
        if (registrations.Count == 0) return 0;

        // Batch-resolve player fees from new fee schema
        var feeByTeam = await _feeService.ResolveFeesByTeamIdsAsync(
            jobId, Domain.Constants.RoleConstants.Player, teamIds, cancellationToken);

        var regIds = registrations.Select(r => r.RegistrationId).ToList();
        var payments = await _regAcctRepo.GetPaymentSummariesAsync(regIds, cancellationToken);

        var updated = 0;
        foreach (var reg in registrations)
        {
            if (!reg.AssignedTeamId.HasValue) continue;

            var resolved = feeByTeam.TryGetValue(reg.AssignedTeamId.Value, out var rf) ? rf : null;
            var resolvedFee = resolved?.EffectiveBalanceDue ?? 0m;
            var summary = payments.GetValueOrDefault(reg.RegistrationId);

            // Refresh PaidTotal from actual accounting records
            reg.PaidTotal = summary?.TotalPayments ?? 0;

            // Guard: skip if fee unchanged and nothing owed
            if (reg.FeeBase == resolvedFee && reg.OwedTotal <= 0)
                continue;

            // Swap-style recalc: only FeeBase changes, modifiers preserved
            await _feeService.ApplySwapFeesAsync(
                reg, jobId, reg.AssignedAgegroupId ?? Guid.Empty, reg.AssignedTeamId.Value,
                new FeeApplicationContext
                {
                    NonCcPayments = summary?.NonCcPayments ?? 0m
                }, cancellationToken);

            reg.Modified = DateTime.UtcNow;
            updated++;
        }

        if (updated > 0)
            await _registrationRepo.SaveChangesAsync(cancellationToken);

        return updated;
    }

    // ═══════════════════════════════════════════
    // Sibling Batch Queries
    // ═══════════════════════════════════════════

    public async Task<List<LeagueDetailDto>> GetLeagueSiblingsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _leagueRepo.GetLeaguesByJobIdAsync(jobId, cancellationToken);
    }

    public async Task<List<AgegroupDetailDto>> GetAgegroupsByLeagueAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
        var agegroups = await _agegroupRepo.GetByLeagueIdAsync(leagueId, cancellationToken);
        return agegroups.Select(MapAgegroup).ToList();
    }

    public async Task<List<DivisionDetailDto>> GetDivisionsByAgegroupAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);
        var divisions = await _divisionRepo.GetByAgegroupIdAsync(agegroupId, cancellationToken);
        return divisions.Select(MapDivision).ToList();
    }

    public async Task<List<TeamDetailDto>> GetTeamsByDivisionAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(divId, jobId, cancellationToken);
        var teams = await _teamRepo.GetByDivisionIdAsync(divId, cancellationToken);
        var playerCounts = await _teamRepo.GetPlayerCountsByTeamAsync(jobId, cancellationToken);
        var clubNames = await _teamRepo.GetClubNamesByJobAsync(jobId, cancellationToken);
        return teams.Select(t => MapTeam(t, playerCounts.GetValueOrDefault(t.TeamId, 0), clubNames.GetValueOrDefault(t.TeamId))).ToList();
    }

    // ═══════════════════════════════════════════
    // Division Name Sync
    // ═══════════════════════════════════════════

    private static readonly HashSet<string> ExcludedDivisionNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Unassigned"
    };

    private static bool IsExcludedDivision(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        var trimmed = name.Trim();
        return ExcludedDivisionNames.Contains(trimmed)
            || trimmed.StartsWith("Unassigned", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSpecialAgegroup(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        return name.Contains("WAITLIST", StringComparison.OrdinalIgnoreCase)
            || name.Contains("DROPPED", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<List<DivisionNameSyncPreview>> PreviewDivisionNameSyncAsync(
        Guid jobId, List<string> themeNames, CancellationToken cancellationToken = default)
    {
        var agegroupDivisions = await GetSyncableDivisionsAsync(jobId, cancellationToken);
        var previews = new List<DivisionNameSyncPreview>();

        foreach (var (agName, agId, divisions) in agegroupDivisions)
        {
            // Alpha-sort the syncable divisions by current name
            var sorted = divisions.OrderBy(d => d.DivName, StringComparer.OrdinalIgnoreCase).ToList();

            var entries = new List<DivisionRenameEntry>();

            // Rename existing divisions up to theme count
            var renameLimit = Math.Min(sorted.Count, themeNames.Count);
            for (var i = 0; i < renameLimit; i++)
            {
                entries.Add(new DivisionRenameEntry
                {
                    DivId = sorted[i].DivId,
                    CurrentName = sorted[i].DivName ?? "(unnamed)",
                    ProposedName = themeNames[i]
                });
            }

            // New divisions to be created (theme names beyond existing count)
            for (var i = sorted.Count; i < themeNames.Count; i++)
            {
                entries.Add(new DivisionRenameEntry
                {
                    DivId = Guid.Empty,
                    CurrentName = "(new)",
                    ProposedName = themeNames[i],
                    IsNew = true
                });
            }

            // Existing divisions beyond theme count — will be deleted (if no teams)
            for (var i = themeNames.Count; i < sorted.Count; i++)
            {
                var hasTeams = await _divisionRepo.HasTeamsAsync(sorted[i].DivId, cancellationToken);
                entries.Add(new DivisionRenameEntry
                {
                    DivId = sorted[i].DivId,
                    CurrentName = sorted[i].DivName ?? "(unnamed)",
                    ProposedName = "",
                    IsDeleted = true,
                    HasTeams = hasTeams
                });
            }

            previews.Add(new DivisionNameSyncPreview
            {
                AgegroupName = agName,
                AgegroupId = agId,
                DivisionCount = themeNames.Count,
                Divisions = entries
            });
        }

        return previews;
    }

    public async Task<DivisionNameSyncResult> ApplyDivisionNameSyncAsync(
        Guid jobId, List<string> themeNames, string userId, CancellationToken cancellationToken = default)
    {
        var agegroupDivisions = await GetSyncableDivisionsAsync(jobId, cancellationToken);
        var errors = new List<string>();
        var renamed = 0;
        var created = 0;
        var deleted = 0;

        foreach (var (agName, agId, divisions) in agegroupDivisions)
        {
            var sorted = divisions.OrderBy(d => d.DivName, StringComparer.OrdinalIgnoreCase).ToList();

            // Rename existing divisions
            var renameLimit = Math.Min(sorted.Count, themeNames.Count);
            for (var i = 0; i < renameLimit; i++)
            {
                var newName = themeNames[i];
                var div = sorted[i];

                if (string.Equals(div.DivName, newName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var tracked = await _divisionRepo.GetByIdAsync(div.DivId, cancellationToken);
                if (tracked == null)
                {
                    errors.Add($"Division {div.DivId} not found in {agName}.");
                    continue;
                }

                tracked.DivName = newName;
                tracked.LebUserId = userId;
                tracked.Modified = DateTime.UtcNow;
                renamed++;

                await _scheduleRepo.SynchronizeScheduleDivisionNameAsync(
                    div.DivId, jobId, newName, cancellationToken);
            }

            // Create new divisions for theme names beyond existing count
            for (var i = sorted.Count; i < themeNames.Count; i++)
            {
                var request = new CreateDivisionRequest
                {
                    AgegroupId = agId,
                    DivName = themeNames[i]
                };
                await CreateDivisionAsync(request, jobId, userId, cancellationToken);
                created++;
            }

            // Delete extra divisions beyond theme count (only if no teams)
            for (var i = themeNames.Count; i < sorted.Count; i++)
            {
                try
                {
                    await DeleteDivisionAsync(sorted[i].DivId, jobId, cancellationToken);
                    deleted++;
                }
                catch (InvalidOperationException ex)
                {
                    errors.Add($"{agName}: {sorted[i].DivName} — {ex.Message}");
                }
            }
        }

        // CreateDivisionAsync/DeleteDivisionAsync save per-call; flush any remaining renames
        if (renamed > 0)
            await _divisionRepo.SaveChangesAsync(cancellationToken);

        return new DivisionNameSyncResult
        {
            DivisionsRenamed = renamed,
            DivisionsCreated = created,
            DivisionsDeleted = deleted,
            Errors = errors
        };
    }

    /// <summary>
    /// Returns syncable divisions (excluding Unassigned/WAITLIST/DROPPED) grouped by agegroup,
    /// for all non-special agegroups in the job. Includes agegroups with 0 syncable divisions
    /// so that new themed divisions can be created for them.
    /// </summary>
    private async Task<List<(string AgName, Guid AgId, List<Divisions> Divisions)>> GetSyncableDivisionsAsync(
        Guid jobId, CancellationToken cancellationToken)
    {
        var leagues = await _leagueRepo.GetLeaguesByJobIdAsync(jobId, cancellationToken);
        var result = new List<(string AgName, Guid AgId, List<Divisions> Divisions)>();

        foreach (var league in leagues)
        {
            var agegroups = await _agegroupRepo.GetByLeagueIdAsync(league.LeagueId, cancellationToken);

            foreach (var ag in agegroups)
            {
                // Skip WAITLIST/DROPPED agegroups entirely
                if (IsSpecialAgegroup(ag.AgegroupName))
                    continue;

                var divisions = await _divisionRepo.GetByAgegroupIdAsync(ag.AgegroupId, cancellationToken);
                var syncable = divisions
                    .Where(d => !IsExcludedDivision(d.DivName)
                             && !IsSpecialAgegroup(d.DivName))
                    .ToList();

                result.Add((ag.AgegroupName ?? "(unnamed)", ag.AgegroupId, syncable));
            }
        }

        return result;
    }

    // ═══════════════════════════════════════════
    // Validation Helpers
    // ═══════════════════════════════════════════

    private async Task ValidateLeagueOwnershipAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await _leagueRepo.BelongsToJobAsync(leagueId, jobId, cancellationToken))
            throw new InvalidOperationException("League does not belong to the current job.");
    }

    private async Task ValidateAgegroupOwnershipAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await _agegroupRepo.BelongsToJobAsync(agegroupId, jobId, cancellationToken))
            throw new InvalidOperationException("Agegroup does not belong to the current job.");
    }

    private async Task ValidateDivisionOwnershipAsync(Guid divId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await _divisionRepo.BelongsToJobAsync(divId, jobId, cancellationToken))
            throw new InvalidOperationException("Division does not belong to the current job.");
    }

    private async Task ValidateTeamOwnershipAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken)
    {
        if (!await _teamRepo.BelongsToJobAsync(teamId, jobId, cancellationToken))
            throw new InvalidOperationException("Team does not belong to the current job.");
    }

    // ═══════════════════════════════════════════
    // DTO Mapping
    // ═══════════════════════════════════════════

    private static AgegroupDetailDto MapAgegroup(Agegroups a) => new()
    {
        AgegroupId = a.AgegroupId,
        LeagueId = a.LeagueId,
        AgegroupName = a.AgegroupName,
        Season = a.Season,
        Color = a.Color,
        Gender = a.Gender,
        DobMin = a.DobMin,
        DobMax = a.DobMax,
        GradYearMin = a.GradYearMin,
        GradYearMax = a.GradYearMax,
        SchoolGradeMin = a.SchoolGradeMin,
        SchoolGradeMax = a.SchoolGradeMax,
        MaxTeams = a.MaxTeams,
        MaxTeamsPerClub = a.MaxTeamsPerClub,
        BAllowSelfRostering = a.BAllowSelfRostering,
        BChampionsByDivision = a.BChampionsByDivision,
        BAllowApiRosterAccess = a.BAllowApiRosterAccess,
        BHideStandings = a.BHideStandings,
        SortAge = a.SortAge
    };

    private static DivisionDetailDto MapDivision(Divisions d) => new()
    {
        DivId = d.DivId,
        AgegroupId = d.AgegroupId,
        DivName = d.DivName,
        MaxRoundNumberToShow = d.MaxRoundNumberToShow
    };

    private static TeamDetailDto MapTeam(TSIC.Domain.Entities.Teams t, int playerCount, string? clubName = null) => new()
    {
        TeamId = t.TeamId,
        DivId = t.DivId,
        AgegroupId = t.AgegroupId,
        LeagueId = t.LeagueId,
        JobId = t.JobId,
        TeamName = t.TeamName,
        ClubName = clubName,
        Active = t.Active,
        DivRank = t.DivRank,
        DivisionRequested = t.DivisionRequested,
        LastLeagueRecord = t.LastLeagueRecord,
        Color = t.Color,
        MaxCount = t.MaxCount,
        BAllowSelfRostering = t.BAllowSelfRostering,
        BHideRoster = t.BHideRoster,
        Startdate = t.Startdate,
        Enddate = t.Enddate,
        Effectiveasofdate = t.Effectiveasofdate,
        Expireondate = t.Expireondate,
        DobMin = t.DobMin,
        DobMax = t.DobMax,
        GradYearMin = t.GradYearMin,
        GradYearMax = t.GradYearMax,
        SchoolGradeMin = t.SchoolGradeMin,
        SchoolGradeMax = t.SchoolGradeMax,
        Gender = t.Gender,
        Season = t.Season,
        Year = t.Year,
        Dow = t.Dow,
        Dow2 = t.Dow2,
        FieldId1 = t.FieldId1,
        FieldId2 = t.FieldId2,
        FieldId3 = t.FieldId3,
        LevelOfPlay = t.LevelOfPlay,
        Requests = t.Requests,
        KeywordPairs = t.KeywordPairs,
        TeamComments = t.TeamComments,
        ClubRepRegistrationId = t.ClubrepRegistrationid,
        ClubTeamId = t.ClubTeamId,
        PlayerCount = playerCount
    };
}
