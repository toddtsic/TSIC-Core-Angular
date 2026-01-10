using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;
using TSIC.Application.Services.Clubs;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services.Teams;

public class TeamRegistrationService : ITeamRegistrationService
{
    private readonly ILogger<TeamRegistrationService> _logger;
    private readonly IClubRepRepository _clubReps;
    private readonly IClubRepository _clubs;
    private readonly IJobRepository _jobs;
    private readonly IJobLeagueRepository _jobLeagues;
    private readonly IAgeGroupRepository _agegroups;
    private readonly ITeamRepository _teams;
    private readonly IRegistrationRepository _registrations;
    private readonly SqlDbContext _context;

    public TeamRegistrationService(
        ILogger<TeamRegistrationService> logger,
        IClubRepRepository clubReps,
        IClubRepository clubs,
        IJobRepository jobs,
        IJobLeagueRepository jobLeagues,
        IAgeGroupRepository agegroups,
        ITeamRepository teams,
        IRegistrationRepository registrations,
        SqlDbContext context)
    {
        _logger = logger;
        _clubReps = clubReps;
        _clubs = clubs;
        _jobs = jobs;
        _jobLeagues = jobLeagues;
        _agegroups = agegroups;
        _teams = teams;
        _registrations = registrations;
        _context = context;
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

        // Check if other club reps have registered teams for this event+club (via join to ClubReps)
        var existingTeamsQuery = from t in _teams.Query()
                                 join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                                 where t.JobId == jobId
                                   && t.ClubrepRegistrationid != null
                                   && _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == clubRep.ClubId)
                                 select t;

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

        // Get already registered teams for this club and event (sorted by age group, then team name)
        var regInfos = await _teams.GetRegisteredTeamsForClubAndJobAsync(jobId, clubRep.ClubId);
        var registeredTeams = regInfos.Select(info => new RegisteredTeamDto
        {
            TeamId = info.TeamId,
            TeamName = info.TeamName,
            AgeGroupId = info.AgeGroupId,
            AgeGroupName = info.AgeGroupName,
            LevelOfPlay = info.LevelOfPlay,
            FeeBase = info.FeeBase,
            FeeProcessing = info.FeeProcessing,
            FeeTotal = info.FeeTotal,
            PaidTotal = info.PaidTotal,
            OwedTotal = info.OwedTotal
        })
        .OrderBy(t => t.AgeGroupName)
        .ThenBy(t => t.TeamName)
        .ToList();

        // Get suggested team names from historical registrations for this club (via join to ClubReps)
        // Sorted alphabetically by name for easier scanning
        var suggestedTeamNames = await (from t in _teams.Query()
                                        join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                                        where t.ClubrepRegistrationid != null
                                          && _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == clubRep.ClubId)
                                          && !string.IsNullOrEmpty(t.TeamName)
                                        select t)
            .GroupBy(t => t.TeamName)
            .Select(g => new
            {
                TeamName = g.Key!,
                UsageCount = g.Count(),
                LastUsedDate = g.Max(t => t.Createdate)
            })
            .OrderBy(x => x.TeamName)
            .Take(20)
            .ToListAsync();

        var suggestions = suggestedTeamNames.Select(s => new SuggestedTeamNameDto
        {
            TeamName = s.TeamName,
            UsageCount = s.UsageCount,
            LastUsedDate = s.LastUsedDate
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

        _logger.LogInformation("Found {RegisteredCount} registered teams, {SuggestionCount} suggested names, {AgeGroupCount} age groups",
            registeredTeams.Count, suggestions.Count, ageGroups.Count);

        return new TeamsMetadataResponse
        {
            ClubId = clubRep.ClubId,
            ClubName = clubRep.ClubName,
            SuggestedTeamNames = suggestions,
            RegisteredTeams = registeredTeams,
            AgeGroups = ageGroups
        };
    }

    public async Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, string userId, int? clubId = null)
    {
        _logger.LogInformation("Registering team for event. JobPath: {JobPath}, TeamName: {TeamName}, AgeGroupId: {AgeGroupId}, User: {UserId}, ClubId: {ClubId}",
            request.JobPath, request.TeamName, request.AgeGroupId, userId, clubId);

        // Validate team name
        if (string.IsNullOrWhiteSpace(request.TeamName))
        {
            throw new InvalidOperationException("Team name is required");
        }

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

        // CRITICAL BUSINESS RULE: One club rep per event
        // Check if another club rep has already registered teams for this event+club combination (via join to ClubReps)
        var existingTeamsForClub = await (from t in _teams.Query()
                                          join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                                          where t.JobId == jobId
                                            && t.ClubrepRegistrationid != null
                                            && _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == effectiveClubId)
                                          select t)
            .Include(t => t.ClubrepRegistration)
            .ToListAsync();

        // Validate one-rep-per-event rule
        var differentRepTeams = existingTeamsForClub
            .Where(t => t.ClubrepRegistrationid != clubRepRegistration.RegistrationId)
            .ToList();

        if (differentRepTeams.Any())
        {
            var otherRepUsername = differentRepTeams[0].ClubrepRegistration?.User?.UserName ?? "another club rep";
            _logger.LogWarning("One-rep-per-event violation: User {UserId} (regId {RegistrationId}) attempted to register team for event {JobId} club {ClubId}, but {OtherRepUser} already has teams registered",
                userId, clubRepRegistration.RegistrationId, jobId, effectiveClubId, otherRepUsername);
            throw new InvalidOperationException($"Only one club representative can register teams per event. {otherRepUsername} has already registered teams for this club in this event. Please contact your organization administrator.");
        }

        // Validate age group
        var ageGroup = await _agegroups.Query()
            .Where(ag => ag.AgegroupId == request.AgeGroupId && ag.LeagueId == leagueId)
            .SingleOrDefaultAsync();

        if (ageGroup == null)
        {
            _logger.LogWarning("Age group not found or invalid: {AgeGroupId}", request.AgeGroupId);
            throw new InvalidOperationException("Invalid age group selected");
        }

        var rosterFee = ageGroup.RosterFee ?? 0;
        var teamFee = ageGroup.TeamFee ?? 0;

        // Verify age group has capacity
        var registeredCount = await _teams.GetRegisteredCountForAgegroupAsync(jobId.Value, request.AgeGroupId);

        if (registeredCount >= ageGroup.MaxTeams)
        {
            _logger.LogWarning("Age group {AgeGroupId} is full ({RegisteredCount}/{MaxTeams})",
                request.AgeGroupId, registeredCount, ageGroup.MaxTeams);
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
            AgegroupId = request.AgeGroupId,
            TeamName = request.TeamName,
            LevelOfPlay = request.LevelOfPlay,
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

        _logger.LogInformation("Team registered successfully. TeamId: {TeamId}, TeamName: {TeamName}, FeeBase: {FeeBase}, FeeProcessing: {FeeProcessing}",
            team.TeamId, request.TeamName, feeBase, feeProcessing);

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

        // Get team and verify ownership via ClubrepRegistration
        var team = await _teams.GetTeamWithDetailsAsync(teamId);

        if (team == null)
        {
            _logger.LogWarning("Team not found: {TeamId}", teamId);
            throw new InvalidOperationException("Team registration not found");
        }

        // Verify team belongs to one of user's clubs via registration (check regId matches user's ClubReps)
        if (team.ClubrepRegistration is null)
        {
            _logger.LogWarning("Team {TeamId} has no ClubrepRegistration", teamId);
            throw new InvalidOperationException("Team registration is invalid");
        }

        var teamBelongsToUserClub = await _context.ClubReps
            .AnyAsync(cr => myClubs.Any(c => c.ClubId == cr.ClubId) && cr.ClubRepUserId == team.ClubrepRegistration.UserId);

        if (!teamBelongsToUserClub)
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

        // Check if club has ANY teams (referential integrity) - via join to ClubReps
        var hasTeams = await (from t in _teams.Query()
                              join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                              where t.ClubrepRegistrationid != null
                                && _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == club!.ClubId)
                              select t).AnyAsync();

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

        // Check if club has ANY teams (prevent rename if teams exist) - via join to ClubReps
        var hasTeams = await (from t in _teams.Query()
                              join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                              where t.ClubrepRegistrationid != null
                                && _context.ClubReps.Any(cr => cr.ClubRepUserId == reg.UserId && cr.ClubId == club.ClubId)
                              select t).AnyAsync();

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
}
