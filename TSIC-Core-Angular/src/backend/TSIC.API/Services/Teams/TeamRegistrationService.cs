using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;
using TSIC.Application.Services.Clubs;
using TSIC.Application.Services.Teams;
using TSIC.Contracts.Repositories;
using TSIC.Infrastructure.Data.SqlDbContext;
using System.Text.RegularExpressions;
using TSIC.API.Services.Auth;
using Microsoft.AspNetCore.Identity;
using TSIC.Infrastructure.Data.Identity;

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
    private readonly IUserRepository _users;
    private readonly SqlDbContext _context;
    private readonly ITokenService _tokenService;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITeamFeeCalculator _teamFeeCalculator;

    public TeamRegistrationService(
        ILogger<TeamRegistrationService> logger,
        IClubRepRepository clubReps,
        IClubRepository clubs,
        IJobRepository jobs,
        IJobLeagueRepository jobLeagues,
        IAgeGroupRepository agegroups,
        ITeamRepository teams,
        IRegistrationRepository registrations,
        IUserRepository users,
        SqlDbContext context,
        ITokenService tokenService,
        UserManager<ApplicationUser> userManager,
        ITeamFeeCalculator teamFeeCalculator)
    {
        _logger = logger;
        _clubReps = clubReps;
        _clubs = clubs;
        _jobs = jobs;
        _jobLeagues = jobLeagues;
        _agegroups = agegroups;
        _teams = teams;
        _registrations = registrations;
        _users = users;
        _context = context;
        _tokenService = tokenService;
        _userManager = userManager;
        _teamFeeCalculator = teamFeeCalculator;
    }

    /// <summary>
    /// Clean team name by removing club prefix
    /// Based on fnCleanTeamName from seed-clubteams-from-2025.sql
    /// </summary>
    private static string CleanTeamName(string teamName, string clubName)
    {
        var trimmed = teamName.Trim();
        var clubTrimmed = clubName.Trim();

        // Exact club match → return as-is
        if (trimmed.Equals(clubTrimmed, StringComparison.OrdinalIgnoreCase))
            return trimmed;

        // Full club prefix
        if (trimmed.StartsWith(clubTrimmed + " ", StringComparison.OrdinalIgnoreCase))
            return trimmed.Substring(clubTrimmed.Length + 1).Trim();

        // Parse club into words
        var clubWords = clubTrimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (clubWords.Length >= 2)
        {
            // Try first 2 words
            var first2 = string.Join(" ", clubWords.Take(2));
            if (trimmed.StartsWith(first2 + " ", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring(first2.Length + 1).Trim();

            // Try second word alone
            if (trimmed.StartsWith(clubWords[1] + " ", StringComparison.OrdinalIgnoreCase))
                return trimmed.Substring(clubWords[1].Length + 1).Trim();

            // Try abbreviations
            var teamWords = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (teamWords.Length > 0)
            {
                var prefix = teamWords[0];
                if (prefix.Length >= 2 && prefix.Length <= 4)
                {
                    // Standard initials (e.g., "3G" for "3D Georgia")
                    var initials = string.Concat(clubWords.Take(2).Select(w => w[0]));
                    if (prefix.Equals(initials, StringComparison.OrdinalIgnoreCase))
                        return string.Join(" ", teamWords.Skip(1)).Trim();

                    // First word + initial (e.g., "3DG" for "3D Georgia")
                    var firstPlusInitial = clubWords[0] + clubWords[1][0];
                    if (prefix.Equals(firstPlusInitial, StringComparison.OrdinalIgnoreCase))
                        return string.Join(" ", teamWords.Skip(1)).Trim();
                }
            }

            // Try first word
            if (trimmed.StartsWith(clubWords[0] + " ", StringComparison.OrdinalIgnoreCase))
            {
                var remainder = trimmed.Substring(clubWords[0].Length + 1).Trim();
                if (!remainder.Equals(clubWords[1], StringComparison.OrdinalIgnoreCase))
                    return remainder;
            }
        }

        // Fallback
        return trimmed;
    }

    public async Task<AuthTokenResponse> InitializeRegistrationAsync(string userId, string clubName, string jobPath)
    {
        _logger.LogInformation("Initializing registration for user {UserId}, club {ClubName}, job {JobPath}", userId, clubName, jobPath);

        // Get user
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("User not found: {UserId}", userId);
            throw new InvalidOperationException("User not found");
        }

        // Verify user is rep for this club
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        var clubRep = myClubs.FirstOrDefault(c => string.Equals(c.ClubName, clubName, StringComparison.OrdinalIgnoreCase));
        if (clubRep == null)
        {
            _logger.LogWarning("User {UserId} is not a rep for club {ClubName}", userId, clubName);
            throw new InvalidOperationException("User is not authorized for this club");
        }

        // Get job
        var jobId = await _jobs.GetJobIdByPathAsync(jobPath);
        if (jobId == null)
        {
            _logger.LogWarning("Job not found: {JobPath}", jobPath);
            throw new InvalidOperationException($"Event not found: {jobPath}");
        }

        var job = await _jobs.Query()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobPath, j.JobDisplayOptions.LogoHeader })
            .FirstOrDefaultAsync();

        // Find or create Registration record
        var registration = await _registrations.Query()
            .Where(r => r.UserId == userId && r.JobId == jobId && r.RoleId == Domain.Constants.RoleConstants.ClubRep)
            .FirstOrDefaultAsync();

        if (registration == null)
        {
            _logger.LogInformation("Creating new registration for user {UserId}, job {JobId}", userId, jobId);
            registration = new Domain.Entities.Registrations
            {
                RegistrationId = Guid.NewGuid(),
                UserId = userId,
                JobId = jobId.Value,
                RoleId = Domain.Constants.RoleConstants.ClubRep,
                ClubName = clubName,
                Assignment = clubName,
                RegistrationCategory = $"Club Rep: {clubName}",
                BActive = true,
                BConfirmationSent = false,
                RegistrationTs = DateTime.UtcNow,
                Modified = DateTime.UtcNow,
                FeeBase = 0,
                FeeProcessing = 0
            };
            _registrations.Add(registration);
            await _registrations.SaveChangesAsync();
            _logger.LogInformation("Created registration {RegistrationId} for user {UserId}", registration.RegistrationId, userId);
        }
        else
        {
            _logger.LogInformation("Found existing registration {RegistrationId} for user {UserId}", registration.RegistrationId, userId);
        }

        // Generate Phase 2 token with regId
        var jobLogo = job?.LogoHeader;
        var token = _tokenService.GenerateEnrichedJwtToken(user, registration.RegistrationId.ToString(), jobPath, jobLogo, "ClubRep");

        _logger.LogInformation("Generated Phase 2 token for user {UserId}, regId {RegistrationId}", userId, registration.RegistrationId);

        return new AuthTokenResponse(
            AccessToken: token,
            RefreshToken: null,
            ExpiresIn: 3600
        );
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

    public async Task<TeamsMetadataResponse> GetTeamsMetadataAsync(Guid regId, string userId, bool bPayBalanceDue = false)
    {
        _logger.LogInformation("Getting teams metadata for regId: {RegId}, user: {UserId}, bPayBalanceDue: {BPayBalanceDue}",
            regId, userId, bPayBalanceDue);

        // Get registration record to extract clubName and jobId
        var registration = await _registrations.Query()
            .Where(r => r.RegistrationId == regId && r.UserId == userId)
            .Select(r => new { r.ClubName, r.JobId })
            .FirstOrDefaultAsync();

        if (registration == null)
        {
            _logger.LogWarning("Registration not found or user mismatch: regId {RegId}, userId {UserId}", regId, userId);
            throw new InvalidOperationException("Registration not found or access denied");
        }

        var clubName = registration.ClubName;
        var jobId = registration.JobId;

        var job = await _jobs.Query()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                j.JobId,
                j.Season,
                j.BTeamsFullPaymentRequired,
                j.PlayerRegRefundPolicy,
                j.PaymentMethodsAllowedCode,
                j.BAddProcessingFees,
                j.BApplyProcessingFeesToTeamDeposit
            })
            .SingleOrDefaultAsync() ?? throw new InvalidOperationException($"Event not found for jobId: {jobId}");

        int currentYear = DateTime.Now.Year;

        var registeredTeams = await GetRegisteredTeamsForJobAsync(job.JobId, userId);
        var suggestions = await GetHistoricalTeamSuggestionsAsync(userId, clubName, currentYear);
        var ageGroups = await GetAgeGroupsWithCountsAsync(job.JobId, job.Season);

        _logger.LogInformation("Found {RegisteredCount} registered teams, {SuggestionCount} suggestions, {AgeGroupCount} age groups",
            registeredTeams.Count, suggestions.Count, ageGroups.Count);

        // Fetch club rep contact info for payment form prefill
        var contactInfo = await _users.GetUserContactInfoAsync(userId);
        UserContactInfoDto? clubRepContactInfo = null;
        if (contactInfo != null)
        {
            clubRepContactInfo = new UserContactInfoDto
            {
                FirstName = contactInfo.FirstName ?? string.Empty,
                LastName = contactInfo.LastName ?? string.Empty,
                Email = contactInfo.Email ?? string.Empty,
                StreetAddress = contactInfo.StreetAddress,
                City = contactInfo.City,
                State = contactInfo.State,
                PostalCode = contactInfo.PostalCode,
                Cellphone = contactInfo.Cellphone,
                Phone = contactInfo.Phone
            };
        }

        return new TeamsMetadataResponse
        {
            ClubId = 0,
            ClubName = clubName,
            SuggestedTeamNames = suggestions,
            RegisteredTeams = registeredTeams,
            AgeGroups = ageGroups,
            BPayBalanceDue = bPayBalanceDue,
            BTeamsFullPaymentRequired = job.BTeamsFullPaymentRequired ?? false,
            PlayerRegRefundPolicy = job.PlayerRegRefundPolicy,
            PaymentMethodsAllowedCode = job.PaymentMethodsAllowedCode,
            BAddProcessingFees = job.BAddProcessingFees,
            BApplyProcessingFeesToTeamDeposit = job.BApplyProcessingFeesToTeamDeposit ?? false,
            ClubRepContactInfo = clubRepContactInfo
        };
    }

    private async Task<List<RegisteredTeamDto>> GetRegisteredTeamsForJobAsync(Guid jobId, string userId)
    {
        return await (from t in _teams.Query()
                      join ag in _context.Agegroups on t.AgegroupId equals ag.AgegroupId
                      join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                      join j in _context.Jobs on t.JobId equals j.JobId
                      where t.JobId == jobId && reg.UserId == userId
                            && t.Active == true
                            && !ag.AgegroupName!.Contains("DROPPED")
                      orderby ag.AgegroupName, t.TeamName
                      select new RegisteredTeamDto
                      {
                          TeamId = t.TeamId,
                          TeamName = t.TeamName,
                          AgeGroupId = ag.AgegroupId,
                          AgeGroupName = ag.AgegroupName ?? string.Empty,
                          LevelOfPlay = t.LevelOfPlay,
                          FeeBase = t.FeeBase ?? 0,
                          FeeProcessing = t.FeeProcessing ?? 0,
                          FeeTotal = (t.FeeBase ?? 0) + (t.FeeProcessing ?? 0),
                          PaidTotal = t.PaidTotal ?? 0,
                          OwedTotal = ((t.FeeBase ?? 0) + (t.FeeProcessing ?? 0)) - (t.PaidTotal ?? 0),
                          DepositDue = (t.PaidTotal >= ag.RosterFee) ? 0 : (ag.RosterFee ?? 0) - (t.PaidTotal ?? 0),
                          AdditionalDue = (t.OwedTotal == 0 && (j.BTeamsFullPaymentRequired ?? false)) ? 0 : (ag.TeamFee ?? 0),
                          RegistrationTs = t.Createdate,
                          BWaiverSigned3 = reg.BWaiverSigned3,
                          // CC includes processing fee (OwedTotal already has it)
                          CcOwedTotal = ((t.FeeBase ?? 0) + (t.FeeProcessing ?? 0)) - (t.PaidTotal ?? 0),
                          // Check payment excludes processing fee
                          CkOwedTotal = (t.FeeBase ?? 0) - (t.PaidTotal ?? 0)
                      })
            .ToListAsync();
    }

    private async Task<List<SuggestedTeamNameDto>> GetHistoricalTeamSuggestionsAsync(string userId, string clubName, int currentYear)
    {
        int previousYear = currentYear - 1;

        var historicalTeams = await (from t in _teams.Query()
                                     join j in _context.Jobs on t.JobId equals j.JobId
                                     join reg in _context.Registrations on t.ClubrepRegistrationid equals reg.RegistrationId
                                     where reg.UserId == userId
                                       && !string.IsNullOrEmpty(t.TeamName)
                                       && !string.IsNullOrEmpty(j.Year)
                                       && (j.Year == currentYear.ToString() || j.Year == previousYear.ToString())
                                     select new { t.TeamName, j.Year })
            .ToListAsync();

        return historicalTeams
            .Select(t => new { CleanedName = CleanTeamName(t.TeamName, clubName), Year = int.TryParse(t.Year, out var y) ? y : 0 })
            .Where(t => t.Year > 0)
            .GroupBy(t => t.CleanedName)
            .Select(g => new SuggestedTeamNameDto { TeamName = g.Key, UsageCount = g.Count(), Year = g.Max(x => x.Year) })
            .OrderBy(s => s.TeamName)
            .ToList();
    }

    private async Task<List<AgeGroupDto>> GetAgeGroupsWithCountsAsync(Guid jobId, string jobSeason)
    {
        var leagueId = await _jobLeagues.GetPrimaryLeagueForJobAsync(jobId);

        if (leagueId == null)
            return new List<AgeGroupDto>();

        var ageGroupEntities = await _agegroups.Query()
            .Where(ag => ag.LeagueId == leagueId && ag.Season == jobSeason && ag.MaxTeams > 0)
            .OrderBy(ag => ag.AgegroupName)
            .ToListAsync();

        var ageGroupIds = ageGroupEntities.Select(ag => ag.AgegroupId).ToList();

        var registrationCounts = await _teams.Query()
            .Where(t => t.JobId == jobId && ageGroupIds.Contains(t.AgegroupId))
            .GroupBy(t => t.AgegroupId)
            .Select(g => new { AgegroupId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.AgegroupId, x => x.Count);

        return ageGroupEntities.Select(ag => new AgeGroupDto
        {
            AgeGroupId = ag.AgegroupId,
            AgeGroupName = ag.AgegroupName ?? string.Empty,
            MaxTeams = ag.MaxTeams,
            RosterFee = ag.RosterFee ?? 0,
            TeamFee = ag.TeamFee ?? 0,
            RegisteredCount = registrationCounts.GetValueOrDefault(ag.AgegroupId, 0)
        }).ToList();
    }

    public async Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, Guid regId, string userId)
    {
        _logger.LogInformation("Registering team for event. TeamName: {TeamName}, AgeGroupId: {AgeGroupId}, RegId: {RegId}, User: {UserId}",
            request.TeamName, request.AgeGroupId, regId, userId);

        // Validate team name
        if (string.IsNullOrWhiteSpace(request.TeamName))
        {
            throw new InvalidOperationException("Team name is required");
        }

        // Get club rep registration from token - this provides all context (clubName, jobId)
        var clubRepRegistration = await _registrations.Query()
            .Where(r => r.RegistrationId == regId && r.UserId == userId && r.RoleId == Domain.Constants.RoleConstants.ClubRep)
            .FirstOrDefaultAsync();

        if (clubRepRegistration == null)
        {
            _logger.LogWarning("Registration not found: regId {RegId} for user {UserId}", regId, userId);
            throw new InvalidOperationException("Registration not found. Please select a club first.");
        }

        var jobId = clubRepRegistration.JobId;
        var clubName = clubRepRegistration.ClubName;

        _logger.LogInformation("Using registration {RegId} for job {JobId}, club {ClubName}", regId, jobId, clubName);

        // Get job settings for fee calculation
        var job = await _jobs.Query()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                j.JobId,
                j.BTeamsFullPaymentRequired,
                j.BAddProcessingFees,
                j.BApplyProcessingFeesToTeamDeposit,
                j.ProcessingFeePercent
            })
            .FirstOrDefaultAsync();

        if (job == null)
        {
            _logger.LogWarning("Job not found: {JobId}", jobId);
            throw new InvalidOperationException("Event not found");
        }

        // Get club ID from ClubName
        var club = await _context.Clubs.Where(c => c.ClubName == clubName).FirstOrDefaultAsync();
        if (club == null)
        {
            _logger.LogWarning("Club not found: {ClubName}", clubName);
            throw new InvalidOperationException($"Club not found: {clubName}");
        }
        var effectiveClubId = club.ClubId;

        // Validate that user has access to this club (via ClubReps table)
        var hasAccess = await _clubReps.ExistsAsync(userId, effectiveClubId);
        if (!hasAccess)
        {
            _logger.LogWarning("User {UserId} does not have access to club {ClubId}", userId, effectiveClubId);
            throw new UnauthorizedAccessException("User does not have access to this club");
        }

        // Get league for this job (prefer primary, fall back to first league)
        var leagueId = await _jobLeagues.GetPrimaryLeagueForJobAsync(jobId);

        if (leagueId == null)
        {
            _logger.LogWarning("No league found for job: {JobId}", jobId);
            throw new InvalidOperationException("Event does not have a league configured");
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
        var registeredCount = await _teams.GetRegisteredCountForAgegroupAsync(jobId, request.AgeGroupId);

        if (registeredCount >= ageGroup.MaxTeams)
        {
            _logger.LogWarning("Age group {AgeGroupId} is full ({RegisteredCount}/{MaxTeams})",
                request.AgeGroupId, registeredCount, ageGroup.MaxTeams);
            throw new InvalidOperationException("This age group is full");
        }

        // Calculate fees using team fee calculator
        var (feeBase, feeProcessing) = _teamFeeCalculator.CalculateTeamFees(
            rosterFee: rosterFee,
            teamFee: teamFee,
            bTeamsFullPaymentRequired: job.BTeamsFullPaymentRequired ?? false,
            bAddProcessingFees: job.BAddProcessingFees,
            bApplyProcessingFeesToTeamDeposit: job.BApplyProcessingFeesToTeamDeposit ?? false,
            jobProcessingFeePercent: job.ProcessingFeePercent,
            paidTotal: 0,  // New team has no payments yet
            currentFeeTotal: 0  // New team has no fees yet
        );

        // Create team registration
        var team = new Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = (Guid)leagueId,
            AgegroupId = request.AgeGroupId,
            TeamName = request.TeamName,
            LevelOfPlay = request.LevelOfPlay,
            ClubrepRegistrationid = clubRepRegistration.RegistrationId,  // Track which club rep registered this team
            FeeBase = feeBase,
            FeeProcessing = feeProcessing,
            FeeTotal = feeBase + feeProcessing,
            OwedTotal = feeBase + feeProcessing,
            PaidTotal = 0,
            Active = true,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            LebUserId = userId  // CRITICAL: Set audit field
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

    public async Task<bool> UnregisterTeamFromEventAsync(Guid teamId)
    {
        _logger.LogInformation("Unregistering team {TeamId}", teamId);

        // Get team
        var team = await _teams.GetTeamFromTeamId(teamId);

        if (team == null)
        {
            _logger.LogWarning("Team not found: {TeamId}", teamId);
            throw new InvalidOperationException("Team registration not found");
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

    public async Task AcceptRefundPolicyAsync(Guid registrationId)
    {
        var registration = await _context.Registrations.FindAsync(registrationId)
            ?? throw new InvalidOperationException($"Registration not found: {registrationId}");

        registration.BWaiverSigned3 = true;
        await _context.SaveChangesAsync();

        _logger.LogInformation("Refund policy accepted for registration {RegistrationId}", registrationId);
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

    public async Task<RecalculateTeamFeesResponse> RecalculateTeamFeesAsync(RecalculateTeamFeesRequest request, string userId)
    {
        _logger.LogInformation("Recalculating team fees for user {UserId}, JobId {JobId}, TeamId {TeamId}",
            userId, request.JobId, request.TeamId);

        var updates = new List<TeamFeeUpdateDto>();
        var skippedReasons = new List<string>();

        // Determine jobId based on request mode
        Guid jobId;
        if (request.JobId.HasValue)
        {
            jobId = request.JobId.Value;
        }
        else if (request.TeamId.HasValue)
        {
            // Single-team mode: lookup JobId from team
            var team = await _teams.Query()
                .Where(t => t.TeamId == request.TeamId.Value)
                .Select(t => new { t.JobId })
                .FirstOrDefaultAsync();

            if (team == null)
            {
                throw new KeyNotFoundException($"Team not found: {request.TeamId.Value}");
            }

            jobId = team.JobId;
        }
        else
        {
            throw new InvalidOperationException("Either JobId or TeamId must be provided");
        }

        // Get job settings for fee calculation
        var job = await _jobs.Query()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                j.JobId,
                j.BTeamsFullPaymentRequired,
                j.BAddProcessingFees,
                j.BApplyProcessingFeesToTeamDeposit,
                j.ProcessingFeePercent
            })
            .FirstOrDefaultAsync();

        if (job == null)
        {
            throw new KeyNotFoundException($"Job not found: {jobId}");
        }

        // Query teams with job and agegroup data
        var teamsQuery = _teams.Query()
            .Include(t => t.Job)
            .Include(t => t.Agegroup)
            .Where(t => t.JobId == jobId);

        // Filter by specific team if provided
        if (request.TeamId.HasValue)
        {
            teamsQuery = teamsQuery.Where(t => t.TeamId == request.TeamId.Value);
        }

        var teams = await teamsQuery.ToListAsync();

        _logger.LogInformation("Found {TeamCount} teams for recalculation", teams.Count);

        // Filter out WAITLIST and DROPPED teams
        var eligibleTeams = teams
            .Where(t => t.Agegroup != null &&
                       !string.IsNullOrEmpty(t.Agegroup.AgegroupName) &&
                       !t.Agegroup.AgegroupName.Contains("WAITLIST", StringComparison.OrdinalIgnoreCase) &&
                       !t.Agegroup.AgegroupName.Contains("DROPPED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("Found {EligibleCount} eligible teams (filtered WAITLIST/DROPPED)", eligibleTeams.Count);

        // Track skipped teams
        var skippedTeams = teams.Except(eligibleTeams).ToList();
        foreach (var skipped in skippedTeams)
        {
            skippedReasons.Add($"Team '{skipped.TeamName}' in age group '{skipped.Agegroup?.AgegroupName}' (WAITLIST/DROPPED)");
        }

        // Process each eligible team
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            foreach (var team in eligibleTeams)
            {
                if (team.Agegroup == null)
                {
                    skippedReasons.Add($"Team '{team.TeamName}' has no age group assigned");
                    continue;
                }

                var oldFeeBase = team.FeeBase ?? 0;
                var oldFeeProcessing = team.FeeProcessing ?? 0;
                var oldFeeTotal = team.FeeTotal ?? 0;

                // Calculate new fees
                var (newFeeBase, newFeeProcessing) = _teamFeeCalculator.CalculateTeamFees(
                    rosterFee: team.Agegroup.RosterFee ?? 0,
                    teamFee: team.Agegroup.TeamFee ?? 0,
                    bTeamsFullPaymentRequired: job.BTeamsFullPaymentRequired ?? false,
                    bAddProcessingFees: job.BAddProcessingFees,
                    bApplyProcessingFeesToTeamDeposit: job.BApplyProcessingFeesToTeamDeposit ?? false,
                    jobProcessingFeePercent: job.ProcessingFeePercent,
                    paidTotal: team.PaidTotal ?? 0,
                    currentFeeTotal: oldFeeTotal
                );

                // Only update if fees changed
                if (newFeeBase != oldFeeBase || newFeeProcessing != oldFeeProcessing)
                {
                    team.FeeBase = newFeeBase;
                    team.FeeProcessing = newFeeProcessing;
                    team.FeeTotal = newFeeBase + newFeeProcessing;
                    team.OwedTotal = team.FeeTotal - (team.PaidTotal ?? 0);

                    // CRITICAL: Set audit fields
                    team.LebUserId = userId;
                    team.Modified = DateTime.UtcNow;

                    updates.Add(new TeamFeeUpdateDto
                    {
                        TeamId = team.TeamId,
                        TeamName = team.TeamName ?? string.Empty,
                        AgeGroupName = team.Agegroup.AgegroupName ?? string.Empty,
                        OldFeeBase = oldFeeBase,
                        NewFeeBase = newFeeBase,
                        OldFeeProcessing = oldFeeProcessing,
                        NewFeeProcessing = newFeeProcessing,
                        UpdatedBy = userId,
                        UpdatedAt = team.Modified
                    });

                    _logger.LogInformation(
                        "Team {TeamId} ({TeamName}): FeeBase {OldFeeBase} → {NewFeeBase}, FeeProcessing {OldFeeProcessing} → {NewFeeProcessing}",
                        team.TeamId, team.TeamName, oldFeeBase, newFeeBase, oldFeeProcessing, newFeeProcessing);
                }
            }

            // Bulk update in single transaction
            if (updates.Any())
            {
                await _teams.UpdateTeamFeesAsync(eligibleTeams);
                await transaction.CommitAsync();
                _logger.LogInformation("Successfully updated {UpdatedCount} teams", updates.Count);
            }
            else
            {
                await transaction.CommitAsync();
                _logger.LogInformation("No teams required fee updates");
            }
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            _logger.LogError(ex, "Error recalculating team fees");
            throw;
        }

        return new RecalculateTeamFeesResponse
        {
            UpdatedCount = updates.Count,
            Updates = updates,
            SkippedCount = skippedReasons.Count,
            SkippedReasons = skippedReasons
        };
    }
}
