using TSIC.Contracts.Dtos;
using TSIC.Domain.Entities;
using TSIC.Application.Services.Clubs;
using TSIC.Domain.Constants;
using TSIC.Contracts.Repositories;
using System.Text.RegularExpressions;
using TSIC.API.Services.Auth;
using Microsoft.AspNetCore.Identity;
using TSIC.Infrastructure.Data.Identity;
using TSIC.API.Services.Shared.TextSubstitution;
using TSIC.Contracts.Services;

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
    private readonly IFeeResolutionService _feeService;
    private readonly ITextSubstitutionService _textSubstitution;
    private readonly IEmailService _emailService;
    private readonly IJobDiscountCodeRepository _discountCodeRepo;
    private readonly IClubTeamRepository _clubTeams;
    private readonly ITeamPlacementService _placement;

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
        IFeeResolutionService feeService,
        ITextSubstitutionService textSubstitution,
        IEmailService emailService,
        IJobDiscountCodeRepository discountCodeRepo,
        IClubTeamRepository clubTeams,
        ITeamPlacementService placement)
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
        _feeService = feeService;
        _textSubstitution = textSubstitution;
        _emailService = emailService;
        _discountCodeRepo = discountCodeRepo;
        _clubTeams = clubTeams;
        _placement = placement;
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
        var token = _tokenService.GenerateEnrichedJwtToken(user, registration.RegistrationId.ToString(), jobPath, jobLogo, RoleConstants.Names.ClubRepName);

        _logger.LogInformation("Generated Phase 2 token for user {UserId}, regId {RegistrationId}", userId, registration.RegistrationId);

        return new AuthTokenResponse
        {
            AccessToken = token,
            RefreshToken = null,
            ExpiresIn = 3600
        };
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
            var otherRepUsername = otherRepTeams[0].Username ?? "another club rep";
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

        // Resolve club to get ClubId
        var club = await _clubs.GetByNameAsync(clubName ?? string.Empty);
        var effectiveClubId = club?.ClubId ?? 0;

        // Load the raw registered-teams data first so we can include its ClubTeamIds in
        // the single batched scheduled-lookup below. Shaped into DTOs after the flag is known.
        var rawRegistered = await _teams.GetRegisteredTeamsForUserAndJobAsync(jobId, userId);
        var suggestions = await GetHistoricalTeamSuggestionsAsync(userId, clubName ?? string.Empty, currentYear);
        var ageGroups = await GetAgeGroupsWithCountsAsync(jobId, job.Season ?? string.Empty);

        // Fetch available ClubTeams for this club, excluding those already registered for this event
        var allClubTeams = effectiveClubId > 0
            ? await _clubTeams.GetByClubIdAsync(effectiveClubId)
            : new List<Domain.Entities.ClubTeams>();

        var registeredClubTeamIds = rawRegistered
            .Select(t => t.ClubTeamId)
            .Where(id => id.HasValue)
            .Select(id => id!.Value)
            .ToHashSet();

        // One batched schedule lookup covering BOTH library and currently-registered teams.
        // The flag is needed on both DTO types so the UI can gate edit/delete consistently.
        var allCandidateIds = allClubTeams.Select(ct => ct.ClubTeamId).Concat(registeredClubTeamIds).Distinct();
        var scheduledIds = await _clubTeams.GetScheduledClubTeamIdsAsync(allCandidateIds);

        var teamIds = rawRegistered.Select(t => t.TeamId).ToList();
        var feesByTeamId = await _feeService.ResolveFeesByTeamIdsAsync(
            jobId, RoleConstants.ClubRep, teamIds);
        var registeredTeams = ShapeRegisteredTeams(
            rawRegistered, scheduledIds, feesByTeamId, job.BTeamsFullPaymentRequired ?? false);

        var availableClubTeams = allClubTeams
            .Where(ct => !registeredClubTeamIds.Contains(ct.ClubTeamId))
            .Select(ct => new ClubTeamDto
            {
                ClubTeamId = ct.ClubTeamId,
                ClubTeamName = ct.ClubTeamName,
                ClubTeamGradYear = ct.ClubTeamGradYear,
                ClubTeamLevelOfPlay = ct.ClubTeamLevelOfPlay ?? string.Empty,
                BHasBeenScheduled = scheduledIds.Contains(ct.ClubTeamId),
                BArchived = !ct.Active,
            })
            .OrderBy(ct => ct.ClubTeamName)
            .ToList();

        _logger.LogInformation("Found {RegisteredCount} registered teams, {SuggestionCount} suggestions, {AgeGroupCount} age groups, {ClubTeamCount} available club teams",
            registeredTeams.Count, suggestions.Count, ageGroups.Count, availableClubTeams.Count);

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

        // Parse LOP options from job's JsonOptions
        var lopOptions = new List<string>();
        var jobMetadata = await _jobs.GetJobMetadataAsync(jobId);
        if (!string.IsNullOrWhiteSpace(jobMetadata?.JsonOptions))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(jobMetadata.JsonOptions);
                if (doc.RootElement.TryGetProperty("List_Lops", out var lopsElement)
                    && lopsElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in lopsElement.EnumerateArray())
                    {
                        var value = item.TryGetProperty("Value", out var v) ? v.GetString()
                                  : item.TryGetProperty("value", out var v2) ? v2.GetString()
                                  : null;
                        if (!string.IsNullOrWhiteSpace(value))
                            lopOptions.Add(value);
                    }
                }
            }
            catch { /* malformed JSON — return empty list */ }
        }

        return new TeamsMetadataResponse
        {
            ClubId = effectiveClubId,
            ClubName = clubName ?? string.Empty,
            ClubTeams = availableClubTeams,
            SuggestedTeamNames = suggestions,
            RegisteredTeams = registeredTeams,
            AgeGroups = ageGroups,
            BPayBalanceDue = bPayBalanceDue,
            BTeamsFullPaymentRequired = job.BTeamsFullPaymentRequired ?? false,
            PlayerRegRefundPolicy = job.PlayerRegRefundPolicy,
            PaymentMethodsAllowedCode = job.PaymentMethodsAllowedCode,
            BAddProcessingFees = job.BAddProcessingFees ?? false,
            BApplyProcessingFeesToTeamDeposit = job.BApplyProcessingFeesToTeamDeposit ?? false,
            HasActiveDiscountCodes = (await _discountCodeRepo.GetActiveCodesForJobAsync(jobId, DateTime.UtcNow)).Any(),
            ClubRepContactInfo = clubRepContactInfo,
            PayTo = job.PayTo,
            MailTo = job.MailTo,
            MailinPaymentWarning = job.MailinPaymentWarning,
            LopOptions = lopOptions,
            BWaiverSigned3 = registration.BWaiverSigned3,
            BEnableEcheck = job.BEnableEcheck,
        };
    }

    private static List<RegisteredTeamDto> ShapeRegisteredTeams(
        IEnumerable<Contracts.Repositories.RegisteredTeamInfo> rawRegistered,
        HashSet<int> scheduledClubTeamIds,
        Dictionary<Guid, Contracts.Repositories.ResolvedFee> feesByTeamId,
        bool bTeamsFullPaymentRequired)
    {
        return rawRegistered.Select(t =>
        {
            var resolved = feesByTeamId.GetValueOrDefault(t.TeamId);
            var deposit = resolved?.Deposit ?? 0m;
            var balanceDue = resolved?.BalanceDue ?? 0m;
            var depositDue = t.PaidTotal >= deposit ? 0m : deposit - t.PaidTotal;
            var additionalDue = (t.OwedTotal == 0m && bTeamsFullPaymentRequired) ? 0m : balanceDue;

            return new RegisteredTeamDto
            {
                TeamId = t.TeamId,
                TeamName = t.TeamName,
                AgeGroupId = t.AgeGroupId,
                AgeGroupName = t.AgeGroupName,
                LevelOfPlay = t.LevelOfPlay,
                FeeBase = t.FeeBase,
                FeeProcessing = t.FeeProcessing,
                FeeDiscount = t.FeeDiscount,
                FeeLatefee = t.FeeLatefee,
                FeeTotal = t.FeeTotal,
                PaidTotal = t.PaidTotal,
                OwedTotal = t.OwedTotal,
                DepositDue = depositDue,
                AdditionalDue = additionalDue,
                RegistrationTs = t.RegistrationTs,
                BWaiverSigned3 = t.BWaiverSigned3,
                CcOwedTotal = t.OwedTotal,
                // Check payment drops the CC processing fee only — Discount and LateFee are
                // already baked into OwedTotal via RecalcTotals.
                CkOwedTotal = Math.Max(0m, t.OwedTotal - t.FeeProcessing),
                ClubTeamId = t.ClubTeamId,
                BHasBeenScheduled = t.ClubTeamId.HasValue && scheduledClubTeamIds.Contains(t.ClubTeamId.Value),
            };
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

        // Resolve fees for each agegroup from fees.JobFees
        var result = new List<AgeGroupDto>();
        foreach (var ag in ageGroupEntities)
        {
            var resolved = await _feeService.ResolveFeeForAgegroupAsync(
                jobId, RoleConstants.ClubRep, ag.AgegroupId);
            result.Add(new AgeGroupDto
            {
                AgeGroupId = ag.AgegroupId,
                AgeGroupName = ag.AgegroupName,
                MaxTeams = ag.MaxTeams,
                Deposit = resolved?.EffectiveDeposit ?? 0m,
                BalanceDue = resolved?.EffectiveBalanceDue ?? 0m,
                RegisteredCount = registrationCounts.GetValueOrDefault(ag.AgegroupId, 0)
            });
        }
        return result;
    }

    public async Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, Guid regId, string userId)
    {
        _logger.LogInformation("Registering team for event. ClubTeamId: {ClubTeamId}, TeamName: {TeamName}, AgeGroupId: {AgeGroupId}, RegId: {RegId}, User: {UserId}",
            request.ClubTeamId, request.TeamName, request.AgeGroupId, regId, userId);

        // Validate: either ClubTeamId (existing) or TeamName (new) must be provided
        if (!request.ClubTeamId.HasValue && string.IsNullOrWhiteSpace(request.TeamName))
        {
            throw new InvalidOperationException("Either select an existing team or provide a new team name");
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

        var processingRate = await _feeService.GetEffectiveProcessingRateAsync(jobId);

        // Get club ID from ClubName
        var club = await _clubs.GetByNameAsync(clubName ?? string.Empty);
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

        // Job-level capability gate — mirrors legacy BRegistrationAllowTeam / BClubRepAllowAdd
        // semantics. Frontend hides the Add CTA via pulse, but the endpoint must still refuse
        // a direct call after the window closes.
        var capabilities = await _jobs.GetTeamCapabilitiesAsync(jobId);
        if (capabilities == null)
        {
            throw new InvalidOperationException("Event not found");
        }
        if (!capabilities.TeamRegistrationOpen)
        {
            _logger.LogWarning("Register-team blocked for job {JobId}: team registration CLOSED", jobId);
            throw new InvalidOperationException("Team registration is CLOSED at this time.");
        }
        if (!capabilities.ClubRepAllowAdd)
        {
            _logger.LogWarning("Register-team blocked for job {JobId}: ClubRepAllowAdd=false", jobId);
            throw new InvalidOperationException("The site is currently CLOSED to ADDING Teams/Coaches.");
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

        // Enforce MaxTeamsPerClub
        if (ageGroup.MaxTeamsPerClub > 0)
        {
            var clubTeamCount = await _teams.GetRegisteredCountForClubRepAndAgegroupAsync(
                jobId, request.AgeGroupId, clubRepRegistration.RegistrationId);
            if (clubTeamCount >= ageGroup.MaxTeamsPerClub)
            {
                _logger.LogWarning(
                    "MaxTeamsPerClub exceeded: club {ClubName} has {Count}/{Max} teams in agegroup {AgeGroupName}",
                    clubName, clubTeamCount, ageGroup.MaxTeamsPerClub, ageGroup.AgegroupName);
                return new RegisterTeamResponse
                {
                    Success = false,
                    TeamId = Guid.Empty,
                    Message = $"Your club has reached the maximum of {ageGroup.MaxTeamsPerClub} team(s) allowed in {ageGroup.AgegroupName}.",
                    IsWaitlisted = false
                };
            }
        }

        // Resolve or create ClubTeam
        int clubTeamId;
        string teamName;
        string levelOfPlay;

        if (request.ClubTeamId.HasValue)
        {
            // Selecting an existing ClubTeam
            var clubTeam = await _clubTeams.GetByIdAsync(request.ClubTeamId.Value);
            if (clubTeam == null)
            {
                throw new InvalidOperationException("Selected team not found");
            }
            if (clubTeam.ClubId != effectiveClubId)
            {
                _logger.LogWarning("ClubTeam {ClubTeamId} belongs to club {ClubTeamClubId}, not {ExpectedClubId}",
                    clubTeam.ClubTeamId, clubTeam.ClubId, effectiveClubId);
                throw new InvalidOperationException("Selected team does not belong to your club");
            }
            clubTeamId = clubTeam.ClubTeamId;
            teamName = clubTeam.ClubTeamName;
            levelOfPlay = request.LevelOfPlay ?? clubTeam.ClubTeamLevelOfPlay ?? string.Empty;
        }
        else
        {
            // Creating a new ClubTeam
            if (string.IsNullOrWhiteSpace(request.ClubTeamGradYear))
            {
                throw new InvalidOperationException("Graduation year is required when creating a new team");
            }

            var newClubTeam = new Domain.Entities.ClubTeams
            {
                ClubId = effectiveClubId,
                ClubTeamName = request.TeamName!.Trim(),
                ClubTeamGradYear = request.ClubTeamGradYear,
                ClubTeamLevelOfPlay = request.LevelOfPlay ?? string.Empty,
                LebUserId = userId,
                Modified = DateTime.UtcNow
            };
            _clubTeams.Add(newClubTeam);
            await _clubTeams.SaveChangesAsync();

            clubTeamId = newClubTeam.ClubTeamId;
            teamName = newClubTeam.ClubTeamName;
            levelOfPlay = request.LevelOfPlay ?? string.Empty;

            _logger.LogInformation("Created new ClubTeam {ClubTeamId} '{ClubTeamName}' for club {ClubId}",
                clubTeamId, teamName, effectiveClubId);
        }

        // Resolve placement (may redirect to waitlist if agegroup is full)
        TeamPlacementResult placement;
        try
        {
            placement = await _placement.ResolvePlacementAsync(
                jobId, request.AgeGroupId, teamName, userId: userId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Agegroup placement failed for team {TeamName} in agegroup {AgeGroupId}", teamName, request.AgeGroupId);
            return new RegisterTeamResponse
            {
                Success = false,
                TeamId = Guid.Empty,
                Message = ex.Message,
                IsWaitlisted = false
            };
        }

        // Create team registration. Fee fields start at zero; non-waitlisted teams get
        // the full Team → Agegroup → Job cascade (including modifiers + net-base processing
        // fee) applied by ApplyNewTeamFeesAsync below. Waitlisted teams stay at zero until
        // promoted to a real agegroup.
        var team = new Domain.Entities.Teams
        {
            TeamId = Guid.NewGuid(),
            JobId = jobId,
            LeagueId = placement.LeagueId,
            AgegroupId = placement.AgegroupId,
            DivId = placement.DivisionId,
            TeamName = teamName,
            LevelOfPlay = levelOfPlay,
            ClubTeamId = clubTeamId,
            ClubrepRegistrationid = clubRepRegistration.RegistrationId,
            FeeBase = 0,
            FeeProcessing = 0,
            FeeDiscount = 0,
            FeeLatefee = 0,
            FeeDonation = 0,
            FeeTotal = 0,
            OwedTotal = 0,
            PaidTotal = 0,
            Active = true,
            Createdate = DateTime.UtcNow,
            Modified = DateTime.UtcNow,
            LebUserId = userId
        };
        _teams.Add(team);

        if (!placement.IsWaitlisted)
        {
            var feeCtx = new TeamFeeApplicationContext
            {
                IsFullPaymentRequired = jobSettings.BTeamsFullPaymentRequired ?? false,
                AddProcessingFees = jobSettings.BAddProcessingFees ?? false,
                ApplyProcessingFeesToDeposit = jobSettings.BApplyProcessingFeesToTeamDeposit ?? false,
                ProcessingFeePercent = processingRate
            };
            await _feeService.ApplyNewTeamFeesAsync(team, jobId, placement.AgegroupId, feeCtx);
        }

        await _teams.SaveChangesAsync();

        _logger.LogInformation("Team registered successfully. TeamId: {TeamId}, TeamName: {TeamName}, ClubTeamId: {ClubTeamId}, FeeBase: {FeeBase}, FeeProcessing: {FeeProcessing}",
            team.TeamId, teamName, clubTeamId, team.FeeBase, team.FeeProcessing);

        return new RegisterTeamResponse
        {
            Success = true,
            TeamId = team.TeamId,
            Message = placement.IsWaitlisted
                ? $"Team placed on waitlist for {ageGroup.AgegroupName}"
                : "Team registered successfully",
            IsWaitlisted = placement.IsWaitlisted,
            WaitlistAgegroupName = placement.WaitlistAgegroupName
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

        // Job-level capability gate — mirrors legacy BRegistrationAllowTeam / BClubRepAllowDelete.
        var capabilities = await _jobs.GetTeamCapabilitiesAsync(team.JobId);
        if (capabilities == null)
        {
            throw new InvalidOperationException("Event not found");
        }
        if (!capabilities.TeamRegistrationOpen)
        {
            _logger.LogWarning("Unregister-team blocked for team {TeamId} (job {JobId}): team registration CLOSED", teamId, team.JobId);
            throw new InvalidOperationException("Team registration is CLOSED at this time.");
        }
        if (!capabilities.ClubRepAllowDelete)
        {
            _logger.LogWarning("Unregister-team blocked for team {TeamId} (job {JobId}): ClubRepAllowDelete=false", teamId, team.JobId);
            throw new InvalidOperationException("The site is currently CLOSED to DELETING Teams/Coaches.");
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

    public async Task<ClubTeamDto> CreateClubTeamAsync(string userId, CreateClubTeamRequest request)
    {
        // Resolve club from user's rep assignments
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        var club = myClubs.FirstOrDefault();
        if (club == null)
            throw new InvalidOperationException("No club found for this user.");

        var name = request.ClubTeamName.Trim();
        var gradYear = request.ClubTeamGradYear.Trim();
        var lop = request.LevelOfPlay?.Trim();

        // Check for existing team with same identity (name + grad year) — single SQL query.
        // FindByIdentityAsync sees active AND archived rows; the archived check below uses that.
        var match = await _clubTeams.FindByIdentityAsync(club.ClubId, name, gradYear);

        if (match != null)
        {
            // Archived rows reserve the name — reuse is blocked to preserve the historical
            // performance attribution tied to the original ClubTeamId.
            if (!match.Active)
                throw new InvalidOperationException(
                    $"'{match.ClubTeamName} {match.ClubTeamGradYear}' is archived. Restore it from the archived section instead of creating a new one.");

            // Update LOP if the new value is higher
            if (!string.IsNullOrEmpty(lop) &&
                string.Compare(lop, match.ClubTeamLevelOfPlay ?? "", StringComparison.OrdinalIgnoreCase) > 0)
            {
                var tracked = await _clubTeams.GetByIdAsync(match.ClubTeamId);
                if (tracked != null)
                {
                    tracked.ClubTeamLevelOfPlay = lop;
                    tracked.Modified = DateTime.UtcNow;
                    await _clubTeams.SaveChangesAsync();
                }
            }

            var scheduledMatch = await _clubTeams.GetScheduledClubTeamIdsAsync(new[] { match.ClubTeamId });
            return new ClubTeamDto
            {
                ClubTeamId = match.ClubTeamId,
                ClubTeamName = match.ClubTeamName,
                ClubTeamGradYear = match.ClubTeamGradYear,
                ClubTeamLevelOfPlay = match.ClubTeamLevelOfPlay ?? string.Empty,
                BHasBeenScheduled = scheduledMatch.Contains(match.ClubTeamId),
                BArchived = false,
            };
        }

        var entity = new Domain.Entities.ClubTeams
        {
            ClubId = club.ClubId,
            ClubTeamName = name,
            ClubTeamGradYear = gradYear,
            ClubTeamLevelOfPlay = lop,
            Active = true,
            Modified = DateTime.UtcNow,
            LebUserId = userId,
        };
        _clubTeams.Add(entity);
        await _clubTeams.SaveChangesAsync();

        return new ClubTeamDto
        {
            ClubTeamId = entity.ClubTeamId,
            ClubTeamName = entity.ClubTeamName,
            ClubTeamGradYear = entity.ClubTeamGradYear,
            ClubTeamLevelOfPlay = entity.ClubTeamLevelOfPlay ?? string.Empty,
            BHasBeenScheduled = false,
            BArchived = false,
        };
    }

    public async Task<ClubTeamDto> UpdateClubTeamAsync(string userId, int clubTeamId, UpdateClubTeamRequest request)
    {
        var entity = await _clubTeams.GetByIdAsync(clubTeamId)
            ?? throw new InvalidOperationException("Team not found.");

        // Authorization: ClubTeam must belong to a club the caller reps.
        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == entity.ClubId))
            throw new UnauthorizedAccessException("You do not have access to this team.");

        // Lock if ever scheduled.
        var scheduledIds = await _clubTeams.GetScheduledClubTeamIdsAsync(new[] { clubTeamId });
        if (scheduledIds.Contains(clubTeamId))
            throw new InvalidOperationException("This team has appeared on a schedule and can no longer be edited.");

        var name = request.ClubTeamName.Trim();
        var gradYear = request.ClubTeamGradYear.Trim();
        var lop = request.ClubTeamLevelOfPlay.Trim();

        if (string.IsNullOrEmpty(name)) throw new InvalidOperationException("Team name is required.");
        if (string.IsNullOrEmpty(gradYear)) throw new InvalidOperationException("Grad year is required.");

        // Name-collision guard: if (name, gradYear) changed, another row in the same club
        // must not already own it — active OR archived. Self-matches are ignored.
        var identityChanged = !string.Equals(entity.ClubTeamName, name, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(entity.ClubTeamGradYear, gradYear, StringComparison.OrdinalIgnoreCase);
        if (identityChanged)
        {
            var collision = await _clubTeams.FindByIdentityAsync(entity.ClubId, name, gradYear);
            if (collision != null && collision.ClubTeamId != clubTeamId)
            {
                throw new InvalidOperationException(collision.Active
                    ? $"Another team already uses '{name} {gradYear}'."
                    : $"'{name} {gradYear}' is archived and cannot be reused. Restore the archived team instead.");
            }
        }

        entity.ClubTeamName = name;
        entity.ClubTeamGradYear = gradYear;
        entity.ClubTeamLevelOfPlay = lop;
        entity.Modified = DateTime.UtcNow;
        entity.LebUserId = userId;
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Updated ClubTeam {ClubTeamId} for user {UserId}", clubTeamId, userId);

        return new ClubTeamDto
        {
            ClubTeamId = entity.ClubTeamId,
            ClubTeamName = entity.ClubTeamName,
            ClubTeamGradYear = entity.ClubTeamGradYear,
            ClubTeamLevelOfPlay = entity.ClubTeamLevelOfPlay ?? string.Empty,
            BHasBeenScheduled = false,
            BArchived = !entity.Active,
        };
    }

    public async Task<ClubTeamDto> ArchiveClubTeamAsync(string userId, int clubTeamId)
    {
        var entity = await _clubTeams.GetByIdAsync(clubTeamId)
            ?? throw new InvalidOperationException("Team not found.");

        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == entity.ClubId))
            throw new UnauthorizedAccessException("You do not have access to this team.");

        // Archive is the retirement path for teams with history. Unscheduled teams should be deleted.
        var scheduledIds = await _clubTeams.GetScheduledClubTeamIdsAsync(new[] { clubTeamId });
        if (!scheduledIds.Contains(clubTeamId))
            throw new InvalidOperationException("Only teams with schedule history can be archived. Delete this team instead.");

        if (!entity.Active)
            throw new InvalidOperationException("This team is already archived.");

        entity.Active = false;
        entity.Modified = DateTime.UtcNow;
        entity.LebUserId = userId;
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Archived ClubTeam {ClubTeamId} for user {UserId}", clubTeamId, userId);

        return new ClubTeamDto
        {
            ClubTeamId = entity.ClubTeamId,
            ClubTeamName = entity.ClubTeamName,
            ClubTeamGradYear = entity.ClubTeamGradYear,
            ClubTeamLevelOfPlay = entity.ClubTeamLevelOfPlay ?? string.Empty,
            BHasBeenScheduled = true,
            BArchived = true,
        };
    }

    public async Task<ClubTeamDto> UnarchiveClubTeamAsync(string userId, int clubTeamId)
    {
        var entity = await _clubTeams.GetByIdAsync(clubTeamId)
            ?? throw new InvalidOperationException("Team not found.");

        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == entity.ClubId))
            throw new UnauthorizedAccessException("You do not have access to this team.");

        if (entity.Active)
            throw new InvalidOperationException("This team is not archived.");

        entity.Active = true;
        entity.Modified = DateTime.UtcNow;
        entity.LebUserId = userId;
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Unarchived ClubTeam {ClubTeamId} for user {UserId}", clubTeamId, userId);

        var scheduledIds = await _clubTeams.GetScheduledClubTeamIdsAsync(new[] { clubTeamId });
        return new ClubTeamDto
        {
            ClubTeamId = entity.ClubTeamId,
            ClubTeamName = entity.ClubTeamName,
            ClubTeamGradYear = entity.ClubTeamGradYear,
            ClubTeamLevelOfPlay = entity.ClubTeamLevelOfPlay ?? string.Empty,
            BHasBeenScheduled = scheduledIds.Contains(clubTeamId),
            BArchived = false,
        };
    }

    public async Task DeleteClubTeamAsync(string userId, int clubTeamId)
    {
        var entity = await _clubTeams.GetByIdAsync(clubTeamId)
            ?? throw new InvalidOperationException("Team not found.");

        var myClubs = await _clubReps.GetClubsForUserAsync(userId);
        if (!myClubs.Any(c => c.ClubId == entity.ClubId))
            throw new UnauthorizedAccessException("You do not have access to this team.");

        var scheduledIds = await _clubTeams.GetScheduledClubTeamIdsAsync(new[] { clubTeamId });
        if (scheduledIds.Contains(clubTeamId))
            throw new InvalidOperationException("This team has appeared on a schedule and can no longer be deleted.");

        // Block deletion while any Teams row still references the ClubTeam —
        // forces the rep to unregister from the current event first.
        if (await _clubTeams.HasAnyTeamRegistrationsAsync(clubTeamId))
            throw new InvalidOperationException("This team is still registered for an event. Remove it from the event before deleting.");

        _clubTeams.Remove(entity);
        await _clubTeams.SaveChangesAsync();

        _logger.LogInformation("Deleted ClubTeam {ClubTeamId} for user {UserId}", clubTeamId, userId);
    }

    public async Task<AddClubToRepResponse> AddClubToRepAsync(string userId, string clubName)
    {
        _logger.LogInformation("Adding club {ClubName} to rep account for user {UserId}", clubName, userId);

        // Search for similar clubs using fuzzy matching
        var similarClubs = await SearchClubsAsync(clubName);

        // Check if exact match exists (90%+ similarity)
        var exactMatch = similarClubs.FirstOrDefault(c => c.MatchScore >= 90);

        Domain.Entities.Clubs? club;
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
        var affectedClubRepIds = new HashSet<Guid>();

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
        var processingRate = await _feeService.GetEffectiveProcessingRateAsync(jobId);

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

            // Skip teams already paid-in-full. On a true→false unflip of
            // BTeamsFullPaymentRequired, re-stamping FeeBase to deposit-only would
            // shrink FeeTotal below PaidTotal — and FeeResolutionService computes
            // OwedTotal = FeeTotal - PaidTotal with no clamp, producing a negative
            // (bogus credit) that flows straight into the rep's pulse total.
            // Symmetric to PlayerRegistrationService.RecalculatePlayerFeesAsync.
            var resolved = await _feeService.ResolveFeeAsync(
                jobId, RoleConstants.ClubRep, team.AgegroupId, team.TeamId);
            var fullAmount = (resolved?.EffectiveDeposit ?? 0m) + (resolved?.EffectiveBalanceDue ?? 0m);
            if (fullAmount > 0m && (team.PaidTotal ?? 0m) >= fullAmount)
            {
                _logger.LogInformation(
                    "Skipping team {TeamId} ({TeamName}): PaidTotal {Paid} >= full {Full} (PIF or balance-due paid).",
                    team.TeamId, team.TeamName, team.PaidTotal, fullAmount);
                skippedReasons.Add($"Team '{team.TeamName}' (paid-in-full: {team.PaidTotal:C} of {fullAmount:C})");
                continue;
            }

            var oldFeeBase = team.FeeBase ?? 0;
            var oldFeeProcessing = team.FeeProcessing ?? 0;

            await _feeService.ApplyTeamSwapFeesAsync(
                team, jobId, team.AgegroupId,
                new TeamFeeApplicationContext
                {
                    IsFullPaymentRequired = job.BTeamsFullPaymentRequired ?? false,
                    AddProcessingFees = job.BAddProcessingFees ?? false,
                    ApplyProcessingFeesToDeposit = job.BApplyProcessingFeesToTeamDeposit ?? false,
                    ProcessingFeePercent = processingRate
                });
            var newFeeBase = team.FeeBase ?? 0m;
            var newFeeProcessing = team.FeeProcessing ?? 0m;

            if (newFeeBase != oldFeeBase || newFeeProcessing != oldFeeProcessing)
            {
                team.LebUserId = userId;
                team.Modified = DateTime.UtcNow;

                if (team.ClubrepRegistrationid.HasValue)
                {
                    affectedClubRepIds.Add(team.ClubrepRegistrationid.Value);
                }

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

            // Re-aggregate the rep registration row for every rep whose teams changed.
            // The rep row carries maintained sums of FeeBase/FeeProcessing/FeeTotal/
            // OwedTotal/PaidTotal across active non-WAITLIST/non-DROPPED teams; without
            // this call, the rep row drifts from its teams after a flag flip and
            // downstream callers (e.g. TeamSearchService balance-due gates) read stale
            // totals. Sequential awaits — same scoped DbContext.
            foreach (var clubRepId in affectedClubRepIds)
            {
                await _registrations.SynchronizeClubRepFinancialsAsync(clubRepId, userId);
            }
            if (affectedClubRepIds.Count > 0)
            {
                _logger.LogInformation(
                    "Re-aggregated financials for {RepCount} club rep registration(s)",
                    affectedClubRepIds.Count);
            }
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

    public async Task<string> GetConfirmationTextAsync(Guid registrationId, string userId)
    {
        _logger.LogInformation("Getting confirmation text for registration {RegistrationId}, user {UserId}", registrationId, userId);

        var reg = await _registrations.GetByIdAsync(registrationId);
        if (reg == null)
        {
            _logger.LogWarning("Registration {RegistrationId} not found", registrationId);
            return string.Empty;
        }

        if (reg.UserId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to access registration {RegistrationId} belonging to {OwnerId}", userId, registrationId, reg.UserId);
            return string.Empty;
        }

        var jobInfo = await _jobs.GetAdultConfirmationInfoAsync(reg.JobId);
        if (jobInfo == null || string.IsNullOrWhiteSpace(jobInfo.AdultRegConfirmationOnScreen))
        {
            _logger.LogWarning("No confirmation template found for job {JobId}", reg.JobId);
            return string.Empty;
        }

        try
        {
            // Credit card payment method ID for token substitution
            Guid ccPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

            string substitutedHtml = await _textSubstitution.SubstituteAsync(
                jobSegment: jobInfo.JobPath,
                jobId: jobInfo.JobId,
                paymentMethodCreditCardId: ccPaymentMethodId,
                registrationId: registrationId,
                familyUserId: string.Empty,
                template: jobInfo.AdultRegConfirmationOnScreen);

            return substitutedHtml;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error substituting confirmation template for registration {RegistrationId}", registrationId);
            return string.Empty;
        }
    }

    public async Task SendConfirmationEmailAsync(Guid registrationId, string userId, bool forceResend = false)
    {
        _logger.LogInformation("Sending confirmation email for registration {RegistrationId}, user {UserId}, forceResend {ForceResend}",
            registrationId, userId, forceResend);

        var reg = await _registrations.GetByIdAsync(registrationId);
        if (reg == null)
        {
            _logger.LogWarning("Registration {RegistrationId} not found", registrationId);
            throw new KeyNotFoundException($"Registration {registrationId} not found");
        }

        if (reg.UserId != userId)
        {
            _logger.LogWarning("User {UserId} attempted to send email for registration {RegistrationId} belonging to {OwnerId}",
                userId, registrationId, reg.UserId);
            throw new UnauthorizedAccessException("You do not have permission to send this confirmation email");
        }

        // Check if already sent (unless force resend)
        if (!forceResend && reg.BConfirmationSent)
        {
            _logger.LogInformation("Confirmation email already sent for registration {RegistrationId}, skipping", registrationId);
            return;
        }

        var jobInfo = await _jobs.GetAdultConfirmationEmailInfoAsync(reg.JobId);
        if (jobInfo == null || string.IsNullOrWhiteSpace(jobInfo.AdultRegConfirmationEmail))
        {
            _logger.LogWarning("No email confirmation template found for job {JobId}", reg.JobId);
            throw new InvalidOperationException("Email confirmation template not configured for this event");
        }

        var user = await _users.GetByIdAsync(userId);
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            _logger.LogWarning("User {UserId} not found or has no email address", userId);
            throw new InvalidOperationException("User email address not available");
        }

        try
        {
            // Credit card payment method ID for token substitution
            Guid ccPaymentMethodId = Guid.Parse("30ECA575-A268-E111-9D56-F04DA202060D");

            string emailHtml = await _textSubstitution.SubstituteAsync(
                jobSegment: jobInfo.JobPath,
                jobId: jobInfo.JobId,
                paymentMethodCreditCardId: ccPaymentMethodId,
                registrationId: registrationId,
                familyUserId: string.Empty,
                template: jobInfo.AdultRegConfirmationEmail);

            var emailMessage = new EmailMessageDto
            {
                ToAddresses = new List<string> { user.Email },
                Subject = $"{jobInfo.JobName ?? "Event"} Registration Confirmation",
                HtmlBody = emailHtml
            };

            // Add From/ReplyTo if configured
            if (!string.IsNullOrWhiteSpace(jobInfo.RegFormFrom))
            {
                emailMessage.FromAddress = jobInfo.RegFormFrom;
            }

            // Add CCs/BCCs if configured
            if (!string.IsNullOrWhiteSpace(jobInfo.RegFormCcs))
            {
                emailMessage.CcAddresses = jobInfo.RegFormCcs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            if (!string.IsNullOrWhiteSpace(jobInfo.RegFormBccs))
            {
                emailMessage.BccAddresses = jobInfo.RegFormBccs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            bool emailSent = await _emailService.SendAsync(emailMessage);

            if (emailSent)
            {
                // Update flag
                await _registrations.SetNotificationSentAsync(registrationId, true);
                _logger.LogInformation("Confirmation email sent successfully for registration {RegistrationId}", registrationId);
            }
            else
            {
                _logger.LogError("Failed to send confirmation email for registration {RegistrationId}", registrationId);
                throw new InvalidOperationException("Failed to send confirmation email");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending confirmation email for registration {RegistrationId}", registrationId);
            throw;
        }
    }
}
