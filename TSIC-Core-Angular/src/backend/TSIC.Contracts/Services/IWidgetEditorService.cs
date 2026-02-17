using TSIC.Contracts.Dtos.Widgets;

namespace TSIC.Contracts.Services;

public interface IWidgetEditorService
{
    // ── Reference data ──
    Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default);
    Task<List<RoleRefDto>> GetRolesAsync(CancellationToken ct = default);
    Task<List<WidgetCategoryRefDto>> GetCategoriesAsync(CancellationToken ct = default);

    // ── Widget definitions ──
    Task<List<WidgetDefinitionDto>> GetWidgetDefinitionsAsync(CancellationToken ct = default);
    Task<WidgetDefinitionDto> CreateWidgetAsync(CreateWidgetRequest request, CancellationToken ct = default);
    Task<WidgetDefinitionDto> UpdateWidgetAsync(int widgetId, UpdateWidgetRequest request, CancellationToken ct = default);
    Task DeleteWidgetAsync(int widgetId, CancellationToken ct = default);

    // ── Widget defaults matrix ──
    Task<WidgetDefaultMatrixResponse> GetDefaultsMatrixAsync(int jobTypeId, CancellationToken ct = default);
    Task SaveDefaultsMatrixAsync(SaveWidgetDefaultsRequest request, CancellationToken ct = default);

    // ── Widget-centric bulk assignment ──
    Task<WidgetAssignmentsResponse> GetWidgetAssignmentsAsync(int widgetId, CancellationToken ct = default);
    Task SaveWidgetAssignmentsAsync(SaveWidgetAssignmentsRequest request, CancellationToken ct = default);
}
