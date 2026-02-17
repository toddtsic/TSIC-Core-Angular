using TSIC.Contracts.Dtos.Widgets;
using TSIC.Contracts.Repositories;
using TSIC.Contracts.Services;
using TSIC.Domain.Entities;

namespace TSIC.API.Services.Widgets;

/// <summary>
/// Service for the SuperUser widget editor.
/// Manages widget definitions and default role assignments per JobType.
/// </summary>
public sealed class WidgetEditorService : IWidgetEditorService
{
    private readonly IWidgetEditorRepository _repo;

    private static readonly HashSet<string> AllowedWidgetTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "content", "chart", "status-card", "quick-action", "workflow-pipeline", "link-group"
    };

    public WidgetEditorService(IWidgetEditorRepository repo)
    {
        _repo = repo;
    }

    // ── Reference data ──

    public Task<List<JobTypeRefDto>> GetJobTypesAsync(CancellationToken ct = default)
        => _repo.GetJobTypesAsync(ct);

    public Task<List<RoleRefDto>> GetRolesAsync(CancellationToken ct = default)
        => _repo.GetRolesAsync(ct);

    public Task<List<WidgetCategoryRefDto>> GetCategoriesAsync(CancellationToken ct = default)
        => _repo.GetCategoriesAsync(ct);

    // ── Widget definitions ──

    public Task<List<WidgetDefinitionDto>> GetWidgetDefinitionsAsync(CancellationToken ct = default)
        => _repo.GetWidgetDefinitionsAsync(ct);

    public async Task<WidgetDefinitionDto> CreateWidgetAsync(CreateWidgetRequest request, CancellationToken ct = default)
    {
        ValidateWidgetType(request.WidgetType);

        if (await _repo.ComponentKeyExistsAsync(request.ComponentKey, ct: ct))
            throw new ArgumentException($"ComponentKey '{request.ComponentKey}' already exists.");

        var entity = new Widget
        {
            Name = request.Name,
            WidgetType = request.WidgetType,
            ComponentKey = request.ComponentKey,
            CategoryId = request.CategoryId,
            Description = request.Description,
        };

        _repo.AddWidget(entity);
        await _repo.SaveChangesAsync(ct);

        // Re-fetch with joins for CategoryName/Workspace
        var dto = await _repo.GetWidgetDefinitionByIdAsync(entity.WidgetId, ct);
        return dto!;
    }

    public async Task<WidgetDefinitionDto> UpdateWidgetAsync(int widgetId, UpdateWidgetRequest request, CancellationToken ct = default)
    {
        ValidateWidgetType(request.WidgetType);

        var entity = await _repo.GetWidgetByIdAsync(widgetId, ct)
            ?? throw new KeyNotFoundException($"Widget {widgetId} not found.");

        if (await _repo.ComponentKeyExistsAsync(request.ComponentKey, excludeWidgetId: widgetId, ct: ct))
            throw new ArgumentException($"ComponentKey '{request.ComponentKey}' already exists.");

        entity.Name = request.Name;
        entity.WidgetType = request.WidgetType;
        entity.ComponentKey = request.ComponentKey;
        entity.CategoryId = request.CategoryId;
        entity.Description = request.Description;

        await _repo.SaveChangesAsync(ct);

        var dto = await _repo.GetWidgetDefinitionByIdAsync(widgetId, ct);
        return dto!;
    }

    public async Task DeleteWidgetAsync(int widgetId, CancellationToken ct = default)
    {
        var entity = await _repo.GetWidgetByIdAsync(widgetId, ct)
            ?? throw new KeyNotFoundException($"Widget {widgetId} not found.");

        if (await _repo.WidgetHasDependenciesAsync(widgetId, ct))
            throw new InvalidOperationException(
                $"Widget '{entity.Name}' has existing default or job-level assignments. Remove those first.");

        _repo.RemoveWidget(entity);
        await _repo.SaveChangesAsync(ct);
    }

    // ── Widget defaults matrix ──

    public async Task<WidgetDefaultMatrixResponse> GetDefaultsMatrixAsync(int jobTypeId, CancellationToken ct = default)
    {
        var entries = await _repo.GetDefaultsByJobTypeAsync(jobTypeId, ct);
        return new WidgetDefaultMatrixResponse
        {
            JobTypeId = jobTypeId,
            Entries = entries,
        };
    }

    public async Task SaveDefaultsMatrixAsync(SaveWidgetDefaultsRequest request, CancellationToken ct = default)
    {
        // Load existing defaults for this JobType (tracked for removal)
        var existing = await _repo.GetDefaultEntitiesByJobTypeAsync(request.JobTypeId, ct);

        // Remove all existing
        if (existing.Count > 0)
            _repo.RemoveDefaults(existing);

        // Insert new set
        if (request.Entries.Count > 0)
            await _repo.BulkInsertDefaultsAsync(request.JobTypeId, request.Entries, ct);

        await _repo.SaveChangesAsync(ct);
    }

    // ── Widget-centric bulk assignment ──

    public async Task<WidgetAssignmentsResponse> GetWidgetAssignmentsAsync(int widgetId, CancellationToken ct = default)
    {
        var widget = await _repo.GetWidgetByIdAsync(widgetId, ct)
            ?? throw new KeyNotFoundException($"Widget {widgetId} not found.");

        var assignments = await _repo.GetAssignmentsByWidgetAsync(widgetId, ct);
        return new WidgetAssignmentsResponse
        {
            WidgetId = widgetId,
            CategoryId = widget.CategoryId,
            Assignments = assignments,
        };
    }

    public async Task SaveWidgetAssignmentsAsync(SaveWidgetAssignmentsRequest request, CancellationToken ct = default)
    {
        _ = await _repo.GetWidgetByIdAsync(request.WidgetId, ct)
            ?? throw new KeyNotFoundException($"Widget {request.WidgetId} not found.");

        // Load existing defaults for this widget (tracked for removal)
        var existing = await _repo.GetDefaultEntitiesByWidgetAsync(request.WidgetId, ct);

        // Remove all existing
        if (existing.Count > 0)
            _repo.RemoveDefaults(existing);

        // Insert new set
        if (request.Assignments.Count > 0)
            await _repo.BulkInsertAssignmentsAsync(request.WidgetId, request.CategoryId, request.Assignments, ct);

        await _repo.SaveChangesAsync(ct);
    }

    // ── Per-job overrides ──

    public Task<List<JobRefDto>> GetJobsByJobTypeAsync(int jobTypeId, CancellationToken ct = default)
        => _repo.GetJobsByJobTypeAsync(jobTypeId, ct);

    public async Task<JobOverridesResponse> GetJobOverridesAsync(Guid jobId, CancellationToken ct = default)
    {
        // 1. Resolve job type (sequential — DbContext not thread-safe)
        var jobTypeId = await _repo.GetJobTypeIdForJobAsync(jobId, ct)
            ?? throw new KeyNotFoundException($"Job {jobId} not found.");

        // 2. Get defaults for this job type
        var defaults = await _repo.GetDefaultsByJobTypeAsync(jobTypeId, ct);

        // 3. Get per-job overrides
        var overrides = await _repo.GetJobWidgetsByJobAsync(jobId, ct);

        // 4. Build override lookup: widgetId|roleId → JobWidgetEntryDto
        var overrideMap = new Dictionary<string, JobWidgetEntryDto>();
        foreach (var o in overrides)
            overrideMap[$"{o.WidgetId}|{o.RoleId}"] = o;

        // 5. Merge: defaults + overrides
        var merged = new List<JobWidgetEntryDto>();

        foreach (var def in defaults)
        {
            var key = $"{def.WidgetId}|{def.RoleId}";
            if (overrideMap.Remove(key, out var ov))
            {
                // Use override (already has IsOverridden=true)
                merged.Add(ov);
            }
            else
            {
                // Inherited default
                merged.Add(new JobWidgetEntryDto
                {
                    WidgetId = def.WidgetId,
                    RoleId = def.RoleId,
                    CategoryId = def.CategoryId,
                    DisplayOrder = def.DisplayOrder,
                    Config = def.Config,
                    IsEnabled = true,
                    IsOverridden = false,
                });
            }
        }

        // 6. Add job-specific additions (overrides with no matching default)
        foreach (var addition in overrideMap.Values)
        {
            merged.Add(addition);
        }

        return new JobOverridesResponse
        {
            JobId = jobId,
            JobTypeId = jobTypeId,
            Entries = merged,
        };
    }

    public async Task SaveJobOverridesAsync(SaveJobOverridesRequest request, CancellationToken ct = default)
    {
        // Only persist entries where IsOverridden=true
        var overridesToSave = request.Entries
            .Where(e => e.IsOverridden)
            .ToList();

        // Load existing JobWidget entries for removal
        var existing = await _repo.GetJobWidgetEntitiesAsync(request.JobId, ct);

        if (existing.Count > 0)
            _repo.RemoveJobWidgets(existing);

        if (overridesToSave.Count > 0)
            await _repo.BulkInsertJobWidgetsAsync(request.JobId, overridesToSave, ct);

        await _repo.SaveChangesAsync(ct);
    }

    private static void ValidateWidgetType(string widgetType)
    {
        if (!AllowedWidgetTypes.Contains(widgetType))
            throw new ArgumentException(
                $"Invalid WidgetType '{widgetType}'. Allowed: {string.Join(", ", AllowedWidgetTypes)}");
    }
}
