using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Dtos.Usage;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Domain.JobRules;
using TSIC.Domain.UsLax;
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

    public async Task<UsLaxJobValidationContext?> GetUsLaxValidationContextAsync(
        string jobPath, Guid? teamId, CancellationToken cancellationToken = default)
    {
        var job = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath == jobPath)
            .Select(j => new { j.JobId, j.UslaxNumberValidThroughDate })
            .FirstOrDefaultAsync(cancellationToken);

        if (job is null) return null;

        // Sequential, never Task.WhenAll — same scoped DbContext.
        var bypass = false;
        if (teamId.HasValue)
        {
            // Scoped to THIS job: the teamId arrives on an anonymous request, so a bypass flag must
            // not be borrowed from some other job's team to switch validation off here.
            bypass = await _context.Teams
                .AsNoTracking()
                .Where(t => t.TeamId == teamId.Value && t.JobId == job.JobId)
                .Select(t => t.BDoNotValidateUslaxNumber ?? false)
                .FirstOrDefaultAsync(cancellationToken);
        }

        return new UsLaxJobValidationContext
        {
            JobId = job.JobId,
            ValidThrough = job.UslaxNumberValidThroughDate,
            TeamValidationDisabled = bypass
        };
    }

    public async Task<DateTime?> GetUsLaxValidThroughAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.UslaxNumberValidThroughDate)
            .FirstOrDefaultAsync(cancellationToken);
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
                AllowPif = j.CoreRegformPlayer != null && j.CoreRegformPlayer.Contains("ALLOWPIF")
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

    public async Task<IReadOnlyList<JobPathIdDto>> GetJobIdsByPathsAsync(
        IReadOnlyCollection<string> jobPaths,
        CancellationToken cancellationToken = default)
    {
        if (jobPaths.Count == 0) return [];

        // NO EF.Functions.Collate here, unlike the single-path lookup above. Two
        // reasons, in order:
        //
        //   1. Case-insensitivity is already guaranteed by the column. Jobs.JobPath is
        //      varchar(80) collated SQL_Latin1_General_CP1_CI_AS -- CI, and the same as
        //      the database default -- so a plain comparison matches case-insensitively
        //      on its own. Checked against the query plan: forcing the collation changes
        //      nothing, the optimizer discards it and seeks UI_JOBPATH either way.
        //   2. Collate INSIDE a Contains is a translation this cannot afford to get
        //      wrong. A failure to translate throws at query time, and every failure on
        //      this path is swallowed into a discarded batch -- so it would surface as
        //      silently missing telemetry, not as an error anyone sees. A plain
        //      Contains always translates to IN.
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath != null && jobPaths.Contains(j.JobPath))
            .Select(j => new JobPathIdDto
            {
                JobPath = j.JobPath!,
                JobId = j.JobId,
            })
            .ToListAsync(cancellationToken);
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
            .Select(j => new { j.JobId, j.JobName, j.DisplayName, j.JobPath, j.AdnArb, j.PlayerRegConfirmationEmail, j.UslaxNumberValidThroughDate, j.RegFormFrom, j.RegFormCcs, j.RegFormBccs, j.BDisallowCcplayerConfirmations })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null
            ? new JobConfirmationEmailInfo
            {
                JobId = result.JobId,
                JobName = result.JobName,
                DisplayName = result.DisplayName,
                JobPath = result.JobPath!,
                AdnArb = result.AdnArb,
                PlayerRegConfirmationEmail = result.PlayerRegConfirmationEmail,
                UsLaxNumberValidThroughDate = result.UslaxNumberValidThroughDate,
                RegFormFrom = result.RegFormFrom,
                RegFormCcs = result.RegFormCcs,
                RegFormBccs = result.RegFormBccs,
                BDisallowCcplayerConfirmations = result.BDisallowCcplayerConfirmations
            }
            : null;
    }

    public async Task<string?> GetAlwaysCopyEmailsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.Alwayscopyemaillist)
            .FirstOrDefaultAsync(cancellationToken);
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
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdultRegConfirmationEmail, j.RegFormFrom, j.RegFormCcs, j.RegFormBccs, j.BDisallowCcplayerConfirmations })
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
                RegFormBccs = result.RegFormBccs,
                BDisallowCcplayerConfirmations = result.BDisallowCcplayerConfirmations
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

    public async Task<int?> GetJobTypeIdAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => (int?)j.JobTypeId)
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

    public async Task<bool> GetCustomerUsesAmexAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        // Inner-join to the customer via the required Customer nav; a job with no matching
        // customer row drops out and FirstOrDefault yields false (fail-closed).
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.Customer.BAllowAmex)
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

    public async Task<bool> GetReseedTournamentFlagAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BReseedTournament)
            .FirstOrDefaultAsync(cancellationToken);
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
        // Effective restriction = the director's flag OR "the event is over". Public rosters are a
        // LIVE-EVENT surface: a concluded event closes them on its own, across EVERY consumer of
        // this method (the public tree + team-roster endpoints and the mobile capabilities payload),
        // so a direct URL/API hit dies with the card. Hiding the landing card alone would not do
        // this — the card is a frontend display list and the route would stay open indefinitely.
        //
        // DERIVED, never written. BRestrictPublicRosters keeps the director's real intent, so an
        // early conclusion caused by a wrong EventEndDate is fixable by correcting the date (a
        // written-back flag would be indistinguishable from the director having set it, forever),
        // and the next clone still carries what they actually chose.
        //
        // One query, not two: the four EventConcluded inputs ride along with the flag rather than a
        // second IsEventConcludedAsync round trip. Same Domain predicate the landing phase uses, so
        // "concluded" here can never drift from "concluded" there.
        var inputs = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                j.BRestrictPublicRosters,
                SchedulePublished = j.BScheduleAllowPublicAccess == true,
                LastGameDate = _context.Schedule
                    .Where(s => s.JobId == j.JobId && s.GDate != null)
                    .Max(s => (DateTime?)s.GDate),
                j.EventEndDate,
                j.ExpiryUsers
            })
            .FirstOrDefaultAsync(cancellationToken);

        // Fail CLOSED on an unknown job (this previously returned the bool default = "not
        // restricted", i.e. it failed open on a bad jobId). Rosters are player data; an
        // unresolvable job must never yield one.
        return inputs == null
            || inputs.BRestrictPublicRosters
            || JobLifecycle.EventConcluded(
                inputs.SchedulePublished, inputs.LastGameDate, inputs.EventEndDate, inputs.ExpiryUsers, DateTime.Now);
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
        // Change-Job targets follow the legacy rule: same customer, same Season + Year as the
        // registrant's current job (legacy SearchController filtered exactly this way), plus
        // not user-expired — a registration must never be movable into a dead event.
        var current = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.CustomerId, j.Season, j.Year })
            .FirstOrDefaultAsync(cancellationToken);

        if (current == null || current.CustomerId == Guid.Empty)
            return [];

        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == current.CustomerId
                && j.JobId != jobId
                && j.Season == current.Season
                && j.Year == current.Year
                && (j.ExpiryUsers == DateTime.MinValue || j.ExpiryUsers > DateTime.Now))
            .OrderBy(j => j.JobName)
            .Select(j => new Contracts.Dtos.RegistrationSearch.JobOptionDto
            {
                JobId = j.JobId,
                JobName = j.JobName ?? "(unnamed)"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsValidChangeJobTargetAsync(
        Guid currentJobId, Guid targetJobId, CancellationToken cancellationToken = default)
    {
        return await (
            from cur in _context.Jobs.AsNoTracking()
            from tgt in _context.Jobs.AsNoTracking()
            where cur.JobId == currentJobId
                && tgt.JobId == targetJobId
                && tgt.CustomerId == cur.CustomerId
                && tgt.Season == cur.Season
                && tgt.Year == cur.Year
                && (tgt.ExpiryUsers == DateTime.MinValue || tgt.ExpiryUsers > DateTime.Now)
            select tgt.JobId)
            .AnyAsync(cancellationToken);
    }

    public async Task<List<Contracts.Dtos.RegistrationSearch.JobOptionDto>> GetInviteTargetJobsForCustomerAsync(
        Guid jobId, Contracts.Dtos.RegistrationSearch.InviteRegistrationKind kind, CancellationToken cancellationToken = default)
    {
        var current = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new { j.CustomerId, j.ExpiryUsers })
            .FirstOrDefaultAsync(cancellationToken);

        if (current == null || current.CustomerId == Guid.Empty)
            return [];

        // Invites are a post-event re-engagement tool: while the CURRENT job is still live to
        // users (now < ExpiryUsers, the JobExpiry.NotExpiredForUsers door), offer NO targets.
        // The empty list is what hides the Invite button client-side for both roles.
        if (DateTime.Now < current.ExpiryUsers)
            return [];

        var customerId = current.CustomerId;

        // Any non-expired event under the same customer, excluding the current job. Job type is
        // intentionally NOT required — a director may invite reps/players across event kinds.
        var query = _context.Jobs
            .AsNoTracking()
            .Where(j => j.CustomerId == customerId
                && j.JobId != jobId
                && (j.ExpiryUsers == DateTime.MinValue || j.ExpiryUsers > DateTime.Now));

        // Only the accept-registration flag gates the target — the requires-token flag is
        // intentionally NOT checked (an invite is valid into an open-enrollment event too).
        query = kind == Contracts.Dtos.RegistrationSearch.InviteRegistrationKind.Player
            ? query.Where(j => j.BRegistrationAllowPlayer == true)
            : query.Where(j => j.BRegistrationAllowTeam == true);

        return await query
            .OrderBy(j => j.JobName)
            .Select(j => new Contracts.Dtos.RegistrationSearch.JobOptionDto
            {
                JobId = j.JobId,
                JobName = j.JobName ?? "(unnamed)"
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<List<Contracts.Dtos.AdminExpiry.AdminExpiryCustomerDto>> GetAdminExpiredJobsByCustomerAsync(
        CancellationToken cancellationToken = default)
    {
        // One flat SQL pass over expired jobs + owning customer; the per-customer
        // grouping is cheap and done in memory (result set is small by definition).
        var expired = await _context.Jobs
            .AsNoTracking()
            .Where(JobExpiry.ExpiredForAdmin)
            .Select(j => new
            {
                j.CustomerId,
                j.Customer.CustomerName,
                j.JobId,
                j.JobName,
                j.ExpiryAdmin
            })
            .ToListAsync(cancellationToken);

        return expired
            .GroupBy(x => new { x.CustomerId, x.CustomerName })
            .OrderBy(g => g.Key.CustomerName)
            .Select(g => new Contracts.Dtos.AdminExpiry.AdminExpiryCustomerDto
            {
                CustomerId = g.Key.CustomerId,
                CustomerName = g.Key.CustomerName ?? "(unnamed)",
                Jobs = g.OrderBy(x => x.JobName)
                    .Select(x => new Contracts.Dtos.AdminExpiry.AdminExpiryJobDto
                    {
                        JobId = x.JobId,
                        JobName = x.JobName ?? "(unnamed)",
                        ExpiryAdmin = x.ExpiryAdmin
                    })
                    .ToList()
            })
            .ToList();
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
                    // Toggle ONLY here. The fee clause and the create door are ANDed in the
                    // step-3 fold via RegistrationReadiness.Compose, which is the same function
                    // the admin readiness readout composes with — one evaluation, two consumers,
                    // so the screen that explains this field cannot drift from the field.
                    PlayerRegistrationOpen = j.BRegistrationAllowPlayer == true,
                    // Team availability comes from the shared snapshot below (one query, one
                    // copy of the self-roster rule) — not an inline subquery per field.
                    PlayerTeamsAvailableForRegistration = false,
                    PlayerRegRequiresToken = j.BplayerRegRequiresToken == true,
                    // USLax requirement is a JSON-parse of PlayerProfileMetadataJson, which
                    // EF can't translate — placeholder here, folded in post-materialization
                    // (UsLaxMetadataPolicy.RequiresUsLax). The valid-through date is a plain
                    // column, so it rides the projection directly.
                    PlayerRegRequiresUsLax = false,
                    UsLaxMembershipValidThrough = j.UslaxNumberValidThroughDate,
                    // Toggle ONLY, as with the player flag above — fees + door are ANDed in the
                    // step-3 fold through RegistrationReadiness.Compose.
                    TeamRegistrationOpen = j.BRegistrationAllowTeam == true,
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
                    // Gate on the three adult availability toggles, NOT on AdultProfileMetadataJson:
                    // an adult form is DERIVED from RegformName_Coach, so the blob is empty on every
                    // job and this predicate was false everywhere — the three adult CTA resolvers
                    // could never emit a button.
                    AdultRegistrationPlanned = j.BRegistrationAllowStaff == true
                        || j.BRegistrationAllowReferee == true
                        || j.BRegistrationAllowRecruiter == true,
                    // Toggle ONLY, as with the player and team flags above. The teams clause
                    // is ANDed in the step-4 fold from the team snapshot, through
                    // AdultTeamPlacementAvailability — the same expression the coach picker
                    // filters on. It used to be an inline `Teams.Any(t => t.JobId == j.JobId)`
                    // here, which asked a LAXER question than the picker answered: a job whose
                    // only team had been dropped passed the gate and emptied the picker, so
                    // "Register Coach" led into a wizard that could never be submitted (AR-054).
                    StaffRegistrationOpen = j.BRegistrationAllowStaff == true,
                    // Referee/recruiter need no teams — gate on their flag alone.
                    RefereeRegistrationOpen = j.BRegistrationAllowReferee == true,
                    RecruiterRegistrationOpen = j.BRegistrationAllowRecruiter == true,
                    PublicSuspended = j.BSuspendPublic,
                    RegistrationExpiry = j.ExpiryUsers,
                    // Countdown dates come from the shared team-availability snapshot too — they
                    // are cuts of the SAME eligible-team set, and were three hand-copies of one
                    // filter (available / closing-soonest / opening-soonest) before it moved.
                    PlayerRegClosesSoonest = null,
                    PlayerRegOpensSoonest = null,
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
                // Fee configuration rides the OUTER shape, not the pulse: it is an input to the
                // registration verdicts (a role with no JobFees row can't be priced, so
                // FeeResolutionService would throw and the card would dead-end), but it is not
                // itself a public fact. Keeping it here lets RegistrationReadiness.Compose do
                // the ANDing without widening the pulse payload.
                PlayerFeesConfigured = _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == playerRoleId),
                TeamFeesConfigured = _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == clubRepRoleId),
                // Pulled for the post-projection USLax-requirement parse (can't run in SQL).
                j.PlayerProfileMetadataJson
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row == null) return null;

        var pulse = row.Pulse;

        // "isNumeric(Year)" guard — only a cleanly-parseable event year feeds the residual
        // new-vs-concluded discriminator; messy/null Years are simply skipped (signal absent).
        var eventYear = int.TryParse(row.Year?.Trim(), out var parsedYear) ? parsedYear : (int?)null;

        // Step 2: supersession check (shared with the admin readiness readout — same series
        // heuristic, so the page that explains supersession agrees with the page that applies it).
        var supersedingEvent = await FindSupersedingEventAsync(
            row.JobId, row.JobName, row.CustomerId, now, cancellationToken);
        if (supersedingEvent is not null)
            pulse = pulse with { SupersededByLaterEvent = supersedingEvent };

        // Step 3: team-availability snapshot — ONE query answering the self-roster question
        // (and its two countdown cuts) through TeamSelfRosterAvailability, the single definition
        // shared with the player wizard's team list and the admin readiness readout.
        var teams = await GetTeamAvailabilitySnapshotAsync(row.JobId, now, cancellationToken);

        // Step 4: compose the registration verdicts. RegistrationReadiness.Compose owns the
        // composition — toggle AND fees AND the create door (NOT concluded AND NOT superseded),
        // with eventConcluded coming from the SAME JobLifecycle predicate the write authority
        // enforces, so a disabled control and a refused write can never disagree. The admin
        // "why isn't this showing?" readout calls the same function over the same facts: it
        // explains this pulse rather than re-deriving a second opinion of it.
        var verdicts = TSIC.Domain.JobRules.RegistrationReadiness.Compose(
            new TSIC.Domain.JobRules.RegistrationReadiness.CoreFacts
            {
                SchedulePublished = pulse.SchedulePublished,
                LastGameDate = pulse.LastGameDate,
                EventEndDate = pulse.EventEndDate,
                ExpiryUsers = pulse.RegistrationExpiry ?? DateTime.MaxValue,
                Superseded = pulse.SupersededByLaterEvent is not null,
                PlayerToggleOn = pulse.PlayerRegistrationOpen,   // projection carried the toggle only
                PlayerFeesConfigured = row.PlayerFeesConfigured,
                PlayerTeamsAvailable = teams.AvailableNow > 0,
                TeamToggleOn = pulse.TeamRegistrationOpen,       // ditto
                TeamFeesConfigured = row.TeamFeesConfigured,
            },
            now);

        // The remaining CREATE fields are ANDed with the same door. Manage-existing fields
        // (ClubRepAllowEdit) and SETTLE/display fields are left untouched — create-freeze,
        // not full-CRUD freeze.
        var door = verdicts.DoorOpen;

        return pulse with
        {
            EventYear = eventYear,
            EventConcluded = verdicts.EventConcluded,
            // Pure profile fact — independent of the create door. The bulletin ANDs it
            // with reg-open client-side; folding the door in here would wrongly drop the
            // notice the instant the event concluded.
            PlayerRegRequiresUsLax = UsLaxMetadataPolicy.RequiresUsLax(row.PlayerProfileMetadataJson),
            PlayerRegistrationOpen = verdicts.PlayerRegistrationOpen,
            PlayerTeamsAvailableForRegistration = verdicts.PlayerTeamsAvailable,
            PlayerRegClosesSoonest = teams.ClosesSoonest,
            PlayerRegOpensSoonest = teams.OpensSoonest,
            TeamRegistrationOpen = verdicts.TeamRegistrationOpen,
            ClubRepAllowAdd = pulse.ClubRepAllowAdd && door,
            ClubRepAllowDelete = pulse.ClubRepAllowDelete && door,
            // Toggle AND a team a coach can actually be placed on AND the create door. The
            // middle clause is the AR-054 fix: without it the link rendered for a job whose
            // only team was dropped, and the wizard behind it could never be submitted.
            StaffRegistrationOpen = pulse.StaffRegistrationOpen && teams.AdultPlaceable > 0 && door,
            RefereeRegistrationOpen = pulse.RefereeRegistrationOpen && door,
            RecruiterRegistrationOpen = pulse.RecruiterRegistrationOpen && door,
        };
    }

    /// <summary>
    /// Every availability question the pulse and the admin readiness readout ask between them:
    /// is any team open now, when does the soonest one close, when does the next one open, and —
    /// for the readout — how many teams exist versus how many are eligible at all.
    ///
    /// The availability test itself is <see cref="TeamSelfRosterAvailability"/>'s expression,
    /// evaluated in SQL. It is NOT re-tested in memory here: a C# copy of that window rule beside
    /// the EF one is precisely the duplication this refactor removed. The countdown cuts below
    /// are STRICT window edges (a real window, not yet closed / not yet opened) — a genuinely
    /// different question from availability, which honours the zero-width-window exemption. That
    /// asymmetry is inherited verbatim from the three subqueries this replaced, and it is right:
    /// a team with no meaningful window is available but has no date to count down to.
    ///
    /// Sequential awaits — one scoped DbContext, never Task.WhenAll.
    /// </summary>
    private async Task<TeamAvailabilitySnapshot> GetTeamAvailabilitySnapshotAsync(
        Guid jobId, DateTime now, CancellationToken cancellationToken)
    {
        var jobTeams = _context.Teams.AsNoTracking().Where(t => t.JobId == jobId);
        var eligible = jobTeams.Where(TeamSelfRosterAvailability.EligibleIgnoringWindow);

        var teamsTotal = await jobTeams.CountAsync(cancellationToken);
        var eligibleCount = await eligible.CountAsync(cancellationToken);

        // The ADULT cut of the same set: active and out of the system buckets, with no
        // self-rostering flag and no date window (a coach doesn't self-roster). Counted here
        // so the pulse's StaffRegistrationOpen and the coach picker answer one question once.
        var adultPlaceable = await jobTeams.CountAsync(
            AdultTeamPlacementAvailability.Placeable, cancellationToken);
        var availableNow = await eligible.CountAsync(
            TeamSelfRosterAvailability.WindowContains(now), cancellationToken);

        var closesSoonest = await eligible
            .Where(t => (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                        && t.Expireondate != null && t.Expireondate >= now)
            .MinAsync(t => (DateTime?)t.Expireondate, cancellationToken);

        var opensSoonest = await eligible
            .Where(t => t.Effectiveasofdate != null && t.Effectiveasofdate > now
                        && (t.Expireondate == null || t.Expireondate >= now))
            .MinAsync(t => (DateTime?)t.Effectiveasofdate, cancellationToken);

        // Readout-only: "they ALL closed, the last one on …" — the sentence that names the
        // year-shifted clone window as the culprit.
        var latestClose = await eligible
            .Where(t => t.Expireondate != null && t.Expireondate < now)
            .MaxAsync(t => (DateTime?)t.Expireondate, cancellationToken);

        return new TeamAvailabilitySnapshot
        {
            TeamsTotal = teamsTotal,
            EligibleIgnoringWindow = eligibleCount,
            AvailableNow = availableNow,
            AdultPlaceable = adultPlaceable,
            ClosesSoonest = closesSoonest,
            OpensSoonest = opensSoonest,
            LatestClose = latestClose,
        };
    }

    /// <summary>
    /// The later-year sibling that has taken this event's place, if any: same customer, same
    /// series name-prefix, higher year, public, accepting registration, and inside its own
    /// deadline. Null when the name carries no parseable year (the heuristic can't identify
    /// the series) or nothing later exists.
    ///
    /// Shared by the public pulse and the admin readiness readout so a director reading "a later
    /// event replaced this one" is reading the same match that redirected their visitors.
    /// </summary>
    private async Task<Contracts.Dtos.SupersedingEventInfoDto?> FindSupersedingEventAsync(
        Guid jobId, string? jobName, Guid customerId, DateTime now, CancellationToken cancellationToken)
    {
        var current = ParseSeriesNameAndYear(jobName);
        if (current is null) return null;

        // Sibling pool: same customer, not this job, released to public, currently
        // accepting either registration type, and within its own deadline.
        var siblings = await _context.Jobs
            .AsNoTracking()
            .Where(s => s.CustomerId == customerId
                && s.JobId != jobId
                && !s.BSuspendPublic
                && (s.BRegistrationAllowPlayer == true || s.BRegistrationAllowTeam == true)
                && s.ExpiryUsers > now
                && s.JobName != null
                && s.JobPath != null)
            .Select(s => new { s.JobName, s.JobPath })
            .ToListAsync(cancellationToken);

        // Match by stripped-prefix + later year; pick the closest year forward.
        return siblings
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
    }

    /// <summary>
    /// Facts for the admin "why isn't this showing?" readout. Deliberately raw: every predicate
    /// is applied by <see cref="RegistrationReadiness"/>, the same type the public pulse composes
    /// through, so this method cannot form its own opinion about visibility.
    /// </summary>
    public async Task<Contracts.Dtos.JobConfig.RegistrationReadinessFacts?> GetRegistrationReadinessFactsAsync(
        Guid jobId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.Now;
        var playerRoleId = RoleConstants.Player;
        var clubRepRoleId = RoleConstants.ClubRep;

        var job = await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new
            {
                j.JobId,
                j.JobName,
                j.CustomerId,
                j.JobTypeId,
                SchedulePublished = j.BScheduleAllowPublicAccess == true,
                LastGameDate = _context.Schedule
                    .Where(s => s.JobId == j.JobId && s.GDate != null)
                    .Max(s => (DateTime?)s.GDate),
                j.EventEndDate,
                j.ExpiryUsers,
                PlayerToggleOn = j.BRegistrationAllowPlayer == true,
                TeamToggleOn = j.BRegistrationAllowTeam == true,
                PlayerFeesConfigured = _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == playerRoleId),
                TeamFeesConfigured = _context.JobFees.Any(f => f.JobId == j.JobId && f.RoleId == clubRepRoleId),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (job == null) return null;

        // Sequential — one scoped DbContext.
        var superseding = await FindSupersedingEventAsync(
            job.JobId, job.JobName, job.CustomerId, now, cancellationToken);
        var teams = await GetTeamAvailabilitySnapshotAsync(job.JobId, now, cancellationToken);

        return new Contracts.Dtos.JobConfig.RegistrationReadinessFacts
        {
            Core = new RegistrationReadiness.CoreFacts
            {
                SchedulePublished = job.SchedulePublished,
                LastGameDate = job.LastGameDate,
                EventEndDate = job.EventEndDate,
                ExpiryUsers = job.ExpiryUsers,
                Superseded = superseding is not null,
                PlayerToggleOn = job.PlayerToggleOn,
                PlayerFeesConfigured = job.PlayerFeesConfigured,
                PlayerTeamsAvailable = teams.AvailableNow > 0,
                TeamToggleOn = job.TeamToggleOn,
                TeamFeesConfigured = job.TeamFeesConfigured,
            },
            Describe = new RegistrationReadiness.DescribeFacts
            {
                JobTypeId = job.JobTypeId,
                TeamsTotal = teams.TeamsTotal,
                TeamsSelfRosterEligible = teams.EligibleIgnoringWindow,
                TeamsInWindowNow = teams.AvailableNow,
                LatestTeamWindowClose = teams.LatestClose,
                NextTeamWindowOpen = teams.OpensSoonest,
                SupersedingJobName = superseding?.JobName,
            },
        };
    }

    /// <summary>Team-availability counts and window dates for one job at one instant.</summary>
    private sealed record TeamAvailabilitySnapshot
    {
        public required int TeamsTotal { get; init; }
        public required int EligibleIgnoringWindow { get; init; }
        public required int AvailableNow { get; init; }
        /// <summary>Teams a COACH can be placed on — active, not a system bucket, no
        /// self-rostering flag and no date window. Gates StaffRegistrationOpen.</summary>
        public required int AdultPlaceable { get; init; }
        public DateTime? ClosesSoonest { get; init; }
        public DateTime? OpensSoonest { get; init; }
        public DateTime? LatestClose { get; init; }
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
                .Select(t => new { t.OwedTotal, t.ViPolicyId, t.AdnSubscriptionId })
                .ToListAsync(cancellationToken);

            return new Contracts.Dtos.JobPulseUserContext
            {
                ClubRepTeamCount = teams.Count,
                ClubRepTotalOwed = teams.Sum(t => t.OwedTotal ?? 0m),
                // Only the non-ARB teams' balances are payable by hand; an ARB team's
                // OwedTotal stays positive while its subscription drips. Gating the
                // "Pay Balance Due" nudge on the full sum would double-charge them.
                ClubRepNonArbOwed = teams
                    .Where(t => t.AdnSubscriptionId == null)
                    .Sum(t => t.OwedTotal ?? 0m),
                ClubRepHasTeamWithoutRegsaver = teams.Any(t => t.ViPolicyId == null),
                FirstName = nameInfo?.FirstName,
                LastName = nameInfo?.LastName
            };
        }

        var reg = await _context.Registrations
            .AsNoTracking()
            .Where(r => r.RegistrationId == regId)
            .Select(r => new
            {
                r.AssignedTeamId,
                r.OwedTotal,
                r.RegsaverPolicyId,
                r.AdnSubscriptionId,
                r.AdnSubscriptionStatus,
                // One nav hop (Registrations.AssignedTeam → Teams.Agegroup) so the header bar can
                // suppress "View Roster" for a holding-bucket team instead of offering a link that
                // MyRosterService will only deny.
                AssignedAgegroupName = r.AssignedTeam != null ? r.AssignedTeam.Agegroup.AgegroupName : null
            })
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
            AssignedTeamHidesRoster = AgegroupConstants.IsSystemBucket(reg.AssignedAgegroupName),
            RegistrationOwedTotal = reg.OwedTotal,
            HasPurchasedPlayerRegsaver = reg.RegsaverPolicyId != null,
            AdnSubscriptionId = reg.AdnSubscriptionId,
            // Liveness, not id-presence: a canceled/terminated plan stops drafting but leaves its
            // id behind, so an id-only gate hides "Pay Balance Due" from exactly the families who owe.
            HasLiveArbSubscription = ArbSubscriptionStatus.IsLive(reg.AdnSubscriptionId, reg.AdnSubscriptionStatus),
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
                JobPath = j.JobPath,
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
