namespace TSIC.API.Services.Shared;

public interface IJobLookupService
{
    Task<Guid?> GetJobIdByPathAsync(string jobPath);
    Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId);
    Task<JobMetadataDto?> GetJobMetadataAsync(string jobPath);
}
