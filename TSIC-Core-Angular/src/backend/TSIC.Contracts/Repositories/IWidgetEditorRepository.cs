using TSIC.Contracts.Dtos.Widgets;
using TSIC.Domain.Entities;

namespace TSIC.Contracts.Repositories;

public interface IWidgetEditorRepository
{
    // ── Reference data ──
    Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default);
    Task<List<RoleRefDto>> GetRolesAsync(CancellationToken ct = default);
    Task<List<WidgetCategoryRefDto>> GetCategoriesAsync(CancellationToken ct = default);

    // ── Widget definitions ──
    Task<List<WidgetDefinitionDto>> GetWidgetDefinitionsAsync(CancellationToken ct = default);
    Task<Widget?> GetWidgetByIdAsync(int widgetId, CancellationToken ct = default);
    Task<bool> ComponentKeyExistsAsync(string componentKey, int? excludeWidgetId = null, CancellationToken ct = default);
    void AddWidget(Widget widget);
    void RemoveWidget(Widget widget);
    Task<bool> WidgetHasDependenciesAsync(int widgetId, CancellationToken ct = default);
    Task<WidgetDefinitionDto?> GetWidgetDefinitionByIdAsync(int widgetId, CancellationToken ct = default);

    // ── Widget defaults matrix ──
    Task<List<WidgetDefaultEntryDto>> GetDefaultsByJobTypeAsync(int jobTypeId, CancellationToken ct = default);
    Task<List<WidgetDefault>> GetDefaultEntitiesByJobTypeAsync(int jobTypeId, CancellationToken ct = default);
    void RemoveDefaults(List<WidgetDefault> defaults);
    Task BulkInsertDefaultsAsync(int jobTypeId, List<WidgetDefaultEntryDto> entries, CancellationToken ct = default);

    // ── Widget-centric assignments ──
    Task<List<WidgetAssignmentDto>> GetAssignmentsByWidgetAsync(int widgetId, CancellationToken ct = default);
    Task<List<WidgetDefault>> GetDefaultEntitiesByWidgetAsync(int widgetId, CancellationToken ct = default);
    Task BulkInsertAssignmentsAsync(int widgetId, int categoryId, List<WidgetAssignmentDto> assignments, CancellationToken ct = default);

    Task<int> SaveChangesAsync(CancellationToken ct = default);
}
