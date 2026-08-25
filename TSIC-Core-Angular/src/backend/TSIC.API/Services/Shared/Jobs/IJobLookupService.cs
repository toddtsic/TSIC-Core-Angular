using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.Shared.Jobs;

public interface IJobLookupService
{
    Task<Guid?> GetJobIdByPathAsync(string jobPath);
    Task<Guid?> GetJobIdByRegistrationAsync(Guid registrationId);
    /// <summary>Job that owns a team. Used to reject cross-job access on teamId routes.</summary>
    Task<Guid?> GetJobIdByTeamAsync(Guid teamId, CancellationToken ct = default);
    /// <summary>Team the caller is rostered on, from their own regId. Null = no reach.</summary>
    Task<Guid?> GetTeamIdByRegistrationAsync(Guid registrationId, CancellationToken ct = default);
    Task<bool> IsPlayerRegistrationActiveAsync(Guid jobId);
    Task<JobMetadataDto?> GetJobMetadataAsync(string jobPath);
}
