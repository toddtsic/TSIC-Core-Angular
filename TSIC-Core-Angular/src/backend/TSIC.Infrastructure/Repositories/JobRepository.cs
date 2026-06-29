using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Domain.JobRules;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

/// <summary>
/// Repository for Jobs entity using Entity Framework Core.
/// </summary>
public class JobRepository : IJobRepository
{
    private readonly SqlDbContext _context;

    public JobRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<JobPreSubmitMetadata?> GetPreSubmitMetadataAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobPreSubmitMetadata
            {
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson,
                JsonOptions = j.JsonOptions,
                CoreRegformPlayer = j.CoreRegformPlayer,
                AllowPif = j.CoreRegformPlayer != null && j.CoreRegformPlayer.Contains("ALLOWPIF"),
                BPlayersFullPaymentRequired = j.BPlayersFullPaymentRequired
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobPaymentInfo?> GetJobPaymentInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobPaymentInfo
            {
                AdnArb = j.AdnArb,
                AdnArbbillingOccurences = j.AdnArbbillingOccurences,
                AdnArbintervalLength = j.AdnArbintervalLength,
                AdnArbstartDate = j.AdnArbstartDate,
                AllowPif = j.CoreRegformPlayer != null && j.CoreRegformPlayer.Contains("ALLOWPIF"),
                BPlayersFullPaymentRequired = j.BPlayersFullPaymentRequired,
                BEnableEcheck = j.BEnableEcheck
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobFullPaymentBaseline?> GetFullPaymentBaselineAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobFullPaymentBaseline
            {
                BPlayersFullPaymentRequired = j.BPlayersFullPaymentRequired,
                BTeamsFullPaymentRequired = j.BTeamsFullPaymentRequired ?? false
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobMetadata?> GetJobMetadataAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobMetadata
            {
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson,
                JsonOptions = j.JsonOptions,
                CoreRegformPlayer = j.CoreRegformPlayer
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> GetJobIdByPathAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath != null && EF.Functions.Collate(j.JobPath!, "SQL_Latin1_General_CP1_CI_AS") == jobPath)
            .Select(j => (Guid?)j.JobId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetJobPathAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.JobPath)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<JobRegistrationStatus?> GetRegistrationStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobRegistrationStatus
            {
                BRegistrationAllowPlayer = j.BRegistrationAllowPlayer ?? false,
                BPlayerRegRequiresToken = j.BplayerRegRequiresToken,
                BRegistrationAllowTeam = j.BRegistrationAllowTeam ?? false,
                BTeamRegRequiresToken = j.BteamRegRequiresToken,
                ExpiryUsers = j.ExpiryUsers
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsJobExpiredForUsersAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Canonical expiry signal: a job is expired for non-admin users once Now reaches ExpiryUsers.
        // Fail closed — an unknown jobId is treated as expired so callers never open writes on it.
        var expiryUsers = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (DateTime?)j.ExpiryUsers)
            .FirstOrDefaultAsync(cancellationToken);

        return expiryUsers == null || DateTime.Now >= expiryUsers.Value;
    }

    public async Task<bool> IsEventConcludedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Load only the four EventConcluded inputs; apply the single shared Domain predicate.
        var inputs = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                SchedulePublished = j.BScheduleAllowPublicAccess == true,
                LastGameDate = _context.Schedule
                    .Where(s => s.JobId == j.JobId && s.GDate != null)
                    .Max(s => (DateTime?)s.GDate),
                j.EventEndDate,
                j.ExpiryUsers
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Fail closed: unknown job → treat as concluded so callers never open a create on it.
        return inputs == null || JobLifecycle.EventConcluded(
            inputs.SchedulePublished, inputs.LastGameDate, inputs.EventEndDate, inputs.ExpiryUsers, DateTime.Now);
    }

    public async Task<JobMetadataDto?> GetJobMetadataByPathAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        return await _context.JobDisplayOptions
            .AsNoTracking()
            .Where(jdo => jdo.Job.JobPath == jobPath)
            .Select(jdo => new JobMetadataDto
            {
                JobId = jdo.Job.JobId,
                JobName = jdo.Job.JobName ?? string.Empty,
                JobPath = jdo.Job.JobPath ?? string.Empty,
                JobLogoPath = jdo.LogoHeader,
                JobBannerPath = jdo.ParallaxSlide1Image,
                JobBannerText1 = jdo.ParallaxSlide1Text1,
                JobBannerText2 = jdo.ParallaxSlide1Text2,
                JobBannerBackgroundPath = jdo.ParallaxBackgroundImage,
                CoreRegformPlayer = jdo.Job.CoreRegformPlayer == "1",
                CoreRegformPlayerRaw = jdo.Job.CoreRegformPlayer,
                USLaxNumberValidThroughDate = jdo.Job.UslaxNumberValidThroughDate,
                ExpiryUsers = jdo.Job.ExpiryUsers,
                PlayerProfileMetadataJson = jdo.Job.PlayerProfileMetadataJson,
                JsonOptions = jdo.Job.JsonOptions,
                MomLabel = jdo.Job.MomLabel,
                DadLabel = jdo.Job.DadLabel,
                PlayerRegReleaseOfLiability = jdo.Job.PlayerRegReleaseOfLiability,
                PlayerRegCodeOfConduct = jdo.Job.PlayerRegCodeOfConduct,
                PlayerRegCovid19Waiver = jdo.Job.PlayerRegCovid19Waiver,
                PlayerRegRefundPolicy = jdo.Job.PlayerRegRefundPolicy,
                OfferPlayerRegsaverInsurance = jdo.Job.BOfferPlayerRegsaverInsurance ?? false,
                BOfferTeamRegsaverInsurance = jdo.Job.BOfferTeamRegsaverInsurance ?? false,
                AdnArb = jdo.Job.AdnArb,
                AdnArbBillingOccurences = jdo.Job.AdnArbbillingOccurences,
                AdnArbIntervalLength = jdo.Job.AdnArbintervalLength,
                AdnArbStartDate = jdo.Job.AdnArbstartDate,
                BRegistrationAllowPlayer = jdo.Job.BRegistrationAllowPlayer ?? false,
                BRegistrationAllowTeam = jdo.Job.BRegistrationAllowTeam ?? false,
                BEnableStore = jdo.Job.BEnableStore ?? false,
                BScheduleAllowPublicAccess = jdo.Job.BScheduleAllowPublicAccess ?? false,
                BBannerIsCustom = jdo.ParallaxSlideCount > 0,
                JobTypeName = jdo.Job.JobType.JobTypeName,
                JobTypeId = jdo.Job.JobTypeId,
                SportName = jdo.Job.Sport.SportName,
                PaymentMethodsAllowedCode = jdo.Job.PaymentMethodsAllowedCode,
                BAddProcessingFees = jdo.Job.BAddProcessingFees,
                PayTo = jdo.Job.PayTo,
                MailTo = jdo.Job.MailTo,
                MailinPaymentWarning = jdo.Job.MailinPaymentWarning,
                AllowPif = jdo.Job.CoreRegformPlayer != null && jdo.Job.CoreRegformPlayer.Contains("ALLOWPIF"),
                BPlayersFullPaymentRequired = jdo.Job.BPlayersFullPaymentRequired,
                BIncludePlayerDonation = jdo.Job.BIncludePlayerDonation,
                BIncludeTeamDonation = jdo.Job.BIncludeTeamDonation,
                BEnableEcheck = jdo.Job.BEnableEcheck
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<InsuranceOfferInfo?> GetInsuranceOfferInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new InsuranceOfferInfo
            {
                JobName = j.JobName,
                BOfferPlayerRegsaverInsurance = j.BOfferPlayerRegsaverInsurance ?? false,
                BOfferTeamRegsaverInsurance = j.BOfferTeamRegsaverInsurance ?? false,
                EventStartDate = j.EventStartDate,
                EventEndDate = j.EventEndDate
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobConfirmationInfo?> GetConfirmationInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdnArb, j.PlayerRegConfirmationOnScreen })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null
            ? new JobConfirmationInfo
            {
                JobId = result.JobId,
                JobName = result.JobName,
                JobPath = result.JobPath!,
                AdnArb = result.AdnArb,
                PlayerRegConfirmationOnScreen = result.PlayerRegConfirmationOnScreen
            }
            : null;
    }

    public async Task<JobConfirmationEmailInfo?> GetConfirmationEmailInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdnArb, j.PlayerRegConfirmationEmail, j.UslaxNumberValidThroughDate })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null
            ? new JobConfirmationEmailInfo
            {
                JobId = result.JobId,
                JobName = result.JobName,
                JobPath = result.JobPath!,
                AdnArb = result.AdnArb,
                PlayerRegConfirmationEmail = result.PlayerRegConfirmationEmail,
                UsLaxNumberValidThroughDate = result.UslaxNumberValidThroughDate
            }
            : null;
    }

    public async Task<AdultConfirmationInfo?> GetAdultConfirmationInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdultRegConfirmationOnScreen, j.RegFormFrom, j.RegFormCcs, j.RegFormBccs })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null
            ? new AdultConfirmationInfo
            {
                JobId = result.JobId,
                JobName = result.JobName,
                JobPath = result.JobPath!,
                AdultRegConfirmationOnScreen = result.AdultRegConfirmationOnScreen,
                RegFormFrom = result.RegFormFrom,
                RegFormCcs = result.RegFormCcs,
                RegFormBccs = result.RegFormBccs
            }
            : null;
    }

    public async Task<AdultConfirmationEmailInfo?> GetAdultConfirmationEmailInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdultRegConfirmationEmail, j.RegFormFrom, j.RegFormCcs, j.RegFormBccs })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null
            ? new AdultConfirmationEmailInfo
            {
                JobId = result.JobId,
                JobName = result.JobName,
                JobPath = result.JobPath!,
                AdultRegConfirmationEmail = result.AdultRegConfirmationEmail,
                RegFormFrom = result.RegFormFrom,
                RegFormCcs = result.RegFormCcs,
                RegFormBccs = result.RegFormBccs
            }
            : null;
    }

    public async Task<JobAuthInfo?> GetJobAuthInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var result = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.JobId, j.JobPath, LogoHeader = j.JobDisplayOptions != null ? j.JobDisplayOptions.LogoHeader : null })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null
            ? new JobAuthInfo
            {
                JobId = result.JobId,
                JobPath = result.JobPath!,
                LogoHeader = result.LogoHeader
            }
            : null;
    }

    public async Task<JobFeeSettings?> GetJobFeeSettingsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobFeeSettings
            {
                BTeamsFullPaymentRequired = j.BTeamsFullPaymentRequired,
                BAddProcessingFees = j.BAddProcessingFees,
                BApplyProcessingFeesToTeamDeposit = j.BApplyProcessingFeesToTeamDeposit,
                PaymentMethodsAllowedCode = j.PaymentMethodsAllowedCode,
                PlayerRegRefundPolicy = j.PlayerRegRefundPolicy,
                Season = j.Season ?? "",
                PayTo = j.PayTo,
                MailTo = j.MailTo,
                MailinPaymentWarning = j.MailinPaymentWarning,
                BEnableEcheck = j.BEnableEcheck,
                BIncludeTeamDonation = j.BIncludeTeamDonation,
                AdnArbTrial = j.AdnArbtrial,
                AdnArbStartDate = j.AdnArbstartDate,
                AdnStartDateAfterTrial = j.AdnStartDateAfterTrial,
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetJobSeasonAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.Season)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<JobSeasonYear?> GetJobSeasonYearAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobSeasonYear { Season = j.Season, Year = j.Year })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<string?> GetJobNameAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.JobName)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Guid?> GetCustomerIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<bool> GetUsesWaitlistsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Waitlists are now MANDATORY for every job (player + team registration alike), so a
        // full team always routes to its WAITLIST twin rather than hard-stopping. The old
        // per-job opt-in (Jobs.bUseWaitlists) is retired; the column is left in place as a
        // vestigial always-true and is no longer read. Returns a constant so both consumers —
        // TeamPlacementService.MintWaitlistMirrorAsync and TeamLookupService's full-team→twin
        // swap — are unconditionally on.
        return Task.FromResult(true);
    }

    public async Task<bool> IsPublicAccessEnabledAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BScheduleAllowPublicAccess == true)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsPublicRostersRestrictedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BRestrictPublicRosters)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<decimal?> GetProcessingFeePercentAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.ProcessingFeePercent)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<decimal?> GetEcprocessingFeePercentAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.EcprocessingFeePercent)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<Contracts.Dtos.RegistrationSearch.JobOptionDto>> GetOtherJobsForCustomerAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var customerId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (customerId == Guid.Empty)
            return [];

        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == customerId && j.JobId != jobId)
            .OrderBy(j => j.JobName)
            .Select(j => new Contracts.Dtos.RegistrationSearch.JobOptionDto
            {
                JobId = j.JobId,
                JobName = j.JobName ?? "(unnamed)"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Contracts.Dtos.RegistrationSearch.JobOptionDto>> GetFutureJobsForCustomerAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var customerId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (customerId == Guid.Empty)
            return [];

        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == customerId
                && j.JobId != jobId
                && (j.ExpiryUsers == DateTime.MinValue || j.ExpiryUsers > DateTime.Now))
            .OrderBy(j => j.JobName)
            .Select(j => new Contracts.Dtos.RegistrationSearch.JobOptionDto
            {
                JobId = j.JobId,
                JobName = j.JobName ?? "(unnamed)"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Guid>> GetCustomerJobIdsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        var customerId = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.CustomerId)
            .FirstOrDefaultAsync(cancellationToken);

        if (customerId == Guid.Empty)
            return [];

        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == customerId)
            .Select(j => j.JobId)
            .ToListAsync(cancellationToken);
    }

    public async Task<Contracts.Dtos.JobPulseDto?> GetJobPulseAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var playerRoleId = RoleConstants.Player;      // player fees live under the Player role
        var clubRepRoleId = RoleConstants.ClubRep;   // team fees live under the ClubRep role

        // Step 1: pulse fields + identity (CustomerId, JobName, JobId) for the supersession check.
        var row = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath == jobPath)
            .Select(j => new
            {
                Pulse = new Contracts.Dtos.JobPulseDto
                {
                    // Fee-driven, symmetric with the team gate below: player reg is only
                    // open when Player fees are configured so the registration can be priced
                    // (a $0 row counts — "configured" means a JobFees row exists, not amount > 0;
                    // no row at all → FeeResolutionService throws, so the card would dead-end).
                    PlayerRegistrationOpen = j.BRegistrationAllowPlayer == true
                        && _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == playerRoleId),
                    // Mirrors TeamRepository.GetAvailableTeamsQueryResultsAsync — same window rule:
                    // a real window must contain 'now'; a null/zero-width/sub-second window is
                    // meaningless, so availability rests on Active + agegroup alone.
                    PlayerTeamsAvailableForRegistration = _context.Teams.Any(t =>
                        t.JobId == j.JobId
                        && (t.Active ?? true)
                        && ((t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
                        && (t.Effectiveasofdate == null
                            || t.Expireondate == null
                            || t.Expireondate <= t.Effectiveasofdate.Value.AddSeconds(1)
                            || (t.Effectiveasofdate <= now && t.Expireondate >= now))
                        && !(t.Agegroup.AgegroupName ?? "").StartsWith("Dropped")
                        && !(t.Agegroup.AgegroupName ?? "").StartsWith("Waitlist")),
                    PlayerRegRequiresToken = j.BplayerRegRequiresToken == true,
                    // USLax requirement is a JSON-parse of PlayerProfileMetadataJson, which
                    // EF can't translate — placeholder here, folded in post-materialization
                    // (see MetadataRequiresUsLax below). The valid-through date is a plain
                    // column, so it rides the projection directly.
                    PlayerRegRequiresUsLax = false,
                    UsLaxMembershipValidThrough = j.UslaxNumberValidThroughDate,
                    // Team reg relevance is fee-driven, not job-type-driven: it's only
                    // meaningful when ClubRep (team) fees are configured so the registration
                    // can be priced. Mirrors the Quick Links editor's TeamFeesConfigured gate.
                    TeamRegistrationOpen = j.BRegistrationAllowTeam == true
                        && _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == clubRepRoleId),
                    TeamRegRequiresToken = j.BteamRegRequiresToken,
                    ClubRepAllowAdd = j.BClubRepAllowAdd == true,
                    ClubRepAllowEdit = j.BClubRepAllowEdit == true,
                    ClubRepAllowDelete = j.BClubRepAllowDelete == true,
                    AllowRosterViewPlayer = j.BAllowRosterViewPlayer == true,
                    AllowRosterViewAdult = j.BAllowRosterViewAdult == true,
                    // Public rosters gate ONLY on bRestrictPublicRosters (the AllowRosterView*
                    // flags above are the logged-in user's OWN-roster gates). Drives the
                    // public hero "Rosters" card.
                    PublicRostersAvailable = j.BRestrictPublicRosters != true,
                    OfferPlayerRegsaverInsurance = j.BOfferPlayerRegsaverInsurance == true,
                    OfferTeamRegsaverInsurance = j.BOfferTeamRegsaverInsurance == true,
                    StoreEnabled = j.BEnableStore == true,
                    StoreHasActiveItems = j.BEnableStore == true
                        && _context.Stores.Any(s => s.JobId == j.JobId
                            && _context.StoreItems.Any(si => si.StoreId == s.StoreId && si.Active)),
                    AllowStoreWalkup = j.BAllowStoreWalkup,
                    EnableStayToPlay = j.BenableStp == true,
                    SchedulePublished = j.BScheduleAllowPublicAccess == true,
                    PlayerRegistrationPlanned = j.PlayerProfileMetadataJson != null
                        && j.BRegistrationAllowPlayer != true,
                    AdultRegistrationPlanned = j.AdultProfileMetadataJson != null,
                    // Coach/staff release gate (BRegistrationAllowStaff) AND teams exist —
                    // a coach can only request a team once teams are in. Mirrors the Quick
                    // Links editor's TeamsConfigured relevance.
                    StaffRegistrationOpen = j.BRegistrationAllowStaff == true
                        && _context.Teams.Any(t => t.JobId == j.JobId),
                    // Referee/recruiter need no teams — gate on their flag alone.
                    RefereeRegistrationOpen = j.BRegistrationAllowReferee == true,
                    RecruiterRegistrationOpen = j.BRegistrationAllowRecruiter == true,
                    PublicSuspended = j.BSuspendPublic,
                    RegistrationExpiry = j.ExpiryUsers,
                    // Soonest close among currently-open self-rosterable teams (same
                    // filter as PlayerTeamsAvailableForRegistration). Null → no open
                    // team with a close date; the hero shows no countdown.
                    PlayerRegClosesSoonest = _context.Teams
                        .Where(t => t.JobId == j.JobId
                            && (t.Active ?? true)
                            && ((t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
                            && (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                            && t.Expireondate != null && t.Expireondate >= now
                            && !(t.Agegroup.AgegroupName ?? "").StartsWith("Dropped")
                            && !(t.Agegroup.AgegroupName ?? "").StartsWith("Waitlist"))
                        .Min(t => (DateTime?)t.Expireondate),
                    // Soonest upcoming open among teams not yet in their window.
                    PlayerRegOpensSoonest = _context.Teams
                        .Where(t => t.JobId == j.JobId
                            && (t.Active ?? true)
                            && ((t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
                            && t.Effectiveasofdate != null && t.Effectiveasofdate > now
                            && (t.Expireondate == null || t.Expireondate >= now)
                            && !(t.Agegroup.AgegroupName ?? "").StartsWith("Dropped")
                            && !(t.Agegroup.AgegroupName ?? "").StartsWith("Waitlist"))
                        .Min(t => (DateTime?)t.Effectiveasofdate),
                    // Factual event bounds from the published schedule (day-granular).
                    // The hero derives "in season" / "concluded" from these vs now,
                    // so a director toggle left on after the last game can't keep the
                    // event looking live. Null when no games are scheduled.
                    FirstGameDate = _context.Schedule
                        .Where(s => s.JobId == j.JobId && s.GDate != null)
                        .Min(s => (DateTime?)s.GDate),
                    LastGameDate = _context.Schedule
                        .Where(s => s.JobId == j.JobId && s.GDate != null)
                        .Max(s => (DateTime?)s.GDate),
                    EventStartDate = j.EventStartDate,
                    EventEndDate = j.EventEndDate,
                    EventConcluded = false, // computed post-projection (see door fold below)
                    // Any active non-admin participant (excluding admins + store-purchase shells).
                    // The residual new-vs-concluded discriminator: a finished event has real
                    // registrants, a brand-new one has none. Display-only (derivePhase tail).
                    HasNonAdminActivity = _context.Registrations.Any(r =>
                        r.JobId == j.JobId
                        && r.BActive == true
                        && r.RoleId != RoleConstants.Superuser
                        && r.RoleId != RoleConstants.Director
                        && r.RoleId != RoleConstants.SuperDirector
                        && r.RegistrationCategory != "Store Purchase"),
                    SupersededByLaterEvent = null
                },
                j.JobId,
                j.JobName,
                j.CustomerId,
                j.Year,
                // Pulled for the post-projection USLax-requirement parse (can't run in SQL).
                j.PlayerProfileMetadataJson
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null) return null;

        var pulse = row.Pulse;

        // "isNumeric(Year)" guard — only a cleanly-parseable event year feeds the residual
        // new-vs-concluded discriminator; messy/null Years are simply skipped (signal absent).
        var eventYear = int.TryParse(row.Year?.Trim(), out var parsedYear) ? parsedYear : (int?)null;

        // Step 2: supersession check. Only meaningful when the current name parses
        // to year + prefix; otherwise we can't identify the series.
        var current = ParseSeriesNameAndYear(row.JobName);
        if (current is not null)
        {
            // Sibling pool: same customer, not this job, released to public, currently
            // accepting either registration type, and within its own deadline.
            var siblings = await _context.Jobs
                .AsNoTracking()
                .Where(s => s.CustomerId == row.CustomerId
                    && s.JobId != row.JobId
                    && !s.BSuspendPublic
                    && (s.BRegistrationAllowPlayer == true || s.BRegistrationAllowTeam == true)
                    && s.ExpiryUsers > now
                    && s.JobName != null
                    && s.JobPath != null)
                .Select(s => new { s.JobName, s.JobPath })
                .ToListAsync(cancellationToken);

            // Match by stripped-prefix + later year; pick the closest year forward.
            var supersedingEvent = siblings
                .Select(s =>
                {
                    var parsed = ParseSeriesNameAndYear(s.JobName);
                    return parsed is null
                        ? null
                        : new { s.JobName, s.JobPath, parsed.Value.Prefix, parsed.Value.Year };
                })
                .Where(s => s != null
                    && string.Equals(s.Prefix, current.Value.Prefix, StringComparison.OrdinalIgnoreCase)
                    && s.Year > current.Value.Year)
                .OrderBy(s => s!.Year)
                .Select(s => new Contracts.Dtos.SupersedingEventInfoDto
                {
                    JobPath = s!.JobPath!,
                    JobName = s.JobName!
                })
                .FirstOrDefault();

            if (supersedingEvent is not null)
                pulse = pulse with { SupersededByLaterEvent = supersedingEvent };
        }

        // Step 3: fold the create DOOR into the pulse. eventConcluded uses the SAME shared
        // predicate as the write authority (JobLifecycle.EventConcluded over the published
        // lastGameDate → EventEndDate → ExpiryUsers fallback hierarchy), so the disabled control
        // and the refused write can never disagree. door = NOT concluded AND NOT superseded;
        // every CREATE field is ANDed with it. Manage-existing fields (ClubRepAllowEdit) and
        // SETTLE/display fields are left untouched (create-freeze, not full-CRUD freeze).
        var concluded = TSIC.Domain.JobRules.JobLifecycle.EventConcluded(
            pulse.SchedulePublished,
            pulse.LastGameDate,
            pulse.EventEndDate,
            pulse.RegistrationExpiry ?? DateTime.MaxValue,
            now);
        var door = !concluded && pulse.SupersededByLaterEvent is null;

        return pulse with
        {
            EventYear = eventYear,
            EventConcluded = concluded,
            // Pure profile fact — independent of the create door. The bulletin ANDs it
            // with reg-open client-side; folding the door in here would wrongly drop the
            // notice the instant the event concluded.
            PlayerRegRequiresUsLax = MetadataRequiresUsLax(row.PlayerProfileMetadataJson),
            PlayerRegistrationOpen = pulse.PlayerRegistrationOpen && door,
            TeamRegistrationOpen = pulse.TeamRegistrationOpen && door,
            ClubRepAllowAdd = pulse.ClubRepAllowAdd && door,
            ClubRepAllowDelete = pulse.ClubRepAllowDelete && door,
            StaffRegistrationOpen = pulse.StaffRegistrationOpen && door,
            RefereeRegistrationOpen = pulse.RefereeRegistrationOpen && door,
            RecruiterRegistrationOpen = pulse.RecruiterRegistrationOpen && door,
        };
    }

    /// <summary>
    /// True when the player profile (PlayerProfileMetadataJson) has a REQUIRED USA Lacrosse
    /// membership field. Mirrors FormSchemaService's USLax detection exactly: a field whose
    /// name is sportAssnId/uslax (case-insensitive) OR whose label mentions "lacrosse", that
    /// is required either directly (field.required) or via validation.required/requiredTrue.
    /// Malformed/empty metadata → false (fail safe to not-shown).
    /// </summary>
    private static bool MetadataRequiresUsLax(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson)) return false;
        try
        {
            using var doc = JsonDocument.Parse(metadataJson);
            if (!doc.RootElement.TryGetProperty("fields", out var fields) || fields.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var f in fields.EnumerateArray())
            {
                if (f.ValueKind != JsonValueKind.Object) continue;
                var name = GetStringCI(f, "name") ?? string.Empty;
                var label = GetStringCI(f, "label") ?? GetStringCI(f, "displayName") ?? GetStringCI(f, "display") ?? string.Empty;
                var isUsLax = name.Equals("sportAssnId", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("uslax", StringComparison.OrdinalIgnoreCase)
                    || label.Contains("lacrosse", StringComparison.OrdinalIgnoreCase);
                if (isUsLax && IsFieldRequired(f)) return true;
            }
        }
        catch (JsonException)
        {
            // Malformed metadata → no USLax requirement asserted.
        }
        return false;
    }

    private static bool IsFieldRequired(JsonElement f)
    {
        if (ReadBoolCI(f, "required")) return true;
        if (TryGetPropertyCI(f, "validation", out var val) && val.ValueKind == JsonValueKind.Object)
        {
            if (ReadBoolCI(val, "required")) return true;
            if (ReadBoolCI(val, "requiredTrue")) return true;
        }
        return false;
    }

    private static bool ReadBoolCI(JsonElement obj, string name)
    {
        if (!TryGetPropertyCI(obj, name, out var el)) return false;
        return el.ValueKind == JsonValueKind.True
            || (el.ValueKind == JsonValueKind.String && bool.TryParse(el.GetString(), out var b) && b);
    }

    private static string? GetStringCI(JsonElement obj, string name)
        => TryGetPropertyCI(obj, name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

    private static bool TryGetPropertyCI(JsonElement obj, string name, out JsonElement value)
    {
        value = default;
        if (obj.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in obj.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Parses a job name like "Lax For The Cure: Summer 2026" into the series prefix
    /// ("Lax For The Cure: Summer") and the year (2026). Returns null when no 4-digit
    /// year is present — those names can't participate in the supersession heuristic.
    /// </summary>
    private static (string Prefix, int Year)? ParseSeriesNameAndYear(string? jobName)
    {
        if (string.IsNullOrWhiteSpace(jobName)) return null;
        var match = Regex.Match(jobName, @"\b(20\d{2})\b");
        if (!match.Success) return null;
        if (!int.TryParse(match.Groups[1].Value, out var year)) return null;
        var stripped = jobName.Remove(match.Index, match.Length);
        // Collapse whitespace and trim so "Lax  Summer" matches "Lax Summer".
        var prefix = Regex.Replace(stripped, @"\s+", " ").Trim();
        return (prefix, year);
    }

    public async Task<List<Contracts.Dtos.SuggestedEventDto>> GetCandidateEventsByCustomersAsync(
        IReadOnlyCollection<Guid> customerIds,
        IReadOnlyCollection<Guid> excludeJobIds,
        Contracts.Dtos.SuggestedEventAudience audience,
        CancellationToken cancellationToken = default)
    {
        if (customerIds.Count == 0) return [];

        var now = DateTime.Now;
        var isFamily = audience == Contracts.Dtos.SuggestedEventAudience.Family;
        return await (
            from j in _context.Jobs
            join c in _context.Customers on j.CustomerId equals c.CustomerId
            join jdo in _context.JobDisplayOptions on j.JobId equals jdo.JobId into jdoGroup
            from jdo in jdoGroup.DefaultIfEmpty()
            where customerIds.Contains(j.CustomerId)
               && !excludeJobIds.Contains(j.JobId)
               && (isFamily ? j.BRegistrationAllowPlayer == true : j.BRegistrationAllowTeam == true)
               && j.ExpiryUsers > now
               && !j.BSuspendPublic
            orderby j.Year descending, j.JobName
            select new Contracts.Dtos.SuggestedEventDto
            {
                JobId = j.JobId,
                JobPath = j.JobPath ?? string.Empty,
                JobName = j.JobName ?? "(unnamed)",
                JobLogo = jdo != null && jdo.LogoHeader != null
                    ? TSIC.Domain.Constants.TsicConstants.BaseUrlStatics + "BannerFiles/" + jdo.LogoHeader
                    : null,
                CustomerName = c.CustomerName ?? string.Empty,
                // Surface only the audience-relevant open flag — the badge in the
                // role-selection modal is meant to call out "this is the channel
                // you can use," not enumerate everything the Job has open.
                PlayerRegistrationOpen = isFamily && j.BRegistrationAllowPlayer == true,
                TeamRegistrationOpen = !isFamily && j.BRegistrationAllowTeam == true,
                StoreOpen = j.BEnableStore == true,
                SchedulePublished = j.BScheduleAllowPublicAccess == true,
                RegistrationExpiry = j.ExpiryUsers
            }
        ).AsNoTracking().ToListAsync(cancellationToken);
    }

    public async Task<Contracts.Dtos.JobPulseUserContext> GetPulseUserContextAsync(
        Guid regId, string role, CancellationToken cancellationToken = default)
    {
        // Name lookup — regId is a Registration row for every role; Registration.UserId
        // points to the AspNetUsers row carrying FirstName/LastName.
        var nameInfo = await (from r in _context.Registrations.AsNoTracking()
                              join u in _context.AspNetUsers.AsNoTracking() on r.UserId equals u.Id
                              where r.RegistrationId == regId
                              select new { u.FirstName, u.LastName })
            .FirstOrDefaultAsync(cancellationToken);

        if (string.Equals(role, "Club Rep", StringComparison.OrdinalIgnoreCase))
        {
            var teams = await _context.Teams
                .AsNoTracking()
                .Where(t => t.ClubrepRegistrationid == regId)
                .Select(t => new { t.OwedTotal, t.ViPolicyId })
                .ToListAsync(cancellationToken);

            return new Contracts.Dtos.JobPulseUserContext
            {
                ClubRepTeamCount = teams.Count,
                ClubRepTotalOwed = teams.Sum(t => t.OwedTotal ?? 0m),
                ClubRepHasTeamWithoutRegsaver = teams.Any(t => t.ViPolicyId == null),
                FirstName = nameInfo?.FirstName,
                LastName = nameInfo?.LastName
            };
        }

        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == regId)
            .Select(r => new { r.AssignedTeamId, r.OwedTotal, r.RegsaverPolicyId, r.AdnSubscriptionId })
            .FirstOrDefaultAsync(cancellationToken);

        if (reg == null)
        {
            return new Contracts.Dtos.JobPulseUserContext
            {
                FirstName = nameInfo?.FirstName,
                LastName = nameInfo?.LastName
            };
        }

        return new Contracts.Dtos.JobPulseUserContext
        {
            AssignedTeamId = reg.AssignedTeamId,
            RegistrationOwedTotal = reg.OwedTotal,
            HasPurchasedPlayerRegsaver = reg.RegsaverPolicyId != null,
            AdnSubscriptionId = reg.AdnSubscriptionId,
            FirstName = nameInfo?.FirstName,
            LastName = nameInfo?.LastName
        };
    }

    public async Task<Contracts.Repositories.JobCapabilityFacts?> GetCapabilityFactsAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var playerRoleId = RoleConstants.Player;
        var clubRepRoleId = RoleConstants.ClubRep;

        // Step 1: flat facts projection (same fee/teams semantics as the pulse) plus the
        // identity columns the supersession heuristic needs.
        var row = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                Facts = new Contracts.Repositories.JobCapabilityFacts
                {
                    // eventConcluded inputs
                    SchedulePublished = j.BScheduleAllowPublicAccess == true,
                    LastGameDate = _context.Schedule
                        .Where(s => s.JobId == j.JobId && s.GDate != null)
                        .Max(s => (DateTime?)s.GDate),
                    EventEndDate = j.EventEndDate,
                    ExpiryUsers = j.ExpiryUsers,
                    SupersededByLaterEvent = false, // filled in Step 2

                    // create toggles
                    AllowPlayer = j.BRegistrationAllowPlayer == true,
                    AllowTeam = j.BRegistrationAllowTeam == true,
                    AllowStaff = j.BRegistrationAllowStaff == true,
                    AllowReferee = j.BRegistrationAllowReferee == true,
                    AllowRecruiter = j.BRegistrationAllowRecruiter == true,
                    ClubRepAllowAdd = j.BClubRepAllowAdd == true,
                    ClubRepAllowEdit = j.BClubRepAllowEdit == true,
                    ClubRepAllowDelete = j.BClubRepAllowDelete == true,

                    // data preconditions (a $0 row still counts — "configured" = a row exists)
                    PlayerFeesConfigured = _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == playerRoleId),
                    ClubRepFeesConfigured = _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == clubRepRoleId),
                    TeamsExist = _context.Teams.Any(t => t.JobId == j.JobId),
                },
                j.JobName,
                j.CustomerId
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null) return null; // unknown job → authority fails closed

        // Step 2: supersession — identical heuristic to the pulse (a live later-year sibling
        // in the same series). Only meaningful when this name parses to prefix + year.
        var current = ParseSeriesNameAndYear(row.JobName);
        if (current is null) return row.Facts;

        var siblings = await _context.Jobs
            .AsNoTracking()
            .Where(s => s.CustomerId == row.CustomerId
                && s.JobId != jobId
                && !s.BSuspendPublic
                && (s.BRegistrationAllowPlayer == true || s.BRegistrationAllowTeam == true)
                && s.ExpiryUsers > now
                && s.JobName != null)
            .Select(s => s.JobName)
            .ToListAsync(cancellationToken);

        var superseded = siblings.Any(name =>
        {
            var parsed = ParseSeriesNameAndYear(name);
            return parsed is not null
                && string.Equals(parsed.Value.Prefix, current.Value.Prefix, StringComparison.OrdinalIgnoreCase)
                && parsed.Value.Year > current.Value.Year;
        });

        return superseded ? row.Facts with { SupersededByLaterEvent = true } : row.Facts;
    }

    public async Task<PriorYearJobInfo?> GetPriorYearJobAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        // Get current job's identity dimensions
        var current = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.CustomerId, j.JobTypeId, j.SportId, j.Season, j.Year })
            .FirstOrDefaultAsync(cancellationToken);

        if (current == null || current.Year == null) return null;

        // Find most recent sibling job with same customer/type/sport/season but earlier year
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == current.CustomerId
                     && j.JobTypeId == current.JobTypeId
                     && j.SportId == current.SportId
                     && j.Season == current.Season
                     && j.Year != null
                     && string.Compare(j.Year, current.Year) < 0)
            .OrderByDescending(j => j.Year)
            .Select(j => new PriorYearJobInfo
            {
                JobId = j.JobId,
                JobName = j.JobName ?? "Unknown",
                Year = j.Year!
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> IsStoreWalkupAllowedAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BAllowStoreWalkup)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<List<EventListingDto>> GetActivePublicEventsAsync(CancellationToken ct = default)
    {
        // Canonical login door (now < ExpiryUsers) — fixes the old `>= now` boundary that left a
        // job expiring at this exact instant still listed; mirrors IsJobExpiredForUsersAsync.
        return await _context.Jobs.AsNoTracking()
            .Where(JobExpiry.NotExpiredForUsers)
            .Where(j => !j.BSuspendPublic && j.BScheduleAllowPublicAccess == true)
            .Select(j => new EventListingDto
            {
                JobId = j.JobId,
                JobName = j.MobileJobName ?? j.JobName ?? "",
                JobLogoUrl = j.JobDisplayOptions != null ? j.JobDisplayOptions.LogoHeader : null,
                City = j.Schedule.Where(s => s.Field != null && s.Field.City != null).Select(s => s.Field!.City).FirstOrDefault(),
                State = j.Schedule.Where(s => s.Field != null && s.Field.State != null).Select(s => s.Field!.State).FirstOrDefault(),
                SportName = j.Sport != null ? j.Sport.SportName : null,
                FirstGameDay = j.Schedule.Where(s => s.GDate != null).Min(s => (DateTime?)s.GDate),
                LastGameDay = j.Schedule.Where(s => s.GDate != null).Max(s => (DateTime?)s.GDate)
            }).OrderBy(e => e.JobName).ToListAsync(ct);
    }

    public async Task<GameClockConfigDto?> GetGameClockConfigAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.GameClockParams.AsNoTracking().Where(gc => gc.JobId == jobId)
            .Select(gc => new GameClockConfigDto
            {
                UtcoffsetHours = gc.UtcoffsetHours,
                HalfMinutes = gc.HalfMinutes,
                HalfTimeMinutes = gc.HalfTimeMinutes,
                QuarterMinutes = gc.QuarterMinutes,
                QuarterTimeMinutes = gc.QuarterTimeMinutes,
                TransitionMinutes = gc.TransitionMinutes,
                PlayoffMinutes = gc.PlayoffMinutes,
                PlayoffHalfMinutes = gc.PlayoffHalfMinutes,
                PlayoffHalfTimeMinutes = gc.PlayoffHalfTimeMinutes
            }).FirstOrDefaultAsync(ct);
    }

    public async Task<GameClockAvailableGameTimesDto> GetActiveGamesAsync(
        Guid jobId, DateTime? preferredGameDate, CancellationToken ct = default)
    {
        // Port of legacy TSIC-Unify-2024 ScheduleService.GetActiveGame — preserve semantics.
        var empty = new GameClockAvailableGameTimesDto
        {
            AvailableRRGameData = Array.Empty<GameClockStartDataDto>(),
            AvailablePOGameData = Array.Empty<GameClockStartDataDto>()
        };

        var gcParams = await _context.GameClockParams.AsNoTracking()
            .Where(gc => gc.JobId == jobId).FirstOrDefaultAsync(ct);
        if (gcParams is null) return empty;

        // RR duration: if quarters configured, 4Q + 2QT + HT + Trans; else 2H + HT + Trans
        decimal rrDuration = (gcParams.QuarterMinutes ?? 0m) > 0m
            ? (4m * (gcParams.QuarterMinutes ?? 0m))
                + (2m * (gcParams.QuarterTimeMinutes ?? 0m))
                + gcParams.HalfTimeMinutes
                + gcParams.TransitionMinutes
            : (2m * gcParams.HalfMinutes) + gcParams.HalfTimeMinutes + gcParams.TransitionMinutes;

        // PO duration: if playoff halves configured, 2PH + PHT + Trans; else PlayoffMinutes + Trans
        decimal poDuration = (gcParams.PlayoffHalfMinutes ?? 0m) > 0m
            ? (2m * (gcParams.PlayoffHalfMinutes ?? 0m))
                + (gcParams.PlayoffHalfTimeMinutes ?? 0m)
                + gcParams.TransitionMinutes
            : gcParams.PlayoffMinutes + gcParams.TransitionMinutes;

        // Event-local "now": match legacy GetActiveGame exactly — derive from server (AZ)
        // local time and the event's UTC offset, NOT from UtcNow. (Identical while the
        // server is in AZ; this mirrors the proven legacy route rather than reinventing it.)
        const int azUtcHoursOffset = 7;
        int eventOffset = gcParams.UtcoffsetHours ?? 0;
        var now = DateTime.Now.AddHours(azUtcHoursOffset - eventOffset);

        var rr = await GetBucketAsync(jobId, now, rrDuration, isRoundRobin: true, ct);
        var po = poDuration > 0m
            ? await GetBucketAsync(jobId, now, poDuration, isRoundRobin: false, ct)
            : (IReadOnlyList<GameClockStartDataDto>)Array.Empty<GameClockStartDataDto>();

        if (preferredGameDate.HasValue)
        {
            rr = rr.Where(g => g.GameStart == preferredGameDate.Value).ToArray();
            po = po.Where(g => g.GameStart == preferredGameDate.Value).ToArray();
        }

        return new GameClockAvailableGameTimesDto
        {
            AvailableRRGameData = rr,
            AvailablePOGameData = po
        };
    }

    private async Task<IReadOnlyList<GameClockStartDataDto>> GetBucketAsync(
        Guid jobId, DateTime now, decimal durationMinutes, bool isRoundRobin, CancellationToken ct)
    {
        var endOffsetMinutes = (double)durationMinutes;

        // Base filter: job + has date + RR vs PO split on T1Type/T2Type
        var baseQuery = _context.Schedule.AsNoTracking()
            .Where(s => s.JobId == jobId && s.GDate != null);

        baseQuery = isRoundRobin
            ? baseQuery.Where(s => s.T1Type == "T" && s.T2Type == "T")
            : baseQuery.Where(s => s.T1Type != "T" && s.T2Type != "T");

        // Active window: now >= GDate AND now < GDate + duration
        var activeDates = await baseQuery
            .Where(s => s.GDate <= now && now < s.GDate!.Value.AddMinutes(endOffsetMinutes))
            .Select(s => s.GDate!.Value)
            .Distinct()
            .OrderBy(d => d)
            .ToListAsync(ct);

        if (activeDates.Count > 0)
        {
            return activeDates.Select(d => new GameClockStartDataDto
            {
                GameStart = d,
                IsRoundRobin = isRoundRobin,
                DurationMinutes = durationMinutes
            }).ToArray();
        }

        // No active — return next single upcoming GDate
        var nextDate = await baseQuery
            .Where(s => s.GDate > now)
            .OrderBy(s => s.GDate)
            .Select(s => s.GDate!.Value)
            .FirstOrDefaultAsync(ct);

        if (nextDate == default)
            return Array.Empty<GameClockStartDataDto>();

        return new[]
        {
            new GameClockStartDataDto
            {
                GameStart = nextDate,
                IsRoundRobin = isRoundRobin,
                DurationMinutes = durationMinutes
            }
        };
    }

    public async Task<List<EventDocDto>> GetJobDocsAsync(Guid jobId, CancellationToken ct = default)
    {
        return await _context.TeamDocs.AsNoTracking().Where(td => td.JobId == jobId).OrderBy(td => td.Label)
            .Select(td => new EventDocDto
            {
                DocId = td.DocId,
                JobId = td.JobId,
                Label = td.Label ?? "",
                DocUrl = td.DocUrl ?? "",
                User = td.User.FirstName + " " + td.User.LastName,
                CreateDate = td.CreateDate
            }).ToListAsync(ct);
    }
}
