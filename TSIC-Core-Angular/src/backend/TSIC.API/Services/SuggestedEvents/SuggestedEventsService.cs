using TSIC.Contracts.Dtos;
using TSIC.Contracts.Repositories;

namespace TSIC.API.Services.SuggestedEvents;

public sealed class SuggestedEventsService : ISuggestedEventsService
{
    private readonly IRegistrationRepository _registrationRepo;
    private readonly IJobRepository _jobRepo;

    public SuggestedEventsService(
        IRegistrationRepository registrationRepo,
        IJobRepository jobRepo)
    {
        _registrationRepo = registrationRepo;
        _jobRepo = jobRepo;
    }

    public async Task<List<SuggestedEventDto>> GetSuggestedEventsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        var customerIds = await _registrationRepo.GetCustomerIdsForFamilyUserAsync(userId, cancellationToken);
        if (customerIds.Count == 0) return [];

        var excludeJobIds = await _registrationRepo.GetActiveFamilyJobIdsForUserAsync(userId, cancellationToken);

        return await _jobRepo.GetCandidateEventsByCustomersAsync(customerIds, excludeJobIds, cancellationToken);
    }
}
