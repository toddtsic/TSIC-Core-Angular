using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.SuggestedEvents;

public interface ISuggestedEventsService
{
    /// <summary>
    /// Returns the "Looking for a new event?" panel rows for a Family-account user.
    /// Empty list when the user has no prior Family history or no candidate Jobs.
    /// </summary>
    Task<List<SuggestedEventDto>> GetSuggestedEventsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
