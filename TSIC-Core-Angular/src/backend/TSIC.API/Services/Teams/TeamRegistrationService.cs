using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;
using TSIC.Application.Services.Clubs;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Teams;

public class TeamRegistrationService : ITeamRegistrationService
{
    private readonly ILogger<TeamRegistrationService> _logger;
    private readonly IClubRepRepository _clubReps;
    private readonly IClubRepository _clubs;
    private readonly IClubTeamRepository _clubTeams;
    private readonly IJobRepository _jobs;
    private readonly IJobLeagueRepository _jobLeagues;
    private readonly IAgeGroupRepository _agegroups;
    private readonly ITeamRepository _teams;

    public TeamRegistrationService(
        ILogger<TeamRegistrationService> logger,
        IClubRepRepository clubReps,
        IClubRepository clubs,
        IClubTeamRepository clubTeams,
        IJobRepository jobs,
        IJobLeagueRepository jobLeagues,
        IAgeGroupRepository agegroups,
        ITeamRepository teams)
    {
        _logger = logger;
        _clubReps = clubReps;
        _clubs = clubs;
        _clubTeams = clubTeams;
        _jobs = jobs;
        _jobLeagues = jobLeagues;
        _agegroups = agegroups;
        _teams = teams;
    }

    public async Task<List<ClubRepClubDto>> GetMyClubsAsync(string userId)
    {
        _logger.LogInformation("Getting clubs for user: {UserId}", userId);

        var clubsRaw = await _clubReps.GetClubsForUserAsync(userId);
        var clubs = clubsRaw
            .Select(c => new ClubRepClubDto { ClubName = c.ClubName, IsInUse = c.IsInUse })
            .OrderBy(c => c.ClubName)
            .ToList();

        _logger.LogInformation("Found {ClubCount} clubs for user {UserId}", clubs.Count, userId);
        return clubs;
    }

    public async Task<TeamsMetadataResponse> GetTeamsMetadataAsync(string jobPath, string userId, string clubName)
    {
        _logger.LogInformation("Getting teams metadata for job: {JobPath}, user: {UserId}, club: {ClubName}", jobPath, userId, clubName);

        // Get club rep for user and club
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        var clubRep = myClubs.FirstOrDefault(c => string.Equals(c.ClubName, clubName, StringComparison.OrdinalIgnoreCase));

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Get job
        var jobId = await _jobs.GetJobIdByPathAsync(jobPath) ?? throw new InvalidOperationException($"Event not found: {jobPath}");
        var jobSeason = await _jobs.Query().Where(j => j.JobId == jobId).Select(j => j.Season).SingleOrDefaultAsync();
        if (jobSeason == null) throw new InvalidOperationException($"Event not found: {jobPath}");

        if (job == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", jobPath);
            throw new InvalidOperationException($"Event not found: {jobPath}");
        }

        // Get league for this job (prefer primary, fall back to first league)
        var jobLeague = await _jobLeagues.GetPrimaryLeagueForJobAsync(jobId);

        if (jobLeague == null)
        {
            _logger.LogWarning("No league found for job: {JobId}", job.JobId);
            throw new InvalidOperationException("Event does not have a league configured");
        }

        var leagueId = jobLeague.LeagueId;

        // Get club teams for this club
        var clubTeams = await _clubTeams.GetClubTeamsForClubAsync(clubRep.ClubId);

        // Get already registered teams for this club and event
        var regInfos = await _teams.GetRegisteredTeamsForClubAndJobAsync(jobId, clubRep.ClubId);
        var registeredTeams = regInfos.Select(info => new RegisteredTeamDto
        {
            TeamId = info.TeamId,
            ClubTeamId = info.ClubTeamId,
            ClubTeamName = info.ClubTeamName,
            ClubTeamGradYear = info.ClubTeamGradYear,
            ClubTeamLevelOfPlay = info.ClubTeamLevelOfPlay,
            AgeGroupId = info.AgeGroupId,
            AgeGroupName = info.AgeGroupName,
            FeeBase = info.FeeBase,
            FeeProcessing = info.FeeProcessing,
            FeeTotal = info.FeeTotal,
            PaidTotal = info.PaidTotal,
            OwedTotal = info.OwedTotal
        }).ToList();

        // Get age groups for this league/season with their registration counts
        var ageGroupEntities = _agegroups.Query()
            .Where(ag => ag.LeagueId == leagueId && ag.Season == jobSeason && ag.MaxTeams > 0)
            .OrderBy(ag => ag.AgegroupName);
        var ageGroups = new List<AgeGroupDto>();
        foreach (var ag in await ageGroupEntities.ToListAsync())
        {
            var regCount = await _teams.GetRegisteredCountForAgegroupAsync(jobId, ag.AgegroupId);
            ageGroups.Add(new AgeGroupDto
            {
                AgeGroupId = ag.AgegroupId,
                AgeGroupName = ag.AgegroupName ?? string.Empty,
                MaxTeams = ag.MaxTeams,
                RosterFee = ag.RosterFee ?? 0,
                TeamFee = ag.TeamFee ?? 0,
                RegisteredCount = regCount
            });
        }

        _logger.LogInformation("Found {ClubTeamCount} club teams, {RegisteredCount} registered teams, {AgeGroupCount} age groups",
            clubTeams.Count, registeredTeams.Count, ageGroups.Count);

        return new TeamsMetadataResponse
        {
            ClubId = clubRep.ClubId,
            ClubName = clubRep.ClubName,
            AvailableClubTeams = clubTeams,
            RegisteredTeams = registeredTeams,
            AgeGroups = ageGroups
        };
    }

    public async Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, string userId)
    {
        _logger.LogInformation("Registering team for event. JobPath: {JobPath}, ClubTeamId: {ClubTeamId}, User: {UserId}",
            request.JobPath, request.ClubTeamId, userId);

        // Get club rep
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        var clubId = myClubs.FirstOrDefault()?.ClubId;
        if (clubId == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Get club team and verify ownership
        var clubTeamEntity = await _clubTeams.GetByIdAsync(request.ClubTeamId);

        if (clubTeamEntity == null)
        {
            _logger.LogWarning("Club team not found: {ClubTeamId}", request.ClubTeamId);
            throw new InvalidOperationException("Club team not found");
        }

        if (clubTeamEntity.ClubId != clubId)
        {
            _logger.LogWarning("Club team {ClubTeamId} does not belong to user's club", request.ClubTeamId);
            throw new InvalidOperationException("You can only register teams from your own club");
        }

        // Get job
        var jobId = await _jobs.GetJobIdByPathAsync(request.JobPath);

        if (jobId == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", request.JobPath);
            throw new InvalidOperationException($"Event not found: {request.JobPath}");
        }

        // Get league for this job (prefer primary, fall back to first league)
        var jobLeague = await _jobLeagues.GetPrimaryLeagueForJobAsync(jobId.Value);

        if (jobLeague == null)
        {
            _logger.LogWarning("No league found for job: {JobId}", job.JobId);
            throw new InvalidOperationException("Event does not have a league configured");
        }

        var leagueId = jobLeague.LeagueId;

        // Check if team is already registered
        var existingTeam = await _teams.Query()
            .Where(t => t.JobId == jobId && t.ClubTeamId == request.ClubTeamId)
            .FirstOrDefaultAsync();

        if (existingTeam != null)
        {
            _logger.LogWarning("Team already registered. JobId: {JobId}, ClubTeamId: {ClubTeamId}",
                job.JobId, request.ClubTeamId);
            throw new InvalidOperationException("This team is already registered for this event");
        }

        // Determine age group
        Guid ageGroupId;
        decimal rosterFee = 0;
        decimal teamFee = 0;

        if (request.AgeGroupId.HasValue)
        {
            // Use provided age group
            ageGroupId = request.AgeGroupId.Value;
            var providedAgeGroup = await _agegroups.Query()
                .Where(ag => ag.AgegroupId == ageGroupId && ag.LeagueId == leagueId)
                .SingleOrDefaultAsync();

            if (providedAgeGroup == null)
            {
                _logger.LogWarning("Age group not found or invalid: {AgeGroupId}", ageGroupId);
                throw new InvalidOperationException("Invalid age group selected");
            }

            rosterFee = providedAgeGroup.RosterFee ?? 0;
            teamFee = providedAgeGroup.TeamFee ?? 0;
        }
        else
        {
            // Auto-determine age group by grad year
            var gradYearString = clubTeamEntity.ClubTeamGradYear;
            if (string.IsNullOrWhiteSpace(gradYearString) || !int.TryParse(gradYearString, out var gradYear))
            {
                _logger.LogWarning("Cannot auto-determine age group: ClubTeam {ClubTeamId} has no valid grad year",
                    request.ClubTeamId);
                throw new InvalidOperationException("Age group must be specified or team must have a valid graduation year");
            }

            var ageGroup = await _agegroups.Query()
                .Where(ag => ag.LeagueId == leagueId
                    && ag.MaxTeams > 0
                    && (ag.GradYearMin == null || ag.GradYearMin <= gradYear)
                    && (ag.GradYearMax == null || ag.GradYearMax >= gradYear))
                .OrderBy(ag => ag.SortAge)
                .FirstOrDefaultAsync();

            if (ageGroup == null)
            {
                _logger.LogWarning("No matching age group found for grad year {GradYear}", gradYear);
                throw new InvalidOperationException($"No age group found for graduation year {gradYear}");
            }

            ageGroupId = ageGroup.AgegroupId;
            rosterFee = ageGroup.RosterFee ?? 0;
            teamFee = ageGroup.TeamFee ?? 0;
        }

        // Verify age group has capacity
        var registeredCount = await _teams.GetRegisteredCountForAgegroupAsync(jobId.Value, ageGroupId);

        var maxTeams = await _agegroups.Query()
            .Where(ag => ag.AgegroupId == ageGroupId)
            .Select(ag => ag.MaxTeams)
            .FirstOrDefaultAsync();

        if (registeredCount >= maxTeams)
        {
            _logger.LogWarning("Age group {AgeGroupId} is full ({RegisteredCount}/{MaxTeams})",
                ageGroupId, registeredCount, maxTeams);
            throw new InvalidOperationException("This age group is full");
        }

        // Calculate fees (for teams, we always require full payment: RosterFee + TeamFee)
        var feeBase = rosterFee + teamFee;

        // For now, no processing fee (can be added later based on configuration)
        var feeProcessing = 0m;

        // Create team registration
        var team = new Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId.Value,
            LeagueId = leagueId,
            AgegroupId = ageGroupId,
            ClubTeamId = request.ClubTeamId,
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            PaidTotal = 0,
            Active = true,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow
        };
        _teams.Add(team);
        await _teams.SaveChangesAsync();

        _logger.LogInformation("Team registered successfully. TeamId: {TeamId}, FeeBase: {FeeBase}, FeeProcessing: {FeeProcessing}",
            team.TeamId, feeBase, feeProcessing);

        return new RegisterTeamResponse
        {
            Success = true,
            TeamId = team.TeamId,
            Message = "Team registered successfully"
        };
    }

    public async Task<bool> UnregisterTeamFromEventAsync(Guid teamId, string userId)
    {
        _logger.LogInformation("Unregistering team {TeamId} for user {UserId}", teamId, userId);

        // Get club rep
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Get team and verify ownership
        var team = await _teams.GetTeamWithDetailsAsync(teamId);

        if (team == null)
        {
            _logger.LogWarning("Team not found: {TeamId}", teamId);
            throw new InvalidOperationException("Team registration not found");
        }

        if (team.ClubTeam is null || !myClubs.Any(c => c.ClubId == team.ClubTeam.ClubId))
        {
            _logger.LogWarning("Team {TeamId} does not belong to user's club", teamId);
            throw new InvalidOperationException("You can only unregister teams from your own club");
        }

        // Check if team has made payments
        if (team.PaidTotal > 0)
        {
            _logger.LogWarning("Cannot unregister team {TeamId} with payments ({PaidTotal})",
                teamId, team.PaidTotal);
            throw new InvalidOperationException("Cannot unregister a team with payments. Please contact support for refunds.");
        }

        // Remove team
        _teams.Remove(team);
        await _teams.SaveChangesAsync();

        _logger.LogInformation("Team {TeamId} unregistered successfully", teamId);
        return true;
    }

    public async Task<AddClubTeamResponse> AddNewClubTeamAsync(AddClubTeamRequest request, string userId)
    {
        _logger.LogInformation("Adding new club team: {TeamName} for user {UserId}", request.ClubTeamName, userId);

        // Get club rep
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        var clubId = myClubs.FirstOrDefault()?.ClubId;
        if (clubId == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Check for duplicate team name in this club
        var exists = await _clubTeams.ExistsByNameAsync(clubId.Value, request.ClubTeamName);

        if (exists)
        {
            _logger.LogWarning("Duplicate team name {TeamName} in club {ClubId}", request.ClubTeamName, clubId.Value);
            throw new InvalidOperationException($"A team named '{request.ClubTeamName}' already exists for your club");
        }

        // Create new club team
        var clubTeam = new ClubTeams
        {
            ClubId = clubId.Value,
            ClubTeamName = request.ClubTeamName,
            ClubTeamGradYear = request.ClubTeamGradYear,
            ClubTeamLevelOfPlay = request.ClubTeamLevelOfPlay,
            Modified = DateTime.UtcNow
        };
        _clubTeams.Add(clubTeam);
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Club team created successfully. ClubTeamId: {ClubTeamId}", clubTeam.ClubTeamId);

        return new AddClubTeamResponse
        {
            Success = true,
            ClubTeamId = clubTeam.ClubTeamId,
            Message = "Team added successfully"
        };
    }

    public async Task<AddClubToRepResponse> AddClubToRepAsync(string userId, string clubName)
    {
        _logger.LogInformation("Adding club {ClubName} to rep account for user {UserId}", clubName, userId);

        // Search for similar clubs using fuzzy matching
        var similarClubs = await SearchClubsAsync(clubName);

        // Check if exact match exists (90%+ similarity)
        var exactMatch = similarClubs.FirstOrDefault(c => c.MatchScore >= 90);

        Domain.Entities.Clubs club;
        if (exactMatch != null)
        {
            // Use existing club
            club = await _db.Clubs.SingleOrDefaultAsync(c => c.ClubId == exactMatch.ClubId);
            if (club == null)
            {
                throw new InvalidOperationException("Club not found");
            }

            // Check if already a rep for this club
            var existingRep = await _db.ClubReps
                .AnyAsync(cr => cr.ClubRepUserId == userId && cr.ClubId == club.ClubId);

            if (existingRep)
            {
                _logger.LogWarning("User {UserId} is already a rep for club {ClubId}", userId, club.ClubId);
                return new AddClubToRepResponse
                {
                    Success = false,
                    ClubName = club.ClubName,
                    Message = "You are already a representative for this club",
                    SimilarClubs = similarClubs.Count > 1 ? similarClubs : null
                };
            }
        }
        else
        {
            // Create new club
            club = new Domain.Entities.Clubs
            {
                ClubName = clubName,
                Modified = DateTime.UtcNow
            };
            _db.Clubs.Add(club);
            await _db.SaveChangesAsync();
        }

        // Create ClubReps link
        var clubRep = new ClubReps
        {
            ClubId = club.ClubId,
            ClubRepUserId = userId,
            Modified = DateTime.UtcNow
        };

        _db.ClubReps.Add(clubRep);
        await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} added as rep for club {ClubId}", userId, club.ClubId);

        return new AddClubToRepResponse
        {
            Success = true,
            ClubName = club.ClubName,
            Message = exactMatch != null ? "Added to existing club" : "New club created and added to your account",
            SimilarClubs = exactMatch == null && similarClubs.Any() ? similarClubs : null
        };
    }

    public async Task<bool> RemoveClubFromRepAsync(string userId, string clubName)
    {
        _logger.LogInformation("Removing club {ClubName} from rep account for user {UserId}", clubName, userId);

        // Find club
        var club = await _clubs.GetByNameAsync(clubName);

        if (club == null)
        {
            _logger.LogWarning("Club not found: {ClubName}", clubName);
            throw new InvalidOperationException("Club not found");
        }

        // Check if club has ANY teams (referential integrity)
        var hasTeams = await _teams.Query().AnyAsync(t => t.ClubTeam!.ClubId == club!.ClubId);

        if (hasTeams)
        {
            _logger.LogWarning("Cannot remove club {ClubId} - has registered teams", club.ClubId);
            throw new InvalidOperationException("Cannot remove club - teams have been registered under this club");
        }

        // Find and remove ClubReps link
        var clubRep = await _clubReps.GetClubRepForUserAndClubAsync(userId, club!.ClubId);

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a rep for club {ClubId}", userId, club.ClubId);
            throw new InvalidOperationException("You are not a representative for this club");
        }

        _clubReps.Remove(clubRep);
        await _clubReps.SaveChangesAsync();

        _logger.LogInformation("Removed club {ClubId} from user {UserId} rep account", club.ClubId, userId);
        return true;
    }

    private async Task<List<ClubSearchResult>> SearchClubsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<ClubSearchResult>();
        }

        var normalized = ClubNameMatcher.NormalizeClubName(query);

        var clubs = await _clubs.GetSearchCandidatesAsync();

        var results = clubs
            .Select(c => new ClubSearchResult
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName,
                State = c.State,
                TeamCount = c.TeamCount,
                MatchScore = ClubNameMatcher.CalculateSimilarity(normalized, ClubNameMatcher.NormalizeClubName(c.ClubName))
            })
            .Where(r => r.MatchScore >= 60)
            .OrderByDescending(r => r.MatchScore)
            .Take(5)
            .ToList();

        return results;
    }

}


