using Microsoft.EntityFrameworkCore;
using TSIC.Contracts.Dtos.AdultRegistration;
using TSIC.Contracts.Repositories;
using TSIC.Domain.Constants;
using TSIC.Domain.Entities;
using TSIC.Infrastructure.Data.SqlDbContext;

namespace TSIC.Infrastructure.Repositories;

public class AdultRegistrationRepository : IAdultRegistrationRepository
{
    private readonly SqlDbContext _context;

    public AdultRegistrationRepository(SqlDbContext context)
    {
        _context = context;
    }

    public async Task<AdultRegJobData?> GetJobAdultRegDataAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobId == jobId)
            .Select(j => new AdultRegJobData
            {
                JobId = j.JobId,
                JobName = j.JobName ?? string.Empty,
                AdultProfileMetadataJson = j.AdultProfileMetadataJson,
                JsonOptions = j.JsonOptions,
                AdultRegConfirmationEmail = j.AdultRegConfirmationEmail,
                AdultRegConfirmationOnScreen = j.AdultRegConfirmationOnScreen,
                AdultRegRefundPolicy = j.AdultRegRefundPolicy,
                AdultRegReleaseOfLiability = j.AdultRegReleaseOfLiability,
                AdultRegCodeOfConduct = j.AdultRegCodeOfConduct,
                RefereeRegConfirmationEmail = j.RefereeRegConfirmationEmail,
                RefereeRegConfirmationOnScreen = j.RefereeRegConfirmationOnScreen,
                RecruiterRegConfirmationEmail = j.RecruiterRegConfirmationEmail,
                RecruiterRegConfirmationOnScreen = j.RecruiterRegConfirmationOnScreen
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AdultRegJobData?> GetJobAdultRegDataByPathAsync(string jobPath, CancellationToken cancellationToken = default)
    {
        return await _context.Jobs
            .AsNoTracking()
            .Where(j => j.JobPath != null && EF.Functions.Collate(j.JobPath!, "SQL_Latin1_General_CP1_CI_AS") == jobPath)
            .Select(j => new AdultRegJobData
            {
                JobId = j.JobId,
                JobName = j.JobName ?? string.Empty,
                AdultProfileMetadataJson = j.AdultProfileMetadataJson,
                JsonOptions = j.JsonOptions,
                AdultRegConfirmationEmail = j.AdultRegConfirmationEmail,
                AdultRegConfirmationOnScreen = j.AdultRegConfirmationOnScreen,
                AdultRegRefundPolicy = j.AdultRegRefundPolicy,
                AdultRegReleaseOfLiability = j.AdultRegReleaseOfLiability,
                AdultRegCodeOfConduct = j.AdultRegCodeOfConduct,
                RefereeRegConfirmationEmail = j.RefereeRegConfirmationEmail,
                RefereeRegConfirmationOnScreen = j.RefereeRegConfirmationOnScreen,
                RecruiterRegConfirmationEmail = j.RecruiterRegConfirmationEmail,
                RecruiterRegConfirmationOnScreen = j.RecruiterRegConfirmationOnScreen
            })
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> HasExistingRegistrationAsync(string userId, Guid jobId, string roleId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .AnyAsync(r => r.UserId == userId && r.JobId == jobId && r.RoleId == roleId && r.BActive == true, cancellationToken);
    }

    public async Task<Registrations?> GetRegistrationWithJobAsync(Guid registrationId, CancellationToken cancellationToken = default)
    {
        return await _context.Registrations
            .AsNoTracking()
            .Include(r => r.Job)
            .Include(r => r.Role)
            .Include(r => r.User)
            .Where(r => r.RegistrationId == registrationId)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public void Add(Registrations registration)
    {
        _context.Registrations.Add(registration);
    }

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.SaveChangesAsync(cancellationToken);
    }
}
