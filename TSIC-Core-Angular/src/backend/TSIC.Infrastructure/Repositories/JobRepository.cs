using Microsoft.EntityFrameworkCore;
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
                BRegistrationAllowTeam = jdo.Job.BRegistrationAllowTeam ?? false
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
}
