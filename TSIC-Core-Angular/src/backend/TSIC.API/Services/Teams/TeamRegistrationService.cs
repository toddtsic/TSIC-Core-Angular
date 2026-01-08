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
    private readonly IRegistrationRepository _registrations;

    public TeamRegistrationService(
        ILogger<TeamRegistrationService> logger,
        IClubRepRepository clubReps,
        IClubRepository clubs,
        IClubTeamRepository clubTeams,
        IJobRepository jobs,
        IJobLeagueRepository jobLeagues,
        IAgeGroupRepository agegroups,
        ITeamRepository teams,
        IRegistrationRepository registrations)
    {
        _logger = logger;
        _clubReps = clubReps;
        _clubs = clubs;
        _clubTeams = clubTeams;
        _jobs = jobs;
        _jobLeagues = jobLeagues;
        _agegroups = agegroups;
        _teams = teams;
        _registrations = registrations;
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

    public async Task<CheckExistingRegistrationsResponse> CheckExistingRegistrationsAsync(string jobPath, string clubName, string userId)
    {
        _logger.LogInformation("Checking existing registrations for user {UserId}, job {JobPath}, club {ClubName}", userId, jobPath, clubName);

        // Get club rep association
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        var clubRep = myClubs.FirstOrDefault(c => string.Equals(c.ClubName, clubName, StringComparison.OrdinalIgnoreCase));

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a rep for club {ClubName}", userId, clubName);
            throw new InvalidOperationException("User is not authorized for this club");
        }

        // Get job ID
        var jobId = await _jobs.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", jobPath);
            throw new InvalidOperationException($"Event not found: {jobPath}");
        }

        // Get current user's registration for this event (if exists)
        var currentUserRegistration = await _registrations.Query()
            .Where(r => r.UserId == userId && r.JobId == jobId && r.RoleId == Domain.Constants.RoleConstants.ClubRep)
            .FirstOrDefaultAsync();

        // Check if other club reps have registered teams for this event+club
        var existingTeamsQuery = _teams.Query()
            .Where(t => t.JobId == jobId && t.ClubTeam!.ClubId == clubRep.ClubId && t.ClubrepRegistrationid != null);

        // Exclude current user's teams if they have a registration
        if (currentUserRegistration != null)
        {
            existingTeamsQuery = existingTeamsQuery.Where(t => t.ClubrepRegistrationid != currentUserRegistration.RegistrationId);
        }

        var otherRepTeams = await existingTeamsQuery
            .Include(t => t.ClubrepRegistration)
            .ThenInclude(r => r!.User)
            .ToListAsync();

        if (otherRepTeams.Any())
        {
            var otherRepUsername = otherRepTeams.First().ClubrepRegistration?.User?.UserName ?? "another club rep";
            _logger.LogInformation("Found conflict: {OtherRep} has {Count} teams registered for job {JobId} club {ClubId}",
                otherRepUsername, otherRepTeams.Count, jobId, clubRep.ClubId);

            return new CheckExistingRegistrationsResponse
            {
                HasConflict = true,
                OtherRepUsername = otherRepUsername,
                TeamCount = otherRepTeams.Count
            };
        }

        _logger.LogInformation("No conflicts found for user {UserId}, job {JobPath}, club {ClubName}", userId, jobPath, clubName);
        return new CheckExistingRegistrationsResponse
        {
            HasConflict = false,
            OtherRepUsername = null,
            TeamCount = 0
        };
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

        // Get league for this job (prefer primary, fall back to first league)
        var jobLeague = await _jobLeagues.GetPrimaryLeagueForJobAsync(jobId);

        if (jobLeague == null)
        {
            _logger.LogWarning("No league found for job: {JobId}", jobId);
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

    public async Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, string userId, int? clubId = null)
    {
        _logger.LogInformation("Registering team for event. JobPath: {JobPath}, ClubTeamId: {ClubTeamId}, User: {UserId}, ClubId: {ClubId}",
            request.JobPath, request.ClubTeamId, userId, clubId);

        // Get club rep - use clubId from JWT if provided, otherwise get first club (backward compatibility)
        int? effectiveClubId = clubId;
        if (effectiveClubId == null)
        {
            var myClubs = await _clubReps.GetClubsForUserAsync(userId);
            effectiveClubId = myClubs.FirstOrDefault()?.ClubId;
        }
        else
        {
            // Validate that user has access to the specified club
            var hasAccess = await _clubReps.ExistsAsync(userId, effectiveClubId.Value);
            if (!hasAccess)
            {
                _logger.LogWarning("User {UserId} attempted to access club {ClubId} without permission", userId, effectiveClubId);
                throw new UnauthorizedAccessException("User does not have access to this club");
            }
        }

        if (effectiveClubId == null)
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

        if (clubTeamEntity.ClubId != effectiveClubId)
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
            _logger.LogWarning("No league found for job: {JobId}", jobId);
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
                jobId, request.ClubTeamId);
            throw new InvalidOperationException("This team is already registered for this event");
        }

        // CRITICAL BUSINESS RULE: One club rep per event
        // Check if another club rep has already registered teams for this event+club combination
        var existingTeamsForClub = await _teams.Query()
            .Where(t => t.JobId == jobId && t.ClubTeam!.ClubId == clubId && t.ClubrepRegistrationid != null)
            .Include(t => t.ClubrepRegistration)
            .ToListAsync();

        // Get or create club rep registration for this user+job
        var clubRepRegistration = await _registrations.Query()
            .Where(r => r.UserId == userId && r.JobId == jobId && r.RoleId == Domain.Constants.RoleConstants.ClubRep)
            .FirstOrDefaultAsync();

        if (clubRepRegistration == null)
        {
            // Create new club rep registration for this event
            clubRepRegistration = new Domain.Entities.Registrations
            {
                RegistrationId = Guid.NewGuid(),
                UserId = userId,
                JobId = jobId.Value,
                RoleId = Domain.Constants.RoleConstants.ClubRep,
                BActive = true,
                BConfirmationSent = false,
                RegistrationTs = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                FeeBase = 0,
                FeeProcessing = 0
            };
            _registrations.Add(clubRepRegistration);
            await _registrations.SaveChangesAsync();
            _logger.LogInformation("Created club rep registration {RegistrationId} for user {UserId} in job {JobId}",
                clubRepRegistration.RegistrationId, userId, jobId);
        }

        // Validate one-rep-per-event rule
        var differentRepTeams = existingTeamsForClub
            .Where(t => t.ClubrepRegistrationid != clubRepRegistration.RegistrationId)
            .ToList();

        if (differentRepTeams.Any())
        {
            var otherRepUsername = differentRepTeams[0].ClubrepRegistration?.User?.UserName ?? "another club rep";
            _logger.LogWarning("One-rep-per-event violation: User {UserId} (regId {RegistrationId}) attempted to register team for event {JobId} club {ClubId}, but {OtherRepUser} already has teams registered",
                userId, clubRepRegistration.RegistrationId, jobId, clubId, otherRepUsername);
            throw new InvalidOperationException($"Only one club representative can register teams per event. {otherRepUsername} has already registered teams for this club in this event. Please contact your organization administrator.");
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
            ClubrepRegistrationid = clubRepRegistration.RegistrationId,  // Track which club rep registered this team
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

        if (!myClubs.Any())
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
            Modified = DateTime.UtcNow,
            Active = true
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
            club = await _clubs.GetByIdAsync(exactMatch.ClubId);
            if (club == null)
            {
                throw new InvalidOperationException("Club not found");
            }

            // Check if already a rep for this club
            var existingRep = await _clubReps.ExistsAsync(userId, club.ClubId);

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
            _clubs.Add(club);
            await _clubs.SaveChangesAsync();
        }

        // Create ClubReps link
        var clubRep = new ClubReps
        {
            ClubId = club.ClubId,
            ClubRepUserId = userId,
            Modified = DateTime.UtcNow
        };

        _clubReps.Add(clubRep);
        await _clubReps.SaveChangesAsync();

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

    public async Task<bool> UpdateClubNameAsync(string userId, string oldClubName, string newClubName)
    {
        _logger.LogInformation("Updating club name from {OldName} to {NewName} for user {UserId}",
            oldClubName, newClubName, userId);

        // Find club by old name
        var club = await _clubs.GetByNameAsync(oldClubName);

        if (club == null)
        {
            _logger.LogWarning("Club not found: {OldClubName}", oldClubName);
            throw new InvalidOperationException("Club not found");
        }

        // Verify user is rep for this club
        var clubRep = await _clubReps.GetClubRepForUserAndClubAsync(userId, club.ClubId);

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a rep for club {ClubId}", userId, club.ClubId);
            throw new InvalidOperationException("You are not a representative for this club");
        }

        // Check if club has ANY teams (prevent rename if teams exist)
        var hasTeams = await _teams.Query().AnyAsync(t => t.ClubTeam!.ClubId == club.ClubId);

        if (hasTeams)
        {
            _logger.LogWarning("Cannot rename club {ClubId} - has registered teams", club.ClubId);
            throw new InvalidOperationException("Cannot rename club - teams have been registered under this club");
        }

        // Check if new name already exists (different club)
        var existingClub = await _clubs.GetByNameAsync(newClubName);

        if (existingClub != null && existingClub.ClubId != club.ClubId)
        {
            _logger.LogWarning("Club name {NewClubName} already exists", newClubName);
            throw new InvalidOperationException("A club with this name already exists");
        }

        // Update club name
        club.ClubName = newClubName.Trim();
        await _clubs.SaveChangesAsync();

        _logger.LogInformation("Club {ClubId} renamed successfully from {OldName} to {NewName}",
            club.ClubId, oldClubName, newClubName);
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

    public async Task<List<ClubTeamManagementDto>> GetClubTeamsAsync(string userId)
    {
        _logger.LogInformation("Getting club teams for user {UserId}", userId);

        // Get all clubs the user is a rep for
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);

        if (myClubs.Count == 0)
        {
            _logger.LogWarning("User {UserId} is not a club rep for any clubs", userId);
            return new List<ClubTeamManagementDto>();
        }

        // Get teams for all clubs
        var allTeams = new List<ClubTeamManagementDto>();
        foreach (var club in myClubs)
        {
            var teams = await _clubTeams.GetClubTeamsWithMetadataAsync(club.ClubId);
            allTeams.AddRange(teams);
        }

        _logger.LogInformation("Found {TeamCount} teams across {ClubCount} clubs for user {UserId}", allTeams.Count, myClubs.Count, userId);

        return allTeams;
    }

    public async Task<ClubTeamOperationResponse> UpdateClubTeamAsync(UpdateClubTeamRequest request, string userId)
    {
        _logger.LogInformation("Updating club team {ClubTeamId} for user {UserId}", request.ClubTeamId, userId);

        // Get the team and verify it exists
        var clubTeam = await _clubTeams.GetByIdAsync(request.ClubTeamId);
        if (clubTeam == null)
        {
            _logger.LogWarning("Club team {ClubTeamId} not found", request.ClubTeamId);
            throw new InvalidOperationException("Club team not found");
        }

        // Verify user is a rep for this club
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == clubTeam.ClubId))
        {
            _logger.LogWarning("User {UserId} is not authorized for club team {ClubTeamId}", userId, request.ClubTeamId);
            throw new UnauthorizedAccessException("You are not authorized to manage this team");
        }

        // Check if team has been registered for any event
        var hasBeenRegistered = await _clubTeams.HasBeenUsedAsync(request.ClubTeamId);

        // If team has been registered, only allow level of play changes
        if (hasBeenRegistered)
        {
            if (clubTeam.ClubTeamName != request.ClubTeamName)
            {
                _logger.LogWarning("Cannot change name of club team {ClubTeamId} - has registration history", request.ClubTeamId);
                return new ClubTeamOperationResponse
                {
                    Success = false,
                    ClubTeamId = request.ClubTeamId,
                    ClubTeamName = clubTeam.ClubTeamName,
                    Message = "Cannot change team name - this team has registration history. Only level of play can be modified."
                };
            }

            // Allow grad year edit ONLY if current value is "N.A." (migration fallback)
            if (clubTeam.ClubTeamGradYear != request.ClubTeamGradYear && clubTeam.ClubTeamGradYear != "N.A.")
            {
                _logger.LogWarning("Cannot change grad year of club team {ClubTeamId} - has registration history", request.ClubTeamId);
                return new ClubTeamOperationResponse
                {
                    Success = false,
                    ClubTeamId = request.ClubTeamId,
                    ClubTeamName = clubTeam.ClubTeamName,
                    Message = "Cannot change graduation year - this team has registration history. Only level of play can be modified."
                };
            }

            // Update allowed fields: grad year (if N.A.) and level of play
            clubTeam.ClubTeamGradYear = request.ClubTeamGradYear;
            clubTeam.ClubTeamLevelOfPlay = request.ClubTeamLevelOfPlay;
        }
        else
        {
            // Team has never been registered - allow all fields to be updated
            // Check for duplicate name if name is changing
            if (clubTeam.ClubTeamName != request.ClubTeamName)
            {
                var exists = await _clubTeams.ExistsByNameExcludingIdAsync(clubTeam.ClubId, request.ClubTeamName, request.ClubTeamId);
                if (exists)
                {
                    _logger.LogWarning("Duplicate team name {TeamName} in club {ClubId}", request.ClubTeamName, clubTeam.ClubId);
                    return new ClubTeamOperationResponse
                    {
                        Success = false,
                        ClubTeamId = request.ClubTeamId,
                        ClubTeamName = clubTeam.ClubTeamName,
                        Message = $"A team named '{request.ClubTeamName}' already exists for your club"
                    };
                }
            }

            // Update all fields
            clubTeam.ClubTeamName = request.ClubTeamName;
            clubTeam.ClubTeamGradYear = request.ClubTeamGradYear;
            clubTeam.ClubTeamLevelOfPlay = request.ClubTeamLevelOfPlay;
        }

        _clubTeams.Update(clubTeam);
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Club team {ClubTeamId} updated successfully", request.ClubTeamId);
        return new ClubTeamOperationResponse
        {
            Success = true,
            ClubTeamId = clubTeam.ClubTeamId,
            ClubTeamName = clubTeam.ClubTeamName,
            Message = "Team updated successfully"
        };
    }

    public async Task<ClubTeamOperationResponse> ActivateClubTeamAsync(int clubTeamId, string userId)
    {
        _logger.LogInformation("Activating club team {ClubTeamId} for user {UserId}", clubTeamId, userId);

        // Get the team and verify it exists
        var clubTeam = await _clubTeams.GetByIdAsync(clubTeamId);
        if (clubTeam == null)
        {
            _logger.LogWarning("Club team {ClubTeamId} not found", clubTeamId);
            throw new InvalidOperationException("Club team not found");
        }

        // Verify user is a rep for this club
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == clubTeam.ClubId))
        {
            _logger.LogWarning("User {UserId} is not authorized for club team {ClubTeamId}", userId, clubTeamId);
            throw new UnauthorizedAccessException("You are not authorized to manage this team");
        }

        // Activate the team
        clubTeam.Active = true;
        _clubTeams.Update(clubTeam);
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Club team {ClubTeamId} activated successfully", clubTeamId);
        return new ClubTeamOperationResponse
        {
            Success = true,
            ClubTeamId = clubTeam.ClubTeamId,
            ClubTeamName = clubTeam.ClubTeamName,
            Message = "Team activated successfully"
        };
    }

    public async Task<ClubTeamOperationResponse> InactivateClubTeamAsync(int clubTeamId, string userId)
    {
        _logger.LogInformation("Inactivating club team {ClubTeamId} for user {UserId}", clubTeamId, userId);

        // Get the team and verify user owns it
        var clubTeam = await _clubTeams.GetByIdAsync(clubTeamId);
        if (clubTeam == null)
        {
            _logger.LogWarning("Club team {ClubTeamId} not found", clubTeamId);
            throw new InvalidOperationException("Club team not found");
        }

        // Verify user is a rep for this club
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == clubTeam.ClubId))
        {
            _logger.LogWarning("User {UserId} is not authorized for club team {ClubTeamId}", userId, clubTeamId);
            throw new UnauthorizedAccessException("You are not authorized to manage this team");
        }

        // Inactivate the team
        clubTeam.Active = false;
        _clubTeams.Update(clubTeam);
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Club team {ClubTeamId} inactivated successfully", clubTeamId);
        return new ClubTeamOperationResponse
        {
            Success = true,
            ClubTeamId = clubTeam.ClubTeamId,
            ClubTeamName = clubTeam.ClubTeamName,
            Message = "Team moved to inactive"
        };
    }

    public async Task<ClubTeamOperationResponse> DeleteClubTeamAsync(int clubTeamId, string userId)
    {
        _logger.LogInformation("Deleting club team {ClubTeamId} for user {UserId}", clubTeamId, userId);

        // Get the team and verify user owns it
        var clubTeam = await _clubTeams.GetByIdAsync(clubTeamId);
        if (clubTeam == null)
        {
            _logger.LogWarning("Club team {ClubTeamId} not found", clubTeamId);
            throw new InvalidOperationException("Club team not found");
        }

        // Verify user is a rep for this club
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == clubTeam.ClubId))
        {
            _logger.LogWarning("User {UserId} is not authorized for club team {ClubTeamId}", userId, clubTeamId);
            throw new UnauthorizedAccessException("You are not authorized to manage this team");
        }

        // Check if team has been registered for any event
        var hasBeenRegistered = await _clubTeams.HasBeenUsedAsync(clubTeamId);

        if (hasBeenRegistered)
        {
            // Soft delete: set inactive to preserve history
            _logger.LogInformation("Club team {ClubTeamId} has registration history - performing soft delete", clubTeamId);
            clubTeam.Active = false;
            _clubTeams.Update(clubTeam);
            await _clubTeams.SaveChangesAsync();

            return new ClubTeamOperationResponse
            {
                Success = true,
                ClubTeamId = clubTeam.ClubTeamId,
                ClubTeamName = clubTeam.ClubTeamName,
                Message = "Team moved to inactive (history preserved)"
            };
        }
        else
        {
            // Hard delete: remove from database
            _logger.LogInformation("Club team {ClubTeamId} has no history - performing hard delete", clubTeamId);
            _clubTeams.Remove(clubTeam);
            await _clubTeams.SaveChangesAsync();

            return new ClubTeamOperationResponse
            {
                Success = true,
                ClubTeamId = clubTeam.ClubTeamId,
                ClubTeamName = clubTeam.ClubTeamName,
                Message = "Team deleted permanently"
            };
        }
    }
}
