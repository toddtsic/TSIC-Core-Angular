using TSIC.Contracts.Dtos.Stp;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;

namespace TSIC.API.Services.Stp;

/// <inheritdoc cref="IStpService"/>
public class StpService : IStpService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobConfigRepository _jobRepo;

    public StpService(IRegistrationRepository registrationRepo, IJobConfigRepository jobRepo)
    {
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
    }

    /// <inheritdoc />
    public async Task<List<StpClubRepDto>?> GetClubRepsAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // The flag is checked HERE, not only at the role picker. The picker
        // (GetStpAdminRegistrationsAsync) stops a vendor logging in while STP is off, but
        // it is a door, not a boundary: a JWT minted while the flag was on stays valid
        // after the director flips it back, and nothing in jobPath validation notices.
        // The consent has to be enforced at the read itself.
        var job = await _jobRepo.GetJobByIdAsync(jobId, cancellationToken);
        if (job?.BenableStp != true) return null;

        return await _registrationRepo.GetStpClubRepsForJobAsync(jobId, cancellationToken);
    }
}
