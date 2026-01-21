using TSIC.Contracts.Dtos;

namespace TSIC.API.Services.Teams;

public interface ITeamRegistrationService
{
    /// <summary>
    /// Initialize registration for club rep after club selection.
    /// Finds or creates Registration record and returns Phase 2 token with regId.
    /// </summary>
    Task<AuthTokenResponse> InitializeRegistrationAsync(string userId, string clubName, string jobPath);

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
    /// Context derived from regId.
    /// </summary>
    Task<TeamsMetadataResponse> GetTeamsMetadataAsync(Guid regId, string userId, bool bPayBalanceDue = false);

    /// <summary>
    /// Register a team for the current event with specified name, age group, and level of play.
    /// Creates a Teams record with TeamName directly (no ClubTeam reference).
    /// Context derived from regId.
    /// </summary>
    Task<RegisterTeamResponse> RegisterTeamForEventAsync(RegisterTeamRequest request, Guid regId, string userId);

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

    /// <summary>
    /// Recalculate team fees for all teams in a job or a specific team.
    /// Triggered by director flag changes or after moving a team to a different age group.
    /// Filters out teams in WAITLIST/DROPPED age groups.
    /// </summary>
    Task<RecalculateTeamFeesResponse> RecalculateTeamFeesAsync(RecalculateTeamFeesRequest request, string userId);

    /// <summary>
    /// Get confirmation text with substituted variables for on-screen display.
    /// Uses AdultRegConfirmationOnScreen template.
    /// </summary>
    Task<string> GetConfirmationTextAsync(Guid registrationId, string userId);

    /// <summary>
    /// Send confirmation email to club rep with substituted template.
    /// Sets bClubrep_NotificationSent flag on Registration.
    /// Uses AdultRegConfirmationEmail template.
    /// </summary>
    Task SendConfirmationEmailAsync(Guid registrationId, string userId, bool forceResend = false);
}
