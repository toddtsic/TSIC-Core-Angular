using TSIC.Contracts.Dtos.Stp;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Stp;

/// <inheritdoc cref="IStpService"/>
public class StpService : IStpService
{
    private readonly IRegistrationRepository _registrationRepo;

    public StpService(IRegistrationRepository registrationRepo)
    {
        _registrationRepo = registrationRepo;
    }

    public Task<List<StpClubRepDto>> GetClubRepsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
        => _registrationRepo.GetStpClubRepsForJobAsync(jobId, cancellationToken);
}
