using TSIC.Contracts.Dtos.Ladt;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

// Note: Use TSIC.Domain.Entities.Teams (fully-qualified) to resolve
// collision with TSIC.API.Services.Teams namespace.

namespace TSIC.API.Services.Admin;

/// <summary>
/// Service for the LADT (League/Age-Group/Division/Team) admin hierarchy.
/// Handles all 4 entity levels with shared validation and authorization logic.
/// </summary>
public sealed class LadtService : ILadtService
{
    private readonly ILeagueRepository _leagueRepo;
    private readonly IAgeGroupRepository _agegroupRepo;
    private readonly IDivisionRepository _divisionRepo;
    private readonly ITeamRepository _teamRepo;

    public LadtService(
        ILeagueRepository leagueRepo,
        IAgeGroupRepository agegroupRepo,
        IDivisionRepository divisionRepo,
        ITeamRepository teamRepo)
    {
        _leagueRepo = leagueRepo;
        _agegroupRepo = agegroupRepo;
        _divisionRepo = divisionRepo;
        _teamRepo = teamRepo;
    }

    // ═══════════════════════════════════════════
    // Tree
    // ═══════════════════════════════════════════

    public async Task<LadtTreeRootDto> GetLadtTreeAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var leagues = await _leagueRepo.GetLeaguesByJobIdAsync(jobId, cancellationToken);
        var playerCounts = await _teamRepo.GetPlayerCountsByTeamAsync(jobId, cancellationToken);

        var totalTeams = 0;
        var totalPlayers = 0;

        var leagueNodes = new List<LadtTreeNodeDto>();

        foreach (var league in leagues)
        {
            var agegroups = await _agegroupRepo.GetByLeagueIdAsync(league.LeagueId, cancellationToken);
            var agegroupNodes = new List<LadtTreeNodeDto>();

            foreach (var ag in agegroups)
            {
                var divisions = await _divisionRepo.GetByAgegroupIdAsync(ag.AgegroupId, cancellationToken);
                var divisionNodes = new List<LadtTreeNodeDto>();

                foreach (var div in divisions)
                {
                    var teams = await _teamRepo.GetByDivisionIdAsync(div.DivId, cancellationToken);
                    var teamNodes = new List<LadtTreeNodeDto>();

                    foreach (var team in teams)
                    {
                        var pc = playerCounts.GetValueOrDefault(team.TeamId, 0);
                        totalPlayers += pc;
                        totalTeams++;

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
                            Children = null
                        });
                    }

                    var divTeamCount = teamNodes.Count;
                    var divPlayerCount = teamNodes.Sum(t => t.PlayerCount);

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
            TotalPlayers = totalPlayers
        };
    }

    // ═══════════════════════════════════════════
    // League CRUD
    // ═══════════════════════════════════════════

    public async Task<LeagueDetailDto> GetLeagueDetailAsync(Guid leagueId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
        var league = await _leagueRepo.GetByIdWithSportAsync(leagueId, cancellationToken)
            ?? throw new KeyNotFoundException($"League {leagueId} not found.");
        return MapLeague(league);
    }

    public async Task<LeagueDetailDto> UpdateLeagueAsync(Guid leagueId, UpdateLeagueRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);
        var league = await _leagueRepo.GetByIdAsync(leagueId, cancellationToken)
            ?? throw new KeyNotFoundException($"League {leagueId} not found.");

        league.LeagueName = request.LeagueName;
        league.SportId = request.SportId;
        league.BAllowCoachScoreEntry = request.BAllowCoachScoreEntry;
        league.BHideContacts = request.BHideContacts;
        league.BHideStandings = request.BHideStandings;
        league.BShowScheduleToTeamMembers = request.BShowScheduleToTeamMembers;
        league.BTakeAttendance = request.BTakeAttendance;
        league.BTrackPenaltyMinutes = request.BTrackPenaltyMinutes;
        league.BTrackSportsmanshipScores = request.BTrackSportsmanshipScores;
        league.RescheduleEmailsToAddon = request.RescheduleEmailsToAddon;
        league.PlayerFeeOverride = request.PlayerFeeOverride;
        league.StandingsSortProfileId = request.StandingsSortProfileId;
        league.PointsMethod = request.PointsMethod;
        league.StrLop = request.StrLop;
        league.StrGradYears = request.StrGradYears;
        league.LebUserId = userId;
        league.Modified = DateTime.UtcNow;

        await _leagueRepo.SaveChangesAsync(cancellationToken);

        var updated = await _leagueRepo.GetByIdWithSportAsync(leagueId, cancellationToken);
        return MapLeague(updated!);
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

        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = request.LeagueId,
            AgegroupName = request.AgegroupName,
            Season = request.Season,
            Color = request.Color,
            Gender = request.Gender,
            DobMin = request.DobMin,
            DobMax = request.DobMax,
            GradYearMin = request.GradYearMin,
            GradYearMax = request.GradYearMax,
            SchoolGradeMin = request.SchoolGradeMin,
            SchoolGradeMax = request.SchoolGradeMax,
            TeamFee = request.TeamFee,
            TeamFeeLabel = request.TeamFeeLabel,
            RosterFee = request.RosterFee,
            RosterFeeLabel = request.RosterFeeLabel,
            DiscountFee = request.DiscountFee,
            DiscountFeeStart = request.DiscountFeeStart,
            DiscountFeeEnd = request.DiscountFeeEnd,
            LateFee = request.LateFee,
            LateFeeStart = request.LateFeeStart,
            LateFeeEnd = request.LateFeeEnd,
            MaxTeams = request.MaxTeams,
            MaxTeamsPerClub = request.MaxTeamsPerClub,
            BAllowSelfRostering = request.BAllowSelfRostering,
            BChampionsByDivision = request.BChampionsByDivision,
            BAllowApiRosterAccess = request.BAllowApiRosterAccess,
            BHideStandings = request.BHideStandings,
            PlayerFeeOverride = request.PlayerFeeOverride,
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
            DivName = "Pool A",
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

        ag.AgegroupName = request.AgegroupName;
        ag.Season = request.Season;
        ag.Color = request.Color;
        ag.Gender = request.Gender;
        ag.DobMin = request.DobMin;
        ag.DobMax = request.DobMax;
        ag.GradYearMin = request.GradYearMin;
        ag.GradYearMax = request.GradYearMax;
        ag.SchoolGradeMin = request.SchoolGradeMin;
        ag.SchoolGradeMax = request.SchoolGradeMax;
        ag.TeamFee = request.TeamFee;
        ag.TeamFeeLabel = request.TeamFeeLabel;
        ag.RosterFee = request.RosterFee;
        ag.RosterFeeLabel = request.RosterFeeLabel;
        ag.DiscountFee = request.DiscountFee;
        ag.DiscountFeeStart = request.DiscountFeeStart;
        ag.DiscountFeeEnd = request.DiscountFeeEnd;
        ag.LateFee = request.LateFee;
        ag.LateFeeStart = request.LateFeeStart;
        ag.LateFeeEnd = request.LateFeeEnd;
        ag.MaxTeams = request.MaxTeams;
        ag.MaxTeamsPerClub = request.MaxTeamsPerClub;
        ag.BAllowSelfRostering = request.BAllowSelfRostering;
        ag.BChampionsByDivision = request.BChampionsByDivision;
        ag.BAllowApiRosterAccess = request.BAllowApiRosterAccess;
        ag.BHideStandings = request.BHideStandings;
        ag.PlayerFeeOverride = request.PlayerFeeOverride;
        ag.SortAge = request.SortAge;
        ag.LebUserId = userId;
        ag.Modified = DateTime.UtcNow;

        await _agegroupRepo.SaveChangesAsync(cancellationToken);
        return MapAgegroup(ag);
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

        _agegroupRepo.Remove(ag);
        await _agegroupRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddStubAgegroupAsync(Guid leagueId, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateLeagueOwnershipAsync(leagueId, jobId, cancellationToken);

        var ag = new Agegroups
        {
            AgegroupId = Guid.NewGuid(),
            LeagueId = leagueId,
            AgegroupName = "New Age Group",
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
            DivName = "Pool A",
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

        div.DivName = request.DivName;
        div.MaxRoundNumberToShow = request.MaxRoundNumberToShow;
        div.LebUserId = userId;
        div.Modified = DateTime.UtcNow;

        await _divisionRepo.SaveChangesAsync(cancellationToken);
        return MapDivision(div);
    }

    public async Task DeleteDivisionAsync(Guid divId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(divId, jobId, cancellationToken);

        if (await _divisionRepo.HasTeamsAsync(divId, cancellationToken))
            throw new InvalidOperationException("Cannot delete division that contains teams. Remove all teams first.");

        var div = await _divisionRepo.GetByIdAsync(divId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");

        _divisionRepo.Remove(div);
        await _divisionRepo.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> AddStubDivisionAsync(Guid agegroupId, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);

        // Count existing divisions to generate name
        var existing = await _divisionRepo.GetByAgegroupIdAsync(agegroupId, cancellationToken);
        var letter = (char)('A' + existing.Count);

        var div = new Divisions
        {
            DivId = Guid.NewGuid(),
            AgegroupId = agegroupId,
            DivName = $"Pool {letter}",
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
        return MapTeam(team, playerCount);
    }

    public async Task<TeamDetailDto> CreateTeamAsync(CreateTeamRequest request, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(request.DivId, jobId, cancellationToken);

        // Get parent agegroup/league from division
        var div = await _divisionRepo.GetByIdReadOnlyAsync(request.DivId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {request.DivId} not found.");
        var ag = await _agegroupRepo.GetByIdAsync(div.AgegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {div.AgegroupId} not found.");

        var maxRank = await _teamRepo.GetMaxDivRankAsync(request.DivId, cancellationToken);

        var team = new TSIC.Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = ag.LeagueId,
            AgegroupId = ag.AgegroupId,
            DivId = request.DivId,
            TeamName = request.TeamName,
            Active = request.Active ?? true,
            DivRank = maxRank + 1,
            DivisionRequested = request.DivisionRequested,
            Color = request.Color,
            MaxCount = request.MaxCount,
            BAllowSelfRostering = request.BAllowSelfRostering,
            BHideRoster = request.BHideRoster,
            FeeBase = request.FeeBase,
            PerRegistrantFee = request.PerRegistrantFee,
            PerRegistrantDeposit = request.PerRegistrantDeposit,
            DiscountFee = request.DiscountFee,
            DiscountFeeStart = request.DiscountFeeStart,
            DiscountFeeEnd = request.DiscountFeeEnd,
            LateFee = request.LateFee,
            LateFeeStart = request.LateFeeStart,
            LateFeeEnd = request.LateFeeEnd,
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
            Season = request.Season,
            Year = request.Year,
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

        if (request.TeamName != null) team.TeamName = request.TeamName;
        if (request.Active.HasValue) team.Active = request.Active;
        if (request.DivisionRequested != null) team.DivisionRequested = request.DivisionRequested;
        if (request.LastLeagueRecord != null) team.LastLeagueRecord = request.LastLeagueRecord;
        if (request.Color != null) team.Color = request.Color;
        if (request.MaxCount.HasValue) team.MaxCount = request.MaxCount.Value;
        if (request.BAllowSelfRostering.HasValue) team.BAllowSelfRostering = request.BAllowSelfRostering;
        if (request.BHideRoster.HasValue) team.BHideRoster = request.BHideRoster.Value;
        team.FeeBase = request.FeeBase;
        team.PerRegistrantFee = request.PerRegistrantFee;
        team.PerRegistrantDeposit = request.PerRegistrantDeposit;
        team.DiscountFee = request.DiscountFee;
        team.DiscountFeeStart = request.DiscountFeeStart;
        team.DiscountFeeEnd = request.DiscountFeeEnd;
        team.LateFee = request.LateFee;
        team.LateFeeStart = request.LateFeeStart;
        team.LateFeeEnd = request.LateFeeEnd;
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
        if (request.Season != null) team.Season = request.Season;
        if (request.Year != null) team.Year = request.Year;
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

        await _teamRepo.SaveChangesAsync(cancellationToken);
        var playerCount = await _teamRepo.GetPlayerCountAsync(teamId, cancellationToken);
        return MapTeam(team, playerCount);
    }

    public async Task<DeleteTeamResultDto> DeleteTeamAsync(Guid teamId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateTeamOwnershipAsync(teamId, jobId, cancellationToken);

        if (await _teamRepo.HasRosteredPlayersAsync(teamId, cancellationToken))
        {
            // Soft delete: set Active = false (players are still assigned)
            var team = await _teamRepo.GetTeamFromTeamId(teamId, cancellationToken)
                ?? throw new KeyNotFoundException($"Team {teamId} not found.");
            team.Active = false;
            team.Modified = DateTime.UtcNow;
            await _teamRepo.SaveChangesAsync(cancellationToken);

            return new DeleteTeamResultDto
            {
                WasDeactivated = true,
                Message = "Team was deactivated because it has rostered players. The team still exists but is no longer active."
            };
        }

        var teamToDelete = await _teamRepo.GetTeamFromTeamId(teamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");
        _teamRepo.Remove(teamToDelete);
        await _teamRepo.SaveChangesAsync(cancellationToken);

        return new DeleteTeamResultDto
        {
            WasDeactivated = false,
            Message = "Team was permanently deleted."
        };
    }

    public async Task<TeamDetailDto> CloneTeamAsync(Guid teamId, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateTeamOwnershipAsync(teamId, jobId, cancellationToken);
        var source = await _teamRepo.GetByIdReadOnlyAsync(teamId, cancellationToken)
            ?? throw new KeyNotFoundException($"Team {teamId} not found.");

        var maxRank = source.DivId.HasValue
            ? await _teamRepo.GetMaxDivRankAsync(source.DivId.Value, cancellationToken)
            : 0;

        var clone = new TSIC.Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = source.JobId,
            LeagueId = source.LeagueId,
            AgegroupId = source.AgegroupId,
            DivId = source.DivId,
            TeamName = $"{source.TeamName} (Copy)",
            Active = true,
            DivRank = maxRank + 1,
            DivisionRequested = source.DivisionRequested,
            Color = source.Color,
            MaxCount = source.MaxCount,
            BAllowSelfRostering = source.BAllowSelfRostering,
            BHideRoster = source.BHideRoster,
            FeeBase = source.FeeBase,
            PerRegistrantFee = source.PerRegistrantFee,
            PerRegistrantDeposit = source.PerRegistrantDeposit,
            DiscountFee = source.DiscountFee,
            DiscountFeeStart = source.DiscountFeeStart,
            DiscountFeeEnd = source.DiscountFeeEnd,
            LateFee = source.LateFee,
            LateFeeStart = source.LateFeeStart,
            LateFeeEnd = source.LateFeeEnd,
            Startdate = source.Startdate,
            Enddate = source.Enddate,
            Effectiveasofdate = source.Effectiveasofdate,
            Expireondate = source.Expireondate,
            DobMin = source.DobMin,
            DobMax = source.DobMax,
            GradYearMin = source.GradYearMin,
            GradYearMax = source.GradYearMax,
            SchoolGradeMin = source.SchoolGradeMin,
            SchoolGradeMax = source.SchoolGradeMax,
            Gender = source.Gender,
            Season = source.Season,
            Year = source.Year,
            Dow = source.Dow,
            Dow2 = source.Dow2,
            FieldId1 = source.FieldId1,
            FieldId2 = source.FieldId2,
            FieldId3 = source.FieldId3,
            LevelOfPlay = source.LevelOfPlay,
            Requests = source.Requests,
            KeywordPairs = source.KeywordPairs,
            TeamComments = source.TeamComments,
            LebUserId = userId,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };

        _teamRepo.Add(clone);
        await _teamRepo.SaveChangesAsync(cancellationToken);
        return MapTeam(clone, 0);
    }

    public async Task<Guid> AddStubTeamAsync(Guid divId, Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        await ValidateDivisionOwnershipAsync(divId, jobId, cancellationToken);

        var div = await _divisionRepo.GetByIdReadOnlyAsync(divId, cancellationToken)
            ?? throw new KeyNotFoundException($"Division {divId} not found.");
        var ag = await _agegroupRepo.GetByIdAsync(div.AgegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {div.AgegroupId} not found.");

        var maxRank = await _teamRepo.GetMaxDivRankAsync(divId, cancellationToken);

        var team = new TSIC.Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = ag.LeagueId,
            AgegroupId = ag.AgegroupId,
            DivId = divId,
            TeamName = "New Team",
            Active = true,
            DivRank = maxRank + 1,
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

    // ═══════════════════════════════════════════
    // Batch Operations
    // ═══════════════════════════════════════════

    public async Task<int> AddWaitlistAgegroupsAsync(Guid jobId, string userId, CancellationToken cancellationToken = default)
    {
        var leagues = await _leagueRepo.GetLeaguesByJobIdAsync(jobId, cancellationToken);
        var count = 0;

        foreach (var league in leagues)
        {
            var agegroups = await _agegroupRepo.GetByLeagueIdAsync(league.LeagueId, cancellationToken);
            var hasWaitlist = agegroups.Exists(a =>
                a.AgegroupName != null && a.AgegroupName.Contains("WAITLIST", StringComparison.OrdinalIgnoreCase));

            if (hasWaitlist) continue;

            var waitlistAg = new Agegroups
            {
                AgegroupId = Guid.NewGuid(),
                LeagueId = league.LeagueId,
                AgegroupName = "WAITLIST",
                MaxTeams = 999,
                MaxTeamsPerClub = 999,
                SortAge = 255,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            };
            _agegroupRepo.Add(waitlistAg);

            var stubDiv = new Divisions
            {
                DivId = Guid.NewGuid(),
                AgegroupId = waitlistAg.AgegroupId,
                DivName = "Pool A",
                LebUserId = userId,
                Modified = DateTime.UtcNow
            };
            _divisionRepo.Add(stubDiv);

            count++;
        }

        if (count > 0)
            await _agegroupRepo.SaveChangesAsync(cancellationToken);

        return count;
    }

    public async Task<int> UpdatePlayerFeesToAgegroupFeesAsync(Guid agegroupId, Guid jobId, CancellationToken cancellationToken = default)
    {
        await ValidateAgegroupOwnershipAsync(agegroupId, jobId, cancellationToken);

        var ag = await _agegroupRepo.GetByIdAsync(agegroupId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agegroup {agegroupId} not found.");

        var teams = await _teamRepo.GetByAgegroupIdAsync(agegroupId, cancellationToken);
        if (teams.Count == 0) return 0;

        // Load tracked versions for update
        var teamIds = teams.Select(t => t.TeamId).ToList();
        var trackedTeams = await _teamRepo.GetTeamsForJobAsync(jobId, teamIds, cancellationToken);

        foreach (var team in trackedTeams)
        {
            team.FeeBase = ag.TeamFee ?? 0;
            team.Modified = DateTime.UtcNow;
        }

        await _teamRepo.UpdateTeamFeesAsync(trackedTeams, cancellationToken);
        return trackedTeams.Count;
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

    private static LeagueDetailDto MapLeague(Leagues l) => new()
    {
        LeagueId = l.LeagueId,
        LeagueName = l.LeagueName ?? string.Empty,
        SportId = l.SportId,
        SportName = l.Sport?.SportName,
        BAllowCoachScoreEntry = l.BAllowCoachScoreEntry,
        BHideContacts = l.BHideContacts,
        BHideStandings = l.BHideStandings,
        BShowScheduleToTeamMembers = l.BShowScheduleToTeamMembers,
        BTakeAttendance = l.BTakeAttendance,
        BTrackPenaltyMinutes = l.BTrackPenaltyMinutes,
        BTrackSportsmanshipScores = l.BTrackSportsmanshipScores,
        RescheduleEmailsToAddon = l.RescheduleEmailsToAddon,
        PlayerFeeOverride = l.PlayerFeeOverride,
        StandingsSortProfileId = l.StandingsSortProfileId,
        PointsMethod = l.PointsMethod,
        StrLop = l.StrLop,
        StrGradYears = l.StrGradYears
    };

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
        TeamFee = a.TeamFee,
        TeamFeeLabel = a.TeamFeeLabel,
        RosterFee = a.RosterFee,
        RosterFeeLabel = a.RosterFeeLabel,
        DiscountFee = a.DiscountFee,
        DiscountFeeStart = a.DiscountFeeStart,
        DiscountFeeEnd = a.DiscountFeeEnd,
        LateFee = a.LateFee,
        LateFeeStart = a.LateFeeStart,
        LateFeeEnd = a.LateFeeEnd,
        MaxTeams = a.MaxTeams,
        MaxTeamsPerClub = a.MaxTeamsPerClub,
        BAllowSelfRostering = a.BAllowSelfRostering,
        BChampionsByDivision = a.BChampionsByDivision,
        BAllowApiRosterAccess = a.BAllowApiRosterAccess,
        BHideStandings = a.BHideStandings,
        PlayerFeeOverride = a.PlayerFeeOverride,
        SortAge = a.SortAge
    };

    private static DivisionDetailDto MapDivision(Divisions d) => new()
    {
        DivId = d.DivId,
        AgegroupId = d.AgegroupId,
        DivName = d.DivName,
        MaxRoundNumberToShow = d.MaxRoundNumberToShow
    };

    private static TeamDetailDto MapTeam(TSIC.Domain.Entities.Teams t, int playerCount) => new()
    {
        TeamId = t.TeamId,
        DivId = t.DivId,
        AgegroupId = t.AgegroupId,
        LeagueId = t.LeagueId,
        JobId = t.JobId,
        TeamName = t.TeamName,
        Active = t.Active,
        DivRank = t.DivRank,
        DivisionRequested = t.DivisionRequested,
        LastLeagueRecord = t.LastLeagueRecord,
        Color = t.Color,
        MaxCount = t.MaxCount,
        BAllowSelfRostering = t.BAllowSelfRostering,
        BHideRoster = t.BHideRoster,
        FeeBase = t.FeeBase,
        PerRegistrantFee = t.PerRegistrantFee,
        PerRegistrantDeposit = t.PerRegistrantDeposit,
        DiscountFee = t.DiscountFee,
        DiscountFeeStart = t.DiscountFeeStart,
        DiscountFeeEnd = t.DiscountFeeEnd,
        LateFee = t.LateFee,
        LateFeeStart = t.LateFeeStart,
        LateFeeEnd = t.LateFeeEnd,
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
        PlayerCount = playerCount
    };
}
