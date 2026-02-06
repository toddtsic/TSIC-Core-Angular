using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.Jobs;

public interface IJobLookupService
{
    Task<Guid?> GetJobIdByPathAsync(string jobPath);
    Task<Guid?> GetJobIdByRegistrationAsync(Guid registrationId);
    Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId);
    Task<JobMetadataDto?> GetJobMetadataAsync(string jobPath);
}
