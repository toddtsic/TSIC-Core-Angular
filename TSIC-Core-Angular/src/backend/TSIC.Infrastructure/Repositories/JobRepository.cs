using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.JobConfig;
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
                CoreRegformPlayer = j.CoreRegformPlayer
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
                AdnArbstartDate = j.AdnArbstartDate
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

    public async Task<JobRegistrationStatus?> GetRegistrationStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobRegistrationStatus
            {
                BRegistrationAllowPlayer = j.BRegistrationAllowPlayer ?? false,
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
                BRegistrationAllowTeam = jdo.Job.BRegistrationAllowTeam ?? false,
                JobTypeName = jdo.Job.JobType.JobTypeName
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
            .Select(j => new { j.JobId, j.JobName, j.JobPath, j.AdnArb, j.PlayerRegConfirmationEmail })
            .FirstOrDefaultAsync(cancellationToken);

        return result != null
            ? new JobConfirmationEmailInfo
            {
                JobId = result.JobId,
                JobName = result.JobName,
                JobPath = result.JobPath!,
                AdnArb = result.AdnArb,
                PlayerRegConfirmationEmail = result.PlayerRegConfirmationEmail
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
            .Select(j => new { j.JobId, j.JobPath, j.JobDisplayOptions.LogoHeader })
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
                Season = j.Season ?? ""
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

    public async Task<decimal?> GetProcessingFeePercentAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => j.ProcessingFeePercent)
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

    // ── Job Config Editor ──

    public async Task<JobConfigDto?> GetJobConfigAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobConfigDto
            {
                // Identity
                JobId = j.JobId,
                JobPath = j.JobPath,
                UpdatedOn = j.UpdatedOn,
                // General
                JobName = j.JobName,
                DisplayName = j.DisplayName,
                JobDescription = j.JobDescription,
                JobTagline = j.JobTagline,
                JobCode = j.JobCode,
                Year = j.Year,
                Season = j.Season,
                JobTypeId = j.JobTypeId,
                SportId = j.SportId,
                BillingTypeId = j.BillingTypeId,
                ExpiryAdmin = j.ExpiryAdmin,
                ExpiryUsers = j.ExpiryUsers,
                EventStartDate = j.EventStartDate,
                EventEndDate = j.EventEndDate,
                SearchenginKeywords = j.SearchenginKeywords,
                SearchengineDescription = j.SearchengineDescription,
                BBannerIsCustom = j.BBannerIsCustom,
                BannerFile = j.BannerFile,
                MobileJobName = j.MobileJobName,
                JobNameQbp = j.JobNameQbp,
                MomLabel = j.MomLabel,
                DadLabel = j.DadLabel,
                BSuspendPublic = j.BSuspendPublic,
                // Registration
                BRegistrationAllowPlayer = j.BRegistrationAllowPlayer,
                BRegistrationAllowTeam = j.BRegistrationAllowTeam,
                BAllowMobileRegn = j.BAllowMobileRegn,
                BUseWaitlists = j.BUseWaitlists,
                BRestrictPlayerTeamsToAgerange = j.BRestrictPlayerTeamsToAgerange,
                BOfferPlayerRegsaverInsurance = j.BOfferPlayerRegsaverInsurance,
                BOfferTeamRegsaverInsurance = j.BOfferTeamRegsaverInsurance,
                PlayerRegMultiPlayerDiscountMin = j.PlayerRegMultiPlayerDiscountMin,
                PlayerRegMultiPlayerDiscountPercent = j.PlayerRegMultiPlayerDiscountPercent,
                CoreRegformPlayer = j.CoreRegformPlayer,
                RegformNamePlayer = j.RegformNamePlayer,
                RegformNameTeam = j.RegformNameTeam,
                RegformNameCoach = j.RegformNameCoach,
                RegformNameClubRep = j.RegformNameClubRep,
                PlayerProfileMetadataJson = j.PlayerProfileMetadataJson,
                UslaxNumberValidThroughDate = j.UslaxNumberValidThroughDate,
                // Payment
                PaymentMethodsAllowedCode = j.PaymentMethodsAllowedCode,
                BAddProcessingFees = j.BAddProcessingFees,
                ProcessingFeePercent = j.ProcessingFeePercent,
                BApplyProcessingFeesToTeamDeposit = j.BApplyProcessingFeesToTeamDeposit,
                BTeamsFullPaymentRequired = j.BTeamsFullPaymentRequired,
                Balancedueaspercent = j.Balancedueaspercent,
                BAllowRefundsInPriorMonths = j.BAllowRefundsInPriorMonths,
                BAllowCreditAll = j.BAllowCreditAll,
                PayTo = j.PayTo,
                MailTo = j.MailTo,
                MailinPaymentWarning = j.MailinPaymentWarning,
                AdnArb = j.AdnArb,
                AdnArbbillingOccurences = j.AdnArbbillingOccurences,
                AdnArbintervalLength = j.AdnArbintervalLength,
                AdnArbstartDate = j.AdnArbstartDate,
                AdnArbMinimunTotalCharge = j.AdnArbMinimunTotalCharge,
                // Email & Templates
                RegFormFrom = j.RegFormFrom,
                RegFormCcs = j.RegFormCcs,
                RegFormBccs = j.RegFormBccs,
                Rescheduleemaillist = j.Rescheduleemaillist,
                Alwayscopyemaillist = j.Alwayscopyemaillist,
                BDisallowCcplayerConfirmations = j.BDisallowCcplayerConfirmations,
                PlayerRegConfirmationEmail = j.PlayerRegConfirmationEmail,
                PlayerRegConfirmationOnScreen = j.PlayerRegConfirmationOnScreen,
                PlayerRegRefundPolicy = j.PlayerRegRefundPolicy,
                PlayerRegReleaseOfLiability = j.PlayerRegReleaseOfLiability,
                PlayerRegCodeOfConduct = j.PlayerRegCodeOfConduct,
                PlayerRegCovid19Waiver = j.PlayerRegCovid19Waiver,
                AdultRegConfirmationEmail = j.AdultRegConfirmationEmail,
                AdultRegConfirmationOnScreen = j.AdultRegConfirmationOnScreen,
                AdultRegRefundPolicy = j.AdultRegRefundPolicy,
                AdultRegReleaseOfLiability = j.AdultRegReleaseOfLiability,
                AdultRegCodeOfConduct = j.AdultRegCodeOfConduct,
                RefereeRegConfirmationEmail = j.RefereeRegConfirmationEmail,
                RefereeRegConfirmationOnScreen = j.RefereeRegConfirmationOnScreen,
                RecruiterRegConfirmationEmail = j.RecruiterRegConfirmationEmail,
                RecruiterRegConfirmationOnScreen = j.RecruiterRegConfirmationOnScreen,
                // Features & Store
                BClubRepAllowEdit = j.BClubRepAllowEdit,
                BClubRepAllowDelete = j.BClubRepAllowDelete,
                BClubRepAllowAdd = j.BClubRepAllowAdd,
                BAllowMobileLogin = j.BAllowMobileLogin,
                BAllowRosterViewAdult = j.BAllowRosterViewAdult,
                BAllowRosterViewPlayer = j.BAllowRosterViewPlayer,
                BShowTeamNameOnlyInSchedules = j.BShowTeamNameOnlyInSchedules,
                BScheduleAllowPublicAccess = j.BScheduleAllowPublicAccess,
                BTeamPushDirectors = j.BTeamPushDirectors,
                BEnableTsicteams = j.BEnableTsicteams,
                BEnableMobileRsvp = j.BEnableMobileRsvp,
                BEnableMobileTeamChat = j.BEnableMobileTeamChat,
                BenableStp = j.BenableStp,
                BEnableStore = j.BEnableStore,
                BSignalRschedule = j.BSignalRschedule,
                MobileScoreHoursPastGameEligible = j.MobileScoreHoursPastGameEligible,
                StoreSalesTax = j.StoreSalesTax,
                StoreRefundPolicy = j.StoreRefundPolicy,
                StorePickupDetails = j.StorePickupDetails,
                StoreContactEmail = j.StoreContactEmail,
                StoreTsicrate = j.StoreTsicrate,
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobConfigLookupsDto> GetJobConfigLookupsAsync(CancellationToken cancellationToken = default)
    {
        var jobTypes = await _context.JobTypes
            .AsNoTracking()
            .OrderBy(jt => jt.JobTypeName)
            .Select(jt => new JobTypeLookupDto
            {
                JobTypeId = jt.JobTypeId,
                JobTypeName = jt.JobTypeName
            })
            .ToListAsync(cancellationToken);

        var sports = await _context.Sports
            .AsNoTracking()
            .OrderBy(s => s.SportName)
            .Select(s => new SportLookupDto
            {
                SportId = s.SportId,
                SportName = s.SportName
            })
            .ToListAsync(cancellationToken);

        var billingTypes = await _context.BillingTypes
            .AsNoTracking()
            .OrderBy(bt => bt.BillingTypeName)
            .Select(bt => new BillingTypeLookupDto
            {
                BillingTypeId = bt.BillingTypeId,
                BillingTypeName = bt.BillingTypeName
            })
            .ToListAsync(cancellationToken);

        return new JobConfigLookupsDto
        {
            JobTypes = jobTypes,
            Sports = sports,
            BillingTypes = billingTypes
        };
    }

    public async Task<bool> UpdateJobConfigAsync(
        Guid jobId, byte[]? expectedRowVersion,
        Action<Jobs> applyChanges, CancellationToken cancellationToken = default)
    {
        var job = await _context.Jobs
            .FirstOrDefaultAsync(j => j.JobId == jobId, cancellationToken);

        if (job is null)
            throw new KeyNotFoundException($"Job {jobId} not found.");

        // Concurrency check against rowversion
        if (expectedRowVersion is not null && job.UpdatedOn is not null
            && !expectedRowVersion.SequenceEqual(job.UpdatedOn))
        {
            return false;
        }

        applyChanges(job);
        job.Modified = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
