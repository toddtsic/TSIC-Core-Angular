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
        // Family and ClubRep classes are mutually exclusive per privilege-separation
        // policy — try Family first, fall through to ClubRep if no Family history.
        var familyCustomerIds = await _registrationRepo.GetCustomerIdsForFamilyUserAsync(userId, cancellationToken);
        if (familyCustomerIds.Count > 0)
        {
            var excludeJobIds = await _registrationRepo.GetActiveFamilyJobIdsForUserAsync(userId, cancellationToken);
            return await _jobRepo.GetCandidateEventsByCustomersAsync(
                familyCustomerIds, excludeJobIds, SuggestedEventAudience.Family, cancellationToken);
        }

        var clubRepCustomerIds = await _registrationRepo.GetCustomerIdsForClubRepUserAsync(userId, cancellationToken);
        if (clubRepCustomerIds.Count > 0)
        {
            var excludeJobIds = await _registrationRepo.GetActiveClubRepJobIdsForUserAsync(userId, cancellationToken);
            return await _jobRepo.GetCandidateEventsByCustomersAsync(
                clubRepCustomerIds, excludeJobIds, SuggestedEventAudience.ClubRep, cancellationToken);
        }

        return [];
    }
}
