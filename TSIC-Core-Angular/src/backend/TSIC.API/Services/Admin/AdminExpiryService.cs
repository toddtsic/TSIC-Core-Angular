using TSIC.Contracts.Dtos.AdminExpiry;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Admin;

/// <summary>
/// SuperUser Admin Expiry tool: cross-customer discovery of jobs whose admin door
/// (Jobs.ExpiryAdmin) has closed, plus the one-field update to reopen them.
/// Migrated from legacy AdminExpiryController.
/// </summary>
public class AdminExpiryService : IAdminExpiryService
{
    private readonly IJobRepository _jobRepository;
    private readonly IJobConfigRepository _jobConfigRepository;

    public AdminExpiryService(IJobRepository jobRepository, IJobConfigRepository jobConfigRepository)
    {
        _jobRepository = jobRepository;
        _jobConfigRepository = jobConfigRepository;
    }

    public async Task<List<AdminExpiryCustomerDto>> GetExpiredJobsAsync(CancellationToken ct = default)
    {
        return await _jobRepository.GetAdminExpiredJobsByCustomerAsync(ct);
    }

    public async Task UpdateExpiryAsync(Guid jobId, UpdateAdminExpiryRequest request, CancellationToken ct = default)
    {
        var job = await _jobConfigRepository.GetJobTrackedAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        job.ExpiryAdmin = request.ExpiryAdmin;
        job.Modified = DateTime.Now;

        await _jobConfigRepository.SaveChangesAsync(ct);
    }
}
