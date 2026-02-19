using TSIC.Contracts.Dtos.Widgets;

namespace TSIC.Contracts.Services;

/// <summary>
/// Service for managing per-user widget customizations.
/// Handles CRUD operations on the UserWidget delta layer.
/// </summary>
public interface IUserWidgetService
{
    /// <summary>
    /// Get the user's widget customization entries for a registration.
    /// </summary>
    Task<List<UserWidgetEntryDto>> GetUserWidgetsAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Save the user's widget customizations (replace-all pattern).
    /// Deletes existing entries and inserts the new set.
    /// </summary>
    Task SaveUserWidgetsAsync(
        Guid registrationId,
        SaveUserWidgetsRequest request,
        CancellationToken ct = default);

    /// <summary>
    /// Reset the user's widget customizations to platform defaults.
    /// Deletes all UserWidget entries for the registration.
    /// </summary>
    Task ResetUserWidgetsAsync(
        Guid registrationId,
        CancellationToken ct = default);

    /// <summary>
    /// Get all widgets available for the user's role/jobType (for library browser).
    /// Includes visibility status based on current user customizations.
    /// </summary>
    Task<List<AvailableWidgetDto>> GetAvailableWidgetsAsync(
        Guid jobId,
        string roleName,
        Guid registrationId,
        CancellationToken ct = default);
}
