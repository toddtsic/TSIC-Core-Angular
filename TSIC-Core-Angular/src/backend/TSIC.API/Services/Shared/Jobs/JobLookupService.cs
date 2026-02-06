using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.Jobs;

public class JobLookupService : IJobLookupService
{
    private readonly IJobRepository _jobRepo;
    private readonly IRegistrationRepository _registrationRepo;
    private readonly ILogger<JobLookupService> _logger;

    public JobLookupService(
        IJobRepository jobRepo,
        IRegistrationRepository registrationRepo,
        ILogger<JobLookupService> logger)
    {
        _jobRepo = jobRepo;
        _registrationRepo = registrationRepo;
        _logger = logger;
    }

    public async Task<Guid?> GetJobIdByPathAsync(string jobPath)
    {
        return await _jobRepo.GetJobIdByPathAsync(jobPath);
    }

    public async Task<Guid?> GetJobIdByRegistrationAsync(Guid registrationId)
    {
        var registration = await _registrationRepo.GetByIdAsync(registrationId);
        return registration?.JobId;
    }

    public async Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId)
    {
        var status = await _jobRepo.GetRegistrationStatusAsync(jobId);
        return status != null && status.BRegistrationAllowPlayer && (status.ExpiryUsers > DateTime.Now);
    }

    public async Task<JobMetadataDto?> GetJobMetadataAsync(string jobPath)
    {
        _logger.LogInformation("Fetching job metadata (JobLookupService) for {JobPath}", jobPath);
        var job = await _jobRepo.GetJobMetadataByPathAsync(jobPath);
        return job;
    }
}
