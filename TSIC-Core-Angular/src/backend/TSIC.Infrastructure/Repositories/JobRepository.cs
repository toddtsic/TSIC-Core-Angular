using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Entities;
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
                BOfferTeamRegsaverInsurance = j.BOfferTeamRegsaverInsurance ?? false
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

    public async Task<bool> GetUsesWaitlistsAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.BUseWaitlists)
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
                && (j.ExpiryUsers == DateTime.MinValue || j.ExpiryUsers > DateTime.UtcNow))
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
        var now = DateTime.UtcNow;
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath == jobPath)
            .Select(j => new Contracts.Dtos.JobPulseDto
            {
                PlayerRegistrationOpen = j.BRegistrationAllowPlayer == true,
                // Mirrors TeamRepository.GetAvailableTeamsQueryResultsAsync filters exactly.
                PlayerTeamsAvailableForRegistration = _context.Teams.Any(t =>
                    t.JobId == j.JobId
                    && (t.Active ?? true)
                    && ((t.BAllowSelfRostering ?? false) || (t.Agegroup.BAllowSelfRostering ?? false))
                    && (t.Effectiveasofdate == null || t.Effectiveasofdate <= now)
                    && (t.Expireondate == null || t.Expireondate >= now)
                    && !t.Agegroup.AgegroupName.StartsWith("Dropped")
                    && !t.Agegroup.AgegroupName.StartsWith("Waitlist")),
                PlayerRegRequiresToken = j.BplayerRegRequiresToken == true,
                // Team reg only meaningful for Tournament (2) and League (3) job types
                TeamRegistrationOpen = j.BRegistrationAllowTeam == true
                    && (j.JobTypeId == 2 || j.JobTypeId == 3),
                TeamRegRequiresToken = j.BteamRegRequiresToken,
                ClubRepAllowAdd = j.BClubRepAllowAdd == true,
                ClubRepAllowEdit = j.BClubRepAllowEdit == true,
                ClubRepAllowDelete = j.BClubRepAllowDelete == true,
                AllowRosterViewPlayer = j.BAllowRosterViewPlayer == true,
                AllowRosterViewAdult = j.BAllowRosterViewAdult == true,
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
                PublicSuspended = j.BSuspendPublic,
                RegistrationExpiry = j.ExpiryUsers
            })
            .FirstOrDefaultAsync(cancellationToken);
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

    public async Task<JobTeamCapabilities?> GetTeamCapabilitiesAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobTeamCapabilities
            {
                TeamRegistrationOpen = j.BRegistrationAllowTeam == true
                    && (j.JobTypeId == 2 || j.JobTypeId == 3),
                ClubRepAllowAdd = j.BClubRepAllowAdd == true,
                ClubRepAllowEdit = j.BClubRepAllowEdit == true,
                ClubRepAllowDelete = j.BClubRepAllowDelete == true
            })
            .FirstOrDefaultAsync(cancellationToken);
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
        var now = DateTime.UtcNow;
        return await _context.Jobs.AsNoTracking()
            .Where(j => j.ExpiryUsers >= now && !j.BSuspendPublic && j.BScheduleAllowPublicAccess == true)
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

        // Event-local "now" computed from UTC and the event offset (positive hours west of UTC).
        // Mirrors the mobile client's getNow() but without assuming the server is in Arizona.
        int eventOffset = gcParams.UtcoffsetHours ?? 0;
        var now = DateTime.UtcNow.AddHours(-eventOffset);

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
