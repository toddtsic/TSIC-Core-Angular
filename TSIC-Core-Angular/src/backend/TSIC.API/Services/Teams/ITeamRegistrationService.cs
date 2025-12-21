using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Teams;

public interface ITeamRegistrationService
{
    /// <summary>
    /// Get list of clubs that the user is a rep for, with usage status.
    /// </summary>
    Task<List<ClubRepClubDto>> GetMyClubsAsync(string userId);

    /// <summary>
    /// Get teams metadata for the current club and event.
    /// Returns club info, available ClubTeams, registered Teams, and age groups.
    /// </summary>
    Task<TeamsMetadataResponse> GetTeamsMetadataAsync(string jobPath, string userId, string clubName);

    /// <summary>
    /// Register a ClubTeam for the current event.
    /// Creates a Teams record linking the ClubTeam to the Job.
    /// </summary>
    Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, string userId);

    /// <summary>
    /// Unregister a Team from the current event.
    /// Deletes the Teams record if it has no payments.
    /// </summary>
    Task<bool> UnregisterTeamFromEventAsync(Guid teamId, string userId);

    /// <summary>
    /// Add a new ClubTeam to the club.
    /// Creates a new ClubTeam record that will be available for all future events.
    /// </summary>
    Task<AddClubTeamResponse> AddNewClubTeamAsync(AddClubTeamRequest request, string userId);

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
    /// Get all club teams for all clubs the user is a rep for.
    /// </summary>
    Task<List<ClubTeamManagementDto>> GetClubTeamsAsync(string userId);
}
