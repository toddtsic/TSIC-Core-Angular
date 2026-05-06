using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.SuggestedEvents;

public interface ISuggestedEventsService
{
    /// <summary>
    /// Returns the "Looking for a new event?" panel rows for the user. Detects
    /// account class by registration history — Family-account users see Jobs
    /// with player registration open; ClubRep accounts see Jobs with team
    /// registration open. Empty list when there's no relevant history or no
    /// candidate Jobs.
    /// </summary>
    Task<List<SuggestedEventDto>> GetSuggestedEventsForUserAsync(
        string userId,
        CancellationToken cancellationToken = default);
}
