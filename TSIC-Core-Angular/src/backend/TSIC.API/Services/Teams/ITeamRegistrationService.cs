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

    /// <summary>
    /// Update a club team. Name and grad year can only be changed if team has never been registered.
    /// Level of play can always be updated.
    /// </summary>
    Task<ClubTeamOperationResponse> UpdateClubTeamAsync(UpdateClubTeamRequest request, string userId);

    /// <summary>
    /// Activate a club team (set Active = true).
    /// </summary>
    Task<ClubTeamOperationResponse> ActivateClubTeamAsync(int clubTeamId, string userId);

    /// <summary>
    /// Inactivate a club team (set Active = false). Team can be reactivated later.
    /// Cannot inactivate if team is currently registered for any event.
    /// </summary>
    Task<ClubTeamOperationResponse> InactivateClubTeamAsync(int clubTeamId, string userId);

    /// <summary>
    /// Delete a club team permanently. 
    /// If team has registration history, it will be soft-deleted (set Active = false).
    /// If team has never been used, it will be hard-deleted (removed from database).
    /// Cannot delete if team is currently registered for any event.
    /// </summary>
    Task<ClubTeamOperationResponse> DeleteClubTeamAsync(int clubTeamId, string userId);
}
