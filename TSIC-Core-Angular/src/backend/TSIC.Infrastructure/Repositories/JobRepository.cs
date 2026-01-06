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

    public IQueryable<Jobs> Query()
    {
        return _context.Jobs.AsQueryable();
    }

    public async Task<JobPreSubmitMetadata?> GetPreSubmitMetadataAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobPreSubmitMetadata(j.PlayerProfileMetadataJson, j.JsonOptions, j.CoreRegformPlayer))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobPaymentInfo?> GetJobPaymentInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobPaymentInfo(j.AdnArb, j.AdnArbbillingOccurences, j.AdnArbintervalLength, j.AdnArbstartDate))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobMetadata?> GetJobMetadataAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new JobMetadata(j.PlayerProfileMetadataJson, j.JsonOptions, j.CoreRegformPlayer))
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
            .Select(j => new JobRegistrationStatus(
                j.BRegistrationAllowPlayer ?? false,
                j.ExpiryUsers))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<JobMetadataDto?> GetJobMetadataByPathAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        return await _context.JobDisplayOptions
            .AsNoTracking()
            .Where(jdo => jdo.Job.JobPath == jobPath)
            .Select(jdo => new JobMetadataDto(
                jdo.Job.JobId,
                jdo.Job.JobName ?? string.Empty,
                jdo.Job.JobPath ?? string.Empty,
                jdo.LogoHeader,
                jdo.ParallaxSlide1Image,
                jdo.ParallaxSlide1Text1,
                jdo.ParallaxSlide1Text2,
                jdo.ParallaxBackgroundImage,
                jdo.Job.CoreRegformPlayer == "1",
                jdo.Job.UslaxNumberValidThroughDate,
                jdo.Job.ExpiryUsers,
                jdo.Job.PlayerProfileMetadataJson,
                jdo.Job.JsonOptions,
                jdo.Job.MomLabel,
                jdo.Job.DadLabel,
                jdo.Job.PlayerRegReleaseOfLiability,
                jdo.Job.PlayerRegCodeOfConduct,
                jdo.Job.PlayerRegCovid19Waiver,
                jdo.Job.PlayerRegRefundPolicy,
                jdo.Job.BOfferPlayerRegsaverInsurance ?? false,
                jdo.Job.AdnArb,
                jdo.Job.AdnArbbillingOccurences,
                jdo.Job.AdnArbintervalLength,
                jdo.Job.AdnArbstartDate,
                jdo.Job.BRegistrationAllowTeam ?? false))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<InsuranceOfferInfo?> GetInsuranceOfferInfoAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new InsuranceOfferInfo(
                j.JobName,
                j.BOfferPlayerRegsaverInsurance ?? false))
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
            ? new JobConfirmationInfo(result.JobId, result.JobName, result.JobPath, result.AdnArb, result.PlayerRegConfirmationOnScreen)
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
            ? new JobConfirmationEmailInfo(result.JobId, result.JobName, result.JobPath, result.AdnArb, result.PlayerRegConfirmationEmail)
            : null;
    }
}
