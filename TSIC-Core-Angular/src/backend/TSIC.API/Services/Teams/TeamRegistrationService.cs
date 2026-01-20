using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;
using TSIC.Application.Services.Clubs;
using TSIC.Application.Services.Teams;
using TSIC.Contracts.Repositories;
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

        var job = await _jobs.GetJobAuthInfoAsync(jobId.Value);

        // Find or create Registration record
        var registration = await _registrations.GetClubRepRegistrationAsync(userId, jobId.Value);

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
        var currentUserRegistration = await _registrations.GetClubRepRegistrationAsync(userId, jobId.Value);

        var otherRepTeams = await _teams.GetTeamsByClubExcludingRegistrationAsync(
            jobId.Value,
            clubRep.ClubId,
            currentUserRegistration?.RegistrationId);

        if (otherRepTeams.Any())
        {
            var otherRepUsername = otherRepTeams.First().Username ?? "another club rep";
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
        var registration = await _registrations.GetRegistrationBasicInfoAsync(regId, userId);

        if (registration == null)
        {
            _logger.LogWarning("Registration not found or user mismatch: regId {RegId}, userId {UserId}", regId, userId);
            throw new InvalidOperationException("Registration not found or access denied");
        }

        var clubName = registration.ClubName;
        var jobId = registration.JobId;

        var job = await _jobs.GetJobFeeSettingsAsync(jobId) ?? throw new InvalidOperationException($"Event not found for jobId: {jobId}");

        int currentYear = DateTime.Now.Year;

        var registeredTeams = await GetRegisteredTeamsForJobAsync(jobId, userId);
        var suggestions = await GetHistoricalTeamSuggestionsAsync(userId, clubName, currentYear);
        var ageGroups = await GetAgeGroupsWithCountsAsync(jobId, job.Season ?? string.Empty);

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
            BAddProcessingFees = job.BAddProcessingFees ?? false,
            BApplyProcessingFeesToTeamDeposit = job.BApplyProcessingFeesToTeamDeposit ?? false,
            ClubRepContactInfo = clubRepContactInfo
        };
    }

    private async Task<List<RegisteredTeamDto>> GetRegisteredTeamsForJobAsync(Guid jobId, string userId)
    {
        var registeredTeams = await _teams.GetRegisteredTeamsForUserAndJobAsync(jobId, userId);

        return registeredTeams.Select(t => new RegisteredTeamDto
        {
            TeamId = t.TeamId,
            TeamName = t.TeamName,
            AgeGroupId = t.AgeGroupId,
            AgeGroupName = t.AgeGroupName,
            LevelOfPlay = t.LevelOfPlay,
            FeeBase = t.FeeBase,
            FeeProcessing = t.FeeProcessing,
            FeeTotal = t.FeeTotal,
            PaidTotal = t.PaidTotal,
            OwedTotal = t.OwedTotal,
            DepositDue = t.DepositDue,
            AdditionalDue = t.AdditionalDue,
            RegistrationTs = t.RegistrationTs,
            BWaiverSigned3 = t.BWaiverSigned3,
            CcOwedTotal = t.OwedTotal,
            CkOwedTotal = t.FeeBase - t.PaidTotal
        }).ToList();
    }

    private async Task<List<SuggestedTeamNameDto>> GetHistoricalTeamSuggestionsAsync(string userId, string clubName, int currentYear)
    {
        int previousYear = currentYear - 1;

        var priorTeams = await _teams.GetHistoricalTeamsForClubAsync(userId, clubName, previousYear);
        var currentTeams = await _teams.GetHistoricalTeamsForClubAsync(userId, clubName, currentYear);

        var historicalTeams = priorTeams.Concat(currentTeams)
            .Select(t => new { t.TeamName, Year = t.Createdate.Year });

        return historicalTeams
            .Select(t => new { CleanedName = CleanTeamName(t.TeamName, clubName), t.Year })
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

        var ageGroupEntities = await _agegroups.GetByLeagueAndSeasonAsync(leagueId.Value, jobSeason);

        var registrationCounts = await _teams.GetRegistrationCountsByAgeGroupAsync(jobId);

        return ageGroupEntities.Select(ag => new AgeGroupDto
        {
            AgeGroupId = ag.AgegroupId,
            AgeGroupName = ag.AgegroupName,
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
        var clubRepRegistration = await _registrations.GetByIdAsync(regId);

        if (clubRepRegistration == null || clubRepRegistration.UserId != userId || clubRepRegistration.RoleId != Domain.Constants.RoleConstants.ClubRep)
        {
            _logger.LogWarning("Invalid club rep registration: regId {RegId} for user {UserId}", regId, userId);
            throw new InvalidOperationException("Invalid club rep registration. Please select a club first.");
        }

        var jobId = clubRepRegistration.JobId;
        var clubName = clubRepRegistration.ClubName;

        _logger.LogInformation("Using registration {RegId} for job {JobId}, club {ClubName}", regId, jobId, clubName);

        var jobSettings = await _jobs.GetJobFeeSettingsAsync(jobId);
        if (jobSettings == null)
        {
            _logger.LogWarning("Job not found: {JobId}", jobId);
            throw new InvalidOperationException("Event not found");
        }

        var processingFeePercent = await _jobs.GetProcessingFeePercentAsync(jobId);

        // Get club ID from ClubName
        var club = await _clubs.GetByNameAsync(clubName);
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
        var existingTeamsForClub = await _teams.GetTeamsByClubExcludingRegistrationAsync(jobId, effectiveClubId, clubRepRegistration.RegistrationId);

        // Validate one-rep-per-event rule
        var differentRepTeams = existingTeamsForClub
            .Where(t => t.ClubrepRegistrationid != clubRepRegistration.RegistrationId)
            .ToList();

        if (differentRepTeams.Any())
        {
            var otherRepUsername = differentRepTeams[0].Username ?? "another club rep";
            _logger.LogWarning("One-rep-per-event violation: User {UserId} (regId {RegistrationId}) attempted to register team for event {JobId} club {ClubId}, but {OtherRepUser} already has teams registered",
                userId, clubRepRegistration.RegistrationId, jobId, effectiveClubId, otherRepUsername);
            throw new InvalidOperationException($"Only one club representative can register teams per event. {otherRepUsername} has already registered teams for this club in this event. Please contact your organization administrator.");
        }

        // Validate age group
        var ageGroup = await _agegroups.GetByIdAsync(request.AgeGroupId);

        if (ageGroup == null || ageGroup.LeagueId != leagueId)
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
            bTeamsFullPaymentRequired: jobSettings.BTeamsFullPaymentRequired ?? false,
            bAddProcessingFees: jobSettings.BAddProcessingFees ?? false,
            bApplyProcessingFeesToTeamDeposit: jobSettings.BApplyProcessingFeesToTeamDeposit ?? false,
            jobProcessingFeePercent: processingFeePercent ?? 0,
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

        var hasTeams = await _teams.HasTeamsForClubRepAsync(userId, club.ClubId);

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

        var hasTeams = await _teams.HasTeamsForClubRepAsync(userId, club.ClubId);

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
        var registration = await _registrations.GetByIdAsync(registrationId)
            ?? throw new InvalidOperationException($"Registration not found: {registrationId}");

        registration.BWaiverSigned3 = true;
        await _registrations.SaveChangesAsync();

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

        Guid jobId;
        if (request.JobId.HasValue)
        {
            jobId = request.JobId.Value;
        }
        else if (request.TeamId.HasValue)
        {
            var teamJobId = await _teams.GetTeamJobIdAsync(request.TeamId.Value)
                ?? throw new KeyNotFoundException($"Team not found: {request.TeamId.Value}");
            jobId = teamJobId;
        }
        else
        {
            throw new InvalidOperationException("Either JobId or TeamId must be provided");
        }

        var job = await _jobs.GetJobFeeSettingsAsync(jobId) ?? throw new KeyNotFoundException($"Job not found: {jobId}");
        var jobProcessingFeePercent = await _jobs.GetProcessingFeePercentAsync(jobId); // Null when no job override - calculator uses default

        var teams = await _teams.GetTeamsWithDetailsForJobAsync(jobId);
        if (request.TeamId.HasValue)
        {
            teams = teams.Where(t => t.TeamId == request.TeamId.Value).ToList();
        }

        _logger.LogInformation("Found {TeamCount} teams for recalculation", teams.Count);

        var eligibleTeams = teams
            .Where(t => t.Agegroup != null &&
                       !string.IsNullOrEmpty(t.Agegroup.AgegroupName) &&
                       !t.Agegroup.AgegroupName.Contains("WAITLIST", StringComparison.OrdinalIgnoreCase) &&
                       !t.Agegroup.AgegroupName.Contains("DROPPED", StringComparison.OrdinalIgnoreCase))
            .ToList();

        _logger.LogInformation("Found {EligibleCount} eligible teams (filtered WAITLIST/DROPPED)", eligibleTeams.Count);

        var skippedTeams = teams.Except(eligibleTeams).ToList();
        foreach (var skipped in skippedTeams)
        {
            skippedReasons.Add($"Team '{skipped.TeamName}' in age group '{skipped.Agegroup?.AgegroupName}' (WAITLIST/DROPPED)");
        }

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

            var (newFeeBase, newFeeProcessing) = _teamFeeCalculator.CalculateTeamFees(
                rosterFee: team.Agegroup.RosterFee ?? 0,
                teamFee: team.Agegroup.TeamFee ?? 0,
                bTeamsFullPaymentRequired: job.BTeamsFullPaymentRequired ?? false,
                bAddProcessingFees: job.BAddProcessingFees ?? false,
                bApplyProcessingFeesToTeamDeposit: job.BApplyProcessingFeesToTeamDeposit ?? false,
                jobProcessingFeePercent: jobProcessingFeePercent,
                paidTotal: team.PaidTotal ?? 0,
                currentFeeTotal: oldFeeTotal);

            if (newFeeBase != oldFeeBase || newFeeProcessing != oldFeeProcessing)
            {
                team.FeeBase = newFeeBase;
                team.FeeProcessing = newFeeProcessing;
                team.FeeTotal = newFeeBase + newFeeProcessing;
                team.OwedTotal = team.FeeTotal - (team.PaidTotal ?? 0);
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
                    "Team {TeamId} ({TeamName}): FeeBase {OldFeeBase} -> {NewFeeBase}, FeeProcessing {OldFeeProcessing} -> {NewFeeProcessing}",
                    team.TeamId, team.TeamName, oldFeeBase, newFeeBase, oldFeeProcessing, newFeeProcessing);
            }
        }

        if (updates.Any())
        {
            await _teams.UpdateTeamFeesAsync(eligibleTeams);
            _logger.LogInformation("Successfully updated {UpdatedCount} teams", updates.Count);
        }
        else
        {
            _logger.LogInformation("No teams required fee updates");
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
