using Microsoft.EntityFrameworkCore;
using TSIC.API.Dtos;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.API.Services;

public class TeamRegistrationService : ITeamRegistrationService
{
    private readonly SqlDbContext _db;
    private readonly ILogger<TeamRegistrationService> _logger;

    public TeamRegistrationService(
        SqlDbContext db,
        ILogger<TeamRegistrationService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<List<ClubRepClubDto>> GetMyClubsAsync(string userId)
    {
        _logger.LogInformation("Getting clubs for user: {UserId}", userId);

        var clubs = await _db.ClubReps
            .Where(cr => cr.ClubRepUserId == userId)
            .Select(cr => new ClubRepClubDto
            {
                ClubName = cr.Club.ClubName,
                IsInUse = _db.Teams.Any(t => t.ClubTeam!.ClubId == cr.ClubId)
            })
            .OrderBy(c => c.ClubName)
            .ToListAsync();

        _logger.LogInformation("Found {ClubCount} clubs for user {UserId}", clubs.Count, userId);
        return clubs;
    }

    public async Task<TeamsMetadataResponse> GetTeamsMetadataAsync(string jobPath, string userId, string clubName)
    {
        _logger.LogInformation("Getting teams metadata for job: {JobPath}, user: {UserId}, club: {ClubName}", jobPath, userId, clubName);

        // Get club rep for user and club
        var clubRep = await _db.ClubReps
            .Where(cr => cr.ClubRepUserId == userId && cr.Club.ClubName == clubName)
            .Select(cr => new { cr.ClubId, cr.Club.ClubName })
            .SingleOrDefaultAsync();

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Get job
        var job = await _db.Jobs
            .Where(j => j.JobPath == jobPath)
            .Select(j => new { j.JobId, j.Season })
            .SingleOrDefaultAsync();

        if (job == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", jobPath);
            throw new InvalidOperationException($"Event not found: {jobPath}");
        }

        // Get league for this job (prefer primary, fall back to first league)
        var jobLeague = await _db.JobLeagues
            .Where(jl => jl.JobId == job.JobId)
            .OrderByDescending(jl => jl.BIsPrimary)
            .Select(jl => new { jl.LeagueId })
            .FirstOrDefaultAsync();

        if (jobLeague == null)
        {
            _logger.LogWarning("No league found for job: {JobId}", job.JobId);
            throw new InvalidOperationException("Event does not have a league configured");
        }

        var leagueId = jobLeague.LeagueId;

        // Get club teams for this club
        var clubTeams = await _db.ClubTeams
            .Where(ct => ct.ClubId == clubRep.ClubId)
            .Select(ct => new ClubTeamDto
            {
                ClubTeamId = ct.ClubTeamId,
                ClubTeamName = ct.ClubTeamName,
                ClubTeamGradYear = ct.ClubTeamGradYear,
                ClubTeamLevelOfPlay = ct.ClubTeamLevelOfPlay
            })
            .OrderBy(ct => ct.ClubTeamName)
            .ToListAsync();

        // Get already registered teams for this club and event
        var registeredTeams = await _db.Teams
            .Where(t => t.JobId == job.JobId && t.ClubTeam!.ClubId == clubRep.ClubId)
            .Select(t => new RegisteredTeamDto
            {
                TeamId = t.TeamId,
                ClubTeamId = t.ClubTeamId!.Value,
                ClubTeamName = t.ClubTeam!.ClubTeamName,
                ClubTeamGradYear = t.ClubTeam!.ClubTeamGradYear,
                ClubTeamLevelOfPlay = t.ClubTeam!.ClubTeamLevelOfPlay,
                AgeGroupId = t.AgegroupId,
                AgeGroupName = t.Agegroup!.AgegroupName ?? string.Empty,
                FeeBase = t.FeeBase ?? 0,
                FeeProcessing = t.FeeProcessing ?? 0,
                FeeTotal = (t.FeeBase ?? 0) + (t.FeeProcessing ?? 0),
                PaidTotal = t.PaidTotal ?? 0,
                OwedTotal = ((t.FeeBase ?? 0) + (t.FeeProcessing ?? 0)) - (t.PaidTotal ?? 0)
            })
            .ToListAsync();

        // Get age groups for this league/season with their registration counts
        var ageGroups = await _db.Agegroups
            .Where(ag => ag.LeagueId == leagueId && ag.Season == job.Season && ag.MaxTeams > 0)
            .Select(ag => new AgeGroupDto
            {
                AgeGroupId = ag.AgegroupId,
                AgeGroupName = ag.AgegroupName ?? string.Empty,
                MaxTeams = ag.MaxTeams,
                RosterFee = ag.RosterFee ?? 0,
                TeamFee = ag.TeamFee ?? 0,
                RegisteredCount = _db.Teams.Count(t => t.JobId == job.JobId && t.AgegroupId == ag.AgegroupId)
            })
            .OrderBy(ag => ag.AgeGroupName)
            .ToListAsync();

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
        var clubRep = await _db.ClubReps
            .Where(cr => cr.ClubRepUserId == userId)
            .Select(cr => new { cr.ClubId })
            .FirstOrDefaultAsync();

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Get club team and verify ownership
        var clubTeam = await _db.ClubTeams
            .Where(ct => ct.ClubTeamId == request.ClubTeamId)
            .Select(ct => new { ct.ClubId, ct.ClubTeamGradYear })
            .SingleOrDefaultAsync();

        if (clubTeam == null)
        {
            _logger.LogWarning("Club team not found: {ClubTeamId}", request.ClubTeamId);
            throw new InvalidOperationException("Club team not found");
        }

        if (clubTeam.ClubId != clubRep.ClubId)
        {
            _logger.LogWarning("Club team {ClubTeamId} does not belong to user's club", request.ClubTeamId);
            throw new InvalidOperationException("You can only register teams from your own club");
        }

        // Get job
        var job = await _db.Jobs
            .Where(j => j.JobPath == request.JobPath)
            .SingleOrDefaultAsync();

        if (job == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", request.JobPath);
            throw new InvalidOperationException($"Event not found: {request.JobPath}");
        }

        // Get league for this job (prefer primary, fall back to first league)
        var jobLeague = await _db.JobLeagues
            .Where(jl => jl.JobId == job.JobId)
            .OrderByDescending(jl => jl.BIsPrimary)
            .Select(jl => new { jl.LeagueId })
            .FirstOrDefaultAsync();

        if (jobLeague == null)
        {
            _logger.LogWarning("No league found for job: {JobId}", job.JobId);
            throw new InvalidOperationException("Event does not have a league configured");
        }

        var leagueId = jobLeague.LeagueId;

        // Check if team is already registered
        var existingTeam = await _db.Teams
            .Where(t => t.JobId == job.JobId && t.ClubTeamId == request.ClubTeamId)
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
            var providedAgeGroup = await _db.Agegroups
                .Where(ag => ag.AgegroupId == ageGroupId && ag.LeagueId == leagueId && ag.Season == job.Season)
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
            var gradYearString = clubTeam.ClubTeamGradYear;
            if (string.IsNullOrWhiteSpace(gradYearString) || !int.TryParse(gradYearString, out var gradYear))
            {
                _logger.LogWarning("Cannot auto-determine age group: ClubTeam {ClubTeamId} has no valid grad year",
                    request.ClubTeamId);
                throw new InvalidOperationException("Age group must be specified or team must have a valid graduation year");
            }

            var ageGroup = await _db.Agegroups
                .Where(ag => ag.LeagueId == leagueId
                    && ag.Season == job.Season
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
        var registeredCount = await _db.Teams
            .Where(t => t.JobId == job.JobId && t.AgegroupId == ageGroupId)
            .CountAsync();

        var maxTeams = await _db.Agegroups
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
        var team = new Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = job.JobId,
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

        _db.Teams.Add(team);
        await _db.SaveChangesAsync();

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
        var clubRep = await _db.ClubReps
            .Where(cr => cr.ClubRepUserId == userId)
            .Select(cr => new { cr.ClubId })
            .FirstOrDefaultAsync();

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Get team and verify ownership
        var team = await _db.Teams
            .Include(t => t.ClubTeam)
            .Where(t => t.TeamId == teamId)
            .SingleOrDefaultAsync();

        if (team == null)
        {
            _logger.LogWarning("Team not found: {TeamId}", teamId);
            throw new InvalidOperationException("Team registration not found");
        }

        if (team.ClubTeam?.ClubId != clubRep.ClubId)
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
        _db.Teams.Remove(team);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Team {TeamId} unregistered successfully", teamId);
        return true;
    }

    public async Task<AddClubTeamResponse> AddNewClubTeamAsync(AddClubTeamRequest request, string userId)
    {
        _logger.LogInformation("Adding new club team: {TeamName} for user {UserId}", request.ClubTeamName, userId);

        // Get club rep
        var clubRep = await _db.ClubReps
            .Where(cr => cr.ClubRepUserId == userId)
            .Select(cr => new { cr.ClubId })
            .FirstOrDefaultAsync();

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a club rep", userId);
            throw new InvalidOperationException("User is not authorized as a club representative");
        }

        // Check for duplicate team name in this club
        var existingTeam = await _db.ClubTeams
            .Where(ct => ct.ClubId == clubRep.ClubId && ct.ClubTeamName == request.ClubTeamName)
            .FirstOrDefaultAsync();

        if (existingTeam != null)
        {
            _logger.LogWarning("Duplicate team name {TeamName} in club {ClubId}", request.ClubTeamName, clubRep.ClubId);
            throw new InvalidOperationException($"A team named '{request.ClubTeamName}' already exists for your club");
        }

        // Create new club team
        var clubTeam = new ClubTeams
        {
            ClubId = clubRep.ClubId,
            ClubTeamName = request.ClubTeamName,
            ClubTeamGradYear = request.ClubTeamGradYear,
            ClubTeamLevelOfPlay = request.ClubTeamLevelOfPlay,
            Modified = DateTime.UtcNow
        };

        _db.ClubTeams.Add(clubTeam);
        await _db.SaveChangesAsync();

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

        Clubs club;
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
            club = new Clubs
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
        var club = await _db.Clubs
            .Where(c => c.ClubName == clubName)
            .Select(c => new { c.ClubId })
            .SingleOrDefaultAsync();

        if (club == null)
        {
            _logger.LogWarning("Club not found: {ClubName}", clubName);
            throw new InvalidOperationException("Club not found");
        }

        // Check if club has ANY teams (referential integrity)
        var hasTeams = await _db.Teams
            .AnyAsync(t => t.ClubTeam!.ClubId == club.ClubId);

        if (hasTeams)
        {
            _logger.LogWarning("Cannot remove club {ClubId} - has registered teams", club.ClubId);
            throw new InvalidOperationException("Cannot remove club - teams have been registered under this club");
        }

        // Find and remove ClubReps link
        var clubRep = await _db.ClubReps
            .Where(cr => cr.ClubRepUserId == userId && cr.ClubId == club.ClubId)
            .SingleOrDefaultAsync();

        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a rep for club {ClubId}", userId, club.ClubId);
            throw new InvalidOperationException("You are not a representative for this club");
        }

        _db.ClubReps.Remove(clubRep);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Removed club {ClubId} from user {UserId} rep account", club.ClubId, userId);
        return true;
    }

    private async Task<List<ClubSearchResult>> SearchClubsAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new List<ClubSearchResult>();
        }

        var normalized = NormalizeClubName(query);

        var clubs = await _db.Clubs
            .Select(c => new
            {
                c.ClubId,
                c.ClubName,
                State = c.LebUser!.State,
                TeamCount = c.ClubTeams.Count
            })
            .ToListAsync();

        var results = clubs
            .Select(c => new ClubSearchResult
            {
                ClubId = c.ClubId,
                ClubName = c.ClubName,
                State = c.State,
                TeamCount = c.TeamCount,
                MatchScore = CalculateSimilarity(normalized, NormalizeClubName(c.ClubName))
            })
            .Where(r => r.MatchScore >= 60)
            .OrderByDescending(r => r.MatchScore)
            .Take(5)
            .ToList();

        return results;
    }

    private static string NormalizeClubName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return string.Empty;

        var normalized = name.ToLowerInvariant();

        normalized = normalized.Replace("lax", "lacrosse")
                               .Replace("lc", "lacrosse club")
                               .Replace("fc", "football club")
                               .Replace("sc", "soccer club")
                               .Replace("yc", "youth club");

        normalized = new string(normalized.Where(c => char.IsLetterOrDigit(c) || char.IsWhiteSpace(c)).ToArray());
        normalized = string.Join(" ", normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return normalized;
    }

    private static int CalculateSimilarity(string s1, string s2)
    {
        if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2)) return 100;
        if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2)) return 0;

        var distance = LevenshteinDistance(s1, s2);
        var maxLen = Math.Max(s1.Length, s2.Length);
        var similarity = (1.0 - (double)distance / maxLen) * 100;
        return (int)Math.Round(similarity);
    }

    private static int LevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++) matrix[i, 0] = i;
        for (int j = 0; j <= s2.Length; j++) matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                int cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost
                );
            }
        }

        return matrix[s1.Length, s2.Length];
    }
}
