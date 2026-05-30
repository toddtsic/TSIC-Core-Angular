using TSIC.Contracts.Dtos.CampGroups;

namespace TSIC.Contracts.Services;

/// <summary>
/// Camp Day/Night Groups admin: list teams, list a team's campers, write Day/Night
/// group assignments per registrant or in bulk. Replaces legacy Rosters/DayNightGroups.
/// </summary>
public interface ICampGroupsService
{
    /// <summary>
    /// All active teams in the job with player counts (left pane of the admin screen).
    /// </summary>
    Task<List<TeamRosterCountDto>> GetTeamsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// Day/Night group dropdown options for the job (from JsonOptions). Lets non-SU
    /// admins fetch just these two lists without DDL Options editor access.
    /// </summary>
    Task<CampGroupOptionsDto> GetGroupOptionsAsync(Guid jobId, CancellationToken ct = default);

    /// <summary>
    /// All active Player registrations on a team, with current Day/Night group values
    /// (right pane). Caller is responsible for authorizing team ↔ job pairing.
    /// </summary>
    Task<List<CampPlayerDto>> GetCampersAsync(Guid teamId, CancellationToken ct = default);

    /// <summary>
    /// Update Day and/or Night group on a single registration. Returns false when the
    /// registration does not exist or is not in the caller's job.
    /// </summary>
    Task<bool> UpdateGroupsAsync(
        Guid jobId,
        Guid registrationId,
        UpdateCampGroupsRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Update Day and/or Night group across many registrations in one shot. Silently
    /// drops any registrations that are not in the caller's job. Returns rows touched.
    /// </summary>
    Task<int> BulkUpdateGroupsAsync(
        Guid jobId,
        BulkUpdateCampGroupsRequest request,
        CancellationToken ct = default);
}
