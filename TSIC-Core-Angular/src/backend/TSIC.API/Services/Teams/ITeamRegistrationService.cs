using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Teams;

public interface ITeamRegistrationService
{
    /// <summary>
    /// Get list of clubs that the user is a rep for, with usage status.
    /// </summary>
    Task<List<ClubRepClubDto>> GetMyClubsAsync(string userId);

    /// <summary>
    /// Check if another club rep has already registered teams for this event+club.
    /// Returns conflict info if another rep has teams registered.
    /// </summary>
    Task<CheckExistingRegistrationsResponse> CheckExistingRegistrationsAsync(string jobPath, string clubName, string userId);

    /// <summary>
    /// Get teams metadata for the current club and event.
    /// Returns club info, suggested team names from history, registered Teams, and age groups.
    /// </summary>
    Task<TeamsMetadataResponse> GetTeamsMetadataAsync(string jobPath, string userId, string clubName, bool bPayBalanceDue = false);

    /// <summary>
    /// Register a team for the current event with specified name, age group, and level of play.
    /// Creates a Teams record with TeamName directly (no ClubTeam reference).
    /// </summary>
    Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, string userId, int? clubId = null);

    /// <summary>
    /// Unregister a Team from the current event.
    /// Deletes the Teams record if it has no payments.
    /// Authorization must be checked at controller level.
    /// </summary>
    Task<bool> UnregisterTeamFromEventAsync(Guid teamId);

    /// <summary>
    /// Accept the refund policy for the club rep's registration.
    /// Sets BWaiverSigned3 = true on the Registration record.
    /// </summary>
    Task AcceptRefundPolicyAsync(Guid registrationId);

    /// <summary>
    /// Add a club to the user's rep account.
    /// Links the user to an existing club or creates a new one.
    /// </summary>
    Task<AddClubToRepResponse> AddClubToRepAsync(string userId, string clubName);

    /// <summary>
    /// Remove a club from the user's rep account.
    /// Only allowed if the club has no team registrations.
    /// </summary>
    Task<bool> RemoveClubFromRepAsync(string userId, string clubName);

    /// <summary>
    /// Update/rename a club name for the current user's rep account.
    /// Only allowed if the club has no team registrations.
    /// </summary>
    Task<bool> UpdateClubNameAsync(string userId, string oldClubName, string newClubName);
}
