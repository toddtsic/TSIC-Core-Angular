using TSIC.Contracts.Dtos.AgeRange;

namespace TSIC.API.Services.Admin;

public interface IAgeRangeService
{
    Task<List<AgeRangeDto>> GetAllForJobAsync(
        Guid jobId,
        CancellationToken cancellationToken = default);

    Task<AgeRangeDto> CreateAsync(
        Guid jobId,
        string userId,
        CreateAgeRangeRequest request,
        CancellationToken cancellationToken = default);

    Task<AgeRangeDto> UpdateAsync(
        int ageRangeId,
        Guid jobId,
        string userId,
        UpdateAgeRangeRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        int ageRangeId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
