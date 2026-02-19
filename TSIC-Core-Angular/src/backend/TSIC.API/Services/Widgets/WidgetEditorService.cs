using System.Text;
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
        "content", "chart-tile", "status-tile", "link-tile"
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
            DefaultConfig = request.DefaultConfig,
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
        entity.DefaultConfig = request.DefaultConfig;

        // Propagate DefaultConfig to existing entries with NULL Config
        await _repo.PropagateDefaultConfigAsync(widgetId, request.DefaultConfig, ct);

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

    // ── Seed script sync ──

    public async Task<SeedScriptSyncResult> GenerateSeedScriptAsync(string outputPath, CancellationToken ct = default)
    {
        // Read all 4 tables sequentially (DbContext not thread-safe)
        var categories = await _repo.GetCategoriesAsync(ct);
        var widgets = await _repo.GetWidgetDefinitionsAsync(ct);
        var defaults = await _repo.GetAllDefaultsAsync(ct);
        var jobWidgets = await _repo.GetAllJobWidgetsAsync(ct);

        var sb = new StringBuilder(64 * 1024);

        AppendHeader(sb, categories.Count, widgets.Count, defaults.Count, jobWidgets.Count);
        AppendSchemaDdl(sb);
        AppendDataBatch(sb, categories, widgets, defaults, jobWidgets);
        AppendVerification(sb);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, sb.ToString(), Encoding.UTF8, ct);

        return new SeedScriptSyncResult
        {
            Message = $"Seed script synced: {categories.Count} categories, {widgets.Count} widgets, {defaults.Count} defaults, {jobWidgets.Count} job overrides.",
            FilePath = outputPath,
            CategoriesCount = categories.Count,
            WidgetsCount = widgets.Count,
            DefaultsCount = defaults.Count,
            JobWidgetsCount = jobWidgets.Count,
        };
    }

    // ════════════════════════════════════════════════════
    // SQL generation helpers
    // ════════════════════════════════════════════════════

    private static string EscStr(string? val)
        => val is null or "" ? "NULL" : $"N'{val.Replace("'", "''")}'";

    private static string EscGuid(Guid val) => $"'{val}'";

    private static string EscBit(bool val) => val ? "1" : "0";

    private static void AppendHeader(StringBuilder sb, int cats, int wids, int defs, int jws)
    {
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine($"-- Widget Dashboard — Complete Setup Script");
        sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine("--");
        sb.AppendLine("-- Auto-generated by the Widget Editor 'Sync Seed Script' button.");
        sb.AppendLine("-- DO NOT EDIT BY HAND — changes will be overwritten on next sync.");
        sb.AppendLine("--");
        sb.AppendLine("-- Handles all scenarios:");
        sb.AppendLine("--   A) Fresh DB — no [widgets] schema");
        sb.AppendLine("--   B) Prod restore — old schema with [Section] column");
        sb.AppendLine("--   C) Already migrated — [Workspace] column exists");
        sb.AppendLine("--");
        sb.AppendLine($"-- Snapshot: {cats} categories, {wids} widgets, {defs} defaults, {jws} job overrides");
        sb.AppendLine("--");
        sb.AppendLine("-- Prerequisites: reference.JobTypes + dbo.AspNetRoles populated");
        sb.AppendLine("-- ============================================================================");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine();
    }

    private static void AppendSchemaDdl(StringBuilder sb)
    {
        sb.AppendLine("-- ════════════════════════════════════════════════════════════");
        sb.AppendLine("-- BATCH 1: SCHEMA + TABLES + MIGRATION");
        sb.AppendLine("-- ════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Schema
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'widgets')");
        sb.AppendLine("    EXEC('CREATE SCHEMA [widgets] AUTHORIZATION [dbo]');");
        sb.AppendLine();

        // WidgetCategory
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetCategory')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    CREATE TABLE [widgets].[WidgetCategory] (");
        sb.AppendLine("        [CategoryId]   INT IDENTITY(1,1) NOT NULL,");
        sb.AppendLine("        [Name]         NVARCHAR(100)     NOT NULL,");
        sb.AppendLine("        [Workspace]    NVARCHAR(20)      NOT NULL,");
        sb.AppendLine("        [Icon]         NVARCHAR(50)      NULL,");
        sb.AppendLine("        [DefaultOrder] INT               NOT NULL DEFAULT 0,");
        sb.AppendLine("        CONSTRAINT [PK_widgets_WidgetCategory] PRIMARY KEY CLUSTERED ([CategoryId])");
        sb.AppendLine("    );");
        sb.AppendLine("END");
        sb.AppendLine();

        // Widget
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'Widget')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    CREATE TABLE [widgets].[Widget] (");
        sb.AppendLine("        [WidgetId]      INT IDENTITY(1,1) NOT NULL,");
        sb.AppendLine("        [Name]          NVARCHAR(100)     NOT NULL,");
        sb.AppendLine("        [WidgetType]    NVARCHAR(30)      NOT NULL,");
        sb.AppendLine("        [ComponentKey]  NVARCHAR(100)     NOT NULL,");
        sb.AppendLine("        [CategoryId]    INT               NOT NULL,");
        sb.AppendLine("        [Description]   NVARCHAR(500)     NULL,");
        sb.AppendLine("        [DefaultConfig] NVARCHAR(MAX)     NULL,");
        sb.AppendLine("        CONSTRAINT [PK_widgets_Widget] PRIMARY KEY CLUSTERED ([WidgetId]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_Widget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),");
        sb.AppendLine("        CONSTRAINT [CK_widgets_Widget_WidgetType] CHECK ([WidgetType] IN ('content','chart-tile','status-tile','link-tile'))");
        sb.AppendLine("    );");
        sb.AppendLine("END");
        sb.AppendLine();

        // WidgetDefault
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'WidgetDefault')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    CREATE TABLE [widgets].[WidgetDefault] (");
        sb.AppendLine("        [WidgetDefaultId] INT IDENTITY(1,1) NOT NULL,");
        sb.AppendLine("        [JobTypeId]       INT               NOT NULL,");
        sb.AppendLine("        [RoleId]          NVARCHAR(450)     NOT NULL,");
        sb.AppendLine("        [WidgetId]        INT               NOT NULL,");
        sb.AppendLine("        [CategoryId]      INT               NOT NULL,");
        sb.AppendLine("        [DisplayOrder]    INT               NOT NULL DEFAULT 0,");
        sb.AppendLine("        [Config]          NVARCHAR(MAX)     NULL,");
        sb.AppendLine("        CONSTRAINT [PK_widgets_WidgetDefault] PRIMARY KEY CLUSTERED ([WidgetDefaultId]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_JobTypeId] FOREIGN KEY ([JobTypeId]) REFERENCES [reference].[JobTypes] ([JobTypeId]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_WidgetDefault_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),");
        sb.AppendLine("        CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category] UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId])");
        sb.AppendLine("    );");
        sb.AppendLine("END");
        sb.AppendLine();

        // JobWidget
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = 'widgets' AND t.name = 'JobWidget')");
        sb.AppendLine("BEGIN");
        sb.AppendLine("    CREATE TABLE [widgets].[JobWidget] (");
        sb.AppendLine("        [JobWidgetId]  INT IDENTITY(1,1)    NOT NULL,");
        sb.AppendLine("        [JobId]        UNIQUEIDENTIFIER     NOT NULL,");
        sb.AppendLine("        [WidgetId]     INT                  NOT NULL,");
        sb.AppendLine("        [RoleId]       NVARCHAR(450)        NOT NULL,");
        sb.AppendLine("        [CategoryId]   INT                  NOT NULL,");
        sb.AppendLine("        [DisplayOrder] INT                  NOT NULL DEFAULT 0,");
        sb.AppendLine("        [IsEnabled]    BIT                  NOT NULL DEFAULT 1,");
        sb.AppendLine("        [Config]       NVARCHAR(MAX)        NULL,");
        sb.AppendLine("        CONSTRAINT [PK_widgets_JobWidget] PRIMARY KEY CLUSTERED ([JobWidgetId]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_JobId] FOREIGN KEY ([JobId]) REFERENCES [Jobs].[Jobs] ([JobId]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_WidgetId] FOREIGN KEY ([WidgetId]) REFERENCES [widgets].[Widget] ([WidgetId]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_RoleId] FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]),");
        sb.AppendLine("        CONSTRAINT [FK_widgets_JobWidget_CategoryId] FOREIGN KEY ([CategoryId]) REFERENCES [widgets].[WidgetCategory] ([CategoryId]),");
        sb.AppendLine("        CONSTRAINT [UQ_widgets_JobWidget_Job_Widget_Role] UNIQUE ([JobId], [WidgetId], [RoleId])");
        sb.AppendLine("    );");
        sb.AppendLine("END");
        sb.AppendLine();

        // Schema migration (Section → Workspace)
        sb.AppendLine("-- Schema migration: Section -> Workspace (prod backup compatibility)");
        sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Section')");
        sb.AppendLine("    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Section];");
        sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')");
        sb.AppendLine("    ALTER TABLE [widgets].[WidgetCategory] DROP CONSTRAINT [CK_widgets_WidgetCategory_Workspace];");
        sb.AppendLine("IF COL_LENGTH('widgets.WidgetCategory', 'Section') IS NOT NULL AND COL_LENGTH('widgets.WidgetCategory', 'Workspace') IS NULL");
        sb.AppendLine("    EXEC sp_rename 'widgets.WidgetCategory.Section', 'Workspace', 'COLUMN';");
        sb.AppendLine();

        // Constraint refresh
        sb.AppendLine("-- Refresh constraints");
        sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget' AND object_id = OBJECT_ID('widgets.WidgetDefault'))");
        sb.AppendLine("    ALTER TABLE [widgets].[WidgetDefault] DROP CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget];");
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UQ_widgets_WidgetDefault_JobType_Role_Widget_Category' AND object_id = OBJECT_ID('widgets.WidgetDefault'))");
        sb.AppendLine("    ALTER TABLE [widgets].[WidgetDefault] ADD CONSTRAINT [UQ_widgets_WidgetDefault_JobType_Role_Widget_Category] UNIQUE ([JobTypeId], [RoleId], [WidgetId], [CategoryId]);");
        sb.AppendLine();
        sb.AppendLine("IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_Widget_WidgetType')");
        sb.AppendLine("    ALTER TABLE [widgets].[Widget] DROP CONSTRAINT [CK_widgets_Widget_WidgetType];");
        sb.AppendLine("ALTER TABLE [widgets].[Widget] ADD CONSTRAINT [CK_widgets_Widget_WidgetType]");
        sb.AppendLine("    CHECK ([WidgetType] IN ('content','chart-tile','status-tile','link-tile'));");
        sb.AppendLine();
        sb.AppendLine("PRINT 'Batch 1 complete: schema + tables + migration';");
        sb.AppendLine("GO");
        sb.AppendLine();
    }

    private static void AppendDataBatch(
        StringBuilder sb,
        List<WidgetCategoryRefDto> categories,
        List<WidgetDefinitionDto> widgets,
        List<WidgetDefault> defaults,
        List<JobWidget> jobWidgets)
    {
        sb.AppendLine("-- ════════════════════════════════════════════════════════════");
        sb.AppendLine("-- BATCH 2: DATA (exported from dev)");
        sb.AppendLine("-- ════════════════════════════════════════════════════════════");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT ON;");
        sb.AppendLine();

        // Workspace CHECK — build from actual data
        var workspaces = categories.Select(c => c.Workspace).Distinct().OrderBy(w => w).ToList();
        var wsValues = string.Join(",", workspaces.Select(w => $"'{w}'"));
        sb.AppendLine("IF NOT EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = 'CK_widgets_WidgetCategory_Workspace')");
        sb.AppendLine("BEGIN");
        sb.AppendLine($"    ALTER TABLE [widgets].[WidgetCategory] ADD CONSTRAINT [CK_widgets_WidgetCategory_Workspace]");
        sb.AppendLine($"        CHECK ([Workspace] IN ({wsValues}));");
        sb.AppendLine("END");
        sb.AppendLine();

        // Clear all data (child tables first)
        sb.AppendLine("-- Clear all widget data (child → parent order)");
        sb.AppendLine("DELETE FROM widgets.JobWidget;");
        sb.AppendLine("DELETE FROM widgets.WidgetDefault;");
        sb.AppendLine("DELETE FROM widgets.Widget;");
        sb.AppendLine("DELETE FROM widgets.WidgetCategory;");
        sb.AppendLine();

        // WidgetCategory
        sb.AppendLine("-- ── WidgetCategory ──");
        sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetCategory ON;");
        foreach (var c in categories)
        {
            sb.AppendLine($"INSERT INTO widgets.WidgetCategory (CategoryId, Name, Workspace, Icon, DefaultOrder)");
            sb.AppendLine($"VALUES ({c.CategoryId}, {EscStr(c.Name)}, {EscStr(c.Workspace)}, {EscStr(c.Icon)}, {c.DefaultOrder});");
        }
        sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetCategory OFF;");
        sb.AppendLine($"PRINT 'Loaded {categories.Count} categories';");
        sb.AppendLine();

        // Widget
        sb.AppendLine("-- ── Widget ──");
        sb.AppendLine("SET IDENTITY_INSERT widgets.Widget ON;");
        foreach (var w in widgets)
        {
            sb.AppendLine($"INSERT INTO widgets.Widget (WidgetId, Name, WidgetType, ComponentKey, CategoryId, Description, DefaultConfig)");
            sb.AppendLine($"VALUES ({w.WidgetId}, {EscStr(w.Name)}, {EscStr(w.WidgetType)}, {EscStr(w.ComponentKey)}, {w.CategoryId}, {EscStr(w.Description)}, {EscStr(w.DefaultConfig)});");
        }
        sb.AppendLine("SET IDENTITY_INSERT widgets.Widget OFF;");
        sb.AppendLine($"PRINT 'Loaded {widgets.Count} widgets';");
        sb.AppendLine();

        // WidgetDefault
        sb.AppendLine("-- ── WidgetDefault ──");
        sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetDefault ON;");
        foreach (var d in defaults)
        {
            sb.AppendLine($"INSERT INTO widgets.WidgetDefault (WidgetDefaultId, JobTypeId, RoleId, WidgetId, CategoryId, DisplayOrder, Config)");
            sb.AppendLine($"VALUES ({d.WidgetDefaultId}, {d.JobTypeId}, {EscStr(d.RoleId)}, {d.WidgetId}, {d.CategoryId}, {d.DisplayOrder}, {EscStr(d.Config)});");
        }
        sb.AppendLine("SET IDENTITY_INSERT widgets.WidgetDefault OFF;");
        sb.AppendLine($"PRINT 'Loaded {defaults.Count} defaults';");
        sb.AppendLine();

        // JobWidget
        if (jobWidgets.Count > 0)
        {
            sb.AppendLine("-- ── JobWidget ──");
            sb.AppendLine("SET IDENTITY_INSERT widgets.JobWidget ON;");
            foreach (var jw in jobWidgets)
            {
                sb.AppendLine($"INSERT INTO widgets.JobWidget (JobWidgetId, JobId, WidgetId, RoleId, CategoryId, DisplayOrder, IsEnabled, Config)");
                sb.AppendLine($"VALUES ({jw.JobWidgetId}, {EscGuid(jw.JobId)}, {jw.WidgetId}, {EscStr(jw.RoleId)}, {jw.CategoryId}, {jw.DisplayOrder}, {EscBit(jw.IsEnabled)}, {EscStr(jw.Config)});");
            }
            sb.AppendLine("SET IDENTITY_INSERT widgets.JobWidget OFF;");
        }
        sb.AppendLine($"PRINT 'Loaded {jobWidgets.Count} job overrides';");
        sb.AppendLine();
    }

    private static void AppendVerification(StringBuilder sb)
    {
        sb.AppendLine("-- ── Verification ──");
        sb.AppendLine("PRINT '';");
        sb.AppendLine("PRINT '================================================';");
        sb.AppendLine("PRINT ' Widget Dashboard Setup — Complete';");
        sb.AppendLine("PRINT '================================================';");
        sb.AppendLine();
        sb.AppendLine("SELECT 'Categories' AS [Table], COUNT(*) AS [Count] FROM widgets.WidgetCategory");
        sb.AppendLine("UNION ALL SELECT 'Widgets', COUNT(*) FROM widgets.Widget");
        sb.AppendLine("UNION ALL SELECT 'Defaults', COUNT(*) FROM widgets.WidgetDefault");
        sb.AppendLine("UNION ALL SELECT 'JobWidgets', COUNT(*) FROM widgets.JobWidget;");
        sb.AppendLine();
        sb.AppendLine("SET NOCOUNT OFF;");
    }

    private static void ValidateWidgetType(string widgetType)
    {
        if (!AllowedWidgetTypes.Contains(widgetType))
            throw new ArgumentException(
                $"Invalid WidgetType '{widgetType}'. Allowed: {string.Join(", ", AllowedWidgetTypes)}");
    }
}
